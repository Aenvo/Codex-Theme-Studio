import fs from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";

const expectedPort = 9229;
const allowedHosts = new Set(["127.0.0.1", "::1"]);
const pollIntervalMilliseconds = 75;
const pollLimit = 54;
const jobSymbolName = "codex-theme-studio.macos-css-canary.main-job.v1";

export async function createBlobLifecycle(api) {
  const expected = [0x43, 0x54, 0x53, 0x36];
  let blob;
  let blobUrl = null;
  let blobContentVerified = false;
  let blobURLCreated = false;
  let blobFetchAllowed = false;
  let blobXHRAllowed = false;
  let blobFetchBlockedByPolicy = false;
  let blobPolicyEventObserved = false;
  let blobPolicyDirectiveMatched = false;
  let blobPolicyBlockedURIExact = false;
  let blobPolicyBlockedURISchemeOnly = false;
  let blobPolicyBlockedURIEmpty = false;
  let blobPolicyBlockedURIOther = false;
  let blobFetchRejectedTypeError = false;
  let blobFetchRejectedDOMException = false;
  let blobFetchRejectedOther = false;
  let blobXHRLoad = false;
  let blobXHRError = false;
  let blobXHRTimeout = false;
  let blobXHRAbort = false;
  let blobXHRStatusZero = false;
  let policyListenerRemoved = false;

  try {
    blob = api.createBlob(expected);
    const bytes = await api.readBlob(blob);
    blobContentVerified =
      bytes.length === expected.length &&
      expected.every((value, index) => bytes[index] === value);
  } catch {
    blobContentVerified = false;
  }

  try {
    blobUrl = api.createObjectURL(blob);
    blobURLCreated =
      typeof blobUrl === "string" && api.parseProtocol(blobUrl) === "blob:";
  } catch {
    blobUrl = null;
    blobURLCreated = false;
  }

  if (blobURLCreated) {
    const listener = (event) => {
      const classification = api.classifyPolicyEvent(event, blobUrl);
      blobPolicyEventObserved ||= classification.observed;
      blobPolicyDirectiveMatched ||= classification.directiveMatched;
      blobPolicyBlockedURIExact ||= classification.blockedURIExact;
      blobPolicyBlockedURISchemeOnly ||=
        classification.blockedURISchemeOnly;
      blobPolicyBlockedURIEmpty ||= classification.blockedURIEmpty;
      blobPolicyBlockedURIOther ||= classification.blockedURIOther;
    };
    try {
      api.addPolicyListener(listener);
      try {
        const bytes = await api.fetchBytes(blobUrl);
        blobFetchAllowed =
          bytes.length === expected.length &&
          expected.every((value, index) => bytes[index] === value);
      } catch (error) {
        const classification = api.classifyFetchFailure(error);
        blobFetchRejectedTypeError = classification.typeError;
        blobFetchRejectedDOMException = classification.domException;
        blobFetchRejectedOther = classification.other;
        try {
          const outcome = await api.xhrOutcome(blobUrl);
          blobXHRLoad = outcome.terminal === "load";
          blobXHRError = outcome.terminal === "error";
          blobXHRTimeout = outcome.terminal === "timeout";
          blobXHRAbort = outcome.terminal === "abort";
          blobXHRStatusZero = outcome.statusZero === true;
          const bytes = outcome.bytes;
          blobXHRAllowed =
            blobXHRLoad && bytes != null &&
            bytes.length === expected.length &&
            expected.every((value, index) => bytes[index] === value);
        } catch {
          blobXHRError = true;
        }
        await api.waitForPolicyEvent();
      }
      blobFetchBlockedByPolicy =
        blobPolicyEventObserved &&
        blobPolicyDirectiveMatched &&
        !blobPolicyBlockedURIEmpty &&
        !blobPolicyBlockedURIOther &&
        (blobPolicyBlockedURIExact || blobPolicyBlockedURISchemeOnly);
    } finally {
      try {
        api.removePolicyListener(listener);
        policyListenerRemoved = true;
      } catch {
        policyListenerRemoved = false;
      }
    }
  } else {
    policyListenerRemoved = true;
  }

  return {
    blobUrl,
    evidence: {
      blobContentVerified,
      blobURLCreated,
      blobFetchAllowed,
      blobXHRAllowed,
      blobFetchBlockedByPolicy,
      blobPolicyEventObserved,
      blobPolicyDirectiveMatched,
      blobPolicyBlockedURIExact,
      blobPolicyBlockedURISchemeOnly,
      blobPolicyBlockedURIEmpty,
      blobPolicyBlockedURIOther,
      blobFetchRejectedTypeError,
      blobFetchRejectedDOMException,
      blobFetchRejectedOther,
      blobXHRLoad,
      blobXHRError,
      blobXHRTimeout,
      blobXHRAbort,
      blobXHRStatusZero,
      policyListenerRemoved
    }
  };
}

