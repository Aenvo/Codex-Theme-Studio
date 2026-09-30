import http from "node:http";
import { classifyAppRoutes, protocolError, retryableError } from "./security.mjs";
import {
  rendererCompatibility,
  rendererWindowProbe,
} from "./renderer-runtime.mjs";

const loopbackHosts = new Set(["127.0.0.1", "localhost", "[::1]"]);
const browserIdPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu;
const maximumMessageBytes = 2 * 1024 * 1024;
const defaultTimeoutMs = 8000;

const rendererCompatibilityProbeExpression =
  `(${rendererWindowProbe.toString()})(${JSON.stringify(rendererCompatibility)})`;

const canaryExpression = `(async () => {
  const structure = ${rendererCompatibilityProbeExpression};
  if (!structure?.eligible) {
    return {
      qualified: false,
      applied: false,
      cleaned: true,
      reason: typeof structure?.reason === "string"
        ? structure.reason
        : "unknown",
      features: {
        shell: structure?.features?.shell === true,
        sidebar: structure?.features?.sidebar === true,
        content: structure?.features?.content === true,
        composer: structure?.features?.composer === true,
      },
      pageMode: typeof structure?.pageMode === "string"
        ? structure.pageMode
        : "unknown",
    };
  }
  const id = "codex-theme-studio-port-canary";
  const className = "codex-theme-studio-port-canary";
  const variableName = "--codex-theme-studio-port-canary";
  document.getElementById(id)?.remove();
  document.documentElement.classList.remove(className);
  document.documentElement.style.removeProperty(variableName);
  const style = document.createElement("style");
  style.id = id;
  style.textContent = ":root." + className + "{" + variableName + ":1}";
  document.head.append(style);
  document.documentElement.classList.add(className);
  const applied = getComputedStyle(document.documentElement)
    .getPropertyValue(variableName).trim() === "1";
  style.remove();
  document.documentElement.classList.remove(className);
  document.documentElement.style.removeProperty(variableName);
  const cleaned = !document.getElementById(id) &&
    !document.documentElement.classList.contains(className) &&
    getComputedStyle(document.documentElement)
      .getPropertyValue(variableName).trim() !== "1";
  return { qualified: true, applied, cleaned };
})()`;

export async function probeRendererPort(port, {
  timeoutMs = 15000,
  fetchMetadata = fetchBrowserMetadata,
  connectClient = CdpWebSocketClient.connect,
  retryDelayMs = 100,
} = {}) {
  const operation = await executeRendererPortExpression(port, canaryExpression, {
    timeoutMs,
    fetchMetadata,
    connectClient,
    retryDelayMs,
  });
  const results = operation.results;
  if (results.some(result =>
    typeof result.applied !== "boolean" ||
    typeof result.cleaned !== "boolean")) {
    throw protocolError("port_canary_evaluation_failed");
  }
  return {
    browserId: operation.browserId,
    targetCount: operation.targetCount,
    eligibleTargetCount: operation.eligibleTargetCount,
    routeTypes: operation.routeTypes,
    canaryApplied: results.every(result => result.applied === true),
    canaryCleaned: results.every(result => result.cleaned === true),
  };
}

export async function executeRendererPortExpression(port, expression, {
  timeoutMs = 15000,
  fetchMetadata = fetchBrowserMetadata,
  connectClient = CdpWebSocketClient.connect,
  retryDelayMs = 100,
} = {}) {
  validatePort(port);
  if (typeof expression !== "string" || !expression) {
    throw protocolError("port_expression_invalid");
  }
  const metadata = await fetchMetadata(port, { timeoutMs });
  const client = await connectClient(metadata.webSocketUrl, timeoutMs);
  try {
    const deadline = Date.now() + timeoutMs;
    let sawCandidate = false;
    let lastObservation = {
      targetCount: 0,
      candidateCount: 0,
      routeTypes: [],
      evaluations: [],
    };
    while (true) {
      const targets = await getCandidateTargets(client, timeoutMs);
      sawCandidate ||= targets.candidates.length > 0;
      const results = [];
      for (const target of targets.candidates) {
        results.push(await evaluateExpression(client, target, expression));
      }
      lastObservation = summarizeAttempt(targets, results);

      const qualified = results.filter(result => result.qualified === true);
      if (qualified.length > 0) {
        return {
          browserId: metadata.browserId,
          targetCount: targets.all.length,
          eligibleTargetCount: qualified.length,
          routeTypes: classifyAppRoutes(targets.all.map(target => target.url ?? "")),
          results: qualified,
        };
      }

      if (Date.now() >= deadline) {
        const error = retryableError(sawCandidate
          ? "port_renderer_unqualified"
          : "port_renderer_unavailable");
        error.details = lastObservation;
        throw error;
      }
      await delay(Math.min(retryDelayMs, Math.max(1, deadline - Date.now())));
    }
  } finally {
    client.close();
  }
}

