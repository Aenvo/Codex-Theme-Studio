#!/bin/zsh

set -euo pipefail

SCRIPT_DIRECTORY=${0:A:h}
REPOSITORY_ROOT=${SCRIPT_DIRECTORY:h}
DOTNET=${CTS_DOTNET10:-"$HOME/Library/Application Support/CodexThemeStudio/devtools/dotnet-10.0.302/dotnet"}
DOTNET8=${CTS_DOTNET8:-"$HOME/Library/Application Support/CodexThemeStudio/devtools/dotnet-8.0.423/dotnet"}
NODE=${CTS_NODE:-"$HOME/Library/Application Support/CodexThemeStudio/devtools/node-v24.18.0-darwin-arm64/bin/node"}
XCODE_APP=${CTS_XCODE:-"$HOME/Applications/Xcode-16.4.app"}
SWIFT_SCRATCH=${CTS_SWIFT_SCRATCH:-"$HOME/Library/Application Support/CodexThemeStudio/devtools/state/macos-productization-swift-build"}

export DEVELOPER_DIR="$XCODE_APP/Contents/Developer"
export DOTNET_ROOT="${DOTNET:h}"
export DOTNET_CLI_HOME="$HOME/Library/Application Support/CodexThemeStudio/devtools/state/dotnet-cli-10"
export NUGET_PACKAGES="$HOME/Library/Application Support/CodexThemeStudio/devtools/state/nuget-packages"
export NUGET_HTTP_CACHE_PATH="$HOME/Library/Application Support/CodexThemeStudio/devtools/state/nuget-http-cache"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_NOLOGO=1
export NUGET_XMLDOC_MODE=skip

[[ -x "$DOTNET" && -x "$DOTNET8" && -x "$NODE" ]] || {
  print -u2 "Required isolated toolchain is unavailable."
  exit 2
}

(
  cd "$REPOSITORY_ROOT"
  export DOTNET_ROOT="${DOTNET8:h}"
  "$DOTNET8" restore \
    tests/CodexRuntime.Tests/CodexThemeStudio.CodexRuntime.Tests.csproj \
    --locked-mode
  "$DOTNET8" build \
    tests/CodexRuntime.Tests/CodexThemeStudio.CodexRuntime.Tests.csproj \
    --configuration Release \
    --no-restore
  "$DOTNET8" test \
    tests/CodexRuntime.Tests/CodexThemeStudio.CodexRuntime.Tests.csproj \
    --configuration Release \
    --no-build \
    --no-restore
)

cd "$SCRIPT_DIRECTORY"
export DOTNET_ROOT="${DOTNET:h}"

"$DOTNET" restore \
  ../tests/CodexAdapter.MacOS.Tests/CodexThemeStudio.CodexAdapter.MacOS.Tests.csproj \
  --locked-mode
"$DOTNET" build \
  ../tests/CodexAdapter.MacOS.Tests/CodexThemeStudio.CodexAdapter.MacOS.Tests.csproj \
  --configuration Release \
  --no-restore
"$DOTNET" test \
  ../tests/CodexAdapter.MacOS.Tests/CodexThemeStudio.CodexAdapter.MacOS.Tests.csproj \
  --configuration Release \
  --no-build \
  --no-restore

"$DOTNET" restore \
  ../tests/CodexThemeStudio.Application.MacOS.Tests/CodexThemeStudio.Application.MacOS.Tests.csproj \
  --locked-mode
"$DOTNET" build \
  ../tests/CodexThemeStudio.Application.MacOS.Tests/CodexThemeStudio.Application.MacOS.Tests.csproj \
  --configuration Release \
  --no-restore
"$DOTNET" test \
  ../tests/CodexThemeStudio.Application.MacOS.Tests/CodexThemeStudio.Application.MacOS.Tests.csproj \
  --configuration Release \
  --no-build \
  --no-restore

"$DOTNET" restore \
  CodexThemeStudio.RuntimeHost/CodexThemeStudio.RuntimeHost.csproj \
  --locked-mode
"$DOTNET" build \
  CodexThemeStudio.RuntimeHost/CodexThemeStudio.RuntimeHost.csproj \
  --configuration Release \
  --no-restore
"$DOTNET" \
  CodexThemeStudio.RuntimeHost/bin/Release/net10.0/CodexThemeStudio.RuntimeHost.dll \
  source-self-test
"$DOTNET" \
  CodexThemeStudio.RuntimeHost/bin/Release/net10.0/CodexThemeStudio.RuntimeHost.dll \
  schema

"$DOTNET" restore \
  CodexThemeStudio.MacOS.AcceptanceHarness/CodexThemeStudio.MacOS.AcceptanceHarness.csproj \
  --locked-mode
"$DOTNET" build \
  CodexThemeStudio.MacOS.AcceptanceHarness/CodexThemeStudio.MacOS.AcceptanceHarness.csproj \
  --configuration Release \
  --no-restore
"$DOTNET" \
  CodexThemeStudio.MacOS.AcceptanceHarness/bin/Release/net10.0/CodexThemeStudio.MacOS.AcceptanceHarness.dll \
  source-self-test
"$DOTNET" \
  CodexThemeStudio.MacOS.AcceptanceHarness/bin/Release/net10.0/CodexThemeStudio.MacOS.AcceptanceHarness.dll \
  schema

/usr/bin/xcrun swift package \
  --package-path ../native/macos/CodexThemeStudio.MacHelper \
  --scratch-path "$SWIFT_SCRATCH" \
  describe
/usr/bin/xcrun swift test \
  --package-path ../native/macos/CodexThemeStudio.MacHelper \
  --scratch-path "$SWIFT_SCRATCH"
/usr/bin/xcrun swift build \
  --package-path ../native/macos/CodexThemeStudio.MacHelper \
  --scratch-path "$SWIFT_SCRATCH" \
  --configuration release

SWIFT_BIN=$(/usr/bin/xcrun swift build \
  --package-path ../native/macos/CodexThemeStudio.MacHelper \
  --scratch-path "$SWIFT_SCRATCH" \
  --configuration release \
  --show-bin-path)
"$SWIFT_BIN/codex-theme-studio-mac-helper" source-self-test
"$SWIFT_BIN/codex-theme-studio-mac-helper" schema
"$SWIFT_BIN/codex-theme-studio-runtime-manifest" schema

"$NODE" --test \
  tests/managed-runtime-identity.test.mjs \
  ../runtime/macos/injector/tests/cdp-client.test.mjs \
  ../runtime/macos/injector/tests/runtime-assembly.test.mjs
"$NODE" ../runtime/macos/injector/cdp-client.mjs self-test
"$NODE" ../runtime/macos/injector/cdp-client.mjs schema
