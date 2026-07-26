import http from "node:http";
import fs from "node:fs";
import { pathToFileURL } from "node:url";

const maximumBytes = 128 * 1024;
const expectedPort = 9229;
const identifierPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu;
const allowedHosts = new Set(["127.0.0.1", "::1"]);

const classifyExpression = `(() => {
  const classify = (raw) => {
    try {
      const parsed = new URL(raw);
      if (parsed.protocol !== "app:") return "non-app";
      if (parsed.searchParams.get("initialRoute") === "/avatar-overlay" ||
          parsed.pathname.includes("/avatar-overlay") ||
          parsed.pathname.includes("/pet-overlay")) {
        return "avatar-overlay";
      }
      const first = parsed.pathname.split("/").filter(Boolean)[0];
      return first ? "app:" + first : "app:root";
    } catch {
      return "non-app";
    }
  };
  const electron = process.mainModule?.require("electron");
  if (!electron?.BrowserWindow?.getAllWindows) {
    throw new Error("browser-window-unavailable");
  }
  const windows = electron.BrowserWindow.getAllWindows();
  const routeTypes = [...new Set(
    windows.map((window) => classify(window.webContents.getURL()))
  )].sort();
  return {
    electronVersion: String(process.versions.electron || ""),
    windowCount: windows.length,
    routeTypes,
    eligibleAppWindowCount: windows.filter((window) => {
      const category = classify(window.webContents.getURL());
      return category.startsWith("app:") && category !== "avatar-overlay";
    }).length
  };
})()`;

export async function runCLI(args = process.argv.slice(2)) {
  try {
    const options = parseArguments(args);
    if (options.command === "inspect-and-close") {
      const metadata = await fetchMetadata(options.host, options.port);
      let facts;
      let primaryError;
      try {
        facts = await evaluate(
          metadata.webSocketUrl,
          classifyExpression,
          2_000,
          false);
        validateFacts(facts);
      } catch (error) {
        primaryError = error;
      }

      const closeRequestedAtUtc = new Date().toISOString();
      try {
        await requestClose(metadata.webSocketUrl);
      } catch (error) {
        if (!primaryError) primaryError = error;
      }
      if (primaryError) throw primaryError;

      emit({
        browserTargetId: metadata.browserTargetId,
        pageTargetId: metadata.pageTargetId,
        protocolVersion: metadata.protocolVersion,
        runtimeKind: "node",
        electronVersion: facts.electronVersion,
        windowCount: facts.windowCount,
        routeTypes: facts.routeTypes,
        eligibleAppWindowCount: facts.eligibleAppWindowCount,
        closeRequestedAtUtc,
      }, 0);
    }

    if (options.command === "close-only") {
      const metadata = await fetchMetadata(options.host, options.port);
      await requestClose(metadata.webSocketUrl);
      emit({ status: "close-requested" }, 0);
    }

    throw helperError("usage.invalid", "usage", "Unsupported helper command.");
  } catch (error) {
    const safe = normalizeError(error);
    emit(safe, 1);
  }
}

if (process.argv[1] &&
    import.meta.url === pathToFileURL(process.argv[1]).href) {
  await runCLI();
}

function parseArguments(args) {
  const command = args.shift();
  if (command !== "inspect-and-close" && command !== "close-only") {
    throw helperError("usage.invalid", "usage", "Unsupported helper command.");
  }
  const values = {};
  while (args.length > 0) {
    const name = args.shift();
    const value = args.shift();
    if (!["--host", "--port", "--pid"].includes(name) || value == null ||
        Object.hasOwn(values, name)) {
      throw helperError("usage.invalid", "usage", "Invalid helper arguments.");
    }
    values[name] = value;
  }
  const host = values["--host"];
  const port = Number(values["--port"]);
  const processId = Number(values["--pid"]);
  if (!allowedHosts.has(host) || port !== expectedPort ||
      !Number.isSafeInteger(processId) || processId <= 0) {
    throw helperError("usage.invalid", "usage", "Invalid helper arguments.");
  }
  return { command, host, port, processId };
}

