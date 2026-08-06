import { createHash } from 'node:crypto';
import { spawn } from 'node:child_process';
import {
  access,
  copyFile,
  mkdir,
  lstat,
  open,
  readFile,
  readdir,
  rename,
  rm,
  stat,
  writeFile,
} from 'node:fs/promises';
import path from 'node:path';

const token = process.argv[2] ?? '';
if (!/^[a-f0-9]{32}$/.test(token)) process.exit(2);

const localAppData = process.env.LOCALAPPDATA;
if (!localAppData) process.exit(3);
const updatesRoot = path.join(localAppData, 'CodexThemeStudio', 'Updates');
const testHealthTimeout = Number(process.env.CODEX_THEME_STUDIO_UPDATER_TEST_TIMEOUT_MS);
const healthTimeoutMs = Number.isInteger(testHealthTimeout) &&
  testHealthTimeout >= 100 && testHealthTimeout <= 5_000
  ? testHealthTimeout
  : 120_000;
const requestPath = path.join(updatesRoot, 'requests', `${token}.json`);
let request;

try {
  request = JSON.parse(await readFile(requestPath, 'utf8'));
  validateRequest(request);
  const retryCleanupPath = path.join(updatesRoot, 'retry', `${token}.json`);
  if (await exists(retryCleanupPath)) {
    const preservedDirectory = await cleanBackup(request);
    await rm(retryCleanupPath, { force: true });
    await writeResult({
      outcome: 'CleanupCompleted',
      oldVersion: request.currentVersion,
      newVersion: request.targetVersion,
      userMessage: '旧文件清理已完成。',
      preservedDirectory,
      backupDirectory: null,
    });
    process.exit(0);
  }
  await waitForExit(request.currentProcessId, 60_000);
  await waitForExclusiveOpen(
    path.join(request.applicationRoot, request.executableRelativePath),
    30_000,
  );
  await rename(request.applicationRoot, request.backupDirectory);
  try {
    await rename(request.stagingRoot, request.applicationRoot);
  } catch (error) {
    await rename(request.backupDirectory, request.applicationRoot);
    throw error;
  }

  const child = launch(
    path.join(request.applicationRoot, request.executableRelativePath),
    token,
  );
  const healthy = await waitForFile(request.healthPath, healthTimeoutMs);
  if (!healthy) {
    try { process.kill(child.pid); } catch { }
    await rollback('新版本未在 120 秒内完成健康检查。');
    process.exit(0);
  }

  try {
    const preservedDirectory = await cleanBackup(request);
    await writeResult({
      outcome: 'Succeeded',
      oldVersion: request.currentVersion,
      newVersion: request.targetVersion,
      userMessage: `更新成功，已升级至 v${request.targetVersion}`,
      preservedDirectory,
      backupDirectory: null,
    });
  } catch {
    await writeResult({
      outcome: 'CleanupIncomplete',
      oldVersion: request.currentVersion,
      newVersion: request.targetVersion,
      userMessage: '应用已更新，但旧目录清理未完成。',
      preservedDirectory: null,
      backupDirectory: request.backupDirectory,
    });
  }
} catch (error) {
  if (request) {
    await writeResult({
      outcome: 'Failed',
      oldVersion: request.currentVersion ?? 'unknown',
      newVersion: request.targetVersion ?? 'unknown',
      userMessage: '更新程序启动失败；未确认任何程序文件变更。',
      preservedDirectory: null,
      backupDirectory: request.backupDirectory ?? null,
    }).catch(() => {});
  }
  process.exitCode = 1;
}

function validateRequest(value) {
  if (value?.schemaVersion !== 1 || value.token !== token ||
      !Number.isInteger(value.currentProcessId) || value.currentProcessId <= 0) {
    throw new Error('Invalid update request.');
  }
  for (const name of [
    'applicationRoot', 'stagingRoot', 'backupDirectory', 'failedDirectory',
    'resultPath', 'healthPath', 'preservedRoot',
  ]) {
    if (typeof value[name] !== 'string' || !path.isAbsolute(value[name])) {
      throw new Error(`Invalid ${name}.`);
    }
  }
  if (!/^[a-zA-Z0-9._-]+\.exe$/.test(value.executableRelativePath)) {
    throw new Error('Invalid executable path.');
  }
  const appParent = path.dirname(value.applicationRoot);
  if (path.dirname(value.stagingRoot).toLowerCase() !== appParent.toLowerCase() ||
      path.dirname(value.backupDirectory).toLowerCase() !== appParent.toLowerCase()) {
    throw new Error('Swap directories must have the same parent.');
  }
}

async function waitForExit(pid, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    let alive = true;
    try { process.kill(pid, 0); } catch { alive = false; }
    if (!alive) return;
    await delay(200);
  }
  throw new Error('Old process did not exit.');
}

async function waitForExclusiveOpen(file, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    try {
      const handle = await open(file, 'r+');
      await handle.close();
      return;
    } catch {
      await delay(200);
    }
  }
  throw new Error('Executable remained locked.');
}

