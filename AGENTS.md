# Codex Theme Studio 项目协作规则

## 1. 项目身份与当前基线

- 正式产品名是 **Codex Theme Studio**；发布入口 `CodexThemeManager.exe` 是兼容文件名，不代表另一个产品。
- 本项目是非 OpenAI 官方的 Windows 本地桌面应用，不得使用暗示官方背书的名称、图标或发布文案。
- 除另有说明外，第一方源码采用 Apache License 2.0；第三方组件和素材继续适用各自许可证。该许可证不授予 OpenAI、Codex 或其他第三方商标使用权。
- 当前维护基线为 `1.3.2`：Windows 10/11 x64、.NET 8 WPF、SQLite、固定 Node.js Runtime、self-contained 便携目录和 ZIP。
- 本文件适用于仓库根目录及全部子目录。更深目录的 `AGENTS.override.md` 或 `AGENTS.md` 可增加局部约束。

## 2. 真相源与修改前检查

开始代码、配置、构建、测试、发布或文档变更前：

1. 检查 `git status`，保留无关修改和未跟踪文件。
2. 读取本文件及任务相关源码、测试和文档。
3. 架构决策以 `docs/adr/` 为准；当前风险以 `docs/risks/risk-register.md` 为准；当前发布验收以最新版本验收记录为准。
4. UI、样式或设计系统变更仅在 `design.md` 存在时读取并遵循；缺失时不得自动创建。
5. 历史 `task-*` 文档是证据快照，不是当前实施状态的唯一真相源。

不确定的项目事实标记为 `To be confirmed`，不得根据旧任务编号或历史验收推断当前状态。

## 3. 模块边界

```text
src/
├─ CodexThemeStudio.Desktop/       WPF UI 与组合根
├─ CodexThemeStudio.ThemeCore/     主题格式和纯业务规则
├─ CodexThemeStudio.Storage/       SQLite、文件存储、图片与迁移
├─ CodexThemeStudio.CodexAdapter/  Codex 发现、Inspector、运行时与持久化
├─ CodexThemeStudio.Agent/         无 WPF 的持久化 Agent
├─ CodexThemeStudio.Update/        无 WPF 的更新发现、校验、暂存与事务安装
└─ CodexThemeStudio.Contracts/     跨模块 DTO、接口和结构化结果
```

- ThemeCore 不依赖 WPF、SQLite、Node 或 Codex。
- Storage 实现存储接口；不得把大型图片写入 SQLite Blob。
- CodexAdapter 不依赖 Desktop；Agent 不依赖 WPF。
- Desktop 不直接拼接 Shell、PowerShell 或 Node 命令。
- 跨进程数据使用结构化 DTO 和结构化错误；主题 JSON 不进入命令行。
- 保留 Assembly Marker；边界测试通过它们检查项目引用。

## 4. 固定工具链与标准命令

- .NET SDK 版本由 `global.json` 固定。
- Node.js 版本、Node 压缩包哈希和 RID 由 `eng/runtime-baseline.json` 固定。
- NuGet 版本由 `Directory.Packages.props` 和各项目 `packages.lock.json` 固定。
- 不新增依赖、框架或测试工具，除非当前变更确有需要并已核对许可证、锁文件与发布影响。

在仓库根目录执行：

```powershell
.\build.ps1 -Configuration Release
.\package.ps1
```

`build.ps1` 必须完成 locked restore、build、tests、format、Agent self-test、Injector self-test 和 Node tests。`package.ps1` 不覆盖同版本目录；Desktop 与 Agent 必须先生成隔离的 self-contained 暂存输出，最终便携包可按 `agent-bundle-manifest.json` 只存一份内容相同的运行时文件，Agent 独有文件保持位于 `agent/`。稳定安装时必须校验 manifest 并重建完整、自包含的 Agent 版本目录。

## 5. 数据与图片安全边界

- 主题是版本化声明式数据；拒绝 JavaScript、命令、可执行文件、HTML、远端 CSS 和生命周期 Hook。
- 主题 ID 使用 UUID；文件路径必须相对于可信 DataRoot。
- 阻止绝对路径、`..`、Zip Slip、符号链接和 Junction 逃逸。
- JSON、状态和定位文件使用临时文件加原子替换；数据库写入使用事务。
- 未知高版本 Schema 只读报告，不得由旧程序覆盖。
- 图片只接受按内容识别的 PNG、JPEG、WebP；上限为 16 MiB、单边 16384、总计 5000 万像素。
- 图片必须完整解码并重新编码为受管副本，去除 EXIF、GPS 和不必要元数据；列表使用独立缩略图。
- 内容寻址的预览缓存可能被并发操作共享；失败回滚不得删除其他操作可能已复用的缓存。
- 不自动删除孤立文件、旧迁移目录或用户数据；先报告并提供可恢复处理。
- 当用户明确授权清理时，可移除已完成可达性核验的纯孤立主题资源：目录必须位于可信 DataRoot 的 `themes/<UUID>/` 下、不含 `theme.json`、不被主题索引（包括应用回收站记录）引用，且不得是符号链接或 Junction。清理必须使用已验证的 Windows 回收站机制；完整但未索引的主题目录只报告或提供单独恢复入口，不自动删除。
- 编辑器可直接永久删除当前编辑会话生成且已确认不再引用的受管背景资源，不发送到 Windows 回收站：仅限取消的新草稿、保存后被替换的旧背景和保存失败的新副本资源；删除前必须验证可信 DataRoot、精确主题 UUID、普通文件/目录、无重解析点、`theme.json` 当前引用以及目录为空条件。该例外不适用于共享预览/缩略图缓存、数据库索引主题、应用回收站主题、未知孤立目录或完整但未索引主题。

