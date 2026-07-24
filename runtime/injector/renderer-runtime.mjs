export const rendererCompatibility = Object.freeze({
  version: 1,
  excludedInitialRoutes: ["/avatar-overlay"],
  excludedPathFragments: ["/avatar-overlay", "/pet-overlay"],
  shellSelectors: [
    "main.main-surface",
    "[data-testid='codex-shell']",
    "[data-testid='app-shell']",
    "#root",
  ],
  sidebarSelectors: [
    "aside",
    "nav",
    "[data-testid*='sidebar']",
    "[data-sidebar]",
    "[aria-label*='sidebar' i]",
    "[aria-label*='侧边栏']",
    "[aria-label*='边栏']",
  ],
  contentSelectors: [
    "main",
    "[role='main']",
    "main.main-surface",
  ],
  composerSelectors: [
    "textarea",
    "[contenteditable='true']",
    "form",
    "[data-testid*='composer']",
  ],
  taskSelectors: [
    "main [data-above-composer-conversation-id]",
    "main [data-local-conversation-user-anchor]",
    "main [data-app-action-timeline-scroll]",
    "main [data-thread-scroll-footer]",
    "article",
    "main [role='article']",
    "[data-thread-id]",
    "[data-conversation-id]",
    "[data-testid*='conversation']",
    "[data-testid*='thread']",
    "[data-testid*='message']",
    "[data-testid*='turn']",
    "[data-message-author-role]",
    "[class*='conversation-turn']",
    "[class*='thread-message']",
  ],
});

export function rendererWindowProbe(compatibility) {
  let parsed;
  try {
    parsed = new URL(window.location.href);
  } catch {
    return { eligible: false, reason: "invalid-url" };
  }
  if (parsed.protocol !== "app:") {
    return { eligible: false, reason: "non-app-url" };
  }
  const initialRoute = parsed.searchParams.get("initialRoute");
  if (compatibility.excludedInitialRoutes.includes(initialRoute) ||
      compatibility.excludedPathFragments.some(
        (fragment) => parsed.pathname.includes(fragment))) {
    return { eligible: false, reason: "excluded-route" };
  }
  if (document.readyState === "loading") {
    return { eligible: false, reason: "dom-not-ready" };
  }

  const hasShell = compatibility.shellSelectors.some(
    (selector) => Boolean(document.querySelector(selector)));
  const hasSidebar = compatibility.sidebarSelectors.some(
    (selector) => Boolean(document.querySelector(selector)));
  const hasContent = compatibility.contentSelectors.some(
    (selector) => Boolean(document.querySelector(selector)));
  const hasComposer = compatibility.composerSelectors.some(
    (selector) => Boolean(document.querySelector(selector)));
  const isTask = compatibility.taskSelectors.some(
    (selector) => Boolean(document.querySelector(selector)));
  return {
    eligible: hasShell && hasSidebar && hasContent && hasComposer,
    reason: hasShell && hasSidebar && hasContent && hasComposer
      ? "main-window"
      : "shell-features-missing",
    featureVersion: compatibility.version,
    features: {
      shell: hasShell,
      sidebar: hasSidebar,
      content: hasContent,
      composer: hasComposer,
    },
    pageMode: isTask ? "task" : "home",
  };
}

