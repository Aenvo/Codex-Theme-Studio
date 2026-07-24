import assert from "node:assert/strict";
import test from "node:test";
import vm from "node:vm";
import { prepareRendererPayload } from "./renderer-payload.mjs";
import { createInput } from "./renderer-test-fixture.mjs";
import {
  rendererBootstrap,
  rendererCompatibility,
  rendererWindowProbe,
} from "./renderer-runtime.mjs";

test("applies once to a complete main window and preserves pointer interaction", () => {
  const environment = createEnvironment();
  environment.addMainFeatures();

  const result = runRenderer(environment, createPayload(), 1);

  assert.equal(result.eligible, true);
  assert.equal(result.applied, true);
  assert.equal(result.pageMode, "home");
  assert.equal(environment.countById("codex-theme-studio-style"), 1);
  assert.equal(environment.countById("codex-theme-studio-layer"), 1);
  assert.equal(environment.createdBlobUrls.length, 1);
  assert.match(environment.findById("codex-theme-studio-style").textContent, /pointer-events: none/);
  assert.match(
    environment.findById("codex-theme-studio-style").textContent,
    /main\.main-surface\s*\{\s*background: transparent !important;/);
  assert.match(
    environment.findById("codex-theme-studio-style").textContent,
    /--color-background-surface: var\(--cts-panel\) !important;/);
  assert.match(
    environment.findById("codex-theme-studio-style").textContent,
    /--color-token-dropdown-background: var\(--cts-panel\) !important;/);
  assert.match(
    environment.findById("codex-theme-studio-style").textContent,
    /\.composer-surface-chrome\s*\{/);
  assert.match(
    environment.findById("codex-theme-studio-style").textContent,
    /\.composer-surface-chrome\s*\{[^}]*box-shadow:\s*0 0 0 1px var\(--cts-border\) !important;/s);
  assert.match(
    environment.findById("codex-theme-studio-style").textContent,
    /\.composer-surface-chrome\s*\{[^}]*backdrop-filter:\s*none !important;/s);
  assert.match(
    environment.findById("codex-theme-studio-style").textContent,
    /:where\(button, a\):focus-visible\s*\{[^}]*outline:\s*2px solid var\(--cts-accent\) !important;/s);
  assert.doesNotMatch(
    environment.findById("codex-theme-studio-style").textContent,
    /:where\([^)]*(?:input|textarea|contenteditable)[^)]*\):focus-visible/);
  assert.match(
    environment.findById("codex-theme-studio-style").textContent,
    /\.sticky\.bottom-0\s*\[class\*="bg-gradient-to-t"\]\[class\*="from-token-main-surface-primary"\]\s*\{[^}]*background-image:\s*none !important;/s);
  assert.match(
    environment.findById("codex-theme-studio-style").textContent,
    /\.sticky\.bottom-0\s*\[class\*="bg-gradient-to-t"\]\[class\*="from-token-main-surface-primary"\]\[class\*="to-transparent"\]\s*\{[^}]*background-color:\s*transparent !important;/s);
  assert.doesNotMatch(
    environment.findById("codex-theme-studio-style").textContent,
    /\[data-page-mode="task-banner"\][^}]*\{[^}]*(?:mask-image|mask-size|mask-repeat|bottom:\s*auto|height:\s*min\(32vh, 320px\))/s);
  assert.doesNotMatch(
    environment.findById("codex-theme-studio-style").textContent,
    /\.composer-surface-chrome\s*\{[^}]*0 12px 32px/s);
  assert.doesNotMatch(
    environment.findById("codex-theme-studio-style").textContent,
    /:where\(aside, nav\)/);
  assert.equal(
    environment.document.documentElement.classList.contains(
      "codex-theme-studio-active"),
    true);
  assert.equal(
    environment.document.mainSurface.style.getPropertyValue("background"),
    "transparent");
  assert.equal(
    environment.document.mainSurface.style.getPropertyPriority("background"),
    "important");
  const layer = environment.findById("codex-theme-studio-layer");
  assert.equal(layer.children[0].style.opacity, "0.82");
  assert.equal(
    layer.children[1].style.backgroundColor,
    "rgba(18, 16, 24, 0.25)");
});

