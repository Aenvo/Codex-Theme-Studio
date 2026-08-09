import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { execFile } from 'node:child_process';
import {
  access,
  copyFile,
  mkdir,
  mkdtemp,
  readFile,
  rm,
  writeFile,
} from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { promisify } from 'node:util';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const execute = promisify(execFile);
const script = path.join(path.dirname(fileURLToPath(import.meta.url)), 'apply-update.mjs');

test('swaps directories, preserves unknown files, and removes the old root', async () => {
  const fixture = await createFixture();
  try {
    await mkdir(path.dirname(fixture.request.healthPath), { recursive: true });
    await writeFile(fixture.request.healthPath, '{}');
    await runUpdater(fixture);

    const result = JSON.parse(await readFile(fixture.request.resultPath, 'utf8'));
    assert.equal(result.outcome, 'Succeeded');
    assert.equal(await readFile(path.join(fixture.appRoot, 'new-version.txt'), 'utf8'), 'new');
    assert.equal(await readFile(path.join(result.preservedDirectory, 'user.txt'), 'utf8'), 'keep');
    await assert.rejects(access(fixture.request.backupDirectory));
  } finally {
    await removeFixture(fixture.root);
  }
});

test('restores the old directory when health check times out', async () => {
  const fixture = await createFixture();
  try {
    await runUpdater(fixture, 200);

    const result = JSON.parse(await readFile(fixture.request.resultPath, 'utf8'));
    assert.equal(result.outcome, 'RolledBack');
    assert.equal(await readFile(path.join(fixture.appRoot, 'old-version.txt'), 'utf8'), 'old');
  } finally {
    await removeFixture(fixture.root);
  }
});

test('keeps the old root when cleanup cannot validate its manifest', async () => {
  const fixture = await createFixture();
  try {
    await writeFile(
      path.join(fixture.appRoot, 'app-install-manifest.json'),
      JSON.stringify({ schemaVersion: 99, files: [] }),
    );
    await mkdir(path.dirname(fixture.request.healthPath), { recursive: true });
    await writeFile(fixture.request.healthPath, '{}');
    await runUpdater(fixture);

    const result = JSON.parse(await readFile(fixture.request.resultPath, 'utf8'));
    assert.equal(result.outcome, 'CleanupIncomplete');
    assert.equal(await readFile(path.join(fixture.appRoot, 'new-version.txt'), 'utf8'), 'new');
    assert.equal(await readFile(path.join(fixture.request.backupDirectory, 'user.txt'), 'utf8'), 'keep');
  } finally {
    await removeFixture(fixture.root);
  }
});

async function createFixture() {
  const root = await mkdtemp(path.join(tmpdir(), 'cts-updater-'));
  const localAppData = path.join(root, 'local');
  const updatesRoot = path.join(localAppData, 'CodexThemeStudio', 'Updates');
  const appRoot = path.join(root, 'Codex Theme Studio');
  const stagingRoot = path.join(root, '.CodexThemeStudio.update-test');
  const token = '0123456789abcdef0123456789abcdef';
  await mkdir(appRoot, { recursive: true });
  await mkdir(stagingRoot, { recursive: true });
  await copyFile(process.execPath, path.join(appRoot, 'TestApp.exe'));
  await copyFile(process.execPath, path.join(stagingRoot, 'TestApp.exe'));
  await writeFile(path.join(appRoot, 'old-version.txt'), 'old');
  await writeFile(path.join(appRoot, 'user.txt'), 'keep');
  await writeFile(path.join(stagingRoot, 'new-version.txt'), 'new');
  const ownedFiles = [];
  for (const name of ['TestApp.exe', 'old-version.txt']) {
    const bytes = await readFile(path.join(appRoot, name));
    ownedFiles.push({
      path: name,
      bytes: bytes.length,
      sha256: createHash('sha256').update(bytes).digest('hex'),
    });
  }
  await writeFile(
    path.join(appRoot, 'app-install-manifest.json'),
    JSON.stringify({ schemaVersion: 1, files: ownedFiles }),
  );
  await writeFile(
    path.join(stagingRoot, 'app-install-manifest.json'),
    JSON.stringify({ schemaVersion: 1, files: [] }),
  );

  const request = {
    schemaVersion: 1,
    token,
    currentVersion: '1.2.2',
    targetVersion: '1.3.0',
    applicationRoot: appRoot,
    stagingRoot,
    backupDirectory: path.join(root, '.CodexThemeStudio.backup-test'),
    failedDirectory: path.join(root, `.CodexThemeStudio.failed-${token}`),
    executableRelativePath: 'TestApp.exe',
    zipSha256: 'a'.repeat(64),
    currentProcessId: 2_147_483_647,
    resultPath: path.join(updatesRoot, 'results', `${token}.json`),
    healthPath: path.join(updatesRoot, 'health', `${token}.json`),
    preservedRoot: path.join(updatesRoot, 'Preserved'),
  };
  await mkdir(path.join(updatesRoot, 'requests'), { recursive: true });
  await writeFile(
    path.join(updatesRoot, 'requests', `${token}.json`),
    JSON.stringify(request),
  );
  return { root, localAppData, appRoot, request, token };
}

async function runUpdater(fixture, timeout = 500) {
  await execute(process.execPath, [script, fixture.token], {
    env: {
      ...process.env,
      LOCALAPPDATA: fixture.localAppData,
      CODEX_THEME_STUDIO_UPDATER_TEST_TIMEOUT_MS: String(timeout),
    },
    timeout: 10_000,
  });
}

async function removeFixture(root) {
  for (let attempt = 0; attempt < 20; attempt += 1) {
    try {
      await rm(root, { recursive: true, force: true });
      return;
    } catch (error) {
      if (error?.code !== 'EBUSY') throw error;
      await new Promise(resolve => setTimeout(resolve, 100));
    }
  }
  await rm(root, { recursive: true, force: true });
}
