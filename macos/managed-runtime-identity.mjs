import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import process from "node:process";
import { pathToFileURL } from "node:url";

const receiptKeys = [
  "assemblyId",
  "completeChainMatch",
  "dotnetFiles",
  "dotnetPayloadHash",
  "helperSha256",
  "helperToDotNetMatch",
  "manifestSha256",
  "manifestToHelperMatch",
  "packagedRuntimeIdentityConfigured",
  "schemaVersion",
  "toolVersion",
].sort();

function sha256Bytes(value) {
  return crypto.createHash("sha256").update(value).digest("hex");
}

function requireSha256(value) {
  if (!/^[0-9a-f]{64}$/u.test(value ?? "")) {
    throw new Error("identity.invalid");
  }
  return value;
}

function requireDirectory(directory) {
  if (!path.isAbsolute(directory)) {
    throw new Error("path.invalid");
  }
  const info = fs.lstatSync(directory);
  if (!info.isDirectory() || info.isSymbolicLink()) {
    throw new Error("path.invalid");
  }
  return directory;
}

function isManagedFile(name) {
  return name.endsWith(".dll") ||
    name.endsWith(".deps.json") ||
    name.endsWith(".runtimeconfig.json");
}

export function buildManagedInventory(macosDirectory) {
  requireDirectory(macosDirectory);
  const names = fs.readdirSync(macosDirectory).sort();
  if (names.length === 0 || names.some((name) => !isManagedFile(name))) {
    throw new Error("inventory.invalid");
  }
  const folded = names.map((name) => name.toLowerCase());
  if (new Set(folded).size !== names.length) {
    throw new Error("inventory.duplicate");
  }
  const files = names.map((name) => {
    const filePath = path.join(macosDirectory, name);
    const info = fs.lstatSync(filePath);
    if (!info.isFile() || info.isSymbolicLink() || info.size <= 0) {
      throw new Error("inventory.invalid");
    }
    return {
      path: `Contents/MacOS/${name}`,
      size: info.size,
      sha256: sha256Bytes(fs.readFileSync(filePath)),
    };
  });
  const canonical = files
    .map((entry) => `${entry.path}\t${entry.size}\t${entry.sha256}\n`)
    .join("");
  return {
    dotnetFiles: files,
    dotnetPayloadHash: sha256Bytes(Buffer.from(canonical, "utf8")),
  };
}

export function computeAssemblyId(
  manifestSha256,
  helperSha256,
  dotnetPayloadHash) {
  const canonical = `${requireSha256(manifestSha256)}\n` +
    `${requireSha256(helperSha256)}\n` +
    `${requireSha256(dotnetPayloadHash)}\n`;
  return sha256Bytes(Buffer.from(canonical, "utf8"));
}

export function createReceipt(
  runtimeRoot,
  manifestSha256,
  helperSha256) {
  requireDirectory(runtimeRoot);
  const inventory = buildManagedInventory(
    path.join(runtimeRoot, "Contents", "MacOS"));
  const assemblyId = computeAssemblyId(
    manifestSha256,
    helperSha256,
    inventory.dotnetPayloadHash);
  return {
    schemaVersion: 2,
    toolVersion: "0.3.0",
    assemblyId,
    manifestSha256: requireSha256(manifestSha256),
    helperSha256: requireSha256(helperSha256),
    dotnetPayloadHash: inventory.dotnetPayloadHash,
    dotnetFiles: inventory.dotnetFiles,
    manifestToHelperMatch: true,
    helperToDotNetMatch: true,
    completeChainMatch: true,
    packagedRuntimeIdentityConfigured: true,
  };
}

export function verifyReceipt(runtimeRoot, expectedAssemblyId) {
  requireDirectory(runtimeRoot);
  const receiptPath = path.join(runtimeRoot, "assembly-receipt.json");
  const info = fs.lstatSync(receiptPath);
  if (!info.isFile() || info.isSymbolicLink()) {
    throw new Error("receipt.invalid");
  }
  const receipt = JSON.parse(fs.readFileSync(receiptPath, "utf8"));
  if (Object.keys(receipt).sort().join("\n") !== receiptKeys.join("\n") ||
      receipt.schemaVersion !== 2 ||
      receipt.toolVersion !== "0.3.0" ||
      receipt.completeChainMatch !== true ||
      receipt.helperToDotNetMatch !== true ||
      receipt.manifestToHelperMatch !== true ||
      receipt.packagedRuntimeIdentityConfigured !== true) {
    throw new Error("receipt.invalid");
  }
  const expected = createReceipt(
    runtimeRoot,
    receipt.manifestSha256,
    receipt.helperSha256);
  if (JSON.stringify(receipt) !== JSON.stringify(expected) ||
      receipt.assemblyId !== requireSha256(expectedAssemblyId)) {
    throw new Error("receipt.mismatch");
  }
  return receipt;
}

function option(name) {
  const index = process.argv.indexOf(name);
  if (index < 0 || index + 1 >= process.argv.length) {
    throw new Error("usage.invalid");
  }
  return process.argv[index + 1];
}

function emit(value) {
  process.stdout.write(`${JSON.stringify(value)}\n`);
}

if (process.argv[1] &&
    import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    const command = process.argv[2];
    if (command === "inventory") {
      emit(buildManagedInventory(option("--macos")));
    } else if (command === "assembly-id") {
      const inventory = buildManagedInventory(option("--macos"));
      emit({
        assemblyId: computeAssemblyId(
          option("--manifest-sha256"),
          option("--helper-sha256"),
          inventory.dotnetPayloadHash),
        ...inventory,
      });
    } else if (command === "write-receipt") {
      const runtimeRoot = option("--runtime");
      const output = option("--output");
      if (!path.isAbsolute(output) ||
          path.dirname(output) !== runtimeRoot ||
          fs.existsSync(output)) {
        throw new Error("path.invalid");
      }
      const receipt = createReceipt(
        runtimeRoot,
        option("--manifest-sha256"),
        option("--helper-sha256"));
      const temporary = `${output}.${crypto.randomUUID()}.tmp`;
      fs.writeFileSync(
        temporary,
        `${JSON.stringify(receipt)}\n`,
        { flag: "wx", mode: 0o600 });
      fs.renameSync(temporary, output);
      emit(receipt);
    } else if (command === "verify") {
      emit(verifyReceipt(
        option("--runtime"),
        option("--expected-assembly-id")));
    } else {
      throw new Error("usage.invalid");
    }
  } catch {
    emit({
      status: "error",
      error: { code: "runtime.assembly_identity_invalid", stage: "identity" },
    });
    process.exitCode = 1;
  }
}