test("leaves avatar overlay and incomplete auxiliary windows untouched", () => {
  const avatar = createEnvironment(
    "app://-/index.html?initialRoute=/avatar-overlay");
  avatar.addMainFeatures();
  const auxiliary = createEnvironment();
  auxiliary.document.selectorMatches.add("#root");

  const avatarResult = runRenderer(avatar, createPayload(), 1);
  const auxiliaryResult = runRenderer(auxiliary, createPayload(), 1);

  assert.equal(avatarResult.reason, "excluded-route");
  assert.equal(auxiliaryResult.reason, "shell-features-missing");
  assert.equal(avatar.countById("codex-theme-studio-style"), 0);
  assert.equal(avatar.countById("codex-theme-studio-layer"), 0);
  assert.equal(auxiliary.countById("codex-theme-studio-style"), 0);
});

test("recognizes a main window whose sidebar is collapsed behind its toggle", () => {
  const environment = createEnvironment();
  for (const selector of [
    "#root",
    "main",
    "textarea",
    "[aria-label*='sidebar' i]",
  ]) {
    environment.document.selectorMatches.add(selector);
  }

  const probe = new vm.Script(
    `(${rendererWindowProbe.toString()})(${JSON.stringify(rendererCompatibility)})`)
    .runInContext(environment.context);
  const result = runRenderer(environment, createPayload(), 1);

  assert.equal(probe.features.sidebar, true);
  assert.equal(result.eligible, true);
  assert.equal(result.applied, true);
});

test("read-only window probe returns feature flags without applying a theme", () => {
  const environment = createEnvironment();
  environment.addMainFeatures();
  const script = new vm.Script(
    `(${rendererWindowProbe.toString()})(${JSON.stringify(rendererCompatibility)})`);

  const result = script.runInContext(environment.context);

  assert.equal(result.eligible, true);
  assert.deepEqual(
    JSON.parse(JSON.stringify(result.features)),
    { shell: true, sidebar: true, content: true, composer: true });
  assert.equal(environment.countById("codex-theme-studio-style"), 0);
});

test("late DOM can apply after the complete shell appears", () => {
  const environment = createEnvironment();
  const first = runRenderer(environment, createPayload(), 1);

  environment.addMainFeatures();
  const second = runRenderer(environment, createPayload(), 1);

  assert.equal(first.eligible, false);
  assert.equal(second.eligible, true);
  assert.equal(environment.countById("codex-theme-studio-layer"), 1);
});

test("repeat is idempotent and replacement revokes only the old Blob URL", () => {
  const environment = createEnvironment();
  environment.addMainFeatures();
  const firstPayload = createPayload();
  runRenderer(environment, firstPayload, 1);
  const oldState = environment.window.__CODEX_THEME_STUDIO_RENDERER_V1__;

  const repeated = runRenderer(environment, firstPayload, 1);
  const replacement = createPayload();
  replacement.themeId = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";
  runRenderer(environment, replacement, 2);

  assert.equal(repeated.applied, false);
  assert.equal(environment.countById("codex-theme-studio-style"), 1);
  assert.equal(environment.countById("codex-theme-studio-layer"), 1);
  assert.equal(environment.createdBlobUrls.length, 2);
  assert.deepEqual(environment.revokedBlobUrls, ["blob:vm-1"]);
  assert.equal(oldState.cleanup(1), false);
  assert.equal(environment.countById("codex-theme-studio-layer"), 1);
  assert.equal(
    environment.window.__CODEX_THEME_STUDIO_RENDERER_V1__.generation,
    2);
});

