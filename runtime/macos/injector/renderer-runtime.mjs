export const runtimeIdentifiers = Object.freeze({
  styleId: "codex-theme-studio-macos-runtime-v1",
  rootClass: "codex-theme-studio-macos-runtime-v1-active",
  markerId: "codex-theme-studio-macos-runtime-v1-marker",
  stateSymbol: "codex-theme-studio.macos-runtime.v1",
});

const colorPattern = /^#[0-9A-F]{6}(?:[0-9A-F]{2})?$/u;

export function validateTheme(theme) {
  if (!theme || typeof theme !== "object" || Array.isArray(theme) ||
      !Number.isSafeInteger(theme.schemaVersion) || theme.schemaVersion !== 1 ||
      typeof theme.themeId !== "string" ||
      !/^[0-9a-f-]{36}$/iu.test(theme.themeId) ||
      !["auto", "light", "dark"].includes(theme.variant) ||
      !theme.palette || typeof theme.palette !== "object") {
    throw safeError("renderer.theme_invalid", "theme");
  }
  const keys = ["background", "panel", "accent", "text", "muted", "border"];
  if (!keys.every((key) =>
    typeof theme.palette[key] === "string" &&
    colorPattern.test(theme.palette[key]))) {
    throw safeError("renderer.theme_invalid", "theme");
  }
  const allowed = new Set(["schemaVersion", "themeId", "variant", "palette"]);
  if (Object.keys(theme).some((key) => !allowed.has(key)) ||
      Object.keys(theme.palette).some((key) => !keys.includes(key))) {
    throw safeError("renderer.theme_invalid", "theme");
  }
  return theme;
}

export function buildRendererExpression(mode, theme) {
  if (!["inspect", "apply", "cleanup"].includes(mode)) {
    throw safeError("renderer.mode_invalid", "renderer");
  }
  if (mode === "apply") validateTheme(theme);
  return `(${rendererTransaction.toString()})(${JSON.stringify(mode)}, ${JSON.stringify(theme ?? null)}, ${JSON.stringify(runtimeIdentifiers)})`;
}

function rendererTransaction(mode, theme, ids) {
  const root = document.documentElement;
  const stateKey = Symbol.for(ids.stateSymbol);
  const styleSelector = `#${ids.styleId}`;
  const markerSelector = `#${ids.markerId}`;
  const variables = [
    "--cts-background", "--cts-panel", "--cts-accent",
    "--cts-text", "--cts-muted", "--cts-border",
  ];
  const count = (selector) => document.querySelectorAll(selector).length;
  const residual = () =>
    count(styleSelector) +
    count(markerSelector) +
    (root.classList.contains(ids.rootClass) ? 1 : 0) +
    variables.filter((name) => root.style.getPropertyValue(name)).length +
    (globalThis[stateKey] ? 1 : 0);
  const cleanup = () => {
    document.querySelectorAll(styleSelector).forEach((item) => item.remove());
    document.querySelectorAll(markerSelector).forEach((item) => item.remove());
    root.classList.remove(ids.rootClass);
    variables.forEach((name) => root.style.removeProperty(name));
    const state = globalThis[stateKey];
    if (state?.timer) clearTimeout(state.timer);
    delete globalThis[stateKey];
    return {
      cleanupVerified: residual() === 0,
      residualCount: residual(),
      styleCount: count(styleSelector),
      markerCount: count(markerSelector),
      rootClassCount: root.classList.contains(ids.rootClass) ? 1 : 0,
      stateCount: globalThis[stateKey] ? 1 : 0,
      timerCount: globalThis[stateKey]?.timer ? 1 : 0,
      visualEffectApplied: false,
    };
  };
  if (mode === "cleanup") return cleanup();
  if (mode === "inspect") {
    return {
      cleanupVerified: residual() === 0,
      residualCount: residual(),
      styleCount: count(styleSelector),
      markerCount: count(markerSelector),
      rootClassCount: root.classList.contains(ids.rootClass) ? 1 : 0,
      stateCount: globalThis[stateKey] ? 1 : 0,
      timerCount: globalThis[stateKey]?.timer ? 1 : 0,
      visualEffectApplied: false,
    };
  }

  if (residual() !== 0) {
    return { ...cleanup(), applyVerified: false, initialResidualDetected: true };
  }
  const style = document.createElement("style");
  style.id = ids.styleId;
  style.textContent = `
    html.${ids.rootClass} {
      background: var(--cts-background) !important;
      color: var(--cts-text) !important;
      color-scheme: dark;
    }
    html.${ids.rootClass} body {
      background: var(--cts-background) !important;
      color: var(--cts-text) !important;
    }
    html.${ids.rootClass} a,
    html.${ids.rootClass} button:focus-visible {
      outline-color: var(--cts-accent) !important;
    }
  `;
  const marker = document.createElement("meta");
  marker.id = ids.markerId;
  marker.setAttribute("data-runtime", "1");
  document.head.append(style, marker);
  root.classList.add(ids.rootClass);
  root.style.setProperty("--cts-background", theme.palette.background);
  root.style.setProperty("--cts-panel", theme.palette.panel);
  root.style.setProperty("--cts-accent", theme.palette.accent);
  root.style.setProperty("--cts-text", theme.palette.text);
  root.style.setProperty("--cts-muted", theme.palette.muted);
  root.style.setProperty("--cts-border", theme.palette.border);
  const timer = setTimeout(() => {}, 60_000);
  globalThis[stateKey] = { timer };
  const computed = getComputedStyle(root);
  const visualEffectApplied =
    root.classList.contains(ids.rootClass) &&
    computed.backgroundColor !== "" &&
    computed.color !== "";
  return {
    applyVerified:
      count(styleSelector) === 1 &&
      count(markerSelector) === 1 &&
      root.classList.contains(ids.rootClass) &&
      Boolean(globalThis[stateKey]) &&
      visualEffectApplied,
    cleanupVerified: false,
    initialResidualDetected: false,
    residualCount: residual(),
    styleCount: count(styleSelector),
    markerCount: count(markerSelector),
    rootClassCount: root.classList.contains(ids.rootClass) ? 1 : 0,
    stateCount: globalThis[stateKey] ? 1 : 0,
    timerCount: globalThis[stateKey]?.timer ? 1 : 0,
    visualEffectApplied,
  };
}

function safeError(code, stage) {
  return Object.assign(new Error(code), { code, stage });
}
