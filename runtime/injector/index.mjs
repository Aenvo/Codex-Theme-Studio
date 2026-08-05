import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";
import path from "node:path";
import {
  createMainApplyExpression,
  createMainOperationExpression,
  createMainProbeExpression,
} from "./main-runtime.mjs";
import {
  prepareRendererPayload,
  readStructuredInput,
} from "./renderer-payload.mjs";
import {
  rendererCompatibility,
  rendererWindowProbe,
} from "./renderer-runtime.mjs";
import {
  assertPortOwner,
  classifyAppRoutes,
  evaluate,
  fetchInspectorMetadata,
} from "./security.mjs";

const service = "CodexThemeStudio.Injector";
const version = "0.3.0";
const protocolVersion = 1;
const inspectorPort = 9229;
const inspectorCloseTimeoutMs = 12000;
const inspectorClosedSettleMs = 3000;
const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const discoveryScript = path.join(scriptDirectory, "windows-discovery.ps1");
const rendererCompatibilityProbeExpression =
  `(${rendererWindowProbe.toString()})(${JSON.stringify(rendererCompatibility)})`;
const canaryExpression = `(async () => {
  const structure = ${rendererCompatibilityProbeExpression};
  if (!structure?.eligible) {
    return { qualified: false, applied: false, cleaned: true };
  }
  const id = "codex-theme-studio-compat-canary";
  const className = "codex-theme-studio-compat-canary";
  const variableName = "--codex-theme-studio-compat-canary";
  document.getElementById(id)?.remove();
  document.documentElement.classList.remove(className);
  document.documentElement.style.removeProperty(variableName);
  const style = document.createElement("style");
  style.id = id;
  style.textContent = \`:root.\${className}{\${variableName}:1}\`;
  const marker = document.createElement("div");
  marker.id = \`\${id}-marker\`;
  marker.hidden = true;
  const blobUrl = URL.createObjectURL(new Blob(["canary"], { type: "text/plain" }));
  document.head.append(style);
  document.body.append(marker);
  document.documentElement.classList.add(className);
  const applied = getComputedStyle(document.documentElement)
    .getPropertyValue(variableName).trim() === "1" &&
    document.documentElement.classList.contains(className) &&
    Boolean(document.getElementById(marker.id));
  style.remove();
  marker.remove();
  document.documentElement.classList.remove(className);
  document.documentElement.style.removeProperty(variableName);
  URL.revokeObjectURL(blobUrl);
  let blobReleased = false;
  try {
    await fetch(blobUrl);
  } catch {
    blobReleased = true;
  }
  const cleaned = !document.getElementById(id) &&
    !document.getElementById(marker.id) &&
    !document.documentElement.classList.contains(className) &&
    getComputedStyle(document.documentElement)
      .getPropertyValue(variableName).trim() !== "1" &&
    blobReleased;
  return { qualified: true, applied, cleaned };
})()`;
const probeExpression = `(async () => {
  const result = {
    electronVersion: process.versions.electron || "",
    electronAvailable: false,
    browserWindowAvailable: false,
    executeJavaScriptAvailable: false,
    urls: [],
    eligibleWindowCount: 0,
    canaryApplied: false,
    canaryCleaned: false,
    diagnosticCode: null
  };
  try {
    const electron = process.mainModule.require("electron");
    result.electronAvailable = Boolean(electron);
    result.browserWindowAvailable = Boolean(
      electron?.BrowserWindow?.getAllWindows);
    if (!result.browserWindowAvailable) {
      result.diagnosticCode = "capability.browser_window_missing";
      return result;
    }
    const windows = electron.BrowserWindow.getAllWindows();
    result.urls = windows.map(window => window.webContents.getURL());
    const eligible = windows.filter(window => {
      const raw = window.webContents.getURL();
      try {
        const parsed = new URL(raw);
        return parsed.protocol === "app:" &&
          parsed.searchParams.get("initialRoute") !== "/avatar-overlay" &&
          !parsed.pathname.includes("/avatar-overlay") &&
          !parsed.pathname.includes("/pet-overlay");
      } catch {
        return false;
      }
    });
    result.executeJavaScriptAvailable = eligible.length > 0 && eligible.every(window =>
      typeof window.webContents.executeJavaScript === "function");
    if (eligible.length === 0 || !result.executeJavaScriptAvailable) {
      result.diagnosticCode = eligible.length === 0
        ? "capability.eligible_window_missing"
        : "capability.execute_javascript_missing";
      return result;
    }
    const checks = await Promise.all(eligible.map(window =>
      window.webContents.executeJavaScript(${JSON.stringify(canaryExpression)}, true)));
    const qualified = checks.filter(check => check?.qualified === true);
    result.eligibleWindowCount = qualified.length;
    if (qualified.length === 0) {
      result.diagnosticCode = "capability.eligible_window_missing";
      return result;
    }
    result.canaryApplied = qualified.every(check => check?.applied === true);
    result.canaryCleaned = qualified.every(check => check?.cleaned === true);
    if (!result.canaryApplied || !result.canaryCleaned) {
      result.diagnosticCode = result.canaryApplied
        ? "capability.canary_cleanup_failed"
        : "capability.canary_apply_failed";
    }
    return result;
  } catch {
    result.diagnosticCode = "capability.probe_failed";
    return result;
  }
})()`;

