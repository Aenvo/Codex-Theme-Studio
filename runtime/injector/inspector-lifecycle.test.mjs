import assert from "node:assert/strict";
import net from "node:net";
import test from "node:test";
import {
  isLoopbackPortOpen,
  openInspector,
  waitForInspectorClosed,
} from "./inspector-lifecycle.mjs";

test("requests Inspector before a slow authoritative owner lookup", async () => {
  const calls = [];
  let currentTime = 0;
  let requested = false;
  const metadata = { webSocketUrl: "ws://127.0.0.1:9229/test" };

  const result = await openInspector({
    processId: 42,
    initialListeners: [],
    port: 9229,
    timeoutMs: 6000,
    now: () => currentTime,
    wait: async () => {},
    requestOpen: () => {
      calls.push("request-open");
      requested = true;
    },
    isPortOpen: async () => {
      calls.push("port-open");
      return requested;
    },
    getPortListeners: async () => {
      calls.push("owner-lookup");
      currentTime += 7000;
      return [{
        localAddress: "127.0.0.1",
        localPort: 9229,
        owningProcessId: 42,
      }];
    },
    assertPortOwner: () => calls.push("owner-valid"),
    fetchMetadata: async () => {
      calls.push("metadata");
      return metadata;
    },
  });

  assert.equal(result, metadata);
  assert.deepEqual(calls, [
    "request-open",
    "port-open",
    "owner-lookup",
    "owner-valid",
    "metadata",
  ]);
});

test("does not fetch metadata when the listening port has a foreign owner", async () => {
  let metadataFetched = false;

  await assert.rejects(openInspector({
    processId: 42,
    initialListeners: [],
    port: 9229,
    timeoutMs: 6000,
    requestOpen: () => {},
    isPortOpen: async () => true,
    getPortListeners: async () => [{
      localAddress: "127.0.0.1",
      localPort: 9229,
      owningProcessId: 41,
    }],
    assertPortOwner: () => {
      throw Object.assign(new Error("port_in_use"), {
        code: "protocol_rejected",
        retryable: false,
      });
    },
    fetchMetadata: async () => {
      metadataFetched = true;
    },
  }), (error) => error.code === "protocol_rejected");

  assert.equal(metadataFetched, false);
});

test("closed-port settling performs one final authoritative lookup", async () => {
  let currentTime = 0;
  let ownerLookups = 0;

  await waitForInspectorClosed({
    processId: 42,
    port: 9229,
    timeoutMs: 12000,
    settleMs: 3000,
    retryDelayMs: 1000,
    now: () => currentTime,
    wait: async (milliseconds) => {
      currentTime += milliseconds;
    },
    isPortOpen: async () => false,
    getPortListeners: async () => {
      ownerLookups += 1;
      return [];
    },
    assertPortOwner: () => assert.fail("closed port has no owner"),
  });

  assert.equal(ownerLookups, 1);
  assert.equal(currentTime, 3000);
});

test("close timeout rejects a listener that changed to a foreign owner", async () => {
  let currentTime = 0;

  await assert.rejects(waitForInspectorClosed({
    processId: 42,
    port: 9229,
    timeoutMs: 200,
    settleMs: 0,
    retryDelayMs: 100,
    now: () => currentTime,
    wait: async (milliseconds) => {
      currentTime += milliseconds;
    },
    isPortOpen: async () => true,
    getPortListeners: async () => [{
      localAddress: "127.0.0.1",
      localPort: 9229,
      owningProcessId: 41,
    }],
    assertPortOwner: () => {
      throw Object.assign(new Error("port_in_use"), {
        code: "protocol_rejected",
        retryable: false,
      });
    },
  }), (error) => error.code === "protocol_rejected");
});

test("loopback port probe distinguishes listening and closed ports", async () => {
  const server = net.createServer();
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  const address = server.address();
  assert.equal(typeof address, "object");
  const port = address.port;

  try {
    assert.equal(await isLoopbackPortOpen(port), true);
  } finally {
    await new Promise((resolve, reject) =>
      server.close((error) => error ? reject(error) : resolve()));
  }

  assert.equal(await isLoopbackPortOpen(port), false);
});
