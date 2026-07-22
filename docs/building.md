# 构建与验证

便携发布的固定输入、生成命令和发布前检查见[便携发布复现与验收](releasing.md)。

## 前置条件

- Windows 10/11 x64。
- `.NET SDK 8.0.423`。
- 开发阶段执行 Injector 自检需要 Node.js 24.x；正式便携包将使用项目固定的随包 Runtime。

项目根目录的 `global.json` 固定 SDK 版本。`build.ps1` 按以下顺序查找 SDK：

1. `DOTNET_ROOT`。
2. `%LOCALAPPDATA%\CodexThemeStudio\devtools\dotnet-8.0.423`。
3. 系统 `dotnet`。

脚本不会修改 PATH、注册表、启动项或 Codex 配置。

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
7. Node Injector 安全协议测试。

调试配置：

```powershell
.\build.ps1 -Configuration Debug
```

## 单独命令

```powershell
dotnet build .\CodexThemeStudio.sln --configuration Release
dotnet test .\CodexThemeStudio.sln --configuration Release
dotnet run --project .\src\CodexThemeStudio.Agent -- self-test
node .\runtime\injector\index.mjs self-test
node --test `
  .\runtime\injector\security.test.mjs `
  .\runtime\injector\renderer-payload.test.mjs `
  .\runtime\injector\renderer-runtime.test.mjs `
  .\runtime\injector\main-runtime.test.mjs
```

如果系统 `dotnet` 没有 8.0.423 SDK，使用 `build.ps1` 或先设置 `DOTNET_ROOT`。

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
  命令行主题值。任务 7 才会把这些内部命令封装为临时应用、状态查询和完整还原
  的应用层语义。
- Storage 已实现 SQLite 索引、主题 JSON/资源文件存储、DataRoot 初始化与
  一致性扫描、图片安全处理、受管 WebP、预览缓存、安全 `.cttheme` 导入导出，
  以及带文件指纹和 SQLite 完整性检查的数据目录迁移。
- Desktop 只显示应用名、未连接状态和非官方声明。
