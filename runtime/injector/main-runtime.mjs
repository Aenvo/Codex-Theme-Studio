import {
  rendererBootstrap,
  rendererCompatibility,
  rendererWindowProbe,
} from "./renderer-runtime.mjs";

const mainStateKey = "__CODEX_THEME_STUDIO_MAIN_V1__";

export function createMainApplyExpression(payload) {
  const request = {
    runtimeVersion: 1,
    themeId: payload.themeId,
    rendererBootstrapSource: rendererBootstrap.toString(),
    compatibility: rendererCompatibility,
    payload,
  };
  return `(${mainRuntimeBootstrap.toString()})(${JSON.stringify(request)})`;
}

export function createMainOperationExpression(operation) {
  const serializedOperation = JSON.stringify(operation);
  return `(async () => {
    const state = globalThis.${mainStateKey};
    const operation = ${serializedOperation};
    const electron = process.mainModule.require("electron");
    const { BrowserWindow } = electron;
    const isEligibleAppWindow = (contents) => {
      if (!contents || contents.isDestroyed()) return false;
      try {
        const parsed = new URL(contents.getURL());
        return parsed.protocol === "app:" &&
          parsed.searchParams.get("initialRoute") !== "/avatar-overlay" &&
          !parsed.pathname.includes("/avatar-overlay") &&
          !parsed.pathname.includes("/pet-overlay");
      } catch {
        return false;
      }
    };
    const detectOkkSkin = \
      '(() => { const root = document.documentElement; return ' +
      'Boolean(document.getElementById("okkskin-style")) || ' +
      'root.classList.contains("okkskin") || ' +
      'Boolean(root.style.getPropertyValue("--ok-art")); })()';
    const cleanupOkkSkin = \
      '(() => { const style = document.getElementById("okkskin-style"); ' +
      'if (style) style.remove(); const root = document.documentElement; ' +
      'root.classList.remove("okkskin"); root.style.removeProperty("color-scheme"); ' +
      '["--ok-bg","--ok-panel","--ok-accent","--ok-text","--ok-muted","--ok-line","--ok-art"]' +
      '.forEach(name => root.style.removeProperty(name)); return true; })()';
    const hasKnownOkkSkinRuntime = async () => {
      if (Boolean(globalThis.__okkskinRJS)) return true;
      for (const window of BrowserWindow.getAllWindows()) {
        const contents = window?.webContents;
        if (!isEligibleAppWindow(contents)) continue;
        try {
          if (await contents.executeJavaScript(detectOkkSkin, true)) return true;
        } catch {
          // Unknown windows do not become positive evidence.
        }
      }
      return false;
    };
    if (operation === "cleanup") {
      globalThis.__okkskinRJS = "";
      for (const window of BrowserWindow.getAllWindows()) {
        const contents = window?.webContents;
        if (!isEligibleAppWindow(contents)) continue;
        try {
          await contents.executeJavaScript(cleanupOkkSkin, true);
        } catch {
          // Managed cleanup continues across independently failing windows.
        }
      }
      const generation = state?.generation ?? null;
      if (state) await state.stop(state.generation, true);
      return {
        runtimeVersion: 1,
        active: false,
        generation,
        themeId: null,
        eligibleWindows: 0,
        appliedWindows: 0,
        pendingWindows: 0,
        auxiliaryWindows: 0,
        hookCount: 0,
        knownExternalThemeActive: false,
      };
    }
    if (!state) {
      return {
        runtimeVersion: 1,
        active: false,
        generation: null,
        themeId: null,
        eligibleWindows: 0,
        appliedWindows: 0,
        pendingWindows: 0,
        auxiliaryWindows: 0,
        hookCount: 0,
        knownExternalThemeActive: await hasKnownOkkSkinRuntime(),
      };
    }
    if (operation === "ensure") {
      return await state.ensure();
    }
    if (operation === "status") {
      return {
        ...state.snapshot(),
        knownExternalThemeActive: await hasKnownOkkSkinRuntime(),
      };
    }
    return state.snapshot();
  })()`;
}

