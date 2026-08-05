# Codex Theme Studio

Codex Theme Studio 是面向 Windows 10/11 x64 的本地 Codex 桌面端主题管理器。它用于创建、导入、预览、临时应用和持久化本地主题；不是 OpenAI 官方产品，也不会修改 Codex 安装文件。

## 下载与启动

1. 打开 [GitHub Releases](https://github.com/Aenvo/Codex-Theme-Studio/releases/latest)。
2. 下载 `Codex-Theme-Studio-<version>-win-x64-portable.zip`。
3. 对照同一 Release 中的 `SHA256SUMS.txt` 校验 ZIP。
4. 将 ZIP 完整解压到普通可写目录，例如
   `%USERPROFILE%\Apps\Codex Theme Studio`。不要直接在压缩包中运行。
5. 双击 `CodexThemeManager.exe`。

GitHub Releases 页面自动附带的 `Source code (zip)` / `Source code (tar.gz)`
只是源码，不能直接运行。普通用户应下载 Release Assets 中名称包含
`win-x64-portable.zip` 的便携包及其 `SHA256SUMS.txt`。

应用为 Windows x64 self-contained 便携包，不要求预装 .NET、Node.js、npm 或 npx，不要求管理员权限。当前 `1.2.0` 未进行代码签名，因此 Windows 可能显示未知发布者警告；只应使用哈希与发布记录匹配的包。

首次启动会使用 `%LOCALAPPDATA%\CodexThemeStudio\Data` 作为明确的默认数据目录，不会自动启用持久化，也不会自动联网。程序目录可以移动，但用户数据和已安装的持久化 Agent 不存放在程序目录内。

发布包不附带第三方壁纸或维护者本机的主题数据。用户可以导入自己有权使用的
本地图片创建主题。

## 主要能力

- 创建、复制、重命名、收藏、搜索、排序和删除本地主题。
- 侧栏以“工作台”统一进入主题资料库；内容区可筛选“全部主题/当前主题”。默认使用独立卡片网格，也可通过右上角的图标分段切换器改为紧凑列表；两种展示都保留搜索、筛选、排序、选择和虚拟化滚动。
- 导入与导出不含可执行代码的 `.cttheme` 主题包。
- 选择 PNG、JPEG 或 WebP 背景并调整颜色、焦点、安全区、遮罩、透明度和模糊。
- 六个主题颜色支持 `#RRGGBB` 与 CSS 顺序的 `#RRGGBBAA`，可在悬浮色板中调整色相、饱和度、明度和 Alpha。
- 自动发现本机 OkkSkin 当前的 Doro 主题，以只读的“外部持久化”卡片展示；可以复制为普通本地主题后编辑。普通浏览、复制和主题切换不会修改 OkkSkin 配置。
- 临时显示主题，或由用户明确启用当前用户级持久化。
- 使用统一的“还原外观”停止当前临时主题；检测到 Theme Studio 或 OkkSkin 持久化时，经确认后同时停用对应启动项和 Agent，使后续 Codex 实例继续保持官方外观。
- 将 DataRoot 安全迁移到新的空目录，并保留原目录作为恢复点。

完整操作、数据位置、彻底清理和故障诊断见[用户指南](./docs/user-guide.md)。

## 支持边界

- 自动检测当前用户安装的 Microsoft Store Codex；也可以在“设置 → Codex 连接”中手动选择一个明确的 EXE。手选来源按文件 SHA-256 提示并确认，不代表项目为第三方构建背书。
- 当前经过真实验证的 Codex 版本为 `26.715.4045.0`、`26.715.10079.0`、`26.721.3404.0`、`26.727.6591.0` 和 `26.730.8199.0`。这些记录是兼容证据，不是运行白名单；未知版本在完整能力探测通过后可以临时使用。
- 新 EXE 指纹首次临时显示会自动执行“应用 → 清理 → 重新应用”闭环；闭环成功后才开放持久化。项目不扫描任意目录或模糊匹配其他 Electron 进程。
- 主题通过本机回环 Inspector 短时应用。操作结束后会关闭 Inspector，但 Codex 更新仍可能改变兼容性。
- 当前发布形态是解压即用的便携目录，不是单文件 EXE，不包含安装器和自动更新。
- 当前 `1.2.0` 未签名；Windows 可能显示未知发布者警告。

## 隐私与安全

主题编辑、切换和持久化不依赖外部服务器。应用不会上传背景图片、主题数据、日志或 Codex 页面，不读取对话正文、认证信息、API Key、模型、MCP 或权限配置。它不修改 `WindowsApps`、`app.asar`、Codex EXE、Appx 或数字签名。

主题包只接受声明式白名单字段，不能携带 JavaScript、命令、可执行文件、HTML 或远端 CSS。图片按内容识别，完整解码后重新编码，并移除不必要元数据。

安全问题请勿通过公开 Issue 披露；报告方式和支持范围见
[安全政策](./SECURITY.md)。

## 开发与构建

开发环境固定使用 .NET SDK `8.0.423` 与 Node.js `24.18.0`。在仓库根目录运行：

```powershell
.\build.ps1
.\package.ps1
```

第二条命令默认从 `Directory.Build.props` 读取版本，并执行 Release 验证、下载及校验固定 Node Runtime、生成 Windows x64 self-contained 目录、ZIP、哈希和发布清单。完整构建见[构建文档](./docs/building.md)，Windows PowerShell 5.1、原子写入和 Codex 更新闭环见[兼容性测试方案](./docs/testing/runtime-compatibility-plan.md)，发布步骤见[发布流程](./docs/releasing.md)；版本验收记录保留在源码仓库中，不嵌入被验收的 ZIP。

1.2.0 起，打包阶段会通过版本化 manifest 去除 Desktop 与 Agent 间内容完全
相同的 .NET Runtime 文件。启用持久化时，应用会校验清单并在当前用户目录重建
完整的稳定 Agent，因此程序目录移动或删除后，已安装 Agent 的行为保持不变。

本地源码启动、完整验证命令和环境迁移说明见[构建文档](./docs/building.md)。
UI 贡献者应同时遵循 [design.md](./design.md) 中的设计 token、图标与交互规范。

## 相关文档

- [用户指南](./docs/user-guide.md)：完整操作、数据位置、清理与故障诊断。
- [构建文档](./docs/building.md)：开发环境、测试命令和源码启动。
- [发布流程](./docs/releasing.md)：版本、tag、Draft Release 与人工发布门禁。
- [安全政策](./SECURITY.md)：支持版本和漏洞报告方式。

## 社区

本项目已在 [LINUX DO](https://linux.do/) 社区分享，欢迎交流与反馈。

## 第三方组件

界面图标基于 Lucide 集合并转换为随程序内置的原生 WPF Geometry；应用构建和运行不从在线图标服务加载资源。依赖版本、许可证与归属见 [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md)，便携包附带对应 `LICENSES/` 文本。

## 许可证

除另有说明外，本仓库的第一方源码依据 [Apache License 2.0](./LICENSE) 开放源代码。第三方组件和素材继续适用各自许可证与归属声明，详见 [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md)。Apache License 2.0 不授予 OpenAI、Codex 或其他第三方商标的使用权，本项目仍是非 OpenAI 官方产品。
