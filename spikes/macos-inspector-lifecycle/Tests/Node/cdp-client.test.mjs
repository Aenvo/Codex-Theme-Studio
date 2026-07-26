import assert from "node:assert/strict";
import crypto from "node:crypto";
import http from "node:http";
import test from "node:test";
import {
  evaluate,
  fetchMetadata,
  requestClose,
  validateFacts,
  validateWebSocket,
} from "../../Sources/CodexInspectorLifecycleCore/cdp-client.mjs";

const pageId = "11111111-2222-4333-8444-555555555555";
const otherId = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";

test("WebSocket policy rejects remote hosts and endpoint components", () => {
  const invalid = [
    `ws://example.com:9229/${pageId}`,
    `ws://user:secret@127.0.0.1:9229/${pageId}`,
    `ws://127.0.0.1:9229/${pageId}?token=secret`,
    `ws://127.0.0.1:9229/${pageId}#fragment`,
    `ws://127.0.0.1:9230/${pageId}`,
  ];
  for (const endpoint of invalid) {
    assert.throws(
      () => validateWebSocket(endpoint, "127.0.0.1", 9229, pageId),
      (error) => error.code === "inspector.websocket_invalid");
  }
});

test("WebSocket policy accepts exact IPv4 and IPv6 loopback endpoints", () => {
  assert.equal(
    validateWebSocket(
      `ws://127.0.0.1:9229/${pageId}`,
      "127.0.0.1",
      9229,
      pageId).pathname,
    `/${pageId}`);
  assert.equal(
    validateWebSocket(
      `ws://[::1]:9229/${pageId}`,
      "::1",
      9229,
      pageId).pathname,
    `/${pageId}`);
});

test("sanitized classification rejects raw and unknown route values", () => {
  assert.doesNotThrow(() => validateFacts({
    electronVersion: "150.0.0",
    windowCount: 2,
    routeTypes: ["app:index.html", "avatar-overlay"],
    eligibleAppWindowCount: 1,
  }));
  for (const routeTypes of [
    ["app://-/index.html?private=value"],
    ["https://example.com/private"],
    ["app:path/second"],
  ]) {
    assert.throws(
      () => validateFacts({
        electronVersion: "150.0.0",
        windowCount: 1,
        routeTypes,
        eligibleAppWindowCount: 1,
      }),
      (error) => error.code === "inspector.classification_invalid");
  }
});

test("metadata accepts one exact Node target and matching Browser identity", async () => {
  await withServer((port) => ({
    version: {
      Browser: "node.js/v24.18.0",
      "Protocol-Version": "1.1",
      webSocketDebuggerUrl: `ws://127.0.0.1:${port}/${pageId}`,
    },
    targets: [{
      id: pageId,
      type: "node",
      webSocketDebuggerUrl: `ws://127.0.0.1:${port}/${pageId}`,
    }],
  }), async (port) => {
    const result = await fetchMetadata("127.0.0.1", port);
    assert.equal(result.browserTargetId, pageId);
    assert.equal(result.pageTargetId, pageId);
    assert.equal(result.protocolVersion, "1.1");
  });
});

test("metadata rejects zero or multiple targets", async () => {
  for (const targets of [
    [],
    [
      targetFor(pageId),
      targetFor(otherId),
    ],
  ]) {
    await withServer((port) => ({
      version: versionFor(port, pageId),
      targets: targets.map((target) => ({
        ...target,
        webSocketDebuggerUrl:
          `ws://127.0.0.1:${port}/${target.id}`,
      })),
    }), async (port) => {
      await assert.rejects(
        fetchMetadata("127.0.0.1", port),
        (error) => error.code === "inspector.metadata_invalid");
    });
  }
});

test("metadata rejects wrong target type and mismatched identities", async () => {
  const cases = [
    (port) => ({
      version: versionFor(port, pageId),
      targets: [{
        ...targetFor(pageId),
        type: "page",
        webSocketDebuggerUrl: `ws://127.0.0.1:${port}/${pageId}`,
      }],
    }),
    (port) => ({
      version: versionFor(port, pageId),
      targets: [{
        ...targetFor(otherId),
        webSocketDebuggerUrl: `ws://127.0.0.1:${port}/${otherId}`,
      }],
    }),
  ];
  for (const configuration of cases) {
    await withServer(configuration, async (port) => {
      await assert.rejects(
        fetchMetadata("127.0.0.1", port),
        (error) =>
          error.code === "inspector.target_type_invalid" ||
          error.code === "inspector.target_identity_mismatch");
    });
  }
});