export async function revokeBlobLifecycle(state, api) {
  const blobUrl = typeof state?.blobUrl === "string"
    ? state.blobUrl
    : null;
  if (state) state.blobUrl = null;
  if (blobUrl == null) {
    return {
      blobRevokeInvoked: true,
      postRevokeDereferenceRejected: true,
      blobReferenceCleared: state?.blobUrl == null
    };
  }

  let blobRevokeInvoked = false;
  let postRevokeDereferenceRejected = false;
  try {
    api.revokeObjectURL(blobUrl);
    blobRevokeInvoked = true;
  } catch {
    blobRevokeInvoked = false;
  }
  try {
    await api.fetchURL(blobUrl);
  } catch {
    postRevokeDereferenceRejected = true;
  }
  return {
    blobRevokeInvoked,
    postRevokeDereferenceRejected,
    blobReferenceCleared: state?.blobUrl == null
  };
}

export const rendererTransactionSource = String.raw`(async (mode) => {
  const createBlobLifecycle = ${createBlobLifecycle.toString()};
  const revokeBlobLifecycle = ${revokeBlobLifecycle.toString()};
  const styleId = "codex-theme-studio-macos-css-canary-v1";
  const rootClass = "codex-theme-studio-macos-css-canary-v1-active";
  const markerId = "codex-theme-studio-macos-css-canary-v1-marker";
  const variableName = "--codex-theme-studio-macos-css-canary-v1-color";
  const stateKey = Symbol.for("codex-theme-studio.macos-css-canary.v1");
  const root = document.documentElement;

  const inventory = () => {
    const state = globalThis[stateKey];
    return {
      style: document.querySelectorAll("#" + styleId).length,
      rootClass: root.classList.contains(rootClass) ? 1 : 0,
      marker: document.querySelectorAll("#" + markerId).length,
      cssVariable: root.style.getPropertyValue(variableName) ? 1 : 0,
      state: state ? 1 : 0,
      timer: state?.timerId != null ? 1 : 0,
      blob: typeof state?.blobUrl === "string" ? 1 : 0,
      policyListener: state?.policyListenerActive ? 1 : 0
    };
  };

  const residualCount = (value) =>
    value.style + value.rootClass + value.marker + value.cssVariable +
    value.state + value.timer + value.blob + value.policyListener;

  const cleanup = async () => {
    const state = globalThis[stateKey];
    let timerCancelled = state?.timerId == null;
    if (state?.timerId != null) {
      clearTimeout(state.timerId);
      timerCancelled = true;
      state.timerId = null;
    }
    const revoked = await revokeBlobLifecycle(state, {
      revokeObjectURL: (value) => URL.revokeObjectURL(value),
      fetchURL: (value) => fetch(value)
    });
    if (state) state.policyListenerActive = false;
    document.querySelectorAll("#" + styleId).forEach((item) => item.remove());
    document.querySelectorAll("#" + markerId).forEach((item) => item.remove());
    root.classList.remove(rootClass);
    root.style.removeProperty(variableName);
    delete globalThis[stateKey];
    const final = inventory();
    return {
      cleanupVerified: residualCount(final) === 0,
      finalResidualCount: residualCount(final),
      timerCancelled,
      ...revoked
    };
  };

  const initial = inventory();
  if (mode === "inspect") {
    return { residualCount: residualCount(initial) };
  }
  if (mode === "cleanup" || residualCount(initial) !== 0) {
    const cleaned = await cleanup();
    return {
      initialResidualDetected: residualCount(initial) !== 0,
      diagnosticOnly: false,
      cleanupOnly: true,
      applyAttempted: false,
      applyVerified: false,
      visualEffectApplied: false,
      blobContentVerified: false,
      blobURLCreated: false,
      blobFetchAllowed: false,
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
      styleCountAfterApply: 0,
      rootClassCountAfterApply: 0,
      markerCountAfterApply: 0,
      stateCountAfterApply: 0,
      cssVariablePresentAfterApply: false,
      ...cleaned
    };
  }

  const blobLifecycle = await createBlobLifecycle({
    createBlob: (bytes) =>
      new Blob([new Uint8Array(bytes)], { type: "application/octet-stream" }),
    readBlob: async (blob) => new Uint8Array(await blob.arrayBuffer()),
    createObjectURL: (blob) => URL.createObjectURL(blob),
    parseProtocol: (value) => new URL(value).protocol,
    fetchBytes: async (value) =>
      new Uint8Array(await (await fetch(value)).arrayBuffer()),
    xhrOutcome: (value) => new Promise((resolve) => {
      const request = new XMLHttpRequest();
      let settled = false;
      const finish = (terminal) => {
        if (settled) return;
        settled = true;
        resolve({
          terminal,
          statusZero: request.status === 0,
          bytes:
            terminal === "load" && request.response instanceof ArrayBuffer
              ? new Uint8Array(request.response)
              : null
        });
      };
      request.responseType = "arraybuffer";
      request.timeout = 250;
      request.addEventListener("load", () => finish("load"), { once: true });
      request.addEventListener("error", () => finish("error"), { once: true });
      request.addEventListener(
        "timeout", () => finish("timeout"), { once: true });
      request.addEventListener("abort", () => finish("abort"), { once: true });
      request.open("GET", value, true);
      request.send();
    }),
    addPolicyListener: (listener) =>
      document.addEventListener("securitypolicyviolation", listener),
    removePolicyListener: (listener) =>
      document.removeEventListener("securitypolicyviolation", listener),
    classifyPolicyEvent: (event, value) => {
      const blocked = typeof event?.blockedURI === "string"
        ? event.blockedURI
        : "";
      const directiveMatched =
        event?.effectiveDirective === "connect-src" ||
        event?.effectiveDirective === "default-src";
      return {
        observed: true,
        directiveMatched,
        blockedURIExact: blocked === value,
        blockedURISchemeOnly: blocked === "blob:" || blocked === "blob",
        blockedURIEmpty: blocked.length === 0,
        blockedURIOther:
          blocked.length !== 0 && blocked !== value &&
          blocked !== "blob:" && blocked !== "blob"
      };
    },
    classifyFetchFailure: (error) => ({
      typeError: error?.name === "TypeError",
      domException:
        typeof DOMException === "function" && error instanceof DOMException,
      other:
        error?.name !== "TypeError" &&
        !(typeof DOMException === "function" && error instanceof DOMException)
    }),
    waitForPolicyEvent: () =>
      new Promise((resolve) => setTimeout(resolve, 500))
  });

  const state = {
    blobUrl: blobLifecycle.blobUrl,
    timerId: null,
    policyListenerActive: false
  };
  globalThis[stateKey] = state;
  if (mode === "blob-diagnostic") {
    const cleaned = await cleanup();
    return {
      initialResidualDetected: false,
      diagnosticOnly: true,
      cleanupOnly: false,
      applyAttempted: false,
      applyVerified: false,
      cleanupVerified: cleaned.cleanupVerified,
      visualEffectApplied: false,
      ...blobLifecycle.evidence,
      ...cleaned,
      styleCountAfterApply: 0,
      rootClassCountAfterApply: 0,
      markerCountAfterApply: 0,
      stateCountAfterApply: 0,
      cssVariablePresentAfterApply: false
    };
  }

  const style = document.createElement("style");
  style.id = styleId;
  style.textContent =
    "html." + rootClass + "::before{" +
    "content:'' !important;display:block !important;" +
    "position:fixed !important;inset:0 !important;" +
    "pointer-events:none !important;" +
    "box-shadow:inset 0 0 0 2px var(" + variableName + ") !important;" +
    "z-index:2147483647 !important;}";
  const marker = document.createElement("div");
  marker.id = markerId;
  marker.hidden = true;
  root.style.setProperty(variableName, "rgba(10, 132, 255, 0.55)");
  root.classList.add(rootClass);
  document.head.append(style);
  document.body.append(marker);

  state.timerId = setTimeout(() => {
    document.querySelectorAll("#" + styleId).forEach((item) => item.remove());
    document.querySelectorAll("#" + markerId).forEach((item) => item.remove());
    root.classList.remove(rootClass);
    root.style.removeProperty(variableName);
    if (typeof state.blobUrl === "string") {
      URL.revokeObjectURL(state.blobUrl);
      state.blobUrl = null;
    }
    delete globalThis[stateKey];
  }, 15_000);

  const applied = inventory();
  const computed = getComputedStyle(root, "::before");
  const visualEffectApplied =
    computed.position === "fixed" &&
    computed.top === "0px" && computed.right === "0px" &&
    computed.bottom === "0px" && computed.left === "0px" &&
    computed.pointerEvents === "none" &&
    computed.boxShadow !== "none" &&
    computed.boxShadow.includes("inset") &&
    computed.boxShadow.includes("10, 132, 255");
  const applyVerified =
    applied.style === 1 && applied.rootClass === 1 &&
    applied.marker === 1 && applied.state === 1 &&
    applied.cssVariable === 1 &&
    blobLifecycle.evidence.blobContentVerified &&
    blobLifecycle.evidence.blobURLCreated &&
    (blobLifecycle.evidence.blobFetchAllowed ||
     blobLifecycle.evidence.blobXHRAllowed ||
     blobLifecycle.evidence.blobFetchBlockedByPolicy) &&
    blobLifecycle.evidence.policyListenerRemoved &&
    visualEffectApplied;
  const cleaned = await cleanup();
  return {
    initialResidualDetected: false,
    diagnosticOnly: false,
    cleanupOnly: false,
    applyAttempted: true,
    applyVerified,
    visualEffectApplied,
    ...blobLifecycle.evidence,
    styleCountAfterApply: applied.style,
    rootClassCountAfterApply: applied.rootClass,
    markerCountAfterApply: applied.marker,
    stateCountAfterApply: applied.state,
    cssVariablePresentAfterApply: applied.cssVariable === 1,
    ...cleaned
  };
})`;

