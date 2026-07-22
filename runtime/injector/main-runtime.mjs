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
    if (!state) {
      return {
        runtimeVersion: 1,
        active: false,
        generation: null,
        themeId: null,
        eligibleWindows: 0,
        appliedWindows: 0,
        auxiliaryWindows: 0,
        hookCount: 0,
      };
    }
    if (${serializedOperation} === "ensure") {
      return await state.ensure();
    }
    if (${serializedOperation} === "status") {
      return state.snapshot();
    }
    if (${serializedOperation} === "cleanup") {
      const generation = state.generation;
      await state.stop(generation, true);
      return {
        runtimeVersion: 1,
        active: false,
        generation,
        themeId: null,
        eligibleWindows: 0,
        appliedWindows: 0,
        auxiliaryWindows: 0,
        hookCount: 0,
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
  const electron = process.mainModule.require("electron");
  const { app, BrowserWindow } = electron;
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
  const auxiliaryWindowIds = new Set();
  const pageModes = new Map();
  let enabled = true;
  let failures = 0;

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
    void applyWindow(window).catch(() => {
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
    const windows = BrowserWindow.getAllWindows();
    for (const window of windows) {
      attachWindow(window);
      await applyWindow(window);
    }
    return snapshot();
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
      void applyWindow(window).catch(() => {
        failures += 1;
      });
    };
    const destroyedHandler = () => detachWindow(contents);
    contents.on("dom-ready", domReadyHandler);
    contents.once("destroyed", destroyedHandler);
    hooks.set(contents.id, {
      contents,
      domReadyHandler,
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
    hooks.delete(contents.id);
    appliedWindowIds.delete(contents.id);
    eligibleWindowIds.delete(contents.id);
    auxiliaryWindowIds.delete(contents.id);
    pageModes.delete(contents.id);
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
      pageModes.delete(contents.id);
      await safeRendererCleanup(contents);
      return;
    }

    const result = await contents.executeJavaScript(rendererExpression, true);
    if (result?.eligible) {
      eligibleWindowIds.add(contents.id);
      auxiliaryWindowIds.delete(contents.id);
      if (result.applied || result.generation === generation) {
        appliedWindowIds.add(contents.id);
      }
      if (typeof result.pageMode === "string") {
        pageModes.set(contents.id, result.pageMode);
      }
    } else {
      auxiliaryWindowIds.add(contents.id);
      eligibleWindowIds.delete(contents.id);
      appliedWindowIds.delete(contents.id);
      pageModes.delete(contents.id);
    }
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
}
