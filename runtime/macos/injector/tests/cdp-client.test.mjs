import assert from "node:assert/strict";
import test from "node:test";
import {
  buildMainExpression,
  requestClose,
  runInspectorSession,
  schemaDocument,
  validateAggregate,
  validateClassification,
  validateRequest,
  validateWebSocket,
} from "../cdp-client.mjs";
import {
  buildRendererExpression,
  expectedAppliedManagedResourceCount,
  runtimeIdentifiers,
  validateTheme,
} from "../renderer-runtime.mjs";

const theme = {
  schemaVersion: 1,
  themeId: "5be08c24-d21f-4db0-bdb0-d6d4cc779d7b",
  variant: "dark",
  palette: {
    background: "#111111",
    panel: "#181818",
    accent: "#2563EB",
    text: "#F5F5F5",
    muted: "#A3A3A3",
    border: "#303030",
  },
};

test("accepts only the palette projection", () => {
  assert.equal(validateTheme(theme), theme);
  assert.throws(() => validateTheme({ ...theme, css: "body{}" }));
  assert.throws(() => validateTheme({ ...theme, url: "https://fixture.invalid" }));
  assert.throws(() => validateTheme({
    ...theme,
    palette: { ...theme.palette, accent: "red" },
  }));
});

test("request rejects extra fields and themes on cleanup", () => {
  const value = {
    command: "apply",
    host: "127.0.0.1",
    port: 9229,
    processId: 42,
    theme,
  };
  assert.equal(validateRequest(value), value);
  assert.throws(() => validateRequest({ ...value, argv: [] }));
  assert.throws(() => validateRequest({ ...value, host: "0.0.0.0" }));
  assert.throws(() => validateRequest({ ...value, command: "cleanup" }));
});

test("inspect and cleanup require an explicit null theme", () => {
  for (const command of ["inspect", "cleanup"]) {
    const value = {
      command,
      host: "127.0.0.1",
      port: 9229,
      processId: 42,
      theme: null,
    };
    assert.equal(validateRequest(value), value);
    const { theme: _, ...missingTheme } = value;
    assert.throws(
      () => validateRequest(missingTheme),
      (error) =>
        error.code === "protocol.request_invalid" &&
        error.stage === "request");
    assert.throws(
      () => validateRequest({ ...value, theme }),
      (error) =>
        error.code === "protocol.request_invalid" &&
        error.stage === "request");
  }
});

test("websocket validation requires loopback, UUID, and no query", () => {
  const id = "12345678-1234-1234-1234-123456789abc";
  assert.equal(
    validateWebSocket(`ws://127.0.0.1:9229/${id}`, "127.0.0.1", 9229, id)
      .pathname,
    `/${id}`);
  assert.throws(() =>
    validateWebSocket(`ws://0.0.0.0:9229/${id}`, "127.0.0.1", 9229, id));
  assert.throws(() =>
    validateWebSocket(`ws://127.0.0.1:9229/${id}?secret=1`, "127.0.0.1", 9229, id));
});

test("expressions contain fixed identifiers and no private output keys", () => {
  const renderer = buildRendererExpression("apply", theme);
  const main = buildMainExpression("apply", theme);
  for (const value of Object.values(runtimeIdentifiers)) {
    assert.match(renderer, new RegExp(value.replaceAll(".", "\\."), "u"));
  }
  for (const denied of [
    "commandLine", "environment", "conversation", "credential", "token",
  ]) {
    assert.doesNotMatch(main, new RegExp(`"${denied}"\\s*:`, "iu"));
  }
});

test("renderer expressions are syntactically valid", () => {
  for (const mode of ["inspect", "apply", "cleanup"]) {
    const expression = buildRendererExpression(
      mode,
      mode === "apply" ? theme : null);
    assert.doesNotThrow(() => new Function(`return ${expression};`));
  }
});

