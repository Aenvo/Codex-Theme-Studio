#!/bin/zsh

set -euo pipefail

SCRIPT_DIRECTORY=${0:A:h}
REPOSITORY_ROOT=${SCRIPT_DIRECTORY:h}
EXPECTED_BASELINE_COMMIT=f5a9e0b5e203cb06627374771a1f6839e20b211a
DOTNET=${CTS_DOTNET10:-"$HOME/Library/Application Support/CodexThemeStudio/devtools/dotnet-10.0.302/dotnet"}
NODE=${CTS_NODE:-"$HOME/Library/Application Support/CodexThemeStudio/devtools/node-v24.18.0-darwin-arm64/bin/node"}
XCODE_APP=${CTS_XCODE:-"$HOME/Applications/Xcode-16.4.app"}
ASSEMBLY_ROOT=${CTS_MACOS_ASSEMBLY_ROOT:-"$HOME/Library/Application Support/CodexThemeStudio/devtools/state/macos-runtime-assembly"}
BASELINE="$SCRIPT_DIRECTORY/runtime-baseline.json"
SWIFT_PACKAGE="$REPOSITORY_ROOT/native/macos/CodexThemeStudio.MacHelper"
CDP_SOURCE="$REPOSITORY_ROOT/runtime/macos/injector/cdp-client.mjs"
RENDERER_SOURCE="$REPOSITORY_ROOT/runtime/macos/injector/renderer-runtime.mjs"
SCHEMA_SOURCE="$SCRIPT_DIRECTORY/runtime-manifest-v1.schema.json"
HOST_PROJECT="$SCRIPT_DIRECTORY/CodexThemeStudio.RuntimeHost/CodexThemeStudio.RuntimeHost.csproj"
MANIFEST_PRODUCT=codex-theme-studio-runtime-manifest
HELPER_PRODUCT=codex-theme-studio-mac-helper

export DEVELOPER_DIR="$XCODE_APP/Contents/Developer"
export DOTNET_ROOT="${DOTNET:h}"
export DOTNET_CLI_HOME="$HOME/Library/Application Support/CodexThemeStudio/devtools/state/dotnet-cli-10"
export NUGET_PACKAGES="$HOME/Library/Application Support/CodexThemeStudio/devtools/state/nuget-packages"
export NUGET_HTTP_CACHE_PATH="$HOME/Library/Application Support/CodexThemeStudio/devtools/state/nuget-http-cache"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_NOLOGO=1
export NUGET_XMLDOC_MODE=skip

hash_file() {
  local result
  result=$(/usr/bin/openssl dgst -sha256 "$1")
  print -r -- "${result##*= }"
}

fail() {
  print -u2 -r -- "$1"
  exit 1
}