export function rendererBootstrap(request) {
  const stateKey = "__CODEX_THEME_STUDIO_RENDERER_V1__";
  const styleId = "codex-theme-studio-style";
  const layerId = "codex-theme-studio-layer";
  const rootClass = "codex-theme-studio-active";
  const variantClasses = [
    "codex-theme-studio-variant-auto",
    "codex-theme-studio-variant-light",
    "codex-theme-studio-variant-dark",
  ];
  const cssVariables = [
    "--cts-background",
    "--cts-panel",
    "--cts-accent",
    "--cts-text",
    "--cts-muted",
    "--cts-border",
    "--cts-focus-x",
    "--cts-focus-y",
    "--cts-blur",
  ];
  const staticCss = `
html.codex-theme-studio-active {
  background: var(--cts-background) !important;
  color-scheme: normal;
  --color-text-foreground: var(--cts-text) !important;
  --color-text-foreground-secondary: var(--cts-muted) !important;
  --color-icon-primary: var(--cts-text) !important;
  --color-icon-secondary: var(--cts-muted) !important;
  --color-text-accent: var(--cts-accent) !important;
  --color-border: var(--cts-border) !important;
  --color-border-focus: var(--cts-accent) !important;
  --color-background-panel: var(--cts-panel) !important;
  --color-background-surface: var(--cts-panel) !important;
  --color-background-control: var(--cts-panel) !important;
  --color-background-control-opaque: var(--cts-panel) !important;
  --color-background-elevated-primary: var(--cts-panel) !important;
  --color-background-elevated-primary-opaque: var(--cts-panel) !important;
  --color-background-elevated-secondary-opaque: var(--cts-panel) !important;
  --color-token-dropdown-background: var(--cts-panel) !important;
  --color-background-accent: color-mix(in srgb, var(--cts-accent) 16%, transparent) !important;
  --color-background-button-primary: var(--cts-accent) !important;
  --color-text-button-primary: var(--cts-background) !important;
  --color-accent-blue: var(--cts-accent) !important;
  --codex-base-accent: var(--cts-accent) !important;
  --codex-base-ink: var(--cts-text) !important;
  --codex-base-surface: var(--cts-panel) !important;
}
html.codex-theme-studio-variant-light { color-scheme: light; }
html.codex-theme-studio-variant-dark { color-scheme: dark; }
html.codex-theme-studio-active body {
  background: transparent !important;
  color: var(--cts-text);
}
html.codex-theme-studio-active body > :not(#codex-theme-studio-layer) {
  position: relative;
  z-index: 1;
}
html.codex-theme-studio-active main.main-surface {
  background: transparent !important;
  box-shadow: none !important;
}
html.codex-theme-studio-active[data-codex-theme-studio-page="task-off"] main.main-surface {
  background: var(--cts-background) !important;
}
#codex-theme-studio-layer {
  position: fixed;
  inset: 0;
  z-index: 0;
  overflow: hidden;
  pointer-events: none !important;
  user-select: none !important;
  contain: strict;
}
#codex-theme-studio-layer .cts-background,
#codex-theme-studio-layer .cts-overlay {
  position: absolute;
  inset: 0;
  pointer-events: none !important;
}
#codex-theme-studio-layer .cts-background {
  background-position: var(--cts-focus-x) var(--cts-focus-y);
  background-repeat: no-repeat;
  filter: blur(var(--cts-blur));
  transform: scale(1.015);
  transform-origin: var(--cts-focus-x) var(--cts-focus-y);
}
html.codex-theme-studio-active aside {
  background-color: color-mix(in srgb, var(--cts-panel) 88%, transparent) !important;
  border-color: var(--cts-border) !important;
}
html.codex-theme-studio-active aside.app-shell-left-panel nav {
  background: transparent !important;
}
html.codex-theme-studio-active nav[class*="navigation"] {
  background: transparent !important;
  box-shadow: none !important;
}
html.codex-theme-studio-active .composer-surface-chrome {
  background-color: var(--cts-panel) !important;
  box-shadow: 0 0 0 1px var(--cts-border) !important;
  -webkit-backdrop-filter: none !important;
  backdrop-filter: none !important;
}
html.codex-theme-studio-active .sticky.bottom-0
  [class*="bg-gradient-to-t"][class*="from-token-main-surface-primary"] {
  background-image: none !important;
}
html.codex-theme-studio-active .sticky.bottom-0
  [class*="bg-gradient-to-t"][class*="from-token-main-surface-primary"][class*="to-transparent"] {
  background-color: transparent !important;
}
html.codex-theme-studio-active [class*="elevation-prominent"] {
  background-color: var(--cts-panel) !important;
  box-shadow:
    0 0 0 1px var(--cts-border),
    0 18px 50px rgba(0, 0, 0, 0.42) !important;
}
html.codex-theme-studio-active :where(input, textarea, [contenteditable="true"])::placeholder {
  color: var(--cts-muted) !important;
}
html.codex-theme-studio-active :where(button, a):focus-visible {
  outline: 2px solid var(--cts-accent) !important;
  outline-offset: 2px;
}
html.codex-theme-studio-active ::selection {
  background: color-mix(in srgb, var(--cts-accent) 38%, transparent);
  color: var(--cts-text);
}`;

  const compatibility = request?.compatibility;
  const payload = request?.payload;
  const generation = request?.generation;
  if (!compatibility || compatibility.version !== 1 ||
      !payload || payload.runtimeVersion !== 1 ||
      !Number.isSafeInteger(generation) || generation < 1) {
    return { eligible: false, applied: false, reason: "invalid-request" };
  }

  const existing = window[stateKey];
  const urlStatus = assessUrl(window.location.href, compatibility);
  if (!urlStatus.eligible) {
    existing?.cleanup(existing.generation);
    return { eligible: false, applied: false, reason: urlStatus.reason };
  }
  if (document.readyState === "loading") {
    return { eligible: false, applied: false, reason: "dom-not-ready" };
  }

  const features = probeFeatures(document, compatibility);
  if (!features.eligible) {
    existing?.cleanup(existing.generation);
    return {
      eligible: false,
      applied: false,
      reason: "shell-features-missing",
      featureVersion: compatibility.version,
    };
  }

  if (existing?.generation === generation &&
      existing?.themeId === payload.themeId) {
    existing.ensure();
    return existing.snapshot(false);
  }
  existing?.cleanup(existing.generation);

  const root = document.documentElement;
  const style = document.createElement("style");
  style.id = styleId;
  style.dataset.runtimeVersion = String(payload.runtimeVersion);
  style.textContent = staticCss;

  const layer = document.createElement("div");
  layer.id = layerId;
  layer.setAttribute("aria-hidden", "true");
  layer.dataset.runtimeVersion = String(payload.runtimeVersion);
  const background = document.createElement("div");
  background.className = "cts-background";
  const overlay = document.createElement("div");
  overlay.className = "cts-overlay";
  layer.append(background, overlay);

  const binary = atob(payload.art.base64);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index += 1) {
    bytes[index] = binary.charCodeAt(index);
  }
  const blobUrl = URL.createObjectURL(
    new Blob([bytes], { type: payload.art.contentType }));
  background.style.backgroundImage = `url("${blobUrl}")`;
  background.style.backgroundSize = payload.art.size;
  const managedMainSurfaces = new Map();

  root.classList.add(rootClass);
  root.classList.add(`codex-theme-studio-variant-${payload.variant}`);
  root.dataset.codexThemeStudioRuntime = String(payload.runtimeVersion);
  root.dataset.codexThemeStudioGeneration = String(generation);
  setVariable(root, "--cts-background", payload.palette.background);
  setVariable(root, "--cts-panel", payload.palette.panel);
  setVariable(root, "--cts-accent", payload.palette.accent);
  setVariable(root, "--cts-text", payload.palette.text);
  setVariable(root, "--cts-muted", payload.palette.muted);
  setVariable(root, "--cts-border", payload.palette.border);
  setVariable(root, "--cts-focus-x", `${payload.art.focusXPercent}%`);
  setVariable(root, "--cts-focus-y", `${payload.art.focusYPercent}%`);
  setVariable(root, "--cts-blur", `${payload.art.blur}px`);

  document.head.append(style);
  document.body.prepend(layer);

  let lastPageMode = "";
  const state = {
    generation,
    themeId: payload.themeId,
    blobUrl,
    cleaned: false,
    ensure,
    cleanup,
    snapshot,
  };
  window[stateKey] = state;

  const observer = new MutationObserver(() => ensure());
  observer.observe(document.body, { childList: true, subtree: true });
  const navigationHandler = () => ensure();
  window.addEventListener("hashchange", navigationHandler);
  window.addEventListener("popstate", navigationHandler);
  const intervalId = window.setInterval(ensure, 750);

  ensure();
  return snapshot(true);

  function ensure() {
    if (state.cleaned || window[stateKey] !== state) {
      return;
    }
    if (!style.isConnected) {
      document.head.append(style);
    }
    if (!layer.isConnected) {
      document.body.prepend(layer);
    }
    updatePageMode();
  }

  function updatePageMode() {
    const isTask = compatibility.taskSelectors.some(
      (selector) => Boolean(document.querySelector(selector)));
    const pageMode = isTask ? `task-${payload.art.taskMode}` : "home";
    updateMainSurfaces(pageMode);
    if (lastPageMode === pageMode) {
      return;
    }
    lastPageMode = pageMode;
    layer.dataset.pageMode = pageMode;
    root.dataset.codexThemeStudioPage = pageMode;

    if (pageMode === "home") {
      background.style.display = "";
      overlay.style.display = "";
      background.style.opacity = String(payload.art.homeOpacity);
      overlay.style.backgroundColor =
        rgbaFromHex(payload.palette.background, payload.art.homeOverlay);
      return;
    }
    if (payload.art.taskMode === "off") {
      background.style.display = "";
      overlay.style.display = "";
      background.style.opacity = String(payload.art.homeOpacity);
      overlay.style.backgroundColor =
        rgbaFromHex(payload.palette.background, payload.art.homeOverlay);
      return;
    }
    background.style.display = "";
    overlay.style.display = "";
    background.style.opacity = String(payload.art.taskOpacity);
    overlay.style.backgroundColor = "transparent";
  }

  function updateMainSurfaces(pageMode) {
    const surfaceBackground = pageMode === "task-off"
      ? "var(--cts-background)"
      : pageMode === "task-banner" || pageMode === "task-ambient"
        ? rgbaFromHex(payload.palette.background, payload.art.taskOverlay)
        : "transparent";
    for (const surface of document.querySelectorAll("main.main-surface")) {
      if (!managedMainSurfaces.has(surface)) {
        managedMainSurfaces.set(surface, {
          value: surface.style.getPropertyValue("background"),
          priority: surface.style.getPropertyPriority("background"),
        });
      }
      surface.style.setProperty(
        "background",
        surfaceBackground,
        "important");
    }
  }

  function cleanup(expectedGeneration) {
    if (state.cleaned ||
        expectedGeneration !== state.generation ||
        window[stateKey] !== state) {
      return false;
    }
    state.cleaned = true;
    observer.disconnect();
    window.removeEventListener("hashchange", navigationHandler);
    window.removeEventListener("popstate", navigationHandler);
    window.clearInterval(intervalId);
    for (const [surface, original] of managedMainSurfaces) {
      if (original.value) {
        surface.style.setProperty(
          "background",
          original.value,
          original.priority);
      } else {
        surface.style.removeProperty("background");
      }
    }
    managedMainSurfaces.clear();
    style.remove();
    layer.remove();
    URL.revokeObjectURL(blobUrl);
    root.classList.remove(rootClass);
    for (const className of variantClasses) {
      root.classList.remove(className);
    }
    for (const variable of cssVariables) {
      root.style.removeProperty(variable);
    }
    delete root.dataset.codexThemeStudioRuntime;
    delete root.dataset.codexThemeStudioGeneration;
    delete root.dataset.codexThemeStudioPage;
    delete window[stateKey];
    return true;
  }

  function snapshot(applied) {
    return {
      eligible: true,
      applied,
      generation,
      runtimeVersion: payload.runtimeVersion,
      pageMode: lastPageMode,
      featureVersion: compatibility.version,
    };
  }

  function assessUrl(rawUrl, config) {
    let parsed;
    try {
      parsed = new URL(rawUrl);
    } catch {
      return { eligible: false, reason: "invalid-url" };
    }
    if (parsed.protocol !== "app:") {
      return { eligible: false, reason: "non-app-url" };
    }
    const initialRoute = parsed.searchParams.get("initialRoute");
    if (config.excludedInitialRoutes.includes(initialRoute) ||
        config.excludedPathFragments.some(
          (fragment) => parsed.pathname.includes(fragment))) {
      return { eligible: false, reason: "excluded-route" };
    }
    return { eligible: true };
  }

  function probeFeatures(targetDocument, config) {
    const hasShell = config.shellSelectors.some(
      (selector) => Boolean(targetDocument.querySelector(selector)));
    const hasSidebar = config.sidebarSelectors.some(
      (selector) => Boolean(targetDocument.querySelector(selector)));
    const hasContent = config.contentSelectors.some(
      (selector) => Boolean(targetDocument.querySelector(selector)));
    const hasComposer = config.composerSelectors.some(
      (selector) => Boolean(targetDocument.querySelector(selector)));
    return {
      eligible: hasShell && hasContent && hasSidebar && hasComposer,
      hasShell,
      hasSidebar,
      hasContent,
      hasComposer,
    };
  }

  function setVariable(element, name, value) {
    element.style.setProperty(name, value);
  }

  function rgbaFromHex(value, opacity) {
    const red = Number.parseInt(value.slice(1, 3), 16);
    const green = Number.parseInt(value.slice(3, 5), 16);
    const blue = Number.parseInt(value.slice(5, 7), 16);
    const alpha = value.length === 9
      ? (Number.parseInt(value.slice(7, 9), 16) / 255) * opacity
      : opacity;
    return `rgba(${red}, ${green}, ${blue}, ${alpha})`;
  }
}

export function createRendererExpression(payload, generation) {
  return `(${rendererBootstrap.toString()})(${JSON.stringify({
    compatibility: rendererCompatibility,
    payload,
    generation,
  })})`;
}

export function createRendererCleanupExpression(expectedGeneration) {
  return `(() => {
    const state = window.__CODEX_THEME_STUDIO_RENDERER_V1__;
    return state ? state.cleanup(${JSON.stringify(expectedGeneration)}) : false;
  })()`;
}