export function createMainProbeExpression() {
  const rendererProbeExpression =
    `(${rendererWindowProbe.toString()})(${JSON.stringify(rendererCompatibility)})`;
  return `(async () => {
    const electron = process.mainModule.require("electron");
    const results = [];
    for (const window of electron.BrowserWindow.getAllWindows()) {
      const contents = window?.webContents;
      if (!contents || contents.isDestroyed()) {
        continue;
      }
      let parsed;
      try {
        parsed = new URL(contents.getURL());
      } catch {
        results.push({ eligible: false, reason: "invalid-url" });
        continue;
      }
      if (parsed.protocol !== "app:") {
        results.push({ eligible: false, reason: "non-app-url" });
        continue;
      }
      if (parsed.searchParams.get("initialRoute") === "/avatar-overlay" ||
          parsed.pathname.includes("/avatar-overlay") ||
          parsed.pathname.includes("/pet-overlay")) {
        results.push({ eligible: false, reason: "excluded-route" });
        continue;
      }
      try {
        results.push(await contents.executeJavaScript(
          ${JSON.stringify(rendererProbeExpression)},
          true));
      } catch {
        results.push({ eligible: false, reason: "probe-failed" });
      }
    }
    return {
      runtimeVersion: 1,
      windowCount: results.length,
      eligibleWindows: results.filter(result => result?.eligible).length,
      auxiliaryWindows: results.filter(result => !result?.eligible).length,
      windows: results,
    };
  })()`;
}

