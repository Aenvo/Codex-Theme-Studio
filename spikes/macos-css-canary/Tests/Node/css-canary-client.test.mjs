import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import {
  buildStartExpression,
  createBlobLifecycle,
  rendererTransactionSource,
  revokeBlobLifecycle,
  validateResult,
} from "../../Sources/CodexCSSCanaryCore/css-canary-client.mjs";

const helperPath = fileURLToPath(
  new URL("../../Sources/CodexCSSCanaryCore/css-canary-client.mjs", import.meta.url));

function goodResult() {
  return {
    initialResidualDetected: false,
    diagnosticOnly: false,
    cleanupOnly: false,
    eligibleWindowCount: 1,
    overlayWindowCount: 1,
    unknownWindowCount: 0,
    overlayUnmodified: true,
    applyAttempted: true,
    applyVerified: true,
    cleanupVerified: true,
    visualEffectApplied: true,
    blobContentVerified: true,
    blobURLCreated: true,
    blobFetchAllowed: false,
    blobXHRAllowed: false,
    blobFetchBlockedByPolicy: true,
    blobPolicyEventObserved: true,
    blobPolicyDirectiveMatched: true,
    blobPolicyBlockedURIExact: true,
    blobPolicyBlockedURISchemeOnly: false,
    blobPolicyBlockedURIEmpty: false,
    blobPolicyBlockedURIOther: false,
    blobFetchRejectedTypeError: true,
    blobFetchRejectedDOMException: false,
    blobFetchRejectedOther: false,
    blobXHRLoad: false,
    blobXHRError: true,
    blobXHRTimeout: false,
    blobXHRAbort: false,
    blobXHRStatusZero: true,
    blobRevokeInvoked: true,
    postRevokeDereferenceRejected: true,
    blobReferenceCleared: true,
    policyListenerRemoved: true,
    styleCountAfterApply: 1,
    rootClassCountAfterApply: 1,
    markerCountAfterApply: 1,
    stateCountAfterApply: 1,
    cssVariablePresentAfterApply: true,
    finalResidualCount: 0,
    timerCancelled: true,
    mainJobCleared: true,
  };
}

test("accepts only the complete apply and cleanup proof", () => {
  assert.doesNotThrow(() => validateResult(goodResult(), "apply-cleanup"));
  for (const [key, code] of [
    ["overlayUnmodified", "canary.overlay_modified"],
    ["applyVerified", "canary.apply_not_verified"],
    ["cleanupVerified", "canary.cleanup_not_verified"],
    ["visualEffectApplied", "canary.visual_effect_not_observed"],
    ["blobContentVerified", "canary.blob_content_invalid"],
    ["blobURLCreated", "canary.blob_url_invalid"],
    ["blobRevokeInvoked", "canary.blob_revoke_not_invoked"],
    ["postRevokeDereferenceRejected", "canary.blob_post_revoke_accessible"],
    ["blobReferenceCleared", "canary.blob_reference_not_cleared"],
    ["policyListenerRemoved", "canary.policy_listener_not_removed"],
    ["timerCancelled", "canary.timer_not_cancelled"],
    ["mainJobCleared", "canary.main_job_not_cleared"],
  ]) {
    const invalid = { ...goodResult(), [key]: false };
    assert.throws(
      () => validateResult(invalid, "apply-cleanup"),
      (error) => error.code === code);
  }
  for (const [key, code] of [
    ["styleCountAfterApply", "canary.style_count_invalid"],
    ["rootClassCountAfterApply", "canary.root_class_count_invalid"],
    ["markerCountAfterApply", "canary.marker_count_invalid"],
    ["stateCountAfterApply", "canary.state_count_invalid"],
  ]) {
    const invalid = { ...goodResult(), [key]: 0 };
    assert.throws(
      () => validateResult(invalid, "apply-cleanup"),
      (error) => error.code === code);
  }
});

