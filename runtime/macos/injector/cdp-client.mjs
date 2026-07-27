import fs from "node:fs";
import http from "node:http";
import { pathToFileURL } from "node:url";
import { buildRendererExpression, validateTheme } from "./renderer-runtime.mjs";

const maximumInputBytes = 64 * 1024;
const maximumResponseBytes = 128 * 1024;
const expectedPort = 9229;
const allowedHosts = new Set(["127.0.0.1", "::1"]);
const identifierPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu;

export async function runCLI() {
  try {
    const request = validateRequest(await readSingleJson(process.stdin));
    const metadata = await fetchMetadata(request.host, request.port);
    let result;
    let primaryError;
    try {
      result = await runOperation(metadata.webSocketUrl, request);
    } catch (error) {
      primaryError = error;
    }
    try {
      await requestClose(metadata.webSocketUrl);
    } catch (error) {
      if (!primaryError) primaryError = error;
    }
    if (primaryError) throw primaryError;
    emit({ status: "ok", result, error: null }, 0);
  } catch (error) {
    emit({
      status: "error",
      result: null,
      error: normalizeError(error),
    }, 1);
  }
}

if (process.argv[1] &&
    import.meta.url === pathToFileURL(process.argv[1]).href) {
  if (process.argv[2] === "self-test") {
    emit({
      schemaVersion: 1,
      toolVersion: "0.1.1",
      status: "ok",
      checks: [
        "fixed-renderer-expressions",
        "loopback-only-cdp",
        "single-json-stdio",
        "bounded-responses",
        "finally-inspector-close",
      ],
    }, 0);
  } else if (process.argv[2] === "schema") {
    emit(schemaDocument(), 0);
  } else if (process.argv.length === 2) {
    await runCLI();
  } else {
    emit({
      status: "error",
      result: null,
      error: { code: "usage.invalid", stage: "usage" },
    }, 2);
  }
}

export function schemaDocument() {
  return {
    schemaVersion: 1,
    toolVersion: "0.1.1",
    commands: ["inspect", "apply", "apply-cleanup", "cleanup"],
    maximumInputBytes,
    maximumResponseBytes,
  };
}

export function validateRequest(value) {
  if (!value || typeof value !== "object" || Array.isArray(value) ||
      !["inspect", "apply", "apply-cleanup", "cleanup"].includes(value.command) ||
      !allowedHosts.has(value.host) || value.port !== expectedPort ||
      !Number.isSafeInteger(value.processId) || value.processId <= 0) {
    throw safeError("protocol.request_invalid", "request");
  }
  const keys = Object.keys(value).sort();
  if (keys.join(",") !==
      ["command", "host", "port", "processId", "theme"].sort().join(",")) {
    throw safeError("protocol.request_invalid", "request");
  }
  if (value.command.includes("apply")) {
    validateTheme(value.theme);
  } else if (value.theme !== null) {
    throw safeError("protocol.request_invalid", "request");
  }
  return value;
}

export async function runOperation(webSocketUrl, request) {
  const classify = await evaluate(
    webSocketUrl,
    buildMainExpression("classify", null),
    2_000,
    false);
  validateClassification(classify);
  if (classify.eligibleWindowCount !== 1 || classify.unknownWindowCount !== 0) {
    throw safeError("inspector.target_invalid", "classification");
  }

  const mode = request.command === "apply-cleanup"
    ? "apply"
    : request.command;
  const first = await evaluate(
    webSocketUrl,
    buildMainExpression(mode, request.theme),
    3_000,
    false);
  validateAggregate(first, mode);
  if (request.command !== "apply-cleanup") return first;

  const cleaned = await evaluate(
    webSocketUrl,
    buildMainExpression("cleanup", null),
    3_000,
    false);
  validateAggregate(cleaned, "cleanup");
  return {
    ...cleaned,
    appliedWindowCount: first.appliedWindowCount,
    applyVerified: first.applyVerified,
    visualEffectApplied: first.visualEffectApplied,
  };
}