export async function runCLI(args = process.argv.slice(2)) {
  try {
    const options = parseArguments(args);
    const lifecycle = await loadLifecycleHelper(options.lifecycleHelper);
    const metadata = await lifecycle.fetchMetadata(options.host, options.port);
    let result;
    let primaryError;
    try {
      result = await runTransaction(lifecycle, metadata.webSocketUrl, options);
    } catch (error) {
      primaryError = error;
    }
    try {
      await lifecycle.requestClose(metadata.webSocketUrl);
    } catch (error) {
      if (!primaryError) primaryError = error;
    }
    if (primaryError) throw primaryError;
    emit(result, 0);
  } catch (error) {
    emit(normalizeError(error), 1);
  }
}

if (process.argv[1] &&
    import.meta.url === pathToFileURL(process.argv[1]).href) {
  await runCLI();
}

async function runTransaction(lifecycle, webSocketUrl, options) {
  const mode = options.command === "cleanup-only"
    ? "cleanup"
    : options.command === "diagnose-blob"
      ? "blob-diagnostic"
      : "apply-cleanup";
  const startExpression = buildStartExpression(mode);
  const started = await lifecycle.evaluate(
    webSocketUrl, startExpression, 2_000, false);
  if (started !== true) {
    throw helperError(
      "canary.job_conflict", "canary", "A canary transaction is already active.");
  }

  const pollExpression = `(() => {
    const job = globalThis[Symbol.for(${JSON.stringify(jobSymbolName)})];
    if (!job) return { missing: true };
    return { done: job.done, failed: job.failed, result: job.result };
  })()`;
  let completed;
  let mainJobCleared = false;
  try {
    for (let index = 0; index < pollLimit; index += 1) {
      const value = await lifecycle.evaluate(
        webSocketUrl, pollExpression, 1_500, false);
      if (value?.done === true) {
        completed = value;
        break;
      }
      await delay(pollIntervalMilliseconds);
    }
  } finally {
    const clearExpression = `(() => {
      const key = Symbol.for(${JSON.stringify(jobSymbolName)});
      const existed = Boolean(globalThis[key]);
      delete globalThis[key];
      return existed && !globalThis[key];
    })()`;
    mainJobCleared = await lifecycle.evaluate(
      webSocketUrl, clearExpression, 1_500, false);
  }
  if (!completed || completed.failed || !completed.result ||
      mainJobCleared !== true) {
    throw helperError(
      "canary.transaction_failed",
      "canary",
      "The bounded canary transaction did not complete safely.");
  }
  const result = { ...completed.result, mainJobCleared: true };
  validateResult(result, mode);
  return result;
}