function summarizeAttempt(targets, results) {
  const normalizeReason = (value) =>
    typeof value === "string" && /^[a-z0-9-]{1,64}$/u.test(value)
      ? value
      : "unknown";
  return {
    targetCount: targets.all.length,
    candidateCount: targets.candidates.length,
    routeTypes: classifyAppRoutes(targets.all.map(target => target.url ?? "")),
    evaluations: results.slice(0, 8).map(result => ({
      qualified: result?.qualified === true,
      reason: normalizeReason(result?.reason),
      pageMode: normalizeReason(result?.pageMode),
      features: {
        shell: result?.features?.shell === true,
        sidebar: result?.features?.sidebar === true,
        content: result?.features?.content === true,
        composer: result?.features?.composer === true,
      },
    })),
  };
}

async function evaluateExpression(client, target, expression) {
  const attached = await client.request("Target.attachToTarget", {
    targetId: target.targetId,
    flatten: true,
  });
  if (typeof attached?.sessionId !== "string" || !attached.sessionId) {
    throw protocolError("port_attach_invalid");
  }

  try {
    const evaluated = await client.request(
      "Runtime.evaluate",
      {
        expression,
        returnByValue: true,
        awaitPromise: true,
      },
      attached.sessionId);
    const value = evaluated?.result?.value;
    if (evaluated?.exceptionDetails ||
        evaluated?.result?.type !== "object" ||
        !value ||
        typeof value.qualified !== "boolean") {
      throw protocolError("port_operation_evaluation_failed");
    }
    return value;
  } finally {
    await client.request("Target.detachFromTarget", {
      sessionId: attached.sessionId,
    });
  }
}

export async function fetchBrowserMetadata(port, {
  timeoutMs = 1500,
  maxBytes = 128 * 1024,
  retryDelayMs = 75,
} = {}) {
  validatePort(port);
  const versionUrl = `http://127.0.0.1:${port}/json/version`;
  const deadline = Date.now() + timeoutMs;
  while (true) {
    try {
      const remainingMs = Math.max(1, deadline - Date.now());
      const version = await getJson(versionUrl, remainingMs, maxBytes);
      return validateBrowserMetadata(version, port);
    } catch (error) {
      if (!error?.retryable || Date.now() >= deadline) {
        throw error;
      }
      await delay(Math.min(retryDelayMs, Math.max(1, deadline - Date.now())));
    }
  }
}

export function validateBrowserMetadata(version, port) {
  validatePort(port);
  if (!version || typeof version !== "object" || Array.isArray(version) ||
      typeof version.Browser !== "string" ||
      !/^(?:Chrome|HeadlessChrome)\/[0-9]+(?:\.|$)/u.test(version.Browser) ||
      typeof version["Protocol-Version"] !== "string" ||
      typeof version.webSocketDebuggerUrl !== "string") {
    throw protocolError("invalid_browser_version_response");
  }

  let endpoint;
  try {
    endpoint = new URL(version.webSocketDebuggerUrl);
  } catch {
    throw protocolError("invalid_browser_websocket");
  }
  const prefix = "/devtools/browser/";
  const browserId = endpoint.pathname.startsWith(prefix)
    ? endpoint.pathname.slice(prefix.length)
    : "";
  if (endpoint.protocol !== "ws:" ||
      !loopbackHosts.has(endpoint.hostname.toLowerCase()) ||
      Number(endpoint.port) !== port ||
      endpoint.username || endpoint.password || endpoint.search || endpoint.hash ||
      !browserIdPattern.test(browserId)) {
    throw protocolError("invalid_browser_websocket");
  }

  return { browserId: browserId.toLowerCase(), webSocketUrl: endpoint.href };
}

class CdpWebSocketClient {
  constructor(socket) {
    this.socket = socket;
    this.nextId = 1;
    this.pending = new Map();
    this.closed = false;
    socket.addEventListener("message", event => this.onMessage(event));
    socket.addEventListener("error", () => this.failAll(
      retryableError("port_websocket_failed")));
    socket.addEventListener("close", () => this.failAll(
      retryableError("port_websocket_closed")));
  }