try {
  const command = process.argv[2];
  if (command === "self-test" && process.argv.length === 3) {
    outputSuccess({
      status: "ok",
      nodeVersion: process.version,
      capabilities: [
        "discover",
        "probe",
        "inspect-status",
        "close-inspector",
        "prepare",
        "renderer-probe",
        "renderer-apply",
        "renderer-ensure",
        "renderer-status",
        "renderer-cleanup",
      ],
    });
  } else if (command === "discover") {
    const options = parseTargetOptions(process.argv.slice(3), false);
    outputSuccess(await discover(options.executablePath));
  } else if (command === "prepare" && process.argv.length === 3) {
    const payload = prepareRendererPayload(await readStructuredInput(process.stdin));
    outputSuccess({
      prepared: {
        runtimeVersion: payload.runtimeVersion,
        themeId: payload.themeId,
        variant: payload.variant,
        taskMode: payload.art.taskMode,
        imageBytes: Buffer.from(payload.art.base64, "base64").length,
      },
    });
  } else if (command === "probe") {
    const options = parseTargetOptions(process.argv.slice(3), true);
    outputSuccess({ probe: await probe(options.processId, options.executablePath) });
  } else if (command === "inspect-status") {
    const options = parseTargetOptions(process.argv.slice(3), true);
    outputSuccess(await inspectStatus(options.processId, options.executablePath));
  } else if (command === "renderer-apply") {
    const options = parseTargetOptions(process.argv.slice(3), true);
    const payload = prepareRendererPayload(await readStructuredInput(process.stdin));
    outputSuccess({
      renderer: await executeRendererOperation(
        options.processId,
        options.executablePath,
        createMainApplyExpression(payload)),
    });
  } else if (command === "renderer-probe") {
    const options = parseTargetOptions(process.argv.slice(3), true);
    outputSuccess({
      renderer: await executeRendererOperation(
        options.processId,
        options.executablePath,
        createMainProbeExpression()),
    });
  } else if (command === "renderer-ensure") {
    const options = parseTargetOptions(process.argv.slice(3), true);
    outputSuccess({
      renderer: await executeRendererOperation(
        options.processId,
        options.executablePath,
        createMainOperationExpression("ensure")),
    });
  } else if (command === "renderer-status") {
    const options = parseTargetOptions(process.argv.slice(3), true);
    outputSuccess({
      renderer: await executeRendererOperation(
        options.processId,
        options.executablePath,
        createMainOperationExpression("status")),
    });
  } else if (command === "renderer-cleanup") {
    const options = parseTargetOptions(process.argv.slice(3), true);
    outputSuccess({
      renderer: await executeRendererOperation(
        options.processId,
        options.executablePath,
        createMainOperationExpression("cleanup")),
    });
  } else if (command === "close-inspector") {
    const options = parseTargetOptions(process.argv.slice(3), true);
    await closeInspectorForProcess(options.processId, options.executablePath);
    outputSuccess({ closed: true, processId: options.processId });
  } else {
    throw commandError(
      "invalid_arguments",
      "命令或参数无效。",
      false);
  }
} catch (error) {
  outputFailure(error);
}