export function buildMainExpression(mode, theme) {
  const primaryRenderer = JSON.stringify(buildRendererExpression(
    mode === "classify" ? "inspect" : mode,
    theme));
  const inspectRenderer = JSON.stringify(
    buildRendererExpression("inspect", null));
  return `(() => {
    const electron = process.mainModule?.require("electron");
    if (!electron?.BrowserWindow?.getAllWindows) throw new Error();
    const classify = (raw) => {
      try {
        const parsed = new URL(raw);
        if (parsed.protocol !== "app:") return "unknown";
        if (parsed.searchParams.get("initialRoute") === "/avatar-overlay" ||
            parsed.pathname.includes("/avatar-overlay") ||
            parsed.pathname.includes("/pet-overlay")) return "overlay";
        return "eligible";
      } catch {
        return "unknown";
      }
    };
    const records = electron.BrowserWindow.getAllWindows().map((window) => ({
      kind: classify(window.webContents.getURL()),
      contents: window.webContents
    }));
    const eligible = records.filter((item) => item.kind === "eligible");
    const overlays = records.filter((item) => item.kind === "overlay");
    const unknown = records.filter((item) => item.kind === "unknown");
    if (${JSON.stringify(mode)} === "classify") {
      return {
        eligibleWindowCount: eligible.length,
        overlayWindowCount: overlays.length,
        unknownWindowCount: unknown.length,
        overlayResidualCount: 0
      };
    }
    if (eligible.length !== 1 || unknown.length !== 0) throw new Error();
    const runPrimary = () =>
      eligible[0].contents.executeJavaScript(${primaryRenderer}, true);
    const inspectOverlays = () => Promise.all(overlays.map((record) =>
      record.contents.executeJavaScript(${inspectRenderer}, true)));
    if (${JSON.stringify(mode)} === "apply") {
      return inspectOverlays().then((before) => {
        const beforeResidualCount = before.reduce(
          (sum, item) => sum + item.residualCount, 0);
        if (beforeResidualCount !== 0) {
          return {
            primary: {
              applyVerified: false,
              cleanupVerified: false,
              visualEffectApplied: false,
              residualCount: 0
            },
            before,
            after: []
          };
        }
        return runPrimary().then((primary) =>
          inspectOverlays().then((after) => ({ primary, before, after })));
      })
        .then(({ primary, before, after }) => {
          const overlayResidualCount = [...before, ...after]
            .reduce((sum, item) => sum + item.residualCount, 0);
          return {
            eligibleWindowCount: eligible.length,
            overlayWindowCount: overlays.length,
            unknownWindowCount: unknown.length,
            overlayResidualCount,
            appliedWindowCount: primary.applyVerified ? 1 : 0,
            applyVerified: Boolean(primary.applyVerified),
            cleanupVerified: Boolean(primary.cleanupVerified),
            visualEffectApplied: Boolean(primary.visualEffectApplied),
            residualCount: primary.residualCount
          };
        });
    }
    const run = (record) =>
      record.contents.executeJavaScript(${primaryRenderer}, true);
    return Promise.all([run(eligible[0]), ...overlays.map(run)]).then((facts) => {
      const primary = facts[0];
      const overlayResidualCount = facts.slice(1).reduce(
        (sum, item) => sum + item.residualCount, 0);
      return {
        eligibleWindowCount: eligible.length,
        overlayWindowCount: overlays.length,
        unknownWindowCount: unknown.length,
        overlayResidualCount,
        appliedWindowCount: primary.applyVerified ? 1 : 0,
        applyVerified: Boolean(primary.applyVerified),
        cleanupVerified: Boolean(primary.cleanupVerified),
        visualEffectApplied: Boolean(primary.visualEffectApplied),
        residualCount: primary.residualCount
      };
    });
  })()`;
}

export async function fetchMetadata(host, port) {
  const version = await getJson(host, port, "/json/version");
  const targets = await getJson(host, port, "/json/list");
  if (!version || typeof version !== "object" || Array.isArray(version) ||
      !Array.isArray(targets) || targets.length !== 1 ||
      typeof version.Browser !== "string" ||
      !version.Browser.startsWith("node.js/")) {
    throw safeError("inspector.metadata_invalid", "metadata");
  }
  const target = targets[0];
  if (target?.type !== "node") {
    throw safeError("inspector.target_invalid", "metadata");
  }
  const targetId = validateIdentifier(target.id);
  const socket = validateWebSocket(
    target.webSocketDebuggerUrl,
    host,
    port,
    targetId);
  if (version.webSocketDebuggerUrl != null) {
    const browser = validateWebSocket(
      version.webSocketDebuggerUrl,
      host,
      port,
      targetId);
    if (browser.pathname !== socket.pathname) {
      throw safeError("inspector.target_invalid", "metadata");
    }
  }
  return { webSocketUrl: socket.href };
}

export function validateWebSocket(raw, expectedHost, expectedPort, expectedId) {
  let endpoint;
  try {
    endpoint = new URL(raw);
  } catch {
    throw safeError("inspector.websocket_invalid", "metadata");
  }
  const expectedHostname = expectedHost === "::1" ? "[::1]" : expectedHost;
  const identifier = validateIdentifier(endpoint.pathname.slice(1));
  if (endpoint.protocol !== "ws:" ||
      endpoint.hostname.toLowerCase() !== expectedHostname ||
      Number(endpoint.port) !== expectedPort ||
      identifier !== expectedId ||
      endpoint.username || endpoint.password || endpoint.search ||
      endpoint.hash || endpoint.pathname !== `/${identifier}`) {
    throw safeError("inspector.websocket_invalid", "metadata");
  }
  return endpoint;
}