test("metadata rejects redirects and oversized output", async () => {
  await withRawServer((request, response) => {
    response.writeHead(302, { Location: "http://example.com/" });
    response.end();
  }, async (port) => {
    await assert.rejects(
      fetchMetadata("127.0.0.1", port),
      (error) => error.code === "inspector.http_invalid");
  });

  await withRawServer((request, response) => {
    response.writeHead(200, { "Content-Type": "application/json" });
    response.end(JSON.stringify({ value: "x".repeat(129 * 1024) }));
  }, async (port) => {
    await assert.rejects(
      fetchMetadata("127.0.0.1", port),
      (error) => error.code === "inspector.response_too_large");
  });
});

test("CDP disconnect rejects classification evaluation", async () => {
  await withWebSocketServer((message, socket) => {
    assert.equal(message.method, "Runtime.evaluate");
    socket.destroy();
  }, async (endpoint) => {
    await assert.rejects(
      evaluate(endpoint, "({ok:true})", 500, false),
      (error) => error.code === "inspector.websocket_failed");
  });
});

test("Inspector close accepts response loss after exact debugEnd expression", async () => {
  await withWebSocketServer((message, socket) => {
    assert.equal(message.method, "Runtime.evaluate");
    assert.equal(message.params.expression, "process._debugEnd()");
    socket.destroy();
  }, async (endpoint) => {
    await assert.doesNotReject(requestClose(endpoint));
  });
});

function versionFor(port, id) {
  return {
    Browser: "node.js/v24.18.0",
    "Protocol-Version": "1.1",
    webSocketDebuggerUrl: `ws://127.0.0.1:${port}/${id}`,
  };
}

function targetFor(id) {
  return {
    id,
    type: "node",
    webSocketDebuggerUrl: `ws://127.0.0.1:1/${id}`,
  };
}

async function withServer(configuration, callback) {
  await withRawServer((request, response, port) => {
    const value = configuration(port);
    response.writeHead(200, { "Content-Type": "application/json" });
    if (request.url === "/json/version") {
      response.end(JSON.stringify(value.version));
    } else if (request.url === "/json/list") {
      response.end(JSON.stringify(value.targets));
    } else {
      response.writeHead(404);
      response.end("{}");
    }
  }, callback);
}

async function withRawServer(handler, callback) {
  const server = http.createServer((request, response) => {
    const address = server.address();
    const port = typeof address === "object" && address ? address.port : 0;
    handler(request, response, port);
  });
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  const address = server.address();
  assert.equal(typeof address, "object");
  try {
    await callback(address.port);
  } finally {
    await new Promise((resolve, reject) =>
      server.close((error) => error ? reject(error) : resolve()));
  }
}

async function withWebSocketServer(onMessage, callback) {
  const server = http.createServer();
  server.on("upgrade", (request, socket) => {
    const key = request.headers["sec-websocket-key"];
    const accept = crypto.createHash("sha1")
      .update(`${key}258EAFA5-E914-47DA-95CA-C5AB0DC85B11`)
      .digest("base64");
    socket.write([
      "HTTP/1.1 101 Switching Protocols",
      "Upgrade: websocket",
      "Connection: Upgrade",
      `Sec-WebSocket-Accept: ${accept}`,
      "",
      "",
    ].join("\r\n"));

    let buffered = Buffer.alloc(0);
    socket.on("data", (chunk) => {
      buffered = Buffer.concat([buffered, chunk]);
      const decoded = decodeClientFrame(buffered);
      if (!decoded) return;
      buffered = buffered.subarray(decoded.consumed);
      onMessage(JSON.parse(decoded.payload), socket);
    });
  });
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  const address = server.address();
  assert.equal(typeof address, "object");
  const endpoint = `ws://127.0.0.1:${address.port}/${pageId}`;
  try {
    await callback(endpoint);
  } finally {
    await new Promise((resolve) => server.close(resolve));
  }
}

function decodeClientFrame(buffer) {
  if (buffer.length < 2) return null;
  let payloadLength = buffer[1] & 0x7f;
  let offset = 2;
  if (payloadLength === 126) {
    if (buffer.length < 4) return null;
    payloadLength = buffer.readUInt16BE(2);
    offset = 4;
  } else if (payloadLength === 127) {
    if (buffer.length < 10) return null;
    const length = buffer.readBigUInt64BE(2);
    if (length > BigInt(Number.MAX_SAFE_INTEGER)) {
      throw new Error("Frame is too large.");
    }
    payloadLength = Number(length);
    offset = 10;
  }
  const masked = (buffer[1] & 0x80) !== 0;
  const maskLength = masked ? 4 : 0;
  if (buffer.length < offset + maskLength + payloadLength) return null;
  const mask = masked ? buffer.subarray(offset, offset + 4) : null;
  offset += maskLength;
  const payload = Buffer.from(buffer.subarray(offset, offset + payloadLength));
  if (mask) {
    for (let index = 0; index < payload.length; index += 1) {
      payload[index] ^= mask[index % 4];
    }
  }
  return {
    payload: payload.toString("utf8"),
    consumed: offset + payloadLength,
  };
}