async function discover(executablePath) {
  ensureWindows();
  const raw = await invokeDiscovery(
    "Discover",
    executablePath ? { ExecutablePath: executablePath } : {});
  if (!raw.package) {
    throw commandError(
      "codex_not_installed",
      "未检测到当前用户注册的官方 Microsoft Store Codex。",
      false);
  }
  validateTarget(raw.package, executablePath);

  return {
    installation: {
      packageFamilyName: raw.package.packageFamilyName,
      packageFullName: raw.package.packageFullName,
      version: raw.package.version,
      executablePath: raw.package.executablePath,
      publisherId: raw.package.publisherId,
      isStoreSigned: raw.package.signatureKind === "Store",
      source: raw.package.source === "manualExecutable" ? 1 : 0,
      identityAssessment:
        raw.package.packageFamilyName === "OpenAI.Codex_2p2nqsd0c76g0" &&
        raw.package.publisherId === "2p2nqsd0c76g0" &&
        raw.package.signatureKind === "Store" ? 0 : 1,
    },
    processes: (raw.processes ?? []).map(toPublicProcess),
  };
}

async function executeRendererOperation(processId, executablePath, expression) {
  ensureWindows();
  const before = await requireTrustedSnapshot(processId, executablePath);
  const initialPort = await getPortListeners();
  const openedAt = performance.now();
  let metadata;
  let result;
  try {
    metadata = await openInspectorForProcess(processId, initialPort);
    await assertSnapshotUnchanged(before, executablePath);
    result = await evaluate(metadata.webSocketUrl, expression, {
      timeoutMs: 8000,
      maxBytes: 256 * 1024,
      awaitPromise: true,
    });
    await assertSnapshotUnchanged(before, executablePath);
  } finally {
    if (!metadata) {
      metadata = await fetchInspectorMetadata(inspectorPort);
    }
    await requestInspectorClose(metadata.webSocketUrl);
    await waitForPortClosed(processId);
  }

  return {
    ...result,
    processId,
    inspectorWasAlreadyOpen: initialPort.length > 0,
    inspectorOpenDuration:
      millisecondsToTimeSpan(performance.now() - openedAt),
  };
}

async function probe(processId, executablePath) {
  ensureWindows();
  const before = await requireTrustedSnapshot(processId, executablePath);
  const initialPort = await getPortListeners();
  const openedAt = performance.now();
  let metadata;
  let probeData;
  try {
    metadata = await openInspectorForProcess(processId, initialPort);
    await assertSnapshotUnchanged(before, executablePath);
    const result = await evaluate(metadata.webSocketUrl, probeExpression, {
      timeoutMs: 8000,
      maxBytes: 256 * 1024,
      awaitPromise: true,
    });
    if (!result || typeof result.electronVersion !== "string" ||
        !Array.isArray(result.urls) ||
        typeof result.electronAvailable !== "boolean" ||
        typeof result.browserWindowAvailable !== "boolean" ||
        typeof result.executeJavaScriptAvailable !== "boolean" ||
        typeof result.eligibleWindowCount !== "number" ||
        typeof result.canaryApplied !== "boolean" ||
        typeof result.canaryCleaned !== "boolean") {
      throw commandError(
        "invalid_response",
        "只读探针返回结构无效。",
        false);
    }

    await assertSnapshotUnchanged(before, executablePath);
    probeData = {
      processId,
      processStartedAtUtc: before.startedAtUtc,
      electronVersion: result.electronVersion,
      windowCount: result.urls.length,
      routeTypes: classifyAppRoutes(result.urls),
      electronAvailable: result.electronAvailable,
      browserWindowAvailable: result.browserWindowAvailable,
      executeJavaScriptAvailable: result.executeJavaScriptAvailable,
      eligibleWindowCount: result.eligibleWindowCount,
      canaryApplied: result.canaryApplied,
      canaryCleaned: result.canaryCleaned,
      diagnosticCode: result.diagnosticCode,
    };
  } finally {
    if (!metadata) {
      metadata = await fetchInspectorMetadata(inspectorPort);
    }
    await requestInspectorClose(metadata.webSocketUrl);
    await waitForPortClosed(processId);
  }

  return {
    ...probeData,
    inspectorOpenDuration: millisecondsToTimeSpan(performance.now() - openedAt),
  };
}