export function buildStartExpression(mode) {
  if (!["cleanup", "apply-cleanup", "blob-diagnostic"].includes(mode)) {
    throw helperError(
      "usage.invalid", "usage", "Invalid canary transaction mode.");
  }
  const rendererSource = JSON.stringify(rendererTransactionSource);
  return `(() => {
    const key = Symbol.for(${JSON.stringify(jobSymbolName)});
    if (globalThis[key]) return false;
    const job = { done: false, failed: false, result: null };
    globalThis[key] = job;
    (async () => {
      try {
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
        const windows = electron.BrowserWindow.getAllWindows();
        const records = windows.map((window) => ({
          kind: classify(window.webContents.getURL()),
          contents: window.webContents
        }));
        const eligible = records.filter((item) => item.kind === "eligible");
        const overlays = records.filter((item) => item.kind === "overlay");
        const unknown = records.filter((item) => item.kind === "unknown");
        const inspect = async (item) => item.contents.executeJavaScript(
          "(" + ${rendererSource} + ")('inspect')", true);
        const before = await Promise.all(records.map(inspect));
        const overlayBefore = before.filter((_, index) =>
          records[index].kind === "overlay");
        const initialResidualDetected =
          before.some((item) => item.residualCount !== 0);
        if (initialResidualDetected || ${JSON.stringify(mode)} === "cleanup") {
          const cleaned = await Promise.all(records.map((item) =>
            item.contents.executeJavaScript(
              "(" + ${rendererSource} + ")('cleanup')", true)));
          job.result = aggregate(
            cleaned, eligible.length, overlays.length, unknown.length,
            initialResidualDetected, overlayBefore);
        } else {
          if (eligible.length !== 1 || unknown.length !== 0) throw new Error();
          const primary = await eligible[0].contents.executeJavaScript(
            "(" + ${rendererSource} + ")(" +
            JSON.stringify(${JSON.stringify(mode)}) + ")", true);
          const overlayAfter = await Promise.all(overlays.map(inspect));
          const overlayUnmodified =
            [...overlayBefore, ...overlayAfter].every(
              (item) => item.residualCount === 0);
          job.result = {
            ...primary,
            eligibleWindowCount: eligible.length,
            overlayWindowCount: overlays.length,
            unknownWindowCount: unknown.length,
            overlayUnmodified
          };
        }
      } catch {
        job.failed = true;
      } finally {
        job.done = true;
      }
      function aggregate(values, eligibleCount, overlayCount, unknownCount,
        initialResidualDetected, overlayBefore) {
        return {
          initialResidualDetected,
          diagnosticOnly: false,
          cleanupOnly: true,
          eligibleWindowCount: eligibleCount,
          overlayWindowCount: overlayCount,
          unknownWindowCount: unknownCount,
          overlayUnmodified:
            overlayBefore.every((item) => item.residualCount === 0) &&
            values.every((item) =>
              item.cleanupVerified && item.finalResidualCount === 0),
          applyAttempted: false,
          applyVerified: false,
          cleanupVerified: values.every((item) => item.cleanupVerified),
          visualEffectApplied: false,
          blobContentVerified: false,
          blobURLCreated: false,
          blobFetchAllowed: false,
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
          blobRevokeInvoked:
            values.every((item) => item.blobRevokeInvoked),
          postRevokeDereferenceRejected:
            values.every((item) => item.postRevokeDereferenceRejected),
          blobReferenceCleared:
            values.every((item) => item.blobReferenceCleared),
          policyListenerRemoved: true,
          styleCountAfterApply: 0,
          rootClassCountAfterApply: 0,
          markerCountAfterApply: 0,
          stateCountAfterApply: 0,
          cssVariablePresentAfterApply: false,
          finalResidualCount: values.reduce(
            (sum, item) => sum + item.finalResidualCount, 0),
          timerCancelled: values.every((item) => item.timerCancelled)
        };
      }
    })();
    return true;
  })()`;
}

