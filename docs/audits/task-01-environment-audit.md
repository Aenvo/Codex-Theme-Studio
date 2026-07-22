# 任务 1 环境审计

- 审计日期：2026-07-20
- 项目根目录：当前 `Codex Theme Studio` 工作目录
- Git 状态：当前目录不是 Git 仓库，无法执行任务文档要求的 `git status` 范围校验

## 项目规则与现有文件

| 项目 | 结果 |
| --- | --- |
| `AGENTS.override.md` | 未发现 |
| `AGENTS.md` | 已发现并作为项目级执行约束 |
| `design.md` | 未发现；任务 1 不创建 |
| README | 未发现；任务 1 不创建 |
| 既有 ADR、代码、测试 | 未发现 |

任务 1 只新增环境审计、ADR 和初始风险清单，不创建解决方案或功能代码。

## 当前机器

| 项目 | 观察值 |
| --- | --- |
| Windows | Windows 11 专业版，10.0.26200，Build 26200，x64 |
| Codex Appx | `OpenAI.Codex`，`26.715.4045.0`，x64 |
| PowerShell | Windows PowerShell `5.1.26100.8875`，Desktop Edition |
| 系统 Node.js | `v24.16.0`，x64 安装目录总计 104,706,123 bytes |
| 系统 `dotnet` Host | `10.0.7`，x64 |
| 系统 .NET SDK | 未安装 |
| 系统 .NET Runtime | `Microsoft.NETCore.App` 8.0.5、10.0.7 |
| 系统 WPF Runtime | `Microsoft.WindowsDesktop.App` 8.0.5、10.0.7 |

系统存在 Runtime 不等于具备构建能力。为完成本任务验证，使用 Microsoft 官方发布元数据取得隔离的 `.NET SDK 8.0.423`，未写入系统 PATH，也未修改项目目录。

## WPF self-contained 与 SQLite 验证

验证探针位于系统临时目录，不属于项目交付物。探针配置：

- WPF：`net8.0-windows`
- RID：`win-x64`
- 发布：`Release`、self-contained、非单文件
- SQLite：`Microsoft.Data.Sqlite 8.0.29`
- 运行检查：打开内存数据库，建表，在事务中写入，提交后读回 `sqlite-ok`

等价验证命令：

```powershell
dotnet publish .\CodexThemeStudio.Task1Probe.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=false
```

验证结果：

| 项目 | 结果 |
| --- | --- |
| Restore | 成功 |
| WPF 编译与发布 | 成功 |
| 探针进程退出码 | `0` |
| SQLite 建表、事务、参数化写入和读回 | 成功，结果为 `sqlite-ok` |
| 发布文件数 | 470 |
| 发布目录总大小 | 169,455,275 bytes（约 161.6 MiB） |
| 主 EXE 大小 | 151,552 bytes |
| SQLite 相关发布文件总大小 | 1,958,721 bytes（包含探针结果文件） |

SDK 下载文件为 285,072,593 bytes，SHA-512 与 Microsoft 发布元数据一致。临时 SDK 首次运行还创建了标准的未信任 ASP.NET Core 开发证书；本任务没有使用该证书，也未修改其信任状态。

## SQLite 方案结论

选用 `Microsoft.Data.Sqlite`：

- Microsoft 官方维护的轻量 ADO.NET SQLite Provider；
- 可独立使用，不要求引入 Entity Framework Core；
- 已在本机 `win-x64` self-contained WPF 发布物中验证原生 `e_sqlite3.dll` 可加载；
- 已验证基本连接、事务、参数化命令和读回。

此验证不覆盖并发、数据库损坏恢复、迁移、符号链接/Junction、离线 DataRoot 或外置磁盘场景；这些属于后续存储与集成任务。

## Node Runtime 与预计发布体积

- 第一版确认随包提供固定版本 Node.js Windows x64 Runtime，不使用系统 Node。
- 初始冻结版本：Node.js `v24.18.0`。
- 官方 `win-x64.zip` 约 37 MB；当前机器 `v24.16.0` 安装目录实测约 99.9 MiB。
- WPF + SQLite 最小 self-contained 探针约 161.6 MiB。
- 因此第一版未压缩便携目录按约 `270–300 MiB` 规划；包含真实应用、注入脚本、许可证、图标和默认资源后的最终 ZIP 大小由任务 12 实测，当前估算为 `100–150 MiB`。

## 下一任务输入

- 项目路径：本文件所在项目根目录。
- 任务 2 应创建解决方案脚手架和模块契约；本任务没有提前创建 `.sln` 或 `.csproj`。
- 当前系统没有全局 .NET SDK。执行任务 2 前需安装或提供 `.NET 8 SDK 8.0.423` 等兼容的受支持 8.0 SDK。
- 脚手架创建后，基线构建命令为：

```powershell
dotnet build .\CodexThemeStudio.sln --configuration Release
dotnet publish .\src\CodexThemeStudio.Desktop\CodexThemeStudio.Desktop.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=false
```

解决方案和项目路径将在任务 2 固化后，以实际文件为准。

## 参考

- [.NET 官方支持策略](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [Microsoft.Data.Sqlite 官方概览](https://learn.microsoft.com/dotnet/standard/data/sqlite/)
- [Node.js v24 官方发布归档](https://nodejs.org/en/download/archive/v24)