test("accepts cleanup-only zero-residual evidence", () => {
  const value = {
    ...goodResult(),
    initialResidualDetected: true,
    cleanupOnly: true,
    applyAttempted: false,
    applyVerified: false,
    visualEffectApplied: false,
    blobContentVerified: false,
    blobURLCreated: false,
    blobFetchAllowed: false,
    blobFetchBlockedByPolicy: false,
    styleCountAfterApply: 0,
    rootClassCountAfterApply: 0,
    markerCountAfterApply: 0,
    stateCountAfterApply: 0,
    cssVariablePresentAfterApply: false,
  };
  assert.doesNotThrow(() => validateResult(value, "cleanup"));
});

test("accepts a directly fetched four-byte Blob lifecycle", async () => {
  const listeners = new Set();
  const created = await createBlobLifecycle(blobFixture({
    listeners,
    fetchBytes: async () => new Uint8Array([0x43, 0x54, 0x53, 0x36]),
  }));
  assert.deepEqual(created.evidence, {
    blobContentVerified: true,
    blobURLCreated: true,
    blobFetchAllowed: true,
    blobXHRAllowed: false,
    blobFetchBlockedByPolicy: false,
    blobPolicyEventObserved: false,
    blobPolicyDirectiveMatched: false,
    blobPolicyBlockedURIExact: false,
    blobPolicyBlockedURISchemeOnly: false,
    blobPolicyBlockedURIEmpty: false,
    blobPolicyBlockedURIOther: false,
    blobFetchRejectedTypeError: false,
    blobFetchRejectedDOMException: false,
    blobFetchRejectedOther: false,
    blobXHRLoad: false,
    blobXHRError: false,
    blobXHRTimeout: false,
    blobXHRAbort: false,
    blobXHRStatusZero: false,
    policyListenerRemoved: true,
  });
  assert.equal(listeners.size, 0);
  const state = { blobUrl: created.blobUrl };
  const revoked = await revokeBlobLifecycle(state, {
    revokeObjectURL() {},
    async fetchURL() { throw new Error("revoked"); },
  });
  assert.deepEqual(revoked, {
    blobRevokeInvoked: true,
    postRevokeDereferenceRejected: true,
    blobReferenceCleared: true,
  });
});

test("classifies an exact connect-src policy block without exposing its URL", async () => {
  const listeners = new Set();
  const url = "blob:fixture/one";
  const created = await createBlobLifecycle(blobFixture({
    listeners,
    objectURL: url,
    fetchBytes: async () => {
      for (const listener of listeners) {
        listener({ blockedURI: url, effectiveDirective: "connect-src" });
      }
      throw new Error("policy");
    },
  }));
  assert.equal(created.evidence.blobFetchAllowed, false);
  assert.equal(created.evidence.blobFetchBlockedByPolicy, true);
  assert.equal(created.evidence.policyListenerRemoved, true);
  assert.equal(listeners.size, 0);
});

test("uses XHR as a diagnostic fallback when Fetch fails", async () => {
  const listeners = new Set();
  const created = await createBlobLifecycle(blobFixture({
    listeners,
    xhrOutcome: async () => ({
      terminal: "load",
      statusZero: true,
      bytes: new Uint8Array([0x43, 0x54, 0x53, 0x36]),
    }),
  }));
  assert.equal(created.evidence.blobFetchAllowed, false);
  assert.equal(created.evidence.blobXHRAllowed, true);
  assert.equal(created.evidence.blobXHRLoad, true);
  assert.equal(created.evidence.blobXHRStatusZero, true);
  assert.equal(created.evidence.blobFetchBlockedByPolicy, false);
  assert.equal(created.evidence.policyListenerRemoved, true);
  assert.equal(listeners.size, 0);
});

