import net from "node:net";

export async function openInspector({
  processId,
  initialListeners,
  port,
  timeoutMs,
  metadataTimeoutMs = 1500,
  retryDelayMs = 75,
  signalRetryMs = 750,
  getPortListeners,
  assertPortOwner,
  fetchMetadata,
  requestOpen,
  isPortOpen = isLoopbackPortOpen,
  now = Date.now,
  wait = delay,
}) {
  if (initialListeners.length > 0) {
    assertPortOwner(initialListeners, processId);
  }

  const deadline = now() + timeoutMs;
  let lastOpenRequestAt = initialListeners.length > 0 ? now() : 0;
  let requestedOnce = false;

  const requestInspectorOpen = () => {
    try {
      requestOpen(processId);
      requestedOnce = true;
    } catch {
      if (!requestedOnce) {
        throw lifecycleError(
          "access_denied",
          "无法为已校验的 Codex 主进程短时打开 Inspector。",
          false);
      }
    }
    lastOpenRequestAt = now();
  };

  if (initialListeners.length === 0) {
    requestInspectorOpen();
  }

  while (now() < deadline) {
    if (await isPortOpen(port)) {
      try {
        const listeners = await getPortListeners();
        assertPortOwner(listeners, processId);
        return await fetchMetadata(port, { timeoutMs: metadataTimeoutMs });
      } catch (error) {
        if (!error?.retryable) {
          throw error;
        }
      }
    } else if (now() - lastOpenRequestAt >= signalRetryMs) {
      requestInspectorOpen();
    }
    await wait(retryDelayMs);
  }

  throw lifecycleError(
    "timeout",
    "等待 Codex Inspector 打开超时。",
    true);
}

export async function waitForInspectorClosed({
  processId,
  port,
  timeoutMs,
  settleMs,
  retryDelayMs = 75,
  getPortListeners,
  assertPortOwner,
  isPortOpen = isLoopbackPortOpen,
  now = Date.now,
  wait = delay,
}) {
  const deadline = now() + timeoutMs + settleMs;
  let closedAt;

  while (now() < deadline) {
    if (await isPortOpen(port)) {
      closedAt = undefined;
    } else {
      closedAt ??= now();
      if (now() - closedAt >= settleMs) {
        const listeners = await getPortListeners();
        if (listeners.length === 0) {
          return;
        }
        assertPortOwner(listeners, processId);
        closedAt = undefined;
      }
    }
    await wait(retryDelayMs);
  }

  const listeners = await getPortListeners();
  if (listeners.length === 0) {
    throw lifecycleError(
      "timeout",
      "等待 Codex Inspector 关闭超时。",
      true);
  }
  assertPortOwner(listeners, processId);
  throw lifecycleError(
    "timeout",
    "等待 Codex Inspector 关闭超时。",
    true);
}

export async function isLoopbackPortOpen(port, timeoutMs = 150) {
  return await new Promise((resolve) => {
    const socket = net.createConnection({ host: "127.0.0.1", port });
    let completed = false;
    const finish = (isOpen) => {
      if (completed) return;
      completed = true;
      socket.destroy();
      resolve(isOpen);
    };
    socket.setTimeout(timeoutMs, () => finish(false));
    socket.once("connect", () => finish(true));
    socket.once("error", () => finish(false));
  });
}

function lifecycleError(code, userMessage, retryable) {
  return Object.assign(new Error(userMessage), {
    code,
    userMessage,
    retryable,
  });
}

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}