function parseArguments(args) {
  const command = args.shift();
  if (!["apply-cleanup", "cleanup-only", "diagnose-blob"].includes(command)) {
    throw helperError("usage.invalid", "usage", "Unsupported helper command.");
  }
  const values = {};
  while (args.length > 0) {
    const name = args.shift();
    const value = args.shift();
    if (!["--lifecycle-helper", "--host", "--port", "--pid"].includes(name) ||
        value == null || Object.hasOwn(values, name)) {
      throw helperError("usage.invalid", "usage", "Invalid helper arguments.");
    }
    values[name] = value;
  }
  const host = values["--host"];
  const port = Number(values["--port"]);
  const processId = Number(values["--pid"]);
  const lifecycleHelper = values["--lifecycle-helper"];
  if (!allowedHosts.has(host) || port !== expectedPort ||
      !Number.isSafeInteger(processId) || processId <= 0 ||
      typeof lifecycleHelper !== "string" || !path.isAbsolute(lifecycleHelper)) {
    throw helperError("usage.invalid", "usage", "Invalid helper arguments.");
  }
  return { command, host, port, processId, lifecycleHelper };
}

async function loadLifecycleHelper(file) {
  const real = fs.realpathSync(file);
  const metadata = fs.lstatSync(file);
  if (real !== file || !metadata.isFile()) {
    throw helperError(
      "helper.path_invalid", "setup", "Lifecycle helper path is invalid.");
  }
  const module = await import(pathToFileURL(real).href);
  if (typeof module.fetchMetadata !== "function" ||
      typeof module.evaluate !== "function" ||
      typeof module.requestClose !== "function") {
    throw helperError(
      "helper.exports_invalid", "setup", "Lifecycle helper exports are invalid.");
  }
  return module;
}

