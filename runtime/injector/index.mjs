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
  assertPortOwner,
  classifyAppRoutes,
  evaluate,
  fetchInspectorMetadata,
} from "./security.mjs";

const service = "CodexThemeStudio.Injector";
const version = "0.3.0";
const protocolVersion = 1;
const inspectorPort = 9229;
const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const discoveryScript = path.join(scriptDirectory, "windows-discovery.ps1");
const probeExpression = `(() => {
  const electron = process.mainModule.require("electron");
  return {
    electronVersion: process.versions.electron || "",
    urls: electron.BrowserWindow.getAllWindows().map(
      window => window.webContents.getURL()
    )
  };
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
        "close-inspector",
        "prepare",
        "renderer-probe",
        "renderer-apply",
        "renderer-ensure",
        "renderer-status",
        "renderer-cleanup",
      ],
    });
  } else if (command === "discover" && process.argv.length === 3) {
    outputSuccess(await discover());
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
    const processId = parseProcessId(process.argv.slice(3));
    outputSuccess({ probe: await probe(processId) });
  } else if (command === "renderer-apply") {
    const processId = parseProcessId(process.argv.slice(3));
    const payload = prepareRendererPayload(await readStructuredInput(process.stdin));
    outputSuccess({
      renderer: await executeRendererOperation(
        processId,
        createMainApplyExpression(payload)),
    });
  } else if (command === "renderer-probe") {
    const processId = parseProcessId(process.argv.slice(3));
    outputSuccess({
      renderer: await executeRendererOperation(
        processId,
        createMainProbeExpression()),
    });
  } else if (command === "renderer-ensure") {
    const processId = parseProcessId(process.argv.slice(3));
    outputSuccess({
      renderer: await executeRendererOperation(
        processId,
        createMainOperationExpression("ensure")),
    });
  } else if (command === "renderer-status") {
    const processId = parseProcessId(process.argv.slice(3));
    outputSuccess({
      renderer: await executeRendererOperation(
        processId,
        createMainOperationExpression("status")),
    });
  } else if (command === "renderer-cleanup") {
    const processId = parseProcessId(process.argv.slice(3));
    outputSuccess({
      renderer: await executeRendererOperation(
        processId,
        createMainOperationExpression("cleanup")),
    });
  } else if (command === "close-inspector") {
    const processId = parseProcessId(process.argv.slice(3));
    await closeInspectorForProcess(processId);
    outputSuccess({ closed: true, processId });
  } else {
    throw commandError(
      "invalid_arguments",
      "命令或参数无效。",
      false);
  }
} catch (error) {
  outputFailure(error);
}

async function discover() {
  ensureWindows();
  const raw = await invokeDiscovery("Discover");
  if (!raw.package) {
    throw commandError(
      "codex_not_installed",
      "未检测到当前用户注册的官方 Microsoft Store Codex。",
      false);
  }
  validatePackage(raw.package);

  return {
    installation: {
      packageFamilyName: raw.package.packageFamilyName,
      packageFullName: raw.package.packageFullName,
      version: raw.package.version,
      executablePath: raw.package.executablePath,
      publisherId: raw.package.publisherId,
      isStoreSigned: raw.package.signatureKind === "Store",
    },
    processes: (raw.processes ?? []).map(toPublicProcess),
  };
}

async function executeRendererOperation(processId, expression) {
  ensureWindows();
  const before = await requireTrustedSnapshot(processId);
  const initialPort = await getPortListeners();
  if (initialPort.length > 0) {
    assertPortOwner(initialPort, processId);
  } else {
    try {
      process._debugProcess(processId);
    } catch {
      throw commandError(
        "access_denied",
        "无法为已校验的 Codex 主进程短时打开 Inspector。",
        false);
    }
  }

  const openedAt = performance.now();
  let metadata;
  let result;
  try {
    const listeners = await waitForPortOwner(processId);
    assertPortOwner(listeners, processId);
    await assertSnapshotUnchanged(before);
    metadata = await fetchInspectorMetadata(inspectorPort);
    result = await evaluate(metadata.webSocketUrl, expression, {
      timeoutMs: 8000,
      maxBytes: 256 * 1024,
      awaitPromise: true,
    });
    await assertSnapshotUnchanged(before);
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

async function probe(processId) {
  ensureWindows();
  const before = await requireTrustedSnapshot(processId);
  const initialPort = await getPortListeners();
  if (initialPort.length > 0) {
    assertPortOwner(initialPort, processId);
  } else {
    try {
      process._debugProcess(processId);
    } catch {
      throw commandError(
        "access_denied",
        "无法为已校验的 Codex 主进程短时打开 Inspector。",
        false);
    }
  }

  const openedAt = performance.now();
  let metadata;
  let probeData;
  try {
    const listeners = await waitForPortOwner(processId);
    assertPortOwner(listeners, processId);
    await assertSnapshotUnchanged(before);

    metadata = await fetchInspectorMetadata(inspectorPort);
    const result = await evaluate(metadata.webSocketUrl, probeExpression);
    if (!result || typeof result.electronVersion !== "string" ||
        !Array.isArray(result.urls)) {
      throw commandError(
        "invalid_response",
        "只读探针返回结构无效。",
        false);
    }

    await assertSnapshotUnchanged(before);
    probeData = {
      processId,
      processStartedAtUtc: before.startedAtUtc,
      electronVersion: result.electronVersion,
      windowCount: result.urls.length,
      routeTypes: classifyAppRoutes(result.urls),
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

async function closeInspectorForProcess(processId) {
  ensureWindows();
  const before = await requireTrustedSnapshot(processId);
  const listeners = await getPortListeners();
  if (listeners.length === 0) {
    return;
  }
  assertPortOwner(listeners, processId);
  const metadata = await fetchInspectorMetadata(inspectorPort);
  await assertSnapshotUnchanged(before);
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

async function requireTrustedSnapshot(processId) {
  const raw = await invokeDiscovery("Snapshot", {
    ProcessId: String(processId),
  });
  if (!raw.package) {
    throw commandError(
      "codex_not_installed",
      "未检测到官方 Store Codex。",
      false);
  }
  validatePackage(raw.package);
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

async function assertSnapshotUnchanged(expected) {
  const actual = await requireTrustedSnapshot(expected.processId);
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

async function waitForPortOwner(processId) {
  const deadline = Date.now() + 2500;
  while (Date.now() < deadline) {
    const listeners = await getPortListeners();
    if (listeners.length > 0) {
      assertPortOwner(listeners, processId);
      return listeners;
    }
    await delay(75);
  }
  throw commandError(
    "timeout",
    "等待 Codex Inspector 打开超时。",
    true);
}

async function waitForPortClosed(processId) {
  const deadline = Date.now() + 2000;
  while (Date.now() < deadline) {
    const listeners = await getPortListeners();
    if (listeners.length === 0) {
      return;
    }
    assertPortOwner(listeners, processId);
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

function validatePackage(packageInfo) {
  if (packageInfo.packageFamilyName !== "OpenAI.Codex_2p2nqsd0c76g0" ||
      packageInfo.publisherId !== "2p2nqsd0c76g0" ||
      packageInfo.signatureKind !== "Store") {
    throw commandError(
      "unsupported_version",
      "Codex Store 包身份或签名来源不符合预期。",
      false);
  }
  const expectedRoot = `${path.resolve(packageInfo.installLocation)}${path.sep}`
    .toLowerCase();
  const executable = path.resolve(packageInfo.executablePath).toLowerCase();
  if (!executable.startsWith(expectedRoot) ||
      path.basename(executable) !== "chatgpt.exe") {
    throw commandError(
      "identity_changed",
      "Codex 可执行文件路径不属于已注册包。",
      false);
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

function parseProcessId(argumentsList) {
  if (argumentsList.length !== 2 || argumentsList[0] !== "--pid" ||
      !/^[1-9][0-9]{0,9}$/u.test(argumentsList[1])) {
    throw commandError("invalid_arguments", "必须提供有效的 --pid。", false);
  }
  const value = Number(argumentsList[1]);
  if (!Number.isSafeInteger(value) || value > 0x7fffffff) {
    throw commandError("invalid_arguments", "PID 超出允许范围。", false);
  }
  return value;
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