export async function fetchMetadata(host, port) {
  const version = await getJson(host, port, "/json/version");
  const targets = await getJson(host, port, "/json/list");
  if (!version || typeof version !== "object" || Array.isArray(version) ||
      !Array.isArray(targets) || targets.length !== 1) {
    throw helperError(
      "inspector.metadata_invalid",
      "metadata",
      "Inspector metadata shape is invalid.");
  }
  if (typeof version.Browser !== "string" ||
      !version.Browser.startsWith("node.js/") ||
      typeof version["Protocol-Version"] !== "string") {
    throw helperError(
      "inspector.version_invalid",
      "metadata",
      "Inspector version metadata is invalid.");
  }

  const target = targets[0];
  if (target?.type !== "node") {
    throw helperError(
      "inspector.target_type_invalid",
      "metadata",
      "Inspector target type is invalid.");
  }
  const pageTargetId = validateIdentifier(
    target.id,
    "inspector.page_target_invalid");
  const targetSocket = validateWebSocket(
    target.webSocketDebuggerUrl,
    host,
    port,
    pageTargetId);

  let browserTargetId = pageTargetId;
  if (version.webSocketDebuggerUrl != null) {
    const browserSocket = validateWebSocket(
      version.webSocketDebuggerUrl,
      host,
      port);
    browserTargetId = validateIdentifier(
      browserSocket.pathname.slice(1),
      "inspector.browser_target_invalid");
    if (browserTargetId !== pageTargetId) {
      throw helperError(
        "inspector.target_identity_mismatch",
        "metadata",
        "Browser and Page target identities differ.");
    }
  }

  return {
    browserTargetId,
    pageTargetId,
    protocolVersion: version["Protocol-Version"],
    webSocketUrl: targetSocket.href,
  };
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
      if (response.statusCode !== 200 ||
          response.headers.location ||
          !String(response.headers["content-type"] ?? "")
            .toLowerCase()
            .startsWith("application/json")) {
        response.resume();
        reject(helperError(
          "inspector.http_invalid",
          "metadata",
          "Inspector HTTP response is invalid."));
        return;
      }
      let byteCount = 0;
      const chunks = [];
      response.on("data", (chunk) => {
        byteCount += chunk.length;
        if (byteCount > maximumBytes) {
          request.destroy(helperError(
            "inspector.response_too_large",
            "metadata",
            "Inspector response exceeded its limit."));
          return;
        }
        chunks.push(chunk);
      });
      response.on("end", () => {
        try {
          resolve(JSON.parse(Buffer.concat(chunks).toString("utf8")));
        } catch {
          reject(helperError(
            "inspector.json_invalid",
            "metadata",
            "Inspector response is not valid JSON."));
        }
      });
    });
    request.on("timeout", () => request.destroy(helperError(
      "inspector.http_timeout",
      "metadata",
      "Inspector HTTP request timed out.")));
    request.on("error", reject);
  });
}

function validateIdentifier(value, code) {
  if (typeof value !== "string" || !identifierPattern.test(value)) {
    throw helperError(code, "metadata", "Inspector target identity is invalid.");
  }
  return value.toLowerCase();
}

export function validateWebSocket(raw, expectedHost, expectedPort, expectedId) {
  if (typeof raw !== "string") {
    throw helperError(
      "inspector.websocket_invalid",
      "metadata",
      "Inspector WebSocket endpoint is invalid.");
  }
  let endpoint;
  try {
    endpoint = new URL(raw);
  } catch {
    throw helperError(
      "inspector.websocket_invalid",
      "metadata",
      "Inspector WebSocket endpoint is invalid.");
  }
  const normalizedExpectedHost =
    expectedHost === "::1" ? "[::1]" : expectedHost;
  const identifier = validateIdentifier(
    endpoint.pathname.slice(1),
    "inspector.websocket_invalid");
  if (endpoint.protocol !== "ws:" ||
      endpoint.hostname.toLowerCase() !== normalizedExpectedHost ||
      Number(endpoint.port) !== expectedPort ||
      endpoint.username || endpoint.password || endpoint.search ||
      endpoint.hash || endpoint.pathname !== `/${identifier}` ||
      (expectedId != null && identifier !== expectedId)) {
    throw helperError(
      "inspector.websocket_invalid",
      "metadata",
      "Inspector WebSocket endpoint is invalid.");
  }
  return endpoint;
}