export function validateResult(value, mode) {
  const integerKeys = [
    "eligibleWindowCount", "overlayWindowCount", "unknownWindowCount",
    "styleCountAfterApply", "rootClassCountAfterApply",
    "markerCountAfterApply", "stateCountAfterApply", "finalResidualCount",
  ];
  const booleanKeys = [
    "initialResidualDetected", "diagnosticOnly", "cleanupOnly",
    "overlayUnmodified",
    "applyAttempted", "applyVerified", "cleanupVerified",
    "visualEffectApplied", "blobContentVerified", "blobURLCreated",
    "blobFetchAllowed", "blobXHRAllowed", "blobFetchBlockedByPolicy",
    "blobPolicyEventObserved", "blobPolicyDirectiveMatched",
    "blobPolicyBlockedURIExact", "blobPolicyBlockedURISchemeOnly",
    "blobPolicyBlockedURIEmpty", "blobPolicyBlockedURIOther",
    "blobFetchRejectedTypeError", "blobFetchRejectedDOMException",
    "blobFetchRejectedOther", "blobXHRLoad", "blobXHRError",
    "blobXHRTimeout", "blobXHRAbort", "blobXHRStatusZero",
    "blobRevokeInvoked",
    "postRevokeDereferenceRejected", "blobReferenceCleared",
    "policyListenerRemoved",
    "cssVariablePresentAfterApply", "timerCancelled", "mainJobCleared",
  ];
  if (!value || typeof value !== "object" ||
      !integerKeys.every((key) =>
        Number.isSafeInteger(value[key]) && value[key] >= 0) ||
      !booleanKeys.every((key) => typeof value[key] === "boolean")) {
    throw helperError(
      "canary.result_shape_invalid",
      "canary",
      "Canary result shape is invalid.");
  }
  requireCheck(
    value.eligibleWindowCount === 1,
    "canary.eligible_window_count_invalid",
    "Eligible window count is invalid.");
  requireCheck(
    value.unknownWindowCount === 0,
    "canary.unknown_window_detected",
    "An unknown window was detected.");
  requireCheck(
    value.overlayUnmodified,
    "canary.overlay_modified",
    "Overlay isolation was not proven.");
  requireCheck(
    value.cleanupVerified,
    "canary.cleanup_not_verified",
    "Cleanup was not verified.");
  requireCheck(
    value.finalResidualCount === 0,
    "canary.residual_detected",
    "Canary residue was detected.");
  requireCheck(
    value.mainJobCleared,
    "canary.main_job_not_cleared",
    "The bounded main-process job was not cleared.");
  if (mode === "cleanup" || value.initialResidualDetected) {
    requireCheck(
      value.cleanupOnly && !value.applyAttempted,
      "canary.cleanup_only_invalid",
      "Cleanup-only result is invalid.");
    requireCheck(
      value.blobRevokeInvoked && value.postRevokeDereferenceRejected &&
      value.blobReferenceCleared && value.policyListenerRemoved,
      "canary.cleanup_blob_lifecycle_invalid",
      "Cleanup-only Blob lifecycle was not verified.");
    return;
  }
  if (mode === "blob-diagnostic") {
    requireCheck(
      value.diagnosticOnly && !value.cleanupOnly && !value.applyAttempted,
      "canary.diagnostic_shape_invalid",
      "Blob diagnostic result is invalid.");
    requireCheck(
      value.blobContentVerified && value.blobURLCreated,
      "canary.diagnostic_blob_invalid",
      "Blob diagnostic creation was not verified.");
    requireCheck(
      value.blobRevokeInvoked && value.postRevokeDereferenceRejected &&
      value.blobReferenceCleared && value.policyListenerRemoved,
      "canary.diagnostic_cleanup_invalid",
      "Blob diagnostic cleanup was not verified.");
    requireCheck(
      !value.visualEffectApplied &&
      value.styleCountAfterApply === 0 &&
      value.rootClassCountAfterApply === 0 &&
      value.markerCountAfterApply === 0 &&
      value.stateCountAfterApply === 0 &&
      !value.cssVariablePresentAfterApply &&
      value.timerCancelled,
      "canary.diagnostic_mutation_detected",
      "Blob diagnostics unexpectedly reported canary mutation.");
    return;
  }
  requireCheck(
    !value.cleanupOnly && value.applyAttempted,
    "canary.apply_not_attempted",
    "Apply was not attempted.");
  requireCheck(
    value.styleCountAfterApply === 1,
    "canary.style_count_invalid",
    "Style count after Apply is invalid.");
  requireCheck(
    value.rootClassCountAfterApply === 1,
    "canary.root_class_count_invalid",
    "Root class count after Apply is invalid.");
  requireCheck(
    value.markerCountAfterApply === 1,
    "canary.marker_count_invalid",
    "Marker count after Apply is invalid.");
  requireCheck(
    value.stateCountAfterApply === 1,
    "canary.state_count_invalid",
    "Renderer state count after Apply is invalid.");
  requireCheck(
    value.cssVariablePresentAfterApply,
    "canary.css_variable_missing",
    "The canary CSS variable was not observed.");
  requireCheck(
    value.blobContentVerified,
    "canary.blob_content_invalid",
    "The fixed four-byte Blob content was not verified.");
  requireCheck(
    value.blobURLCreated,
    "canary.blob_url_invalid",
    "The Blob URL was not created correctly.");
  requireCheck(
    value.blobFetchAllowed || value.blobXHRAllowed ||
      value.blobFetchBlockedByPolicy,
    "canary.blob_fetch_failure_unclassified",
    "Blob URL fetch failure could not be classified safely.");
  requireCheck(
    value.policyListenerRemoved,
    "canary.policy_listener_not_removed",
    "The temporary policy listener was not removed.");
  requireCheck(
    value.visualEffectApplied,
    "canary.visual_effect_not_observed",
    "The computed visual effect was not observed.");
  requireCheck(
    value.applyVerified,
    "canary.apply_not_verified",
    "Apply was not verified.");
  requireCheck(
    value.timerCancelled,
    "canary.timer_not_cancelled",
    "The canary timer was not cancelled.");
  requireCheck(
    value.blobRevokeInvoked,
    "canary.blob_revoke_not_invoked",
    "Blob URL revocation was not invoked.");
  requireCheck(
    value.postRevokeDereferenceRejected,
    "canary.blob_post_revoke_accessible",
    "The revoked Blob URL remained dereferenceable.");
  requireCheck(
    value.blobReferenceCleared,
    "canary.blob_reference_not_cleared",
    "The Blob URL reference was not cleared.");
}

function requireCheck(condition, code, message) {
  if (!condition) throw helperError(code, "canary", message);
}

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
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
      : "The CSS canary helper failed.",
  };
}

function emit(value, exitCode) {
  fs.writeFileSync(1, `${JSON.stringify(value)}\n`);
  process.exit(exitCode);
}