async function inspectStatus(processId, executablePath) {
  ensureWindows();
  const before = await requireTrustedSnapshot(processId, executablePath);
  const initialPort = await getPortListeners();
  const openedAt = performance.now();
  let metadata;
  let probeData;
  let rendererData;
  try {
    metadata = await openInspectorForProcess(processId, initialPort);
    await assertSnapshotUnchanged(before, executablePath);

    const probeResult = await evaluate(metadata.webSocketUrl, probeExpression, {
      timeoutMs: 8000,
      maxBytes: 256 * 1024,
      awaitPromise: true,
    });
    if (!probeResult || typeof probeResult.electronVersion !== "string" ||
        !Array.isArray(probeResult.urls) ||
        typeof probeResult.electronAvailable !== "boolean" ||
        typeof probeResult.browserWindowAvailable !== "boolean" ||
        typeof probeResult.executeJavaScriptAvailable !== "boolean" ||
        typeof probeResult.eligibleWindowCount !== "number" ||
        typeof probeResult.canaryApplied !== "boolean" ||
        typeof probeResult.canaryCleaned !== "boolean") {
      throw commandError(
        "invalid_response",
        "只读探针返回结构无效。",
        false);
    }

    rendererData = await evaluate(
      metadata.webSocketUrl,
      createMainOperationExpression("status"),
      {
        timeoutMs: 8000,
        maxBytes: 256 * 1024,
        awaitPromise: true,
      });
    await assertSnapshotUnchanged(before, executablePath);
    probeData = {
      processId,
      processStartedAtUtc: before.startedAtUtc,
      electronVersion: probeResult.electronVersion,
      windowCount: probeResult.urls.length,
      routeTypes: classifyAppRoutes(probeResult.urls),
      electronAvailable: probeResult.electronAvailable,
      browserWindowAvailable: probeResult.browserWindowAvailable,
      executeJavaScriptAvailable: probeResult.executeJavaScriptAvailable,
      eligibleWindowCount: probeResult.eligibleWindowCount,
      canaryApplied: probeResult.canaryApplied,
      canaryCleaned: probeResult.canaryCleaned,
      diagnosticCode: probeResult.diagnosticCode,
    };
  } finally {
    if (!metadata) {
      metadata = await fetchInspectorMetadata(inspectorPort);
    }
    await requestInspectorClose(metadata.webSocketUrl);
    await waitForPortClosed(processId);
  }

  const duration = millisecondsToTimeSpan(performance.now() - openedAt);
  return {
    probe: {
      ...probeData,
      inspectorOpenDuration: duration,
    },
    renderer: {
      ...rendererData,
      processId,
      inspectorWasAlreadyOpen: initialPort.length > 0,
      inspectorOpenDuration: duration,
    },
  };
}

async function closeInspectorForProcess(processId, executablePath) {
  ensureWindows();
  const before = await requireTrustedSnapshot(processId, executablePath);
  const listeners = await getPortListeners();
  if (listeners.length === 0) {
    return;
  }
  assertPortOwner(listeners, processId);
  const metadata = await fetchInspectorMetadata(inspectorPort);
  await assertSnapshotUnchanged(before, executablePath);
  await requestInspectorClose(metadata.webSocketUrl);
  await waitForPortClosed(processId);
}

async function requestInspectorClose(webSocketUrl) {
  try {
    await evaluate(webSocketUrl, "process._debugEnd()", { timeoutMs: 1200 });
  } catch (error) {
    // A successful _debugEnd can close the socket before its response arrives.
    if (error?.code !== "inspector_unavailable" && error?.code !== "timeout") {
      throw error;
    }
  }
}