test("accepts only exact or scheme-only policy evidence without ambiguity", async () => {
  for (const [blockedURI, key, accepted] of [
    ["blob:", "blobPolicyBlockedURISchemeOnly", true],
    ["", "blobPolicyBlockedURIEmpty", false],
    ["redacted", "blobPolicyBlockedURIOther", false],
  ]) {
    const listeners = new Set();
    const created = await createBlobLifecycle(blobFixture({
      listeners,
      fetchBytes: async () => {
        for (const listener of listeners) {
          listener({ blockedURI, effectiveDirective: "connect-src" });
        }
        throw Object.assign(new Error(), { name: "TypeError" });
      },
    }));
    assert.equal(created.evidence.blobPolicyEventObserved, true);
    assert.equal(created.evidence.blobPolicyDirectiveMatched, true);
    assert.equal(created.evidence[key], true);
    assert.equal(created.evidence.blobFetchBlockedByPolicy, accepted);
    assert.equal(created.evidence.blobFetchRejectedTypeError, true);
  }
});

test("rejects scheme-only evidence for an unrelated CSP directive", async () => {
  const listeners = new Set();
  const created = await createBlobLifecycle(blobFixture({
    listeners,
    fetchBytes: async () => {
      for (const listener of listeners) {
        listener({ blockedURI: "blob:", effectiveDirective: "img-src" });
      }
      throw Object.assign(new Error(), { name: "TypeError" });
    },
  }));
  assert.equal(created.evidence.blobPolicyEventObserved, true);
  assert.equal(created.evidence.blobPolicyDirectiveMatched, false);
  assert.equal(created.evidence.blobPolicyBlockedURISchemeOnly, true);
  assert.equal(created.evidence.blobFetchBlockedByPolicy, false);
});

test("rejects mixed scheme-only and ambiguous policy evidence", async () => {
  const listeners = new Set();
  const created = await createBlobLifecycle(blobFixture({
    listeners,
    fetchBytes: async () => {
      for (const listener of listeners) {
        listener({ blockedURI: "blob:", effectiveDirective: "connect-src" });
        listener({ blockedURI: "redacted", effectiveDirective: "connect-src" });
      }
      throw Object.assign(new Error(), { name: "TypeError" });
    },
  }));
  assert.equal(created.evidence.blobPolicyBlockedURISchemeOnly, true);
  assert.equal(created.evidence.blobPolicyBlockedURIOther, true);
  assert.equal(created.evidence.blobFetchBlockedByPolicy, false);
});

test("fails closed when a Blob fetch failure has no matching policy event", async () => {
  const value = goodResult();
  value.blobFetchAllowed = false;
  value.blobFetchBlockedByPolicy = false;
  assert.throws(
    () => validateResult(value, "apply-cleanup"),
    (error) => error.code === "canary.blob_fetch_failure_unclassified");
});

test("reports invalid bytes, URL scheme, revoke failure, and retained references", async () => {
  const wrongBytes = await createBlobLifecycle(blobFixture({
    listeners: new Set(),
    readBlob: async () => new Uint8Array([0, 0, 0, 0]),
  }));
  assert.equal(wrongBytes.evidence.blobContentVerified, false);

  const wrongURL = await createBlobLifecycle(blobFixture({
    listeners: new Set(),
    objectURL: "https://fixture.invalid/value",
  }));
  assert.equal(wrongURL.evidence.blobURLCreated, false);

  const state = { blobUrl: "blob:fixture/two" };
  const failedRevoke = await revokeBlobLifecycle(state, {
    revokeObjectURL() { throw new Error("revoke"); },
    async fetchURL() {},
  });
  assert.equal(failedRevoke.blobRevokeInvoked, false);
  assert.equal(failedRevoke.postRevokeDereferenceRejected, false);
  assert.equal(failedRevoke.blobReferenceCleared, true);
});

