import assert from "node:assert/strict";
import { EventEmitter } from "node:events";
import test from "node:test";
import vm from "node:vm";
import {
  createMainOperationExpression,
  mainRuntimeBootstrap,
} from "./main-runtime.mjs";

test("main runtime isolates auxiliary windows and installs guarded hooks", async () => {
  const main = new FakeWindow(1, "app://-/index.html", true);
  const avatar = new FakeWindow(
    2,
    "app://-/index.html?initialRoute=/avatar-overlay",
    false);
  const environment = createMainEnvironment([main, avatar]);

  const state = await runMain(environment);

  assert.equal(state.active, true);
  assert.equal(state.appliedWindows, 1);
  assert.equal(state.auxiliaryWindows, 1);
  assert.equal(state.hookCount, 2);
  assert.equal(main.webContents.listenerCount("dom-ready"), 1);
  assert.equal(avatar.webContents.listenerCount("dom-ready"), 1);
  assert.equal(environment.app.listenerCount("browser-window-created"), 1);
});

test("managed apply pauses only the current process OkkSkin hook", async () => {
  const main = new FakeWindow(1, "app://-/index.html", true);
  const avatar = new FakeWindow(
    2,
    "app://-/index.html?initialRoute=/avatar-overlay",
    false);
  const environment = createMainEnvironment([main, avatar]);
  environment.context.__okkskinRJS = "active-hook";

  await runMain(environment);

  assert.equal(environment.context.__okkskinRJS, "");
  assert.equal(
    main.webContents.expressions.some(expression => expression.includes("okkskin-style")),
    true);
  assert.equal(
    avatar.webContents.expressions.some(expression => expression.includes("okkskin-style")),
    false);
});

test("status detects and cleanup removes OkkSkin without managed state", async () => {
  const main = new FakeWindow(1, "app://-/index.html", true);
  main.webContents.okkSkinActive = true;
  const avatar = new FakeWindow(
    2,
    "app://-/index.html?initialRoute=/avatar-overlay",
    false);
  avatar.webContents.okkSkinActive = true;
  const environment = createMainEnvironment([main, avatar]);
  environment.context.__okkskinRJS = "active-hook";

  const status = await runOperation(environment, "status");
  assert.equal(status.knownExternalThemeActive, true);

  const cleaned = await runOperation(environment, "cleanup");
  assert.equal(cleaned.active, false);
  assert.equal(cleaned.knownExternalThemeActive, false);
  assert.equal(environment.context.__okkskinRJS, "");
  assert.equal(main.webContents.okkSkinActive, false);
  assert.equal(avatar.webContents.okkSkinActive, true);
});

test("late DOM, renderer refresh, and future windows are ensured", async () => {
  const late = new FakeWindow(1, "app://-/index.html", false);
  const environment = createMainEnvironment([late]);
  await runMain(environment);
  assert.equal(environment.context.__CODEX_THEME_STUDIO_MAIN_V1__.snapshot().appliedWindows, 0);

  late.webContents.rendererEligible = true;
  late.webContents.emit("dom-ready");
  await tick();
  assert.equal(environment.context.__CODEX_THEME_STUDIO_MAIN_V1__.snapshot().appliedWindows, 1);

  const future = new FakeWindow(3, "app://-/index.html", true);
  environment.windows.push(future);
  environment.app.emit("browser-window-created", {}, future);
  await tick();
  assert.equal(environment.context.__CODEX_THEME_STUDIO_MAIN_V1__.snapshot().appliedWindows, 2);
  assert.equal(future.webContents.listenerCount("dom-ready"), 1);
});

test("replacement disables old hooks and stale cleanup cannot remove new generation", async () => {
  const main = new FakeWindow(1, "app://-/index.html", true);
  const environment = createMainEnvironment([main]);
  await runMain(environment);
  const oldState = environment.context.__CODEX_THEME_STUDIO_MAIN_V1__;

  const replaced = await runMain(environment, "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee");

  assert.equal(replaced.generation, 2);
  assert.equal(await oldState.stop(1, true), false);
  assert.equal(environment.context.__CODEX_THEME_STUDIO_MAIN_V1__.generation, 2);
  assert.equal(environment.app.listenerCount("browser-window-created"), 1);
  assert.equal(main.webContents.listenerCount("dom-ready"), 1);
});

test("cleanup removes hooks and prevents future automatic injection", async () => {
  const main = new FakeWindow(1, "app://-/index.html", true);
  const environment = createMainEnvironment([main]);
  await runMain(environment);
  const state = environment.context.__CODEX_THEME_STUDIO_MAIN_V1__;

  assert.equal(await state.stop(state.generation, true), true);
  assert.equal(environment.context.__CODEX_THEME_STUDIO_MAIN_V1__, undefined);
  assert.equal(environment.app.listenerCount("browser-window-created"), 0);
  assert.equal(main.webContents.listenerCount("dom-ready"), 0);

  const future = new FakeWindow(4, "app://-/index.html", true);
  environment.app.emit("browser-window-created", {}, future);
  await tick();
  assert.equal(future.webContents.executeCount, 0);
});

async function runMain(
  environment,
  themeId = "1296cb77-2297-4992-af72-5c3cc40b32be") {
  const request = {
    runtimeVersion: 1,
    themeId,
    rendererBootstrapSource: "() => ({ eligible: true, applied: true })",
    compatibility: { version: 1 },
    payload: { runtimeVersion: 1, themeId },
  };
  const script = new vm.Script(
    `(${mainRuntimeBootstrap.toString()})(${JSON.stringify(request)})`);
  return await script.runInContext(environment.context);
}

async function runOperation(environment, operation) {
  const script = new vm.Script(createMainOperationExpression(operation));
  return await script.runInContext(environment.context);
}

function createMainEnvironment(windows) {
  const app = new EventEmitter();
  const electron = {
    app,
    BrowserWindow: {
      getAllWindows: () => windows,
    },
  };
  const context = vm.createContext({
    URL,
    process: {
      mainModule: {
        require(name) {
          assert.equal(name, "electron");
          return electron;
        },
      },
    },
  });
  return { context, app, windows };
}

class FakeWindow {
  constructor(id, url, rendererEligible) {
    this.webContents = new FakeWebContents(id, url, rendererEligible);
  }
}

class FakeWebContents extends EventEmitter {
  constructor(id, url, rendererEligible) {
    super();
    this.id = id;
    this.url = url;
    this.rendererEligible = rendererEligible;
    this.executeCount = 0;
    this.expressions = [];
    this.okkSkinActive = false;
  }

  isDestroyed() {
    return false;
  }

  getURL() {
    return this.url;
  }

  async executeJavaScript(expression) {
    this.executeCount += 1;
    this.expressions.push(expression);
    if (expression.includes("classList.contains(\"okkskin\")")) {
      return this.okkSkinActive;
    }
    if (expression.includes("style.remove()") && expression.includes("--ok-art")) {
      this.okkSkinActive = false;
      return true;
    }
    if (expression.includes("state.cleanup")) {
      return true;
    }
    return {
      eligible: this.rendererEligible,
      applied: this.rendererEligible,
      generation: 1,
    };
  }
}

function tick() {
  return new Promise((resolve) => setImmediate(resolve));
}
