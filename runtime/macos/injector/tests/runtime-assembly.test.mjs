import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "../../../..");

test("macOS runtime baseline pins the complete Node identity", () => {
  const baseline = JSON.parse(fs.readFileSync(
    path.join(repositoryRoot, "macos/runtime-baseline.json"),
    "utf8"));
  assert.deepEqual(Object.keys(baseline).sort(), [
    "node", "runtimeVersion", "schemaVersion",
  ]);
  assert.equal(baseline.node.version, "24.18.0");
  assert.equal(baseline.node.platform, "darwin");
  assert.equal(baseline.node.architecture, "arm64");
  assert.equal(baseline.node.executableSize, 120965360);
  assert.match(baseline.node.archiveSha256, /^[0-9a-f]{64}$/u);
  assert.match(baseline.node.executableSha256, /^[0-9a-f]{64}$/u);
});

test("manifest schema rejects unknown fields at every object level", () => {
  const schema = JSON.parse(fs.readFileSync(
    path.join(repositoryRoot, "macos/runtime-manifest-v1.schema.json"),
    "utf8"));
  assert.equal(schema.additionalProperties, false);
  assert.equal(schema.properties.target.additionalProperties, false);
  assert.equal(
    schema.properties.files.items.additionalProperties,
    false);
  assert.equal(schema.properties.files.minItems, 3);
  assert.equal(schema.properties.files.maxItems, 3);
});

test("assembly entry is offline, locked, and contains no runtime bypass", () => {
  const script = fs.readFileSync(
    path.join(repositoryRoot, "macos/assemble-runtime.zsh"),
    "utf8");
  assert.match(script, /--locked-mode/u);
  assert.match(script, /MacRuntimeIdentityGeneratedSource/u);
  assert.match(script, /dotnet-harness-publish/u);
  assert.match(script, /managed-runtime-identity\.mjs/u);
  assert.doesNotMatch(script, /\beval\b/u);
  assert.doesNotMatch(script, /\bcurl\b|\bwget\b/u);
  assert.doesNotMatch(script, /SIGUSR1|9229|\/json\/|ws:\/\/|\bkill\s*\(/u);
  assert.doesNotMatch(script, /allow-unconfigured|development-mode/iu);
});