test("diagnostic mode reports Fetch, XHR, and CSP booleans without Apply", () => {
  const value = {
    ...goodResult(),
    diagnosticOnly: true,
    applyAttempted: false,
    applyVerified: false,
    visualEffectApplied: false,
    blobFetchAllowed: false,
    blobXHRAllowed: false,
    blobFetchBlockedByPolicy: false,
    styleCountAfterApply: 0,
    rootClassCountAfterApply: 0,
    markerCountAfterApply: 0,
    stateCountAfterApply: 0,
    cssVariablePresentAfterApply: false,
  };
  assert.doesNotThrow(() => validateResult(value, "blob-diagnostic"));
  assert.throws(
    () => validateResult(
      { ...value, styleCountAfterApply: 1 },
      "blob-diagnostic"),
    (error) => error.code === "canary.diagnostic_mutation_detected");
});

test("source fixes identifiers and four-byte blob without private output keys", async () => {
  const source = await readFile(helperPath, "utf8");
  for (const marker of [
    "codex-theme-studio-macos-css-canary-v1",
    "codex-theme-studio-macos-css-canary-v1-active",
    "codex-theme-studio-macos-css-canary-v1-marker",
    "--codex-theme-studio-macos-css-canary-v1-color",
    "0x43, 0x54, 0x53, 0x36",
    "box-shadow:inset 0 0 0 2px var(",
    "pointer-events:none !important",
  ]) {
    assert.match(source, new RegExp(marker.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")));
  }
  for (const forbidden of [
    "commandLine:", "environment:", "targetId:",
    "conversation:", "credential:", "token:", "dom:",
  ]) {
    assert.equal(source.includes(forbidden), false);
  }
  const output = JSON.stringify(goodResult()).toLowerCase();
  assert.equal(output.includes("\"bloburl\""), false);
});

test("fixed main and renderer expressions are syntactically valid", () => {
  const cleanupExpression = buildStartExpression("cleanup");
  assert.doesNotThrow(() =>
    new Function(`return (${rendererTransactionSource});`));
  assert.doesNotThrow(() =>
    new Function(`return (${buildStartExpression("apply-cleanup")});`));
  assert.doesNotThrow(() =>
    new Function(`return (${cleanupExpression});`));
  assert.doesNotThrow(() =>
    new Function(`return (${buildStartExpression("blob-diagnostic")});`));
  assert.match(cleanupExpression, /blobXHRAllowed: false/);
});

function blobFixture({
  listeners,
  objectURL = "blob:fixture/value",
  readBlob = async (blob) => new Uint8Array(await blob.arrayBuffer()),
  fetchBytes = async () => {
    throw new Error("unclassified");
  },
  xhrOutcome = async () => ({
    terminal: "error",
    statusZero: true,
    bytes: null,
  }),
}) {
  return {
    createBlob: (bytes) => new Blob([new Uint8Array(bytes)]),
    readBlob,
    createObjectURL: () => objectURL,
    parseProtocol: (value) => new URL(value).protocol,
    fetchBytes,
    xhrOutcome,
    addPolicyListener: (listener) => listeners.add(listener),
    removePolicyListener: (listener) => listeners.delete(listener),
    classifyPolicyEvent: (event, value) => {
      const blocked = typeof event?.blockedURI === "string"
        ? event.blockedURI
        : "";
      return {
        observed: true,
        directiveMatched:
          ["connect-src", "default-src"].includes(event.effectiveDirective),
        blockedURIExact: blocked === value,
        blockedURISchemeOnly: blocked === "blob:" || blocked === "blob",
        blockedURIEmpty: blocked.length === 0,
        blockedURIOther:
          blocked.length !== 0 && blocked !== value &&
          blocked !== "blob:" && blocked !== "blob",
      };
    },
    classifyFetchFailure: (error) => ({
      typeError: error?.name === "TypeError",
      domException: error?.name === "SecurityError",
      other:
        error?.name !== "TypeError" && error?.name !== "SecurityError",
    }),
    waitForPolicyEvent: async () => {},
  };
}