test("task banner and off modes scope overlays without reading page text", () => {
  const environment = createEnvironment();
  environment.addMainFeatures();
  environment.document.selectorMatches.add("article");

  const bannerPayload = createPayload();
  bannerPayload.art.taskMode = "banner";
  const banner = runRenderer(environment, bannerPayload, 1);

  assert.equal(banner.pageMode, "task-banner");
  let layer = environment.findById("codex-theme-studio-layer");
  assert.equal(layer.children[0].style.display, "");
  assert.equal(layer.children[1].style.display, "");
  assert.equal(layer.children[0].style.opacity, "0.32");
  assert.equal(layer.children[1].style.backgroundColor, "transparent");
  assert.equal(
    environment.document.mainSurface.style.getPropertyValue("background"),
    "rgba(18, 16, 24, 0.68)");

  const offPayload = createPayload();
  offPayload.art.taskMode = "off";
  const off = runRenderer(environment, offPayload, 2);

  assert.equal(off.pageMode, "task-off");
  layer = environment.findById("codex-theme-studio-layer");
  assert.equal(layer.children[0].style.display, "");
  assert.equal(layer.children[1].style.display, "");
  assert.equal(layer.children[0].style.opacity, "0.82");
  assert.equal(layer.children[1].style.backgroundColor, "rgba(18, 16, 24, 0.25)");
  assert.equal(
    environment.document.mainSurface.style.getPropertyValue("background"),
    "var(--cts-background)");
});

test("route changes update page mode without replacing the current Blob URL", () => {
  const environment = createEnvironment();
  environment.addMainFeatures();
  const applied = runRenderer(environment, createPayload(), 1);
  assert.equal(applied.pageMode, "home");

  environment.document.selectorMatches.add("article");
  environment.dispatchWindowEvent("popstate");

  const state = environment.window.__CODEX_THEME_STUDIO_RENDERER_V1__;
  assert.equal(state.snapshot().pageMode, "task-ambient");
  const layer = environment.findById("codex-theme-studio-layer");
  assert.equal(layer.children[0].style.opacity, "0.32");
  assert.equal(layer.children[1].style.backgroundColor, "transparent");
  assert.equal(
    environment.document.mainSurface.style.getPropertyValue("background"),
    "rgba(18, 16, 24, 0.68)");
  assert.equal(environment.createdBlobUrls.length, 1);
  assert.deepEqual(environment.revokedBlobUrls, []);
});

test("cleanup removes styles, classes, hooks, and the current Blob URL", () => {
  const environment = createEnvironment();
  environment.addMainFeatures();
  runRenderer(environment, createPayload(), 1);

  const state = environment.window.__CODEX_THEME_STUDIO_RENDERER_V1__;
  assert.equal(state.cleanup(1), true);

  assert.equal(environment.countById("codex-theme-studio-style"), 0);
  assert.equal(environment.countById("codex-theme-studio-layer"), 0);
  assert.deepEqual(environment.revokedBlobUrls, ["blob:vm-1"]);
  assert.equal(
    environment.document.documentElement.classList.contains(
      "codex-theme-studio-active"),
    false);
  assert.equal(environment.window.__CODEX_THEME_STUDIO_RENDERER_V1__, undefined);
  assert.equal(
    environment.document.mainSurface.style.getPropertyValue("background"),
    "");
});

function createPayload() {
  return prepareRendererPayload(createInput());
}

function runRenderer(environment, payload, generation) {
  const request = {
    compatibility: rendererCompatibility,
    payload,
    generation,
  };
  const script = new vm.Script(
    `(${rendererBootstrap.toString()})(${JSON.stringify(request)})`);
  return script.runInContext(environment.context);
}