export function evaluate(
  webSocketUrl,
  expression,
  timeoutMs,
  tolerateDisconnect
) {
  return new Promise((resolve, reject) => {
    const socket = new WebSocket(webSocketUrl);
    const requestId = 1;
    let opened = false;
    let settled = false;
    const timer = setTimeout(() => {
      finish(reject, helperError(
        "inspector.cdp_timeout",
        "cdp",
        "Inspector CDP request timed out."));
    }, timeoutMs);

    socket.addEventListener("open", () => {
      opened = true;
      socket.send(JSON.stringify({
        id: requestId,
        method: "Runtime.evaluate",
        params: {
          expression,
          returnByValue: true,
          awaitPromise: false,
        },
      }));
    });
    socket.addEventListener("message", (event) => {
      const text = typeof event.data === "string" ? event.data : "";
      if (Buffer.byteLength(text, "utf8") > maximumBytes) {
        finish(reject, helperError(
          "inspector.response_too_large",
          "cdp",
          "Inspector response exceeded its limit."));
        return;
      }
      let message;
      try {
        message = JSON.parse(text);
      } catch {
        finish(reject, helperError(
          "inspector.json_invalid",
          "cdp",
          "Inspector CDP response is invalid."));
        return;
      }
      if (message.id !== requestId) return;
      if (message.error || message.result?.exceptionDetails) {
        finish(reject, helperError(
          "inspector.evaluation_failed",
          "cdp",
          "The allowed Inspector expression failed."));
        return;
      }
      finish(resolve, message.result?.result?.value);
    });
    socket.addEventListener("error", () => {
      if (tolerateDisconnect && opened) {
        finish(resolve, undefined);
      } else {
        finish(reject, helperError(
          "inspector.websocket_failed",
          "cdp",
          "Inspector WebSocket failed."));
      }
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
        // Socket closure is best effort after the bounded request.
      }
      callback(value);
    }
  });
}

export async function requestClose(webSocketUrl) {
  await evaluate(
    webSocketUrl,
    "process._debugEnd()",
    1_200,
    true);
}

export function validateFacts(value) {
  const allowedRoute =
    /^(app:root|app:[A-Za-z0-9._-]+|avatar-overlay|non-app)$/u;
  if (!value || typeof value !== "object" ||
      typeof value.electronVersion !== "string" ||
      !value.electronVersion ||
      !Number.isSafeInteger(value.windowCount) || value.windowCount < 0 ||
      !Array.isArray(value.routeTypes) ||
      !value.routeTypes.every((item) =>
        typeof item === "string" && allowedRoute.test(item)) ||
      !Number.isSafeInteger(value.eligibleAppWindowCount) ||
      value.eligibleAppWindowCount < 0 ||
      value.eligibleAppWindowCount > value.windowCount) {
    throw helperError(
      "inspector.classification_invalid",
      "classification",
      "Sanitized window classification is invalid.");
  }
}

function helperError(code, stage, message) {
  return Object.assign(new Error(code), { code, stage, safeMessage: message });
}

function normalizeError(error) {
  return {
    code: typeof error?.code === "string" ? error.code : "helper.unexpected",
    stage: typeof error?.stage === "string" ? error.stage : "helper",
    message: typeof error?.safeMessage === "string"
      ? error.safeMessage
      : "The Inspector helper failed.",
  };
}

function emit(value, exitCode) {
  fs.writeFileSync(1, `${JSON.stringify(value)}\n`);
  process.exit(exitCode);
}