## 6. Codex 注入与持久化安全

- 自动发现仅查找当前用户注册的 Microsoft Store Codex；用户也可以明确选择一个 EXE 精确路径，不扫描任意目录或 Electron 进程。
- Store 身份、Publisher 和签名用于来源提示，不单独决定兼容性；非 Store 或身份不匹配目标必须按 EXE SHA-256 由用户确认一次，文件变化后重新确认。
- 校验 EXE 精确路径、进程路径、PID、创建时间、主进程命令行、回环端点、端口所有者、Browser ID、Page Target ID 和 `app://` URL。
- 排除 `--type=` 子进程、`initialRoute=/avatar-overlay` 和不完整辅助窗口；未知情况 fail-closed。
- 兼容性以能力探测和注入闭环为准；版本记录只作为已验证证据。目标不明确、必需能力不存在、Inspector 无法关闭或应用/验证/清理失败时必须阻断。
- 新 EXE 指纹通过首次“应用 → 清理 → 重新应用”闭环后才取得本机持久化资格；Codex 更新导致指纹变化时，Agent 暂停注入，等待新的临时闭环。
- Inspector 只短时开放，操作结束后必须确认端口 `9229` 无监听。
- 注入保持幂等，切换或 Restore 时清理旧 Style、Class、DOM、CSS 变量和 Blob URL。
- 不读取或记录对话正文、认证数据、Token、完整 URL、API Key、模型、MCP 或权限配置。
- 不修改 `WindowsApps`、`app.asar`、Codex EXE、Appx 或数字签名。
- 真实持久化 enable/disable 会修改当前用户启动项并启动或停止 Agent，执行前必须取得明确授权和验证恢复点；结束后核对 Run、Agent、配置和 9229。

## 7. 测试与结论边界

- 变更至少运行受影响项目测试；跨模块、构建或发布变更运行完整 `build.ps1`。
- 并发、原子写入、迁移、PID 复用和 Inspector 生命周期变更必须保留专门回归测试。
- UI 变更在环境允许时使用真实窗口或截图验证布局、交互、键盘、高 DPI 和状态变化。
- 真实 Codex 验证不得发送消息或保存私人对话内容。
- 构建成功不等于真实注入成功；进程存在不等于主题可见；配置正确不等于持久化实际执行。
- 失败或不可用的病毒扫描必须报告为未覆盖，不得声称通过。

## 8. 发布规则

- 发布目标为 Windows x64 self-contained 便携目录和 ZIP，用户无需预装 .NET 或 Node。
- 版本号更高的归档文件不自动构成当前维护基线或发布验收证据；维护基线以项目配置为准，已验收状态以对应验收记录为准。
- 发布包不得包含 PDB、测试夹具、截图、日志、数据库、主题包、私人素材、凭证或开发机绝对路径。
- 发布包根目录必须包含第一方 `LICENSE`，并包含 README、用户指南、Build Info、`THIRD-PARTY-NOTICES.md`、`LICENSES/` 下对应的第三方许可证、SHA256SUMS 和 release manifest。
- 仅重命名发布后的 Desktop apphost 为 `CodexThemeManager.exe`；不要改变内部程序集名。
- 未签名必须如实披露。允许用户在界面中主动检查并下载更新，且只可在 GitHub 三重摘要校验、危险 ZIP 检查、同盘事务替换、健康检查和可回滚条件全部满足后安装；不得后台静默安装。`workflow_dispatch` 可以上传私有 Actions 验证产物；与项目版本精确匹配、由维护者显式创建并推送的 tag 可以触发 Draft Release。不得自动签名、自动创建或推送 tag、自动发布 Release、自动公开仓库或绕过人工发布门禁。

## 9. 本地生成物、清理、Git 与文档

- `bin/`、`obj/`、`artifacts/work` 和已验证可再生成的缓存属于生成物，不进入 Git。
- GitHub 仓库及其 Git 历史是源码真相源；开发环境迁移应通过 clone/fetch 完成，不得用本机快照覆盖历史或丢弃现有修改与未跟踪文件。
- 本地发布 ZIP、校验文件、固定 Node 缓存和历史验收归档不是源码真相源，不进入公开仓库。根目录本机交接快照不得提交。
- 新环境先检查 `git status`、分支与 HEAD，核对固定工具链配置，再运行完整 `.\build.ps1 -Configuration Release`。
- 批量移除、覆盖、迁移、Git 丢弃或其他可能损失状态的操作必须先确认精确范围和恢复点，并使用已验证的可恢复机制。
- 不执行 `git reset --hard`、破坏性 `git clean`、永久删除、清空回收站或未经授权的提交、推送、PR、Release。
- README 面向用户；ADR 记录架构决策；风险登记表记录当前风险；历史任务文档保留证据边界。
- 不把个人绝对路径、全局 Codex 偏好或一次性测试结果写入可复用项目规则。

## 10. 交付要求

最终回复默认说明：结果、变更文件、已执行验证、未覆盖风险。文件使用可点击的绝对链接。所有验证结论必须与实际证据严格对应。