  static connect(webSocketUrl, timeoutMs) {
    return new Promise((resolve, reject) => {
      let settled = false;
      const socket = new WebSocket(webSocketUrl);
      const timer = setTimeout(() => {
        socket.close();
        finish(reject, retryableError("port_websocket_timeout"));
      }, timeoutMs);
      socket.addEventListener("open", () => {
        clearTimeout(timer);
        finish(resolve, new CdpWebSocketClient(socket));
      });
      socket.addEventListener("error", () => {
        clearTimeout(timer);
        finish(reject, retryableError("port_websocket_failed"));
      });

      function finish(callback, value) {
        if (settled) return;
        settled = true;
        callback(value);
      }
    });
  }

  request(method, params = {}, sessionId = undefined, timeoutMs = defaultTimeoutMs) {
    if (this.closed) {
      return Promise.reject(retryableError("port_websocket_closed"));
    }
    const id = this.nextId++;
    const message = { id, method, params };
    if (sessionId) message.sessionId = sessionId;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        reject(retryableError("timeout"));
      }, timeoutMs);
      this.pending.set(id, { resolve, reject, timer });
      try {
        this.socket.send(JSON.stringify(message));
      } catch {
        clearTimeout(timer);
        this.pending.delete(id);
        reject(retryableError("port_websocket_failed"));
      }
    });
  }

  close() {
    if (this.closed) return;
    this.closed = true;
    this.failAll(retryableError("port_websocket_closed"));
    try {
      this.socket.close();
    } catch {
      // Best-effort close only.
    }
  }

  onMessage(event) {
    if (typeof event.data !== "string" ||
        Buffer.byteLength(event.data, "utf8") > maximumMessageBytes) {
      this.failAll(protocolError("port_response_invalid"));
      this.close();
      return;
    }
    let message;
    try {
      message = JSON.parse(event.data);
    } catch {
      this.failAll(protocolError("port_response_invalid"));
      this.close();
      return;
    }
    if (!Number.isInteger(message.id)) return;
    const pending = this.pending.get(message.id);
    if (!pending) return;
    clearTimeout(pending.timer);
    this.pending.delete(message.id);
    if (message.error) pending.reject(protocolError("port_protocol_error"));
    else pending.resolve(message.result);
  }

  failAll(error) {
    for (const pending of this.pending.values()) {
      clearTimeout(pending.timer);
      pending.reject(error);
    }
    this.pending.clear();
  }
}

async function getCandidateTargets(client, timeoutMs) {
  const result = await client.request("Target.getTargets", {}, undefined, timeoutMs);
  if (!Array.isArray(result?.targetInfos)) {
    throw protocolError("port_targets_invalid");
  }
  const all = result.targetInfos.filter(target => target?.type === "page");
  const candidates = all.filter(target =>
    typeof target.targetId === "string" &&
    target.targetId.length > 0 && target.targetId.length <= 200 &&
    isEligibleAppUrl(target.url));
  return { all, candidates };
}

function isEligibleAppUrl(rawUrl) {
  try {
    const url = new URL(rawUrl);
    return url.protocol === "app:" &&
      url.searchParams.get("initialRoute") !== "/avatar-overlay";
  } catch {
    return false;
  }
}

function validatePort(port) {
  if (!Number.isSafeInteger(port) || port < 1024 || port > 65535) {
    throw protocolError("port_invalid");
  }
}

function delay(milliseconds) {
  return new Promise(resolve => setTimeout(resolve, milliseconds));
}

async function getJson(rawUrl, timeoutMs, maxBytes) {
  return await new Promise((resolve, reject) => {
    const request = http.get(rawUrl, {
      timeout: timeoutMs,
      headers: { Accept: "application/json" },
    }, response => {
      if (response.statusCode !== 200 || response.headers.location ||
          !String(response.headers["content-type"] ?? "")
            .toLowerCase().startsWith("application/json")) {
        response.resume();
        reject(protocolError("invalid_http_response"));
        return;
      }
      let bytes = 0;
      const chunks = [];
      response.on("data", chunk => {
        bytes += chunk.length;
        if (bytes > maxBytes) {
          request.destroy(protocolError("response_too_large"));
          return;
        }
        chunks.push(chunk);
      });
      response.on("end", () => {
        try {
          resolve(JSON.parse(Buffer.concat(chunks).toString("utf8")));
        } catch {
          reject(protocolError("invalid_response"));
        }
      });
    });
    request.on("timeout", () => request.destroy(retryableError("timeout")));
    request.on("error", error => reject(
      error?.code === "protocol_rejected"
        ? error
        : retryableError("inspector_unavailable")));
  });
}