require_regular_input() {
  [[ "$1" == /* && -f "$1" && ! -L "$1" ]] ||
    fail "A required input is not an absolute regular non-symlink file."
}

preflight() {
  [[ "$(/usr/bin/git -C "$REPOSITORY_ROOT" branch --show-current)" == \
      "codex/macos-productization" ]] ||
    fail "Unexpected Git branch."
  /usr/bin/git -C "$REPOSITORY_ROOT" merge-base --is-ancestor \
    "$EXPECTED_BASELINE_COMMIT" HEAD ||
    fail "The verified productization baseline is not an ancestor of HEAD."
  [[ "$(/usr/bin/git -C "$REPOSITORY_ROOT" rev-parse origin/main)" == \
      "5c749394aa2b5a1ab6c71cf28f43247b3f9427c5" ]] ||
    fail "origin/main changed from the verified baseline."
  [[ -x "$DOTNET" && -x "$NODE" && -d "$DEVELOPER_DIR" ]] ||
    fail "The isolated toolchain is unavailable."
  require_regular_input "$NODE"
  require_regular_input "$CDP_SOURCE"
  require_regular_input "$RENDERER_SOURCE"
  require_regular_input "$SCHEMA_SOURCE"

  local expected_node_hash expected_node_size actual_size node_identity
  expected_node_hash=$(/usr/bin/plutil -extract node.executableSha256 raw \
    -o - "$BASELINE")
  expected_node_size=$(/usr/bin/plutil -extract node.executableSize raw \
    -o - "$BASELINE")
  [[ "$(hash_file "$NODE")" == "$expected_node_hash" ]] ||
    fail "The Node executable hash does not match the independent baseline."
  actual_size=$(/usr/bin/stat -f %z "$NODE")
  [[ "$actual_size" == "$expected_node_size" ]] ||
    fail "The Node executable size does not match the independent baseline."
  /usr/bin/file "$NODE" | /usr/bin/grep -q "Mach-O 64-bit executable arm64" ||
    fail "The Node executable is not arm64 Mach-O."
  node_identity=$("$NODE" -p \
    'process.version + " " + process.platform + " " + process.arch')
  [[ "$node_identity" == "v24.18.0 darwin arm64" ]] ||
    fail "The Node runtime identity is invalid."
  [[ "$(cd "$SCRIPT_DIRECTORY" && "$DOTNET" --version)" == "10.0.302" ]] ||
    fail "The .NET SDK identity is invalid."
}

build_manifest_tool() {
  local scratch="$1"
  /usr/bin/xcrun swift build \
    --package-path "$SWIFT_PACKAGE" \
    --scratch-path "$scratch" \
    --configuration release \
    --product "$MANIFEST_PRODUCT" 1>&2
  local bin
  bin=$(/usr/bin/xcrun swift build \
    --package-path "$SWIFT_PACKAGE" \
    --scratch-path "$scratch" \
    --configuration release \
    --show-bin-path)
  print -r -- "$bin/$MANIFEST_PRODUCT"
}

verify_staging() {
  local staging="$1"
  local manifest_tool="$2"
  local contents="$staging/Contents"
  local manifest="$contents/Resources/runtime-manifest.json"
  local helper="$contents/Helpers/CodexThemeStudio.MacHelper"
  local host="$contents/MacOS/CodexThemeStudio.RuntimeHost.dll"
  require_regular_input "$manifest"
  require_regular_input "$helper"
  require_regular_input "$host"
  local manifest_hash
  manifest_hash=$(hash_file "$manifest")
  "$manifest_tool" verify \
    --contents "$contents" \
    --manifest "$manifest" \
    --expected-sha256 "$manifest_hash" 1>&2
  "$helper" self-test 1>&2
  "$DOTNET" "$host" self-test 1>&2
}

if [[ "${1:-}" == "verify" ]]; then
  [[ $# == 2 && "$2" == /* && -d "$2" && ! -L "$2" ]] ||
    fail "verify requires one absolute staging directory."
  preflight
  /bin/mkdir -p "$ASSEMBLY_ROOT/verifier-swift"
  MANIFEST_TOOL=$(build_manifest_tool "$ASSEMBLY_ROOT/verifier-swift")
  verify_staging "$2" "$MANIFEST_TOOL"
  print -r -- \
    '{"completeChainMatch":true,"helperToDotNetMatch":true,"manifestToHelperMatch":true,"packagedRuntimeIdentityConfigured":true,"status":"ok"}'
  exit 0
fi

[[ $# == 0 ]] || fail "Usage: assemble-runtime.zsh [verify <absolute-staging>]"
preflight

/bin/mkdir -p "$ASSEMBLY_ROOT/work" "$ASSEMBLY_ROOT/staging"
WORK_ROOT=$(/usr/bin/mktemp -d "$ASSEMBLY_ROOT/work/assembly.XXXXXXXX")
INCOMPLETE=""
cleanup_current_work() {
  set +e
  case "$INCOMPLETE" in
    "$ASSEMBLY_ROOT"/staging/pending-assembly.*.incomplete)
      [[ -d "$INCOMPLETE" ]] && /bin/rm -rf -- "$INCOMPLETE"
      ;;
  esac
  case "$WORK_ROOT" in
    "$ASSEMBLY_ROOT"/work/assembly.*)
      [[ -d "$WORK_ROOT" ]] && /bin/rm -rf -- "$WORK_ROOT"
      ;;
  esac
}
trap cleanup_current_work EXIT INT TERM

MANIFEST_SCRATCH="$WORK_ROOT/manifest-swift"
MANIFEST_TOOL=$(build_manifest_tool "$MANIFEST_SCRATCH")
INCOMPLETE="$ASSEMBLY_ROOT/staging/pending-${WORK_ROOT:t}.incomplete"
CONTENTS="$INCOMPLETE/Contents"
/bin/mkdir -p \
  "$CONTENTS/MacOS" \
  "$CONTENTS/Helpers" \
  "$CONTENTS/Resources/runtime/macos" \
  "$CONTENTS/Resources/schema"

/usr/bin/ditto "$NODE" "$CONTENTS/Helpers/node"
/usr/bin/ditto "$CDP_SOURCE" \
  "$CONTENTS/Resources/runtime/macos/cdp-client.mjs"
/usr/bin/ditto "$RENDERER_SOURCE" \
  "$CONTENTS/Resources/runtime/macos/renderer-runtime.mjs"
/usr/bin/ditto "$SCHEMA_SOURCE" \
  "$CONTENTS/Resources/schema/runtime-manifest-v1.schema.json"

MANIFEST="$CONTENTS/Resources/runtime-manifest.json"
"$MANIFEST_TOOL" generate \
  --contents "$CONTENTS" \
  --output "$MANIFEST" 1>&2
MANIFEST_HASH=$(hash_file "$MANIFEST")

SWIFT_SOURCE_INPUT=""
while IFS= read -r source_file; do
  relative_source=${source_file#"$SWIFT_PACKAGE/"}
  SWIFT_SOURCE_INPUT+="$relative_source $(hash_file "$source_file")
"
done < <(
  {
    print -r -- "$SWIFT_PACKAGE/Package.swift"
    /usr/bin/find "$SWIFT_PACKAGE/Sources" -type f -print
  } | /usr/bin/sort
)
SWIFT_SOURCE_FINGERPRINT=$(print -rn -- "$SWIFT_SOURCE_INPUT" |
  /usr/bin/openssl dgst -sha256 | /usr/bin/sed 's/^.*= //')
SWIFT_KEY=$(print -rn -- "$MANIFEST_HASH
$SWIFT_SOURCE_FINGERPRINT
" | /usr/bin/openssl dgst -sha256 | /usr/bin/sed 's/^.*= //')
SWIFT_GENERATED_ROOT="$ASSEMBLY_ROOT/generated/swift/$SWIFT_KEY"
SWIFT_COPY="$SWIFT_GENERATED_ROOT/package"
SWIFT_CANDIDATE="$WORK_ROOT/generated-swift"
/usr/bin/ditto "$SWIFT_PACKAGE" "$SWIFT_CANDIDATE/package"
SWIFT_IDENTITY="$SWIFT_CANDIDATE/package/Sources/CodexThemeStudioMacHelperCore/GeneratedRuntimeIdentity.swift"
print -r -- \
"import Foundation

public enum GeneratedRuntimeIdentity {
    public static let runtimeManifestSha256 = \"$MANIFEST_HASH\"

    public static var isConfigured: Bool {
        runtimeManifestSha256.count == 64 &&
            runtimeManifestSha256.allSatisfy(\\.isHexDigit)
    }
}" > "$SWIFT_IDENTITY"

if [[ -d "$SWIFT_GENERATED_ROOT" && ! -L "$SWIFT_GENERATED_ROOT" ]]; then
  /usr/bin/diff -qr "$SWIFT_CANDIDATE" "$SWIFT_GENERATED_ROOT" 1>&2 ||
    fail "The content-addressed generated Swift source was modified."
else
  /bin/mkdir -p "$ASSEMBLY_ROOT/generated/swift"
  /bin/mv "$SWIFT_CANDIDATE" "$SWIFT_GENERATED_ROOT"
fi

SWIFT_BUILD="$ASSEMBLY_ROOT/build/swift/$SWIFT_KEY"
/bin/mkdir -p "${SWIFT_BUILD:h}"
/usr/bin/xcrun swift build \
  --package-path "$SWIFT_COPY" \
  --scratch-path "$SWIFT_BUILD" \
  --configuration release \
  --product "$HELPER_PRODUCT" \
  -Xlinker -no_uuid 1>&2
SWIFT_BIN=$(/usr/bin/xcrun swift build \
  --package-path "$SWIFT_COPY" \
  --scratch-path "$SWIFT_BUILD" \
  --configuration release \
  --show-bin-path)
/usr/bin/ditto "$SWIFT_BIN/$HELPER_PRODUCT" \
  "$CONTENTS/Helpers/CodexThemeStudio.MacHelper"
/bin/chmod 0755 "$CONTENTS/Helpers/CodexThemeStudio.MacHelper" \
  "$CONTENTS/Helpers/node"

"$MANIFEST_TOOL" verify \
  --contents "$CONTENTS" \
  --manifest "$MANIFEST" \
  --expected-sha256 "$MANIFEST_HASH" 1>&2
HELPER_HASH=$(hash_file "$CONTENTS/Helpers/CodexThemeStudio.MacHelper")

DOTNET_IDENTITY="$WORK_ROOT/GeneratedMacRuntimeIdentity.g.cs"
print -r -- \
"namespace CodexThemeStudio.CodexAdapter.MacOS;

internal static class GeneratedMacRuntimeIdentity
{
    public const string HelperSha256 = \"$HELPER_HASH\";

    public static bool IsConfigured =>
        HelperSha256.Length == 64 &&
        HelperSha256.All(Uri.IsHexDigit);
}" > "$DOTNET_IDENTITY"
[[ "$DOTNET_IDENTITY" == /* && -f "$DOTNET_IDENTITY" &&
   ! -L "$DOTNET_IDENTITY" ]] ||
  fail "The generated .NET identity source is invalid."

PUBLISH="$WORK_ROOT/dotnet-publish"
(
  cd "$SCRIPT_DIRECTORY"
  "$DOTNET" restore "$HOST_PROJECT" --locked-mode 1>&2
  "$DOTNET" publish "$HOST_PROJECT" \
    --configuration Release \
    --self-contained false \
    --no-restore \
    -p:UseAppHost=false \
    -p:MacRuntimeIdentityGeneratedSource="$DOTNET_IDENTITY" \
    --output "$PUBLISH" 1>&2
)
/usr/bin/ditto "$PUBLISH" "$CONTENTS/MacOS"
[[ ! -e "$CONTENTS/MacOS/CodexThemeStudio.RuntimeHost" ]] ||
  fail "The offline Runtime Host unexpectedly produced an apphost executable."

HOST_HASH=$(hash_file "$CONTENTS/MacOS/CodexThemeStudio.RuntimeHost.dll")
ASSEMBLY_ID_INPUT="$MANIFEST_HASH
$HELPER_HASH
$HOST_HASH
"
ASSEMBLY_ID=$(print -rn -- "$ASSEMBLY_ID_INPUT" |
  /usr/bin/openssl dgst -sha256 | /usr/bin/sed 's/^.*= //')
FINAL_ROOT="$ASSEMBLY_ROOT/staging/$ASSEMBLY_ID"
FINAL="$FINAL_ROOT/CodexThemeStudio.runtime"
if [[ -d "$FINAL" && ! -L "$FINAL" ]]; then
  verify_staging "$FINAL" "$MANIFEST_TOOL"
  print -r -- \
"{\"assemblyId\":\"$ASSEMBLY_ID\",\"completeChainMatch\":true,\"helperToDotNetMatch\":true,\"manifestToHelperMatch\":true,\"packagedRuntimeIdentityConfigured\":true,\"reused\":true,\"stagingPath\":\"$FINAL\",\"status\":\"ok\"}"
  exit 0
fi
[[ ! -e "$FINAL_ROOT" ]] ||
  fail "The content-addressed staging target is invalid."

verify_staging "$INCOMPLETE" "$MANIFEST_TOOL"

RECEIPT="$INCOMPLETE/assembly-receipt.json"
print -r -- \
"{\"assemblyId\":\"$ASSEMBLY_ID\",\"completeChainMatch\":true,\"helperToDotNetMatch\":true,\"manifestToHelperMatch\":true,\"packagedRuntimeIdentityConfigured\":true,\"runtimeVersion\":\"1\",\"schemaVersion\":1,\"toolVersion\":\"0.2.0\"}" \
  > "$RECEIPT"
"$MANIFEST_TOOL" sync --root "$INCOMPLETE" 1>&2
/bin/mkdir "$FINAL_ROOT"
/bin/mv "$INCOMPLETE" "$FINAL"
INCOMPLETE=""

verify_staging "$FINAL" "$MANIFEST_TOOL"
print -r -- \
"{\"assemblyId\":\"$ASSEMBLY_ID\",\"completeChainMatch\":true,\"helperToDotNetMatch\":true,\"manifestToHelperMatch\":true,\"packagedRuntimeIdentityConfigured\":true,\"stagingPath\":\"$FINAL\",\"status\":\"ok\"}"