async function requireTrustedSnapshot(processId, executablePath) {
  const namedArguments = { ProcessId: String(processId) };
  if (executablePath) namedArguments.ExecutablePath = executablePath;
  const raw = await invokeDiscovery("Snapshot", namedArguments);
  if (!raw.package) {
    throw commandError(
      "codex_not_installed",
      "未检测到官方 Store Codex。",
      false);
  }
  validateTarget(raw.package, executablePath);
  if (!raw.process) {
    throw commandError("process_exited", "Codex 主进程已退出。", true);
  }
  if (!raw.process.identityValid || raw.process.commandLineKind !== "main") {
    throw commandError(
      "identity_changed",
      "目标进程不再是已校验的 Codex 主进程。",
      false);
  }
  return raw.process;
}

async function assertSnapshotUnchanged(expected, executablePath) {
  const actual = await requireTrustedSnapshot(expected.processId, executablePath);
  if (actual.startedAtUtc !== expected.startedAtUtc ||
      !samePath(actual.executablePath, expected.executablePath)) {
    throw commandError(
      "identity_changed",
      "Codex 进程身份在操作期间发生变化。",
      false);
  }
}

async function getPortListeners() {
  const result = await invokeDiscovery("Port", { Port: String(inspectorPort) });
  return result.listeners ?? [];
}

async function openInspectorForProcess(processId, initialListeners) {
  if (initialListeners.length > 0) {
    assertPortOwner(initialListeners, processId);
  }

  const deadline = Date.now() + 6000;
  let lastOpenRequestAt = initialListeners.length > 0 ? Date.now() : 0;
  let requestedOnce = false;
  while (Date.now() < deadline) {
    const listeners = await getPortListeners();
    if (listeners.length > 0) {
      assertPortOwner(listeners, processId);
      try {
        return await fetchInspectorMetadata(inspectorPort, {
          timeoutMs: Math.min(750, Math.max(1, deadline - Date.now())),
        });
      } catch (error) {
        if (!error?.retryable) {
          throw error;
        }
      }
    } else if (Date.now() - lastOpenRequestAt >= 750) {
      try {
        process._debugProcess(processId);
        requestedOnce = true;
      } catch {
        if (!requestedOnce) {
          throw commandError(
            "access_denied",
            "无法为已校验的 Codex 主进程短时打开 Inspector。",
            false);
        }
      }
      lastOpenRequestAt = Date.now();
    }
    await delay(75);
  }
  throw commandError(
    "timeout",
    "等待 Codex Inspector 打开超时。",
    true);
}

async function waitForPortClosed(processId) {
  const deadline = Date.now() +
    inspectorCloseTimeoutMs +
    inspectorClosedSettleMs;
  let closedAt;
  while (Date.now() < deadline) {
    const listeners = await getPortListeners();
    if (listeners.length === 0) {
      closedAt ??= Date.now();
      if (Date.now() - closedAt >= inspectorClosedSettleMs) {
        return;
      }
    } else {
      closedAt = undefined;
      assertPortOwner(listeners, processId);
    }
    await delay(75);
  }
  throw commandError(
    "timeout",
    "等待 Codex Inspector 关闭超时。",
    true);
}

async function invokeDiscovery(mode, namedArguments = {}) {
  const argumentsList = [
    "-NoLogo",
    "-NoProfile",
    "-NonInteractive",
    "-File",
    discoveryScript,
    "-Mode",
    mode,
  ];
  for (const [name, value] of Object.entries(namedArguments)) {
    argumentsList.push(`-${name}`, value);
  }

  return await new Promise((resolve, reject) => {
    const child = spawn(
      "powershell.exe",
      argumentsList,
      {
        windowsHide: true,
        shell: false,
        stdio: ["ignore", "pipe", "pipe"],
      });
    const chunks = [];
    const errors = [];
    let bytes = 0;
    const timer = setTimeout(() => {
      child.kill();
      reject(commandError("timeout", "Windows 身份发现操作超时。", true));
    }, 10000);

    child.stdout.on("data", (chunk) => {
      bytes += chunk.length;
      if (bytes > 256 * 1024) {
        child.kill();
        reject(commandError(
          "invalid_response",
          "Windows 身份发现响应超过限制。",
          false));
        return;
      }
      chunks.push(chunk);
    });
    child.stderr.on("data", (chunk) => errors.push(chunk));
    child.on("error", () => {
      clearTimeout(timer);
      reject(commandError(
        "access_denied",
        "无法执行 Windows 身份发现。",
        false));
    });
    child.on("close", (exitCode) => {
      clearTimeout(timer);
      if (exitCode !== 0) {
        reject(commandError(
          "access_denied",
          "Windows 身份发现失败。",
          false,
          errors.length ? "powershell_failed" : undefined));
        return;
      }
      try {
        resolve(JSON.parse(Buffer.concat(chunks).toString("utf8")));
      } catch {
        reject(commandError(
          "invalid_response",
          "Windows 身份发现返回无效 JSON。",
          false));
      }
    });
  });
}