function launch(executable, updateToken) {
  const child = spawn(executable, ['--update-token', updateToken], {
    cwd: path.dirname(executable),
    detached: true,
    stdio: 'ignore',
    windowsHide: true,
  });
  child.unref();
  return child;
}

async function waitForFile(file, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    try { await access(file); return true; } catch { await delay(250); }
  }
  return false;
}

async function rollback(reason) {
  let rollbackComplete = false;
  try {
    if (await exists(request.failedDirectory)) await rm(request.failedDirectory, { recursive: true });
    if (await exists(request.applicationRoot)) {
      await rename(request.applicationRoot, request.failedDirectory);
    }
    await rename(request.backupDirectory, request.applicationRoot);
    rollbackComplete = true;
  } catch { }

  if (rollbackComplete) {
    await writeResult({
      outcome: 'RolledBack',
      oldVersion: request.currentVersion,
      newVersion: request.targetVersion,
      userMessage: `更新失败，已恢复到 v${request.currentVersion}。${reason}`,
      preservedDirectory: null,
      backupDirectory: null,
    });
    launch(path.join(request.applicationRoot, request.executableRelativePath), token);
  } else {
    await writeResult({
      outcome: 'RollbackIncomplete',
      oldVersion: request.currentVersion,
      newVersion: request.targetVersion,
      userMessage: '更新失败且自动回滚未完成；已保留备份，请停止重试。',
      preservedDirectory: null,
      backupDirectory: request.backupDirectory,
    });
    const backupExecutable = path.join(request.backupDirectory, request.executableRelativePath);
    if (await exists(backupExecutable)) launch(backupExecutable, token);
  }
}

async function cleanBackup(value) {
  const manifestPath = path.join(value.backupDirectory, 'app-install-manifest.json');
  const manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
  if (manifest.schemaVersion !== 1 || !Array.isArray(manifest.files)) {
    throw new Error('Invalid old install manifest.');
  }
  const owned = new Map();
  for (const entry of manifest.files) {
    const relative = normalizeRelative(entry.path);
    if (owned.has(relative.toLowerCase()) || !/^[a-f0-9]{64}$/.test(entry.sha256)) {
      throw new Error('Invalid install manifest entry.');
    }
    owned.set(relative.toLowerCase(), { ...entry, path: relative });
  }

  const files = await walkFiles(value.backupDirectory);
  const timestamp = new Date().toISOString().replace(/[:.]/g, '-');
  const preservedDirectory = path.join(
    value.preservedRoot,
    `${safeSegment(value.currentVersion)}-${timestamp}`,
  );
  let preserved = false;
  for (const file of files) {
    const relative = normalizeRelative(path.relative(value.backupDirectory, file));
    if (relative.toLowerCase() === 'app-install-manifest.json') {
      await rm(file);
      continue;
    }
    const record = owned.get(relative.toLowerCase());
    const info = await stat(file);
    if (record && info.size === record.bytes && (await sha256(file)) === record.sha256) {
      await rm(file);
      continue;
    }
    const destination = path.join(preservedDirectory, relative);
    await mkdir(path.dirname(destination), { recursive: true });
    await moveAcrossVolumes(file, destination);
    preserved = true;
  }
  await rm(value.backupDirectory, { recursive: true });
  return preserved ? preservedDirectory : null;
}

async function walkFiles(root) {
  const output = [];
  for (const entry of await readdir(root, { withFileTypes: true })) {
    const full = path.join(root, entry.name);
    const info = await lstat(full);
    if (info.isSymbolicLink()) throw new Error('Links are not allowed in install roots.');
    if (info.isDirectory()) output.push(...await walkFiles(full));
    else if (info.isFile()) output.push(full);
  }
  return output;
}

async function moveAcrossVolumes(source, destination) {
  try {
    await rename(source, destination);
  } catch (error) {
    if (error?.code !== 'EXDEV') throw error;
    await copyFile(source, destination);
    await rm(source);
  }
}

async function sha256(file) {
  const hash = createHash('sha256');
  hash.update(await readFile(file));
  return hash.digest('hex');
}

function normalizeRelative(value) {
  const normalized = value.replaceAll('\\', '/');
  if (!normalized || path.isAbsolute(normalized) || normalized.includes(':') ||
      normalized.split('/').some(segment => segment === '..' || segment === '.')) {
    throw new Error('Unsafe relative path.');
  }
  return normalized;
}

function safeSegment(value) {
  return String(value).replace(/[^0-9A-Za-z._-]/g, '_');
}

async function writeResult(result) {
  await mkdir(path.dirname(request.resultPath), { recursive: true });
  const temporary = `${request.resultPath}.tmp`;
  await writeFile(temporary, JSON.stringify({ schemaVersion: 1, token, ...result }, null, 2));
  await rename(temporary, request.resultPath);
}

async function exists(file) {
  try { await access(file); return true; } catch { return false; }
}

function delay(milliseconds) {
  return new Promise(resolve => setTimeout(resolve, milliseconds));
}
