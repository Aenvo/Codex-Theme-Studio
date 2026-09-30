import assert from "node:assert/strict";
import test from "node:test";
import {
  executeRendererPortExpression,
  probeRendererPort,
  validateBrowserMetadata,
} from "./cdp-port.mjs";

const browserId = "d23f7f83-879d-4d18-9d71-5446465f0cf5";

test("port probe qualifies an app renderer and removes the canary", async () => {
  const client = new FakeClient();
  const result = await probeRendererPort(49152, {
    fetchMetadata: async () => ({
      browserId,
      webSocketUrl: `ws://127.0.0.1:49152/devtools/browser/${browserId}`,
    }),
    connectClient: async () => client,
  });

  assert.equal(result.browserId, browserId);
  assert.equal(result.targetCount, 2);
  assert.equal(result.eligibleTargetCount, 1);
  assert.equal(result.canaryApplied, true);
  assert.equal(result.canaryCleaned, true);
  assert.deepEqual(result.routeTypes, ["app:root", "avatar-overlay"]);
  assert.equal(client.closed, true);
});

test("port probe waits for the main renderer DOM to become eligible", async () => {
  const client = new FakeClient({
    evaluations: [
      { qualified: false, applied: false, cleaned: true },
      { qualified: true, applied: true, cleaned: true },
    ],
  });
  const result = await probeRendererPort(49152, {
    fetchMetadata: async () => ({
      browserId,
      webSocketUrl: `ws://127.0.0.1:49152/devtools/browser/${browserId}`,
    }),
    connectClient: async () => client,
    retryDelayMs: 1,
  });

  assert.equal(result.eligibleTargetCount, 1);
  assert.equal(result.canaryApplied, true);
  assert.equal(client.targetRequests, 2);
});

test("port probe ignores an unqualified auxiliary app window", async () => {
  const client = new FakeClient({
    targets: [
      { targetId: "target-main", type: "page", url: "app://-/index.html" },
      {
        targetId: "target-detached",
        type: "page",
        url: "app://-/detached-window.html?initialRoute=%2Fdetached-window",
      },
    ],
    evaluationsByTarget: {
      "target-main": { qualified: true, applied: true, cleaned: true },
      "target-detached": { qualified: false, applied: false, cleaned: true },
    },
  });
  const result = await probeRendererPort(49152, {
    fetchMetadata: async () => ({
      browserId,
      webSocketUrl: `ws://127.0.0.1:49152/devtools/browser/${browserId}`,
    }),
    connectClient: async () => client,
  });

  assert.equal(result.targetCount, 2);
  assert.equal(result.eligibleTargetCount, 1);
  assert.equal(result.canaryApplied, true);
  assert.equal(result.canaryCleaned, true);
  assert.deepEqual(result.routeTypes, ["app:detached-window.html", "app:index.html"]);
});

test("browser metadata accepts only a loopback Chromium browser endpoint", () => {
  const result = validateBrowserMetadata({
    Browser: "Chrome/142.0.0.0",
    "Protocol-Version": "1.3",
    webSocketDebuggerUrl: `ws://127.0.0.1:49152/devtools/browser/${browserId}`,
  }, 49152);

  assert.deepEqual(result, {
    browserId,
    webSocketUrl: `ws://127.0.0.1:49152/devtools/browser/${browserId}`,
  });
  assert.throws(
    () => validateBrowserMetadata({
      Browser: "Chrome/142.0.0.0",
      "Protocol-Version": "1.3",
      webSocketDebuggerUrl: `ws://192.168.1.5:49152/devtools/browser/${browserId}`,
    }, 49152),
    error => error?.diagnosticCode === "invalid_browser_websocket");
});

test("port probe rejects invalid ports before connecting", async () => {
  await assert.rejects(
    () => probeRendererPort(80),
    error => error?.diagnosticCode === "port_invalid");
});

test("port expression returns only structurally qualified renderer results", async () => {
  const client = new FakeClient({
    targets: [
      { targetId: "target-main", type: "page", url: "app://-/index.html" },
      { targetId: "target-detached", type: "page", url: "app://-/detached-window.html" },
    ],
    evaluationsByTarget: {
      "target-main": {
        qualified: true,
        runtime: { runtimeVersion: 1, active: true },
      },
      "target-detached": { qualified: false },
    },
  });

  const result = await executeRendererPortExpression(49152, "({})", {
    fetchMetadata: async () => ({
      browserId,
      webSocketUrl: `ws://127.0.0.1:49152/devtools/browser/${browserId}`,
    }),
    connectClient: async () => client,
  });

  assert.equal(result.eligibleTargetCount, 1);
  assert.deepEqual(result.results, [{
    qualified: true,
    runtime: { runtimeVersion: 1, active: true },
  }]);
});

test("unqualified renderer reports only sanitized structural evidence", async () => {
  const client = new FakeClient({
    targets: [
      { targetId: "target-main", type: "page", url: "app://-/index.html?secret=ignored" },
    ],
    evaluationsByTarget: {
      "target-main": {
        qualified: false,
        reason: "shell-features-missing",
        pageMode: "home",
        features: {
          shell: true,
          sidebar: false,
          content: true,
          composer: true,
        },
        unsafe: "must-not-be-copied",
      },
    },
  });

  await assert.rejects(
    () => executeRendererPortExpression(49152, "({})", {
      timeoutMs: 0,
      fetchMetadata: async () => ({
        browserId,
        webSocketUrl: `ws://127.0.0.1:49152/devtools/browser/${browserId}`,
      }),
      connectClient: async () => client,
      retryDelayMs: 1,
    }),
    error => {
      assert.equal(error?.diagnosticCode, "port_renderer_unqualified");
      assert.deepEqual(error?.details, {
        targetCount: 1,
        candidateCount: 1,
        routeTypes: ["app:index.html"],
        evaluations: [{
          qualified: false,
          reason: "shell-features-missing",
          pageMode: "home",
          features: {
            shell: true,
            sidebar: false,
            content: true,
            composer: true,
          },
        }],
      });
      assert.equal(JSON.stringify(error.details).includes("secret"), false);
      assert.equal(JSON.stringify(error.details).includes("must-not-be-copied"), false);
      return true;
    });
});

class FakeClient {
  closed = false;
  targetRequests = 0;

  constructor({
    targets = [
      { targetId: "target-main", type: "page", url: "app://codex/" },
      {
        targetId: "target-overlay",
        type: "page",
        url: "app://codex/?initialRoute=%2Favatar-overlay",
      },
    ],
    evaluations = [],
    evaluationsByTarget = {},
  } = {}) {
    this.targets = targets;
    this.evaluations = [...evaluations];
    this.evaluationsByTarget = evaluationsByTarget;
    this.sessionTargets = new Map();
  }

  async request(method, params, sessionId) {
    if (method === "Target.getTargets") {
      this.targetRequests += 1;
      return { targetInfos: this.targets };
    }
    if (method === "Target.attachToTarget") {
      const attachedSessionId = `session-${params.targetId}`;
      this.sessionTargets.set(attachedSessionId, params.targetId);
      return { sessionId: attachedSessionId };
    }
    if (method === "Runtime.evaluate") {
      const targetId = this.sessionTargets.get(sessionId);
      const value = this.evaluations.length > 0
        ? this.evaluations.shift()
        : this.evaluationsByTarget[targetId] ??
          { qualified: true, applied: true, cleaned: true };
      return {
        result: {
          type: "object",
          value,
        },
      };
    }
    if (method === "Target.detachFromTarget") return {};
    throw new Error(`Unexpected method: ${method}`);
  }

  close() {
    this.closed = true;
  }
}