function validateTarget(packageInfo, requestedExecutablePath) {
  const executable = path.resolve(packageInfo.executablePath).toLowerCase();
  if (requestedExecutablePath && !samePath(executable, requestedExecutablePath)) {
    throw commandError(
      "identity_changed",
      "Codex 可执行文件路径与已选择目标不一致。",
      false);
  }
  if (!requestedExecutablePath) {
    const expectedRoot = `${path.resolve(packageInfo.installLocation)}${path.sep}`
      .toLowerCase();
    if (!executable.startsWith(expectedRoot) ||
        path.basename(executable) !== "chatgpt.exe") {
      throw commandError(
        "identity_changed",
        "自动发现的 Codex 可执行文件路径无效。",
        false);
    }
  }
}

function toPublicProcess(processInfo) {
  return {
    processId: processInfo.processId,
    startedAtUtc: processInfo.startedAtUtc,
    executablePath: processInfo.executablePath,
    browserId: null,
  };
}

function parseTargetOptions(argumentsList, requireProcessId) {
  let processId;
  let executablePath;
  if (argumentsList.length % 2 !== 0) {
    throw commandError("invalid_arguments", "目标参数无效。", false);
  }
  for (let index = 0; index < argumentsList.length; index += 2) {
    const name = argumentsList[index];
    const value = argumentsList[index + 1];
    if (!value || (name !== "--pid" && name !== "--executable")) {
      throw commandError("invalid_arguments", "目标参数无效。", false);
    }
    if (name === "--pid") processId = value;
    if (name === "--executable") executablePath = path.resolve(value);
  }
  if (requireProcessId && !/^[1-9][0-9]{0,9}$/u.test(processId ?? "")) {
    throw commandError("invalid_arguments", "必须提供有效的 --pid。", false);
  }
  const value = processId ? Number(processId) : undefined;
  if (value !== undefined && (!Number.isSafeInteger(value) || value > 0x7fffffff)) {
    throw commandError("invalid_arguments", "PID 超出允许范围。", false);
  }
  return { processId: value, executablePath };
}

function samePath(left, right) {
  return path.resolve(left).toLowerCase() === path.resolve(right).toLowerCase();
}

function millisecondsToTimeSpan(milliseconds) {
  const totalMilliseconds = Math.max(0, Math.round(milliseconds));
  const seconds = Math.floor(totalMilliseconds / 1000);
  const fraction = String(totalMilliseconds % 1000).padStart(3, "0");
  return `00:00:${String(seconds).padStart(2, "0")}.${fraction}0000`;
}

function ensureWindows() {
  if (process.platform !== "win32") {
    throw commandError(
      "unsupported_version",
      "Injector 仅支持 Windows。",
      false);
  }
}

function commandError(code, userMessage, retryable, diagnosticCode) {
  return Object.assign(new Error(userMessage), {
    code,
    userMessage,
    retryable,
    diagnosticCode,
  });
}

function outputSuccess(payload) {
  process.stdout.write(`${JSON.stringify({
    service,
    version,
    protocolVersion,
    status: "ok",
    ...payload,
  })}\n`);
}

function outputFailure(error) {
  process.stderr.write(`${JSON.stringify({
    service,
    version,
    protocolVersion,
    status: "error",
    error: {
      code: error?.code ?? "external_tool_failure",
      retryable: Boolean(error?.retryable),
      userMessage: error?.userMessage ?? "Injector 操作失败。",
      diagnosticCode: error?.diagnosticCode,
    },
  })}\n`);
  process.exitCode = 2;
}

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}