test("real renderer apply has the fixed active count and cleanup reaches zero", () => {
  const fixture = createRendererDocumentFixture();
  const originalDocument = globalThis.document;
  const originalComputedStyle = globalThis.getComputedStyle;
  globalThis.document = fixture.document;
  globalThis.getComputedStyle = () => ({
    backgroundColor: "rgb(17, 17, 17)",
    color: "rgb(245, 245, 245)",
  });
  try {
    const applied = eval(buildRendererExpression("apply", theme));
    assert.equal(applied.applyVerified, true);
    assert.equal(
      applied.residualCount,
      expectedAppliedManagedResourceCount);
    const cleaned = eval(buildRendererExpression("cleanup", null));
    assert.equal(cleaned.cleanupVerified, true);
    assert.equal(cleaned.residualCount, 0);
  } finally {
    globalThis.document = originalDocument;
    globalThis.getComputedStyle = originalComputedStyle;
  }
});

test("apply targets only eligible and inspects overlay before and after", async () => {
  const fixture = await evaluateWindowExpression("apply");

  assert.equal(fixture.value.eligibleWindowCount, 1);
  assert.equal(fixture.value.overlayWindowCount, 1);
  assert.equal(fixture.value.overlayResidualCount, 0);
  assert.doesNotThrow(() => validateAggregate(fixture.value, "apply"));
  assert.deepEqual(
    fixture.calls.map((call) => [call.kind, call.expression]),
    [
      ["overlay", buildRendererExpression("inspect", null)],
      ["eligible", buildRendererExpression("apply", theme)],
      ["overlay", buildRendererExpression("inspect", null)],
    ]);
});

test("apply fails closed when overlay inspection reports residue", async () => {
  const fixture = await evaluateWindowExpression(
    "apply",
    { overlayResidualCount: 1 });

  assert.throws(
    () => validateAggregate(fixture.value, "apply"),
    (error) =>
      error.code === "renderer.overlay_modified" &&
      error.stage === "renderer");
  assert.deepEqual(
    fixture.calls.map((call) => call.kind),
    ["overlay"]);
});

test("post-apply overlay residue prevents an apply success result", async () => {
  const fixture = await evaluateWindowExpression(
    "apply",
    { overlayResidualCounts: [0, 1] });

  assert.throws(
    () => validateAggregate(fixture.value, "apply"),
    (error) =>
      error.code === "renderer.overlay_modified" &&
      error.stage === "renderer");
  assert.deepEqual(
    fixture.calls.map((call) => call.kind),
    ["overlay", "eligible", "overlay"]);
});

test("classification shape and overlay residue use distinct errors", () => {
  const validClassification = {
    eligibleWindowCount: 1,
    overlayWindowCount: 1,
    unknownWindowCount: 0,
    overlayResidualCount: 1,
  };
  assert.doesNotThrow(() => validateClassification(validClassification));
  assert.throws(
    () => validateAggregate({
      ...validClassification,
      residualCount: 7,
      appliedWindowCount: 1,
      applyVerified: true,
      cleanupVerified: false,
      visualEffectApplied: true,
    }, "apply"),
    (error) => error.code === "renderer.overlay_modified");
  assert.throws(
    () => validateClassification({
      ...validClassification,
      unknownWindowCount: "invalid",
    }),
    (error) => error.code === "inspector.classification_invalid");
});

test("cleanup targets eligible and overlay and proves zero residue", async () => {
  const fixture = await evaluateWindowExpression("cleanup");

  assert.equal(fixture.value.overlayResidualCount, 0);
  assert.doesNotThrow(() => validateAggregate(fixture.value, "cleanup"));
  assert.deepEqual(
    fixture.calls.map((call) => [call.kind, call.expression]),
    [
      ["eligible", buildRendererExpression("cleanup", null)],
      ["overlay", buildRendererExpression("cleanup", null)],
    ]);
});

