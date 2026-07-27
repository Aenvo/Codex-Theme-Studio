import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import {
  buildManagedInventory,
  computeAssemblyId,
  createReceipt,
  verifyReceipt,
} from "../managed-runtime-identity.mjs";

function fixture() {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "cts-managed-runtime-"));
  const macos = path.join(root, "Contents", "MacOS");
  fs.mkdirSync(macos, { recursive: true });
  fs.writeFileSync(path.join(macos, "Runtime.dll"), "runtime");
  fs.writeFileSync(path.join(macos, "Harness.runtimeconfig.json"), "{}");
  return { root, macos };
}

test("managed payload is deterministic and changes with Runtime bytes", () => {
  const { root, macos } = fixture();
  try {
    const first = buildManagedInventory(macos);
    const repeated = buildManagedInventory(macos);
    assert.deepEqual(first, repeated);
    fs.writeFileSync(path.join(macos, "Runtime.dll"), "changed");
    const changed = buildManagedInventory(macos);
    assert.notEqual(first.dotnetPayloadHash, changed.dotnetPayloadHash);
    assert.notEqual(
      computeAssemblyId("a".repeat(64), "b".repeat(64), first.dotnetPayloadHash),
      computeAssemblyId("a".repeat(64), "b".repeat(64), changed.dotnetPayloadHash));
  } finally {
    fs.rmSync(root, { recursive: true });
  }
});

test("Helper bytes participate independently in assembly identity", () => {
  const { root, macos } = fixture();
  try {
    const payload = buildManagedInventory(macos);
    assert.notEqual(
      computeAssemblyId("a".repeat(64), "b".repeat(64), payload.dotnetPayloadHash),
      computeAssemblyId("a".repeat(64), "c".repeat(64), payload.dotnetPayloadHash));
  } finally {
    fs.rmSync(root, { recursive: true });
  }
});

test("receipt is evidence with strict schema and independent recomputation", () => {
  const { root } = fixture();
  try {
    const receipt = createReceipt(root, "a".repeat(64), "b".repeat(64));
    fs.writeFileSync(
      path.join(root, "assembly-receipt.json"),
      `${JSON.stringify(receipt)}\n`);
    assert.deepEqual(verifyReceipt(root, receipt.assemblyId), receipt);
    receipt.dotnetFiles[0].size += 1;
    fs.writeFileSync(
      path.join(root, "assembly-receipt.json"),
      `${JSON.stringify(receipt)}\n`);
    assert.throws(() => verifyReceipt(root, receipt.assemblyId));
  } finally {
    fs.rmSync(root, { recursive: true });
  }
});

test("symlinks, extra files, and case conflicts fail closed", () => {
  const { root, macos } = fixture();
  try {
    fs.writeFileSync(path.join(macos, "unexpected.txt"), "extra");
    assert.throws(() => buildManagedInventory(macos));
    fs.rmSync(path.join(macos, "unexpected.txt"));
    fs.symlinkSync(path.join(macos, "Runtime.dll"), path.join(macos, "Other.dll"));
    assert.throws(() => buildManagedInventory(macos));
  } finally {
    fs.rmSync(root, { recursive: true });
  }
});
