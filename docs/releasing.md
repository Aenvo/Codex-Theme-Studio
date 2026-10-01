# 便携发布复现与验收

## 固定输入

- Windows 10/11 x64。
- .NET SDK 基准 `8.0.423`；`global.json` 的 `latestPatch` 允许同一 feature band 内的后续 SDK 补丁。`BUILD-INFO.md` 记录实际选择的 SDK 与自包含包内 .NET 8 Runtime 补丁，不把 SDK 基准或 SQLite 包版本当作运行时版本。
- Node.js Windows x64 `v24.18.0`。
- Node 压缩包 SHA-256：
  `0ae68406b42d7725661da979b1403ec9926da205c6770827f33aac9d8f26e821`。
- 发布版本以仓库根目录 `Directory.Build.props` 的 `Version` 为准；不要从本机归档目录推断维护基线。

Node Runtime 只从 `https://nodejs.org/download/release/v24.18.0/` 获取。脚本会在解压前校验固定 SHA-256，并在打包前执行 `node.exe --version`。

## 生成发布包

在仓库根目录运行：

```powershell
.\package.ps1
```

未传入 `-Version` 时，脚本以 `Directory.Build.props` 的 `Version` 作为发布版本；显式传入版本仅用于有意生成预发布测试包，可使用 SemVer 预发布格式。正式候选包应直接使用源码中已同步的稳定版本运行 `package.ps1`，不传入 `-SkipVerification`。

脚本按顺序执行：

1. `build.ps1` 的 Release 构建、测试、格式、自检和 Injector 测试。
2. 下载并校验固定 Node Runtime（已存在且哈希正确时复用缓存）。
3. 分别发布 Desktop 与 Agent 的 `win-x64` self-contained 暂存目录。
4. 按相对路径、大小和 SHA-256 去除内容完全相同的 Agent 运行时文件，并生成
   `agent/agent-bundle-manifest.json`；冲突文件保留在 `agent/`。
5. 只复制生产 Injector 文件，不包含测试、fixture、日志、数据库或用户数据。
6. 附带第一方 Apache-2.0 `LICENSE`、README、用户指南、自制应用图标、Lucide WPF 矢量资源、第三方 Notices 和对应许可证。
7. 仅保留简体中文卫星资源和根目录中性英文资源。
8. 生成逐文件大小与 SHA-256 的 Schema v1 `app-install-manifest.json`，并将其摘要写入 Schema v3 `release-manifest.json`。
9. 生成 ZIP、`SHA256SUMS.txt` 和 `release-manifest.json`；ZIP 超过
   100,000,000 字节时警告，超过 120,000,000 字节时失败。

脚本不会覆盖已有的同版本发布目录。需要重建同一版本时，应先由维护者将旧产物移到可恢复的归档位置，或使用新的版本号；不要用破坏性清理命令。

完成 ZIP、`SHA256SUMS.txt` 和 `release-manifest.json` 校验后，可以按 `docs/building.md` 中的 `ArchivesOnly` 清理策略回收已解压便携目录，同时保留每个版本的三项归档文件。需要再次检查 EXE 签名或目录内容时，应把对应 ZIP 解压到独立临时目录，不要假定历史发布目录仍保留解压副本。

## 产物

```text
artifacts/release/<version>/
├─ Codex-Theme-Studio-<version>-win-x64-portable/
├─ Codex-Theme-Studio-<version>-win-x64-portable.zip
├─ SHA256SUMS.txt
└─ release-manifest.json
```

便携目录中的主程序是 `CodexThemeManager.exe`，产品元数据名称为 `Codex Theme Studio`。持久化组件的独有文件位于 `agent/`，共享运行时位于包根；`agent-bundle-manifest.json` 描述如何重建完整稳定 Agent。`runtime/updater/apply-update.mjs` 与包内 Node 组成外置更新 runner；`app-install-manifest.json` 是成功更新后清理旧程序文件的所有权依据。稳定安装后仍使用已冻结的 Agent 文件名、进程与启动项契约，不代表第二个产品。

历史版本的验收证据保留在 `docs/testing/`。本机 `artifacts/release/` 仅存放产物，不决定当前维护版本；归档清理遵循 `docs/building.md` 的可恢复流程。

## 发布前检查

```powershell
[xml]$props = Get-Content -Raw .\Directory.Build.props
$version = [string]($props.Project.PropertyGroup.Version | Select-Object -First 1)
$releaseRoot = Join-Path '.\artifacts\release' $version
$packageName = "Codex-Theme-Studio-$version-win-x64-portable"
$packageRoot = Join-Path $releaseRoot $packageName
$zip = Join-Path $releaseRoot "$packageName.zip"
Get-Content (Join-Path $releaseRoot 'SHA256SUMS.txt')
Get-FileHash $zip -Algorithm SHA256
Get-AuthenticodeSignature (Join-Path $packageRoot 'CodexThemeManager.exe')
Get-AuthenticodeSignature (Join-Path $packageRoot 'agent\CodexThemeStudio.Agent.exe')
```

核对实际 ZIP 哈希与 `SHA256SUMS.txt`、`release-manifest.json` 一致，Agent manifest、包内 `app-install-manifest.json` 与逐文件大小和哈希一致。当前发布流程预期主程序和 Agent 均为 `NotSigned`，必须在用户文档中如实披露。

