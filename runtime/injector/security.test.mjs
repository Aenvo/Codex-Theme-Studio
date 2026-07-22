import assert from "node:assert/strict";
import http from "node:http";
import test from "node:test";
import {
  assertPortOwner,
  fetchInspectorMetadata,
  validateEndpoint,
} from "./security.mjs";

const browserId = "11111111-2222-4333-8444-555555555555";
const otherId = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";

test("rejects remote hosts, credentials, query strings, and wrong paths", () => {
  const cases = [
    ["http://example.com:9229/json/version", "/json/version"],
    ["http://user:secret@127.0.0.1:9229/json/version", "/json/version"],
    ["http://127.0.0.1:9229/json/version?token=secret", "/json/version"],
    ["http://127.0.0.1:9229/json/list", "/json/version"],
    ["http://127.0.0.1:9230/json/version", "/json/version"],
  ];

  for (const [url, expectedPath] of cases) {
    assert.throws(
      () => validateEndpoint(url, {
        scheme: "http",
        port: 9229,
        path: expectedPath,
      }),
      (error) => error.code === "protocol_rejected");
  }
});

test("rejects a forged remote WebSocket endpoint", () => {
  assert.throws(
    () => validateEndpoint(
      `ws://example.com:9229/${browserId}`,
      {
        scheme: "ws",
        port: 9229,
        path: `/${browserId}`,
      }),
    (error) =>
      error.code === "protocol_rejected" &&
      error.message === "non_loopback_host");
});

test("fake local inspector rejects mismatched Browser and Page target IDs", async () => {
  await withFakeInspector({
    version: (port) => ({
      Browser: "node.js/v24",
      "Protocol-Version": "1.1",
      webSocketDebuggerUrl: `ws://127.0.0.1:${port}/${browserId}`,
    }),
    targets: (port) => [{
      id: otherId,
      type: "node",
      webSocketDebuggerUrl: `ws://127.0.0.1:${port}/${otherId}`,
    }],
  }, async (port) => {
    await assert.rejects(
      fetchInspectorMetadata(port),
      (error) =>
        error.code === "protocol_rejected" &&
        error.message === "browser_id_mismatch");
  });
});

test("fake local inspector accepts the exact expected metadata shape", async () => {
  await withFakeInspector({
    version: (port) => ({
      Browser: "node.js/v24",
      "Protocol-Version": "1.1",
      webSocketDebuggerUrl: `ws://127.0.0.1:${port}/${browserId}`,
    }),
    targets: (port) => [{
      id: browserId,
      type: "node",
      webSocketDebuggerUrl: `ws://127.0.0.1:${port}/${browserId}`,
    }],
  }, async (port) => {
    const result = await fetchInspectorMetadata(port);
    assert.equal(result.browserId, browserId);
    assert.equal(result.pageId, browserId);
  });
});

test("fake local inspector accepts Node version metadata without a browser socket", async () => {
  await withFakeInspector({
    version: () => ({
      Browser: "node.js/v24",
      "Protocol-Version": "1.1",
    }),
    targets: (port) => [{
      id: browserId,
      type: "node",
      webSocketDebuggerUrl: `ws://127.0.0.1:${port}/${browserId}`,
    }],
  }, async (port) => {
    const result = await fetchInspectorMetadata(port);
    assert.equal(result.browserId, browserId);
    assert.equal(result.pageId, browserId);
  });
});

test("port reuse by another PID or a non-loopback listener fails closed", () => {
  assert.throws(
    () => assertPortOwner([{
      localAddress: "127.0.0.1",
      localPort: 9229,
      owningProcessId: 41,
    }], 42),
    (error) => error.code === "protocol_rejected");

  assert.throws(
    () => assertPortOwner([{
      localAddress: "0.0.0.0",
      localPort: 9229,
      owningProcessId: 42,
    }], 42),
    (error) => error.code === "protocol_rejected");
});

async function withFakeInspector(configuration, callback) {
  const server = http.createServer((request, response) => {
    const address = server.address();
    const port = typeof address === "object" && address ? address.port : 0;
    response.writeHead(200, { "Content-Type": "application/json" });
    if (request.url === "/json/version") {
      response.end(JSON.stringify(configuration.version(port)));
    } else if (request.url === "/json/list") {
      response.end(JSON.stringify(configuration.targets(port)));
    } else {
      response.writeHead(404);
      response.end("{}");
    }
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