test("cleanup after partial apply clears product residue in eligible and overlay", async () => {
  const fixture = await evaluateCleanupWithManagedResidue();

  assert.deepEqual(fixture.before, { eligible: 7, overlay: 5 });
  assert.deepEqual(fixture.after, { eligible: 0, overlay: 0 });
  assert.doesNotThrow(() => validateAggregate(fixture.value, "cleanup"));
  assert.deepEqual(
    fixture.calls.map((call) => [call.kind, call.expression]),
    [
      ["eligible", buildRendererExpression("cleanup", null)],
      ["overlay", buildRendererExpression("cleanup", null)],
    ]);
});

test("cleanup expression is scoped to fixed product identifiers", () => {
  const expression = buildRendererExpression("cleanup", null);
  for (const value of Object.values(runtimeIdentifiers)) {
    assert.match(expression, new RegExp(value.replaceAll(".", "\\."), "u"));
  }
  for (const variable of [
    "--cts-background",
    "--cts-panel",
    "--cts-accent",
    "--cts-text",
    "--cts-muted",
    "--cts-border",
  ]) {
    assert.match(expression, new RegExp(variable, "u"));
  }
  assert.doesNotMatch(
    expression,
    /querySelectorAll\(["']\*["']\)|replaceChildren|innerHTML\s*=/u);
});

test("schema is finite and declares only fixed commands", () => {
  assert.deepEqual(
    schemaDocument().commands,
    ["inspect", "apply", "apply-cleanup", "cleanup"]);
  assert.equal(schemaDocument().maximumInputBytes, 64 * 1024);
  assert.equal(schemaDocument().maximumResponseBytes, 128 * 1024);
});

test("close request uses non-awaiting evaluation and tolerates disconnect", async () => {
  const originalWebSocket = globalThis.WebSocket;
  const sent = [];
  globalThis.WebSocket = class {
    listeners = new Map();
    addEventListener(name, callback) {
      this.listeners.set(name, callback);
      if (name === "open") queueMicrotask(() => callback());
    }
    send(value) {
      sent.push(JSON.parse(value));
      queueMicrotask(() => this.listeners.get("close")?.());
    }
    close() {}
  };
  try {
    await requestClose("ws://127.0.0.1:9229/fixture");
    assert.equal(sent.length, 1);
    assert.equal(sent[0].method, "Runtime.evaluate");
    assert.equal(sent[0].params.expression, "process._debugEnd()");
    assert.equal(sent[0].params.awaitPromise, false);
  } finally {
    globalThis.WebSocket = originalWebSocket;
  }
});

test("session preserves operation errors when close succeeds", async () => {
  const operationError = Object.assign(new Error(), {
    code: "renderer.proof_invalid",
    stage: "renderer",
  });
  await assert.rejects(
    runInspectorSession(validRequest(), {
      fetchMetadata: async () => ({ webSocketUrl: "ws://fixture" }),
      runOperation: async () => { throw operationError; },
      requestClose: async () => {},
    }),
    (error) => error === operationError);
});

test("session prioritizes close failure over an operation failure", async () => {
  const closeError = Object.assign(new Error(), {
    code: "inspector.close_request_failed",
    stage: "close",
  });
  await assert.rejects(
    runInspectorSession(validRequest(), {
      fetchMetadata: async () => ({ webSocketUrl: "ws://fixture" }),
      runOperation: async () => {
        throw Object.assign(new Error(), {
          code: "renderer.proof_invalid",
          stage: "renderer",
        });
      },
      requestClose: async () => { throw closeError; },
    }),
    (error) => error === closeError);
});

test("metadata failure is distinct and does not invent a close proof", async () => {
  let closeCalled = false;
  const metadataError = Object.assign(new Error(), {
    code: "inspector.metadata_invalid",
    stage: "metadata",
  });
  await assert.rejects(
    runInspectorSession(validRequest(), {
      fetchMetadata: async () => { throw metadataError; },
      runOperation: async () => assert.fail("operation must not run"),
      requestClose: async () => { closeCalled = true; },
    }),
    (error) => error === metadataError);
  assert.equal(closeCalled, false);
});

function validRequest() {
  return {
    command: "apply",
    host: "127.0.0.1",
    port: 9229,
    processId: 42,
    theme,
  };
}

function createRendererDocumentFixture() {
  const elements = [];
  const classes = new Set();
  const properties = new Map();
  const root = {
    classList: {
      contains: (value) => classes.has(value),
      add: (value) => classes.add(value),
      remove: (value) => classes.delete(value),
    },
    style: {
      getPropertyValue: (value) => properties.get(value) ?? "",
      setProperty: (name, value) => properties.set(name, value),
      removeProperty: (name) => properties.delete(name),
    },
  };
  const createElement = (tagName) => ({
    tagName,
    id: "",
    textContent: "",
    attributes: new Map(),
    setAttribute(name, value) {
      this.attributes.set(name, value);
    },
    remove() {
      const index = elements.indexOf(this);
      if (index >= 0) elements.splice(index, 1);
    },
  });
  return {
    document: {
      documentElement: root,
      head: { append: (...items) => elements.push(...items) },
      createElement,
      querySelectorAll: (selector) =>
        elements.filter((item) => selector === `#${item.id}`),
    },
  };
}

async function evaluateWindowExpression(
  mode,
  {
    overlayResidualCount = 0,
    overlayResidualCounts = null,
  } = {}
) {
  const calls = [];
  let overlayCallIndex = 0;
  const originalProcess = globalThis.process;
  const makeWindow = (kind) => ({
    webContents: {
      getURL: () => kind === "eligible"
        ? "app://fixture/main"
        : "app://fixture/avatar-overlay",
      executeJavaScript: async (expression) => {
        calls.push({ kind, expression });
        if (mode === "cleanup") {
          return {
            residualCount: 0,
            applyVerified: false,
            cleanupVerified: true,
            visualEffectApplied: false,
          };
        }
        if (kind === "overlay") {
          const currentOverlayResidualCount =
            overlayResidualCounts?.[overlayCallIndex++] ??
            overlayResidualCount;
          return {
            residualCount: currentOverlayResidualCount,
            applyVerified: false,
            cleanupVerified: currentOverlayResidualCount === 0,
            visualEffectApplied: false,
          };
        }
        return {
          residualCount: expectedAppliedManagedResourceCount,
          applyVerified: true,
          cleanupVerified: false,
          visualEffectApplied: true,
        };
      },
    },
  });
  globalThis.process = {
    mainModule: {
      require: () => ({
        BrowserWindow: {
          getAllWindows: () => [
            makeWindow("eligible"),
            makeWindow("overlay"),
          ],
        },
      }),
    },
  };
  try {
    const value = await eval(buildMainExpression(
      mode,
      mode === "apply" ? theme : null));
    return { calls, value };
  } finally {
    globalThis.process = originalProcess;
  }
}

async function evaluateCleanupWithManagedResidue() {
  const calls = [];
  const managedResidue = { eligible: 7, overlay: 5 };
  const before = { ...managedResidue };
  const cleanupExpression = buildRendererExpression("cleanup", null);
  const originalProcess = globalThis.process;
  const makeWindow = (kind) => ({
    webContents: {
      getURL: () => kind === "eligible"
        ? "app://fixture/main"
        : "app://fixture/avatar-overlay",
      executeJavaScript: async (expression) => {
        calls.push({ kind, expression });
        assert.equal(expression, cleanupExpression);
        managedResidue[kind] = 0;
        return {
          residualCount: managedResidue[kind],
          applyVerified: false,
          cleanupVerified: managedResidue[kind] === 0,
          visualEffectApplied: false,
        };
      },
    },
  });
  globalThis.process = {
    mainModule: {
      require: () => ({
        BrowserWindow: {
          getAllWindows: () => [
            makeWindow("eligible"),
            makeWindow("overlay"),
          ],
        },
      }),
    },
  };
  try {
    const value = await eval(buildMainExpression("cleanup", null));
    return {
      before,
      after: { ...managedResidue },
      calls,
      value,
    };
  } finally {
    globalThis.process = originalProcess;
  }
}