以上 EXE 命令适用于刚生成、尚未按 `ArchivesOnly` 清理的便携目录。若只保留归档文件，应先校验 ZIP，再解压到项目外的独立临时目录并对其中相同相对路径执行签名、许可证和内容检查；检查结束后按可恢复策略处理临时目录。

同时确认便携目录根部包含第一方 `LICENSE`，`LICENSES/` 包含 `Lucide-LICENSE.txt` 等第三方许可证，并且界面图标不需要联网加载。

在 Defender 服务、实时保护和签名库有效的环境中，沿用上方 PowerShell 会话中的 `$zip` 使用自定义扫描；若不可用，记录为未覆盖：

```powershell
Start-MpScan -ScanType CustomScan -ScanPath $zip
```

出现检测时停止分发并审查原因，不建议用户关闭安全软件或盲目加白。

## GitHub 私有预演与 Draft Release

普通 `main` push、Pull Request 和手动运行 CI 只执行完整
`.\build.ps1 -Configuration Release`，不会创建 tag、打包 Release 资产或创建
Release。同一分支的新 CI 会取消尚未完成的旧运行。

版本提交审核完成后按以下顺序执行，并核对每一步的结果：

1. 将已验证的版本提交按授权推送到 `main`，确认该提交的 CI 完整通过；如使用 PR，也需确认合并后的 `main` CI。
2. 手动运行 `Windows release` workflow。`workflow_dispatch` 只生成保留
   14 天的私有 Actions Artifact，不创建 Release。
3. 下载 Artifact，复核 ZIP、`SHA256SUMS.txt`、Schema v3 `release-manifest.json`、包内 Schema v1 `app-install-manifest.json`、120 MB 上限和 `NotSigned` 状态。
4. 核对 tag 尚不存在、目标是通过 CI 的版本提交，并人工创建及推送与
   `Directory.Build.props` 精确匹配的带注释 tag。若本版本缺少有效 Defender、
   干净 Windows 或真实中断边界证据，后续 Release 必须保持 Draft，明确披露
   所有缺失门禁，并在公开前另行取得仅针对本版本的风险接受；风险接受不代表
   门禁通过，也不自动适用于后续版本：

```powershell
[xml]$props = Get-Content -Raw .\Directory.Build.props
$version = [string]($props.Project.PropertyGroup.Version | Select-Object -First 1)
$tag = "v$version"
git tag -a $tag -m "release: Codex Theme Studio $tag"
git push origin $tag
```

精确 tag 触发的 workflow 会创建标题为 `Codex Theme Studio v<version>` 的 Draft
Release，并上传便携 ZIP、`SHA256SUMS.txt` 和 `release-manifest.json`。GitHub
自动附带的 Source code ZIP/TAR 不是可运行产品。workflow 不会自动创建 tag，
也不会把 Draft 公开发布。

随包 README、用户指南及兼容性方案中的版本验收记录链接会指向该 tag 下的公开仓库文档；验收记录不作为便携运行时文件打入包。

带注释 tag 使用 `release: Codex Theme Studio v<version>`；Draft Release Notes 由
GitHub 自动生成，并附带未签名、哈希校验、未覆盖验收边界和非 OpenAI 官方产品说明。

发布前逐项核对 GitHub Private Vulnerability Reporting、Secret Scanning 和 Push Protection
的启用状态，并写入本版本验收记录。确认 Draft 的 tag、提交、三项产品资产、两个源码归档、
哈希、未签名披露和干净机证据均正确后，才由维护者人工发布 Draft。若使用
本版本的风险接受路径，维护者必须复核全部未覆盖风险的公开披露并另行授权
公开；Codex 仍不得自动公开。

## 最终场景

应在无系统 .NET 和 Node 的临时 Windows 用户或干净虚拟机执行：

1. 把 ZIP 解压到同时含空格和中文的路径。
2. 双击主 EXE。
3. 创建主题并导入本地图片。
4. 临时显示、切换和 Restore。
5. 启用持久化，确认启动项和稳定 Agent 路径。
6. 完全退出并重启 Codex，确认主题由 Agent 恢复。
7. 停用持久化，确认启动项移除、Agent 退出和 Codex 还原。
8. 将 DataRoot 迁移到新的空目录，核对报告并确认旧目录保留。
9. 删除程序目录，确认文档所述的数据与 Agent 行为。

验收结论必须区分源码自动化、当前用户现场验证和真正干净环境验证；后两者不能由构建成功替代。

## 公网自更新验收

维护者人工公开本版本 Draft 后，应从经过官方哈希验证的上一稳定版隔离便携副本执行一次真实程序内更新，核对自动发现、版本、成功通知、旧目录清理、未知文件保留及持久化 Agent 状态。具体前置版本、实测结果和未覆盖边界写入对应版本的 `docs/testing/` 验收记录；历史版本结论不自动适用于新版本。

首次公网验收失败时停止后续传播并保留 runner、result、backup 或 Preserved 证据；不得把 Draft、构建成功或本地 fixture 当作公网更新成功。Codex 不自动公开 Release。
