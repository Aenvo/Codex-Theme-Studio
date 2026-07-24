# ADR 0001：平台与架构基线

- 状态：Accepted
- 日期：2026-07-20
- 决策范围：第一版 Windows 便携应用

## 背景

Codex Theme Studio 是非 OpenAI 官方的 Windows 本地主题管理器。第一版需要在不要求管理员权限、不修改 Codex 官方安装文件、且用户无需预装 .NET 或 Node.js 的前提下，以 ZIP 解压即用的形式交付。

任务 1 已在当前 Windows x64 环境完成最小 WPF self-contained 发布和 SQLite 运行验证。验证结果见 [任务 1 环境审计](../audits/task-01-environment-audit.md)。

## 决策

### GUI 与目标平台

- GUI 使用 `.NET 8 + WPF`。
- 目标为 Windows 10/11 x64，目标框架使用 `net8.0-windows`，发布 RID 使用 `win-x64`。
- 第一版不引入跨平台 UI 框架。

### 数据库与文件存储

- SQLite 访问层使用 `Microsoft.Data.Sqlite`；任务 1 验证版本为 `8.0.29`。
- SQLite 只保存主题索引、元数据和应用状态，不保存大型图片 Blob。
- 图片、缩略图、主题 JSON 和导出包存放在受信任 DataRoot 下的文件系统中。
- 数据库写入使用事务；主题文件和状态文件使用临时文件加原子替换。

### 便携发布结构

- 使用 Windows x64 self-contained 便携目录，再生成 ZIP；不强制单文件 EXE。
- 应用包包含 .NET 运行文件，因此终端用户不需要预装 .NET。
- 第一版捆绑固定版本的 Node.js Windows x64 Runtime，初始冻结为 `v24.18.0`；运行时版本升级必须经过兼容性、许可证、哈希和真实 Codex 场景验证。
- 发布包不依赖系统 `node`、`npm` 或 `npx`。
- 任务 1 曾按 `270–300 MiB` 规划未压缩目录；1.0.0 实测为 `365,821,021 B`，ZIP 为 `151,374,519 B`，后续版本以各自 release manifest 为准。

### 模块边界

后续解决方案保持以下分层：

- Desktop：WPF 界面与用户操作编排。
- Theme Core：主题模型、校验与纯业务规则，不依赖 WPF、SQLite、Node 或 Codex。
- Storage：SQLite 仓储、文件存储和 DataRoot 迁移。
- Codex Adapter：Codex 发现、身份校验、短时 Inspector 通道与注入编排，不依赖 Desktop。
- Persistence Agent：无 WPF 依赖的轻量持久化进程。
- Contracts：跨模块和跨进程的结构化 DTO、错误与版本契约。

### 注入助手边界

- Node Runtime 只运行随应用发布、固定版本且经过审计的注入助手。
- Desktop 不拼接 Shell、PowerShell 或 Node 命令；跨进程请求使用结构化 DTO，主题数据不得拼入命令行。
- 主题包只包含声明式、白名单校验的数据，不得携带任意 JavaScript、命令、HTML 或远端 CSS。
- 只连接回环地址上的短时 Inspector 通道，并校验 Appx 包身份、主进程路径、PID、进程创建时间、Browser ID、Page Target ID 和 `app://` URL。
- 注入或探测结束后关闭 Inspector；不长期开放调试端口。
- 使用主进程 `dom-ready` 生命周期信号触发受控重应用，但必须再次执行目标身份和窗口特征校验。
- 明确排除 `initialRoute=/avatar-overlay`，并以路由规则和完整主界面 DOM 特征进行双重窗口隔离；无法识别的窗口保持原样。

### 本地优先与隐私

- 已有主题的编辑、预览、切换和持久化默认离线可用。
- 不上传用户图片、主题数据、日志、Codex 页面或对话内容。
- 不读取或修改 API Key、Base URL、模型供应商、MCP、身份或权限配置。
- 远端主题源若以后加入，必须另行设计下载白名单、完整性校验、签名、防回滚和隐私授权。

### Codex 安装边界

- 不修改、替换或取得 `WindowsApps` 所有权。
- 不修改 `app.asar`、Codex 官方 EXE、Appx 内容或数字签名。
- 不以管理员权限或绕过系统安全控制作为正常功能前提。

## 结果与约束

- 便携包体积高于 framework-dependent 发布，但换取终端用户零运行时安装。
- Node 与 .NET Runtime 都成为需跟踪安全公告、许可证和哈希的供应链组件。
- `.NET 8` 将于 2026-11-10 结束支持；项目在该日期前发布时必须使用仍受支持的最新 8.0 补丁。若发布日期跨过该日期，需要以新 ADR 评估迁移至受支持的 .NET LTS，不得静默变更技术基线。
- 本 ADR 不证明真实 Codex 注入、窗口隔离或持久化已实现；这些能力分别留给后续编号任务验证。
