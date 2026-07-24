# 构建与验证

便携发布的固定输入、生成命令和发布前检查见[便携发布复现与验收](releasing.md)。

## 前置条件

- Windows 10/11 x64。
- `.NET SDK 8.0.423`。
- 构建与 Injector 自检严格使用 Node.js `24.18.0`；正式便携包使用相同的随包 Runtime。

项目根目录的 `global.json` 固定 SDK 版本，`eng/runtime-baseline.json` 固定 Node、下载哈希和 RID。`build.ps1` 按以下顺序查找 SDK：

1. `DOTNET_ROOT`。
2. `%LOCALAPPDATA%\CodexThemeStudio\devtools\dotnet-8.0.423`。
3. 系统 `dotnet`。

脚本不会修改 PATH、注册表、启动项或 Codex 配置。

Node 按 `runtime/node`、`artifacts/cache`、最新本地发布包、系统 PATH 的顺序查找，但只有版本精确为 `v24.18.0` 才会接受。

普通构建、测试和 Agent 自检固定选择 `win-x64`，并保持 framework-dependent；这会避免把其他平台的 native assets 复制到本地输出。正式发布仍由 `package.ps1` 分别生成 Desktop 和 Agent 的 self-contained 输出。

## 一键验证

在项目根目录运行：

```powershell
.\build.ps1
```

默认执行：

1. 使用锁文件还原 NuGet 包；
2. Release 构建；
3. 全部测试；
4. 代码格式检查；
5. Agent `self-test`；
6. Node Injector `self-test`；
7. Node Injector 安全协议测试；
8. .NET 测试中的 Windows PowerShell 5.1 精确 EXE 发现与结构化 JSON 回归。

调试配置：

```powershell
.\build.ps1 -Configuration Debug
```

## 单独命令

```powershell
dotnet build .\CodexThemeStudio.sln --configuration Release -p:CodexRuntimeIdentifier=win-x64 -p:SelfContained=false
dotnet test .\CodexThemeStudio.sln --configuration Release -p:CodexRuntimeIdentifier=win-x64 -p:SelfContained=false
dotnet run --project .\src\CodexThemeStudio.Agent --runtime win-x64 --no-self-contained -- self-test
node .\runtime\injector\index.mjs self-test
node --test `
  .\runtime\injector\security.test.mjs `
  .\runtime\injector\renderer-payload.test.mjs `
  .\runtime\injector\renderer-runtime.test.mjs `
  .\runtime\injector\main-runtime.test.mjs
```

如果系统 `dotnet` 没有 8.0.423 SDK，使用 `build.ps1` 或先设置 `DOTNET_ROOT`。

## 本地机器交接

项目可以在不依赖 GitHub remote 的情况下，通过完整项目交接 ZIP 转移到另一台 Windows 机器。交接包不是最终用户便携发布包，应保留 `.git`、打包时的已修改和未跟踪文件、源码、测试、文档、许可证、`artifacts/cache` 中的固定 Node Runtime，以及归档保留策略要求的发布 ZIP、校验和与 manifest。

机器级 `.NET SDK` 和 NuGet 全局缓存不属于项目交接包。接收方应安装 `.NET SDK 8.0.423`，或把对应 SDK 放到受信任目录并设置 `DOTNET_ROOT`；首次 locked restore 仍可能需要访问 NuGet 源。交接包内的固定 Node Runtime 可以直接由 `build.ps1` 发现并复用。

当前源码维护基线仍由 `Directory.Build.props` 固定为 `1.1.7`。交接包保留的 `1.1.8`、`1.1.9` 和 `1.1.10` 只有 ZIP、校验和与 manifest，没有对应本地验收记录，因此必须标记为未验收归档，不能据此升级源码基线或声明发布通过。

接收方按以下顺序验收：

1. 对照随包校验文件核对交接 ZIP 的 SHA-256。
2. 将 ZIP 完整解压到普通可写目录，不要直接在压缩包中构建。
3. 阅读根目录 `HANDOFF.md`、`HANDOFF-MANIFEST.json`、`AGENTS.md`、本文件和 `docs/risks/risk-register.md`。
4. 确认 `.git` 存在，运行 `git status --short --branch` 和 `git log -1 --oneline`；没有 remote 是允许状态，不要重新初始化仓库或清理当前工作树。
5. 核对 `global.json` 与 `eng/runtime-baseline.json` 的固定版本，然后运行：

```powershell
.\build.ps1 -Configuration Release
```

交接清单中的分支、HEAD、文件数、字节数和工作树状态只是打包时快照。接收方完成解压后应以实际文件系统和 Git 输出为准。`bin/`、`obj/`、`artifacts/work`、`artifacts/validation` 和已解压发布目录不进入交接包，它们可由构建、验证或发布流程重新生成。

## 生成物清理

`eng/clean-generated.ps1` 只处理 Git 已忽略且位于项目根目录内的生成物。默认命令仅预览目标、证据文件、发布归档校验和回收站容量，不修改任何文件：

