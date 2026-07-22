import http from "node:http";

const loopbackHosts = new Set(["127.0.0.1", "localhost", "[::1]"]);
const identifierPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu;

export function validateEndpoint(rawUrl, {
  scheme,
  port,
  path,
}) {
  let endpoint;
  try {
    endpoint = new URL(rawUrl);
  } catch {
    throw protocolError("invalid_url");
  }

  if (endpoint.protocol !== `${scheme}:`) {
    throw protocolError("invalid_scheme");
  }
  if (!loopbackHosts.has(endpoint.hostname.toLowerCase())) {
    throw protocolError("non_loopback_host");
  }
  if (endpoint.username || endpoint.password || endpoint.search || endpoint.hash) {
    throw protocolError("endpoint_components_rejected");
  }
  if (Number(endpoint.port) !== port) {
    throw protocolError("unexpected_port");
  }
  if (endpoint.pathname !== path) {
    throw protocolError("unexpected_path");
  }

  return endpoint;
}

export function validateIdentifier(value, diagnosticCode = "invalid_identifier") {
  if (typeof value !== "string" || !identifierPattern.test(value)) {
    throw protocolError(diagnosticCode);
  }
  return value.toLowerCase();
}

export function assertPortOwner(listeners, expectedProcessId) {
  if (!Array.isArray(listeners) || listeners.length === 0) {
    throw retryableError("inspector_unavailable");
  }

  if (listeners.some((listener) =>
    listener.owningProcessId !== expectedProcessId ||
    !isLoopbackAddress(listener.localAddress))) {
    throw protocolError("port_in_use", false);
  }
}

export async function fetchInspectorMetadata(port, {
  timeoutMs = 1500,
  maxBytes = 128 * 1024,
} = {}) {
  const versionUrl = `http://127.0.0.1:${port}/json/version`;
  const listUrl = `http://127.0.0.1:${port}/json/list`;
  validateEndpoint(versionUrl, { scheme: "http", port, path: "/json/version" });
  validateEndpoint(listUrl, { scheme: "http", port, path: "/json/list" });

  const version = await getJson(versionUrl, timeoutMs, maxBytes);
  const targets = await getJson(listUrl, timeoutMs, maxBytes);
  if (!version || typeof version !== "object" || Array.isArray(version) ||
      !Array.isArray(targets) || targets.length !== 1) {
    throw protocolError("invalid_response");
  }

  if (typeof version.Browser !== "string" ||
      !version.Browser.startsWith("node.js/") ||
      typeof version["Protocol-Version"] !== "string") {
    throw protocolError("invalid_version_response");
  }

  const target = targets[0];
  const pageId = validateIdentifier(target?.id, "invalid_page_target_id");
  if (target?.type !== "node") {
    throw protocolError("unexpected_target_type");
  }

  const targetWebSocket = validateWebSocketUrl(
    target.webSocketDebuggerUrl,
    port,
    "invalid_target_websocket");
  if (targetWebSocket.pathname !== `/${pageId}`) {
    throw protocolError("page_target_id_mismatch");
  }

  let browserId = pageId;
  if (version.webSocketDebuggerUrl != null) {
    const browserWebSocket = validateWebSocketUrl(
      version.webSocketDebuggerUrl,
      port,
      "invalid_browser_websocket");
    browserId = validateIdentifier(
      browserWebSocket.pathname.slice(1),
      "invalid_browser_id");
    if (browserId !== pageId) {
      throw protocolError("browser_id_mismatch");
    }
  }

  return {
    browserId,
    pageId,
    webSocketUrl: targetWebSocket.href,
  };
}

export async function evaluate(webSocketUrl, expression, {
  timeoutMs = 2000,
  maxBytes = 128 * 1024,
  awaitPromise = false,
} = {}) {
  const endpoint = new URL(webSocketUrl);
  const targetId = validateIdentifier(endpoint.pathname.slice(1));
  validateEndpoint(webSocketUrl, {
    scheme: "ws",
    port: Number(endpoint.port),
    path: `/${targetId}`,
  });

  return await new Promise((resolve, reject) => {
    const socket = new WebSocket(endpoint);
    const requestId = 1;
    const timer = setTimeout(() => {
      socket.close();
      reject(retryableError("timeout"));
    }, timeoutMs);

    socket.addEventListener("open", () => {
      socket.send(JSON.stringify({
        id: requestId,
        method: "Runtime.evaluate",
        params: {
          expression,
          returnByValue: true,
          awaitPromise,
        },
      }));
    });

    socket.addEventListener("message", (event) => {
      const text = typeof event.data === "string" ? event.data : "";
      if (Buffer.byteLength(text, "utf8") > maxBytes) {
        finish(reject, protocolError("response_too_large"));
        return;
      }

      let message;
      try {
        message = JSON.parse(text);
      } catch {
        finish(reject, protocolError("invalid_response"));
        return;
      }

      if (message.id !== requestId) {
        return;
      }
      if (message.error || message.result?.exceptionDetails) {
        finish(reject, protocolError("evaluation_failed"));
        return;
      }

      finish(resolve, message.result?.result?.value);
    });

    socket.addEventListener("error", () => {
      finish(reject, retryableError("inspector_unavailable"));
    });

    function finish(callback, value) {
      clearTimeout(timer);
      try {
        socket.close();
      } catch {
        // Best-effort close only.
      }
      callback(value);
    }
  });
}

export function classifyAppRoutes(urls) {
  if (!Array.isArray(urls)) {
    throw protocolError("invalid_probe_result");
  }

  return [...new Set(urls.map((rawUrl) => {
    let url;
    try {
      url = new URL(rawUrl);
    } catch {
      return "non-app";
    }
    if (url.protocol !== "app:") {
      return "non-app";
    }
    if (url.searchParams.get("initialRoute") === "/avatar-overlay") {
      return "avatar-overlay";
    }
    const firstSegment = url.pathname.split("/").filter(Boolean)[0];
    return firstSegment ? `app:${firstSegment}` : "app:root";
  }))].sort();
}

export function protocolError(diagnosticCode, retryable = false) {
  return Object.assign(new Error(diagnosticCode), {
    code: "protocol_rejected",
    diagnosticCode,
    retryable,
  });
}

export function retryableError(code) {
  return Object.assign(new Error(code), { code, retryable: true });
}

function validateWebSocketUrl(rawUrl, port, diagnosticCode) {
  if (typeof rawUrl !== "string") {
    throw protocolError(diagnosticCode);
  }
  let endpoint;
  try {
    endpoint = new URL(rawUrl);
  } catch {
    throw protocolError(diagnosticCode);
  }
  const identifier = validateIdentifier(endpoint.pathname.slice(1), diagnosticCode);
  return validateEndpoint(rawUrl, {
    scheme: "ws",
    port,
    path: `/${identifier}`,
  });
}

function isLoopbackAddress(address) {
  return address === "127.0.0.1" || address === "::1";
}

async function getJson(rawUrl, timeoutMs, maxBytes) {
  return await new Promise((resolve, reject) => {
    const request = http.get(rawUrl, {
      timeout: timeoutMs,
      headers: { Accept: "application/json" },
    }, (response) => {
      if (response.statusCode !== 200 ||
          response.headers.location ||
          !String(response.headers["content-type"] ?? "")
            .toLowerCase()
            .startsWith("application/json")) {
        response.resume();
        reject(protocolError("invalid_http_response"));
        return;
      }

      let bytes = 0;
      const chunks = [];
      response.on("data", (chunk) => {
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
    request.on("error", reject);
  });
}