export async function mainRuntimeBootstrap(request) {
  const stateKey = "__CODEX_THEME_STUDIO_MAIN_V1__";
  const pendingRetryAttempts = 20;
  const pendingRetryDelayMs = 250;
  const electron = process.mainModule.require("electron");
  const { app, BrowserWindow } = electron;
  // A known OkkSkin runtime may be active while its user-level persistence remains
  // intentionally configured. Suspend only the current Codex process hook before
  // applying a managed Theme Studio theme; do not touch files, agents, or Run keys.
  globalThis.__okkskinRJS = "";
  const okkSkinCleanup = `(() => {
    const style = document.getElementById("okkskin-style");
    if (style) style.remove();
    const root = document.documentElement;
    root.classList.remove("okkskin");
    root.style.removeProperty("color-scheme");
    ["--ok-bg", "--ok-panel", "--ok-accent", "--ok-text", "--ok-muted", "--ok-line", "--ok-art"]
      .forEach(name => root.style.removeProperty(name));
    return true;
  })()`;
  for (const window of BrowserWindow.getAllWindows()) {
    const contents = window?.webContents;
    if (!contents || contents.isDestroyed()) continue;
    try {
      const parsed = new URL(contents.getURL());
      if (parsed.protocol === "app:" &&
          parsed.searchParams.get("initialRoute") !== "/avatar-overlay") {
        await contents.executeJavaScript(okkSkinCleanup, true);
      }
    } catch {
      // Unknown or auxiliary windows stay untouched.
    }
  }
  const previous = globalThis[stateKey];
  const previousGeneration = Number.isSafeInteger(previous?.generation)
    ? previous.generation
    : 0;
  if (previous) {
    await previous.stop(previous.generation, true);
  }

  const generation = previousGeneration + 1;
  const rendererExpression = `(${request.rendererBootstrapSource})(${JSON.stringify({
    compatibility: request.compatibility,
    payload: request.payload,
    generation,
  })})`;
  const rendererCleanupExpression = `(() => {
    const state = window.__CODEX_THEME_STUDIO_RENDERER_V1__;
    return state ? state.cleanup(${JSON.stringify(generation)}) : false;
  })()`;
  const hooks = new Map();
  const appliedWindowIds = new Set();
  const eligibleWindowIds = new Set();
  const pendingWindowIds = new Set();
  const auxiliaryWindowIds = new Set();
  const pageModes = new Map();
  let enabled = true;
  let failures = 0;
  let pendingRetryPromise = null;

  const state = {
    runtimeVersion: request.runtimeVersion,
    themeId: request.themeId,
    generation,
    ensure,
    stop,
    snapshot,
  };
  globalThis[stateKey] = state;

  const createdHandler = (_event, window) => {
    if (!enabled) {
      return;
    }
    attachWindow(window);
    void applyWindowAndRetry(window).catch(() => {
      failures += 1;
    });
  };
  app.on("browser-window-created", createdHandler);

  await ensure();
  return snapshot();

  async function ensure() {
    if (!enabled || globalThis[stateKey] !== state) {
      return snapshot();
    }
    await ensureOnce();
    await retryPendingWindows();
    return snapshot();
  }

  async function ensureOnce() {
    const windows = BrowserWindow.getAllWindows();
    for (const window of windows) {
      attachWindow(window);
      await applyWindow(window);
    }
  }

  function attachWindow(window) {
    const contents = window?.webContents;
    if (!contents || contents.isDestroyed() || hooks.has(contents.id)) {
      return;
    }
    const domReadyHandler = () => {
      if (!enabled || globalThis[stateKey] !== state) {
        return;
      }
      void applyWindowAndRetry(window).catch(() => {
        failures += 1;
      });
    };
    const showHandler = () => {
      if (!enabled || globalThis[stateKey] !== state) {
        return;
      }
      void applyWindowAndRetry(window).catch(() => {
        failures += 1;
      });
    };
    const destroyedHandler = () => detachWindow(contents);
    contents.on("dom-ready", domReadyHandler);
    contents.once("destroyed", destroyedHandler);
    if (typeof window.on === "function") {
      window.on("show", showHandler);
    }
    hooks.set(contents.id, {
      window,
      contents,
      domReadyHandler,
      showHandler,
      destroyedHandler,
    });
  }

  function detachWindow(contents) {
    const hook = hooks.get(contents.id);
    if (!hook) {
      return;
    }
    hook.contents.removeListener("dom-ready", hook.domReadyHandler);
    hook.contents.removeListener("destroyed", hook.destroyedHandler);
    if (typeof hook.window.removeListener === "function") {
      hook.window.removeListener("show", hook.showHandler);
    }
    hooks.delete(contents.id);
    appliedWindowIds.delete(contents.id);
    eligibleWindowIds.delete(contents.id);
    pendingWindowIds.delete(contents.id);
    auxiliaryWindowIds.delete(contents.id);
    pageModes.delete(contents.id);
  }

  async function applyWindowAndRetry(window) {
    await applyWindow(window);
    await retryPendingWindows();
  }

  async function applyWindow(window) {
    const contents = window?.webContents;
    if (!enabled || !contents || contents.isDestroyed()) {
      return;
    }
    const urlStatus = assessUrl(contents.getURL());
    if (!urlStatus.eligible) {
      auxiliaryWindowIds.add(contents.id);
      eligibleWindowIds.delete(contents.id);
      appliedWindowIds.delete(contents.id);
      pendingWindowIds.delete(contents.id);
      pageModes.delete(contents.id);
      await safeRendererCleanup(contents);
      return;
    }

    const result = await contents.executeJavaScript(rendererExpression, true);
    if (result?.eligible) {
      eligibleWindowIds.add(contents.id);
      pendingWindowIds.delete(contents.id);
      auxiliaryWindowIds.delete(contents.id);
      if (result.applied || result.generation === generation) {
        appliedWindowIds.add(contents.id);
      }
      if (typeof result.pageMode === "string") {
        pageModes.set(contents.id, result.pageMode);
      }
    } else {
      eligibleWindowIds.delete(contents.id);
      appliedWindowIds.delete(contents.id);
      pageModes.delete(contents.id);
      if (isPendingReason(result?.reason) && isUserFacingWindow(window)) {
        pendingWindowIds.add(contents.id);
        auxiliaryWindowIds.delete(contents.id);
      } else {
        pendingWindowIds.delete(contents.id);
        auxiliaryWindowIds.add(contents.id);
        if (!isPendingReason(result?.reason)) {
          failures += 1;
        }
      }
    }
  }

  function retryPendingWindows() {
    if (pendingRetryPromise) {
      return pendingRetryPromise;
    }
    const current = runPendingRetry().finally(() => {
      if (pendingRetryPromise === current) {
        pendingRetryPromise = null;
      }
    });
    pendingRetryPromise = current;
    return current;
  }

  async function runPendingRetry() {
    for (let attempt = 0;
         attempt < pendingRetryAttempts && pendingWindowIds.size > 0;
         attempt += 1) {
      await delay(pendingRetryDelayMs);
      if (!enabled || globalThis[stateKey] !== state) {
        return;
      }
      const windows = BrowserWindow.getAllWindows();
      const existingIds = new Set();
      for (const window of windows) {
        const contents = window?.webContents;
        if (!contents || contents.isDestroyed()) {
          continue;
        }
        existingIds.add(contents.id);
        if (pendingWindowIds.has(contents.id)) {
          await applyWindow(window);
        }
      }
      for (const id of [...pendingWindowIds]) {
        if (!existingIds.has(id)) {
          pendingWindowIds.delete(id);
        }
      }
    }
  }

  function delay(milliseconds) {
    return new Promise(resolve => setTimeout(resolve, milliseconds));
  }

  async function safeRendererCleanup(contents) {
    try {
      await contents.executeJavaScript(rendererCleanupExpression, true);
    } catch {
      failures += 1;
    }
  }

  async function stop(expectedGeneration, cleanWindows) {
    if (!enabled ||
        expectedGeneration !== generation ||
        globalThis[stateKey] !== state) {
      return false;
    }
    enabled = false;
    app.removeListener("browser-window-created", createdHandler);
    for (const hook of hooks.values()) {
      hook.contents.removeListener("dom-ready", hook.domReadyHandler);
      hook.contents.removeListener("destroyed", hook.destroyedHandler);
      if (typeof hook.window.removeListener === "function") {
        hook.window.removeListener("show", hook.showHandler);
      }
    }
    hooks.clear();

    if (cleanWindows) {
      for (const window of BrowserWindow.getAllWindows()) {
        const contents = window?.webContents;
        if (contents && !contents.isDestroyed()) {
          await safeRendererCleanup(contents);
        }
      }
    }
    appliedWindowIds.clear();
    eligibleWindowIds.clear();
    pendingWindowIds.clear();
    auxiliaryWindowIds.clear();
    pageModes.clear();
    if (globalThis[stateKey] === state) {
      delete globalThis[stateKey];
    }
    return true;
  }

  function snapshot() {
    return {
      runtimeVersion: request.runtimeVersion,
      active: enabled && globalThis[stateKey] === state,
      generation,
      themeId: request.themeId,
      eligibleWindows: eligibleWindowIds.size,
      appliedWindows: appliedWindowIds.size,
      pendingWindows: pendingWindowIds.size,
      auxiliaryWindows: auxiliaryWindowIds.size,
      hookCount: hooks.size,
      pageModes: [...new Set(pageModes.values())].sort(),
      failures,
    };
  }

  function assessUrl(rawUrl) {
    let parsed;
    try {
      parsed = new URL(rawUrl);
    } catch {
      return { eligible: false };
    }
    if (parsed.protocol !== "app:") {
      return { eligible: false };
    }
    if (parsed.searchParams.get("initialRoute") === "/avatar-overlay" ||
        parsed.pathname.includes("/avatar-overlay") ||
        parsed.pathname.includes("/pet-overlay")) {
      return { eligible: false };
    }
    return { eligible: true };
  }

  function isPendingReason(reason) {
    return reason === "dom-not-ready" || reason === "shell-features-missing";
  }

  function isUserFacingWindow(window) {
    try {
      return typeof window?.isVisible !== "function" || window.isVisible();
    } catch {
      return true;
    }
  }
}