```powershell
.\eng\clean-generated.ps1
```

当前 `ArchivesOnly` 策略保留每个发布版本的 ZIP、`SHA256SUMS.txt` 和 `release-manifest.json`，只回收对应的解压便携目录。`artifacts/cache` 包含固定 Node Runtime，始终排除在清理范围之外。

实际执行必须显式提供 `-Execute`、确认短语、`-ReleaseRetention ArchivesOnly` 和一个位于项目外部、尚不存在的证据备份目录：

```powershell
.\eng\clean-generated.ps1 `
  -Execute `
  -ConfirmCleanup RECYCLE_CODEX_THEME_STUDIO_GENERATED_OUTPUTS `
  -ReleaseRetention ArchivesOnly `
  -EvidenceBackupRoot '<已确认的外部备份目录>'
```

脚本会先备份 `artifacts/validation`，以及 `artifacts/work` 中的截图、TRX、日志和 Markdown，逐文件核对 SHA-256 后才开始回收。每个目标必须由 `.gitignore` 排除、不得包含重解析点或 Git 跟踪文件；发布 ZIP 校验失败时保留该版本的解压目录。

如果执行因单个回收站元数据文件被短暂占用而中止，可以在确认已处理目标的 `$I/$R` 后使用相同参数加 `-Resume`。续执行会重新核对现有证据 manifest、全部备份文件及剩余目标，不会覆盖或重新创建备份。

脚本通过当前用户回收站处理目录，并验证新增 `$I` 元数据和对应 `$R` 数据项。移入回收站只缩小项目目录，不会释放源卷空间；脚本不会清空回收站，最终清空操作由用户自行决定。

## Windows 主机兼容性

Injector 的目标发现明确由系统 `powershell.exe` 执行，因此运行时脚本以 Windows PowerShell 5.1 为最低语法和 API 基线，不能假设 PowerShell 7 或 .NET Core API 可用。`WindowsDiscoveryCompatibilityTests` 会真实调用 Windows PowerShell 5.1，覆盖精确 EXE `Discover`、缺少可用映像路径时的 `Snapshot` 和单一 JSON 响应。

完整静态扫描、文件句柄检查和真实 Codex 更新验收步骤见[Windows 主机与 Codex 更新兼容性测试方案](testing/runtime-compatibility-plan.md)。

## 本地源码启动

`Start-CodexThemeStudio.cmd` 是仓库内的开发辅助入口，不属于便携发布包。它使用 `%LOCALAPPDATA%\CodexThemeStudio\devtools\dotnet-8.0.423\dotnet.exe` 构建 `win-x64` Desktop Release，并启动 `src/CodexThemeStudio.Desktop/bin/Release/net8.0-windows/win-x64/CodexThemeStudio.Desktop.exe`；不会修改或覆盖 `artifacts/release` 中的历史便携发布。

运行前应关闭旧便携版 `CodexThemeManager.exe`。脚本检测到旧进程时会停止启动并给出提示，避免同时看到旧发布界面和当前源码界面。正式用户仍应从经过验收的便携目录启动 `CodexThemeManager.exe`。

## 当前边界

- Agent 支持 `self-test`、`run --config`、`once --config` 和
  `signal-stop`。普通构建与测试不会注册启动项；只有用户明确启用持久化时，
  `IPersistenceService` 才创建当前用户 Run 项并启动低频 Agent。
- Injector 已实现 `self-test`、`discover`、`probe --pid <PID>` 和
  `close-inspector --pid <PID>`，以及任务 6 内部验证所需的 `prepare`、
  `renderer-probe`、`renderer-apply`、`renderer-ensure`、
  `renderer-status`、`renderer-cleanup`。`discover` 只读取 Store 包与进程身份；
  `probe` 会短时打开经校验主进程的 Inspector，执行不读取页面文本的只读探针，
  并在结束时关闭 Inspector。不要把 `probe` 用作常驻监控。
- Renderer 命令通过 UTF-8 JSON 标准输入接收声明式主题和受管图片，不接受
  命令行主题值。Desktop 和 Agent 已通过结构化服务封装临时应用、状态查询和
  完整还原语义。
- Storage 已实现 SQLite 索引、主题 JSON/资源文件存储、DataRoot 初始化与
  一致性扫描、图片安全处理、受管 WebP、预览缓存、安全 `.cttheme` 导入导出，
  以及带文件指纹和 SQLite 完整性检查的数据目录迁移。
- Desktop 提供主题资料库、编辑器、模拟预览、导入导出、存储迁移和运行时操作，并持续显示非官方产品声明。
- Desktop 的 Lucide 图标在设计与开发阶段通过 `icons0/i0` 检索和选型，随后固化为 `Themes/Icons.xaml` 中的原生 WPF Geometry；构建和运行不依赖 React、SVG Runtime、Iconify 数据库或在线 `icons0/i0` 服务。