function createEnvironment(href = "app://-/index.html") {
  const document = new FakeDocument();
  const createdBlobUrls = [];
  const revokedBlobUrls = [];
  const listeners = new Map();
  const intervals = new Set();
  class RuntimeUrl extends URL {}
  RuntimeUrl.createObjectURL = () => {
    const value = `blob:vm-${createdBlobUrls.length + 1}`;
    createdBlobUrls.push(value);
    return value;
  };
  RuntimeUrl.revokeObjectURL = (value) => revokedBlobUrls.push(value);

  const window = {
    location: { href },
    addEventListener(name, handler) {
      listeners.set(name, handler);
    },
    removeEventListener(name) {
      listeners.delete(name);
    },
    setInterval(handler) {
      intervals.add(handler);
      return handler;
    },
    clearInterval(handler) {
      intervals.delete(handler);
    },
  };
  const context = vm.createContext({
    window,
    document,
    URL: RuntimeUrl,
    Blob,
    Uint8Array,
    atob: (value) => Buffer.from(value, "base64").toString("binary"),
    MutationObserver: class {
      observe() {
      }

      disconnect() {
      }
    },
  });

  return {
    context,
    window,
    document,
    createdBlobUrls,
    revokedBlobUrls,
    dispatchWindowEvent(name) {
      const handler = listeners.get(name);
      assert.equal(typeof handler, "function");
      handler();
    },
    addMainFeatures() {
      for (const selector of ["#root", "aside", "main", "textarea"]) {
        document.selectorMatches.add(selector);
      }
    },
    findById(id) {
      return findElement(document.documentElement, id);
    },
    countById(id) {
      return countElements(document.documentElement, id);
    },
  };
}

class FakeDocument {
  constructor() {
    this.readyState = "complete";
    this.selectorMatches = new Set();
    this.documentElement = new FakeElement("html");
    this.head = new FakeElement("head");
    this.body = new FakeElement("body");
    this.mainSurface = new FakeElement("main");
    this.documentElement.append(this.head, this.body);
  }

  createElement(tagName) {
    return new FakeElement(tagName);
  }

  querySelector(selector) {
    return this.selectorMatches.has(selector) ? { selector } : null;
  }

  querySelectorAll(selector) {
    return selector === "main.main-surface" &&
      this.selectorMatches.has("main")
      ? [this.mainSurface]
      : [];
  }
}

class FakeElement {
  constructor(tagName) {
    this.tagName = tagName;
    this.id = "";
    this.className = "";
    this.textContent = "";
    this.dataset = {};
    this.children = [];
    this.parent = null;
    this.connected = false;
    this.style = new FakeStyle();
    this.classList = new FakeClassList();
    this.attributes = new Map();
  }

  get isConnected() {
    return this.connected;
  }

  append(...children) {
    for (const child of children) {
      child.remove();
      child.parent = this;
      this.children.push(child);
      child.setConnected(this.connected || this.tagName === "html");
    }
  }

  prepend(...children) {
    for (const child of [...children].reverse()) {
      child.remove();
      child.parent = this;
      this.children.unshift(child);
      child.setConnected(this.connected || this.tagName === "html");
    }
  }

  remove() {
    if (this.parent) {
      const index = this.parent.children.indexOf(this);
      if (index >= 0) {
        this.parent.children.splice(index, 1);
      }
    }
    this.parent = null;
    this.setConnected(false);
  }

  setAttribute(name, value) {
    this.attributes.set(name, value);
  }

  setConnected(value) {
    this.connected = value;
    for (const child of this.children) {
      child.setConnected(value);
    }
  }
}

class FakeStyle {
  constructor() {
    this.properties = new Map();
  }

  setProperty(name, value, priority = "") {
    this.properties.set(name, { value, priority });
  }

  removeProperty(name) {
    this.properties.delete(name);
  }

  getPropertyValue(name) {
    return this.properties.get(name)?.value ?? "";
  }

  getPropertyPriority(name) {
    return this.properties.get(name)?.priority ?? "";
  }
}

class FakeClassList {
  constructor() {
    this.values = new Set();
  }

  add(...values) {
    for (const value of values) {
      this.values.add(value);
    }
  }

  remove(...values) {
    for (const value of values) {
      this.values.delete(value);
    }
  }

  contains(value) {
    return this.values.has(value);
  }
}

function findElement(element, id) {
  if (element.id === id) {
    return element;
  }
  for (const child of element.children) {
    const found = findElement(child, id);
    if (found) {
      return found;
    }
  }
  return null;
}

function countElements(element, id) {
  return (element.id === id ? 1 : 0) +
    element.children.reduce(
      (count, child) => count + countElements(child, id),
      0);
}