export function evaluate(
  webSocketUrl,
  expression,
  timeoutMilliseconds,
  tolerateDisconnect
) {
  return new Promise((resolve, reject) => {
    const socket = new WebSocket(webSocketUrl);
    let opened = false;
    let settled = false;
    const timer = setTimeout(
      () => finish(reject, safeError("inspector.cdp_timeout", "cdp")),
      timeoutMilliseconds);
    socket.addEventListener("open", () => {
      opened = true;
      socket.send(JSON.stringify({
        id: 1,
        method: "Runtime.evaluate",
        params: {
          expression,
          returnByValue: true,
          awaitPromise: true,
        },
      }));
    });
    socket.addEventListener("message", (event) => {
      const text = typeof event.data === "string" ? event.data : "";
      if (Buffer.byteLength(text) > maximumResponseBytes) {
        finish(reject, safeError("inspector.response_too_large", "cdp"));
        return;
      }
      let message;
      try {
        message = JSON.parse(text);
      } catch {
        finish(reject, safeError("inspector.json_invalid", "cdp"));
        return;
      }
      if (message.id !== 1) return;
      if (message.error || message.result?.exceptionDetails) {
        finish(reject, safeError("inspector.evaluation_failed", "cdp"));
        return;
      }
      finish(resolve, message.result?.result?.value);
    });
    socket.addEventListener("error", () => {
      finish(
        tolerateDisconnect && opened ? resolve : reject,
        tolerateDisconnect && opened
          ? undefined
          : safeError("inspector.websocket_failed", "cdp"));
    });
    socket.addEventListener("close", () => {
      if (tolerateDisconnect && opened) finish(resolve, undefined);
    });

    function finish(callback, value) {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      try {
        socket.close();
      } catch {
        // Best effort after the bounded request.
      }
      callback(value);
    }
  });
}

export async function requestClose(webSocketUrl) {
  await evaluate(webSocketUrl, "process._debugEnd()", 1_200, true);
}

function getJson(host, port, path) {
  return new Promise((resolve, reject) => {
    const request = http.get({
      hostname: host,
      port,
      path,
      timeout: 1_500,
      headers: { Accept: "application/json" },
    }, (response) => {
      if (response.statusCode !== 200 || response.headers.location ||
          !String(response.headers["content-type"] ?? "")
            .toLowerCase().startsWith("application/json")) {
        response.resume();
        reject(safeError("inspector.http_invalid", "metadata"));
        return;
      }
      let count = 0;
      const chunks = [];
      response.on("data", (chunk) => {
        count += chunk.length;
        if (count > maximumResponseBytes) {
          request.destroy(safeError(
            "inspector.response_too_large",
            "metadata"));
          return;
        }
        chunks.push(chunk);
      });
      response.on("end", () => {
        try {
          resolve(JSON.parse(Buffer.concat(chunks).toString("utf8")));
        } catch {
          reject(safeError("inspector.json_invalid", "metadata"));
        }
      });
    });
    request.on("timeout", () =>
      request.destroy(safeError("inspector.http_timeout", "metadata")));
    request.on("error", reject);
  });
}

async function readSingleJson(stream) {
  const chunks = [];
  let count = 0;
  for await (const chunk of stream) {
    count += chunk.length;
    if (count > maximumInputBytes) {
      throw safeError("protocol.request_too_large", "request");
    }
    chunks.push(chunk);
  }
  try {
    const text = Buffer.concat(chunks).toString("utf8");
    const value = JSON.parse(text);
    if (JSON.stringify(value).length === 0) throw new Error();
    return value;
  } catch {
    throw safeError("protocol.request_invalid", "request");
  }
}

function validateIdentifier(value) {
  if (typeof value !== "string" || !identifierPattern.test(value)) {
    throw safeError("inspector.target_invalid", "metadata");
  }
  return value.toLowerCase();
}

export function validateClassification(value) {
  const keys = [
    "eligibleWindowCount",
    "overlayWindowCount",
    "unknownWindowCount",
    "overlayResidualCount",
  ];
  if (!value || typeof value !== "object" ||
      !keys.every((key) =>
        Number.isSafeInteger(value[key]) && value[key] >= 0)) {
    throw safeError("inspector.classification_invalid", "classification");
  }
}

export function validateAggregate(value, mode) {
  validateClassification(value);
  if (value.overlayResidualCount !== 0) {
    throw safeError("renderer.overlay_modified", "renderer");
  }
  if (!Number.isSafeInteger(value.residualCount) ||
      value.residualCount < 0 ||
      !Number.isSafeInteger(value.appliedWindowCount) ||
      value.appliedWindowCount < 0 ||
      typeof value.applyVerified !== "boolean" ||
      typeof value.cleanupVerified !== "boolean" ||
      typeof value.visualEffectApplied !== "boolean" ||
      (mode === "apply" &&
        (!value.applyVerified || !value.visualEffectApplied)) ||
      (mode === "cleanup" &&
        (!value.cleanupVerified || value.residualCount !== 0))) {
    throw safeError("renderer.proof_invalid", "renderer");
  }
}

function safeError(code, stage) {
  return Object.assign(new Error(code), { code, stage });
}

function normalizeError(error) {
  return {
    code: typeof error?.code === "string" ? error.code : "helper.unexpected",
    stage: typeof error?.stage === "string" ? error.stage : "helper",
  };
}

function emit(value, exitCode) {
  fs.writeFileSync(1, `${JSON.stringify(value)}\n`);
  process.exitCode = exitCode;
}
