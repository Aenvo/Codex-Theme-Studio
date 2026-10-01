<p align="center">
  <img src="docs/assets/readme/app-icon.png" alt="Codex Theme Studio 应用图标" width="96">
</p>

<h1 align="center">Codex Theme Studio — Windows Codex 可视化主题编辑器</h1>

<p align="center">
  面向 Windows 10/11 的本地优先 Codex Desktop 主题管理器：可视化创建、预览、导入、切换和持久化自定义主题。
</p>

<p align="center">
  <a href="README.md">简体中文</a> · <a href="README.en.md">English</a> ·
  <a href="https://github.com/Aenvo/Codex-Theme-Studio/releases/latest">下载最新版</a> ·
  <a href="docs/user-guide.md">用户指南</a>
</p>

<p align="center">
  <a href="https://github.com/Aenvo/Codex-Theme-Studio/releases/latest"><img alt="GitHub Release" src="https://img.shields.io/github/v/release/Aenvo/Codex-Theme-Studio?display_name=tag&sort=semver"></a>
  <img alt="Windows 10/11 x64" src="https://img.shields.io/badge/Windows-10%20%7C%2011%20x64-0078D4?logo=windows">
  <a href="LICENSE"><img alt="Apache License 2.0" src="https://img.shields.io/github/license/Aenvo/Codex-Theme-Studio"></a>
</p>

> [!IMPORTANT]
> Codex Theme Studio 是独立的开源项目，不是 OpenAI 官方产品，也不代表 OpenAI 或任何截图素材权利人。它不会修改 Codex 安装文件。

![Codex Theme Studio 主题资料库，展示主题卡片、搜索筛选和主题操作](docs/assets/readme/theme-library.jpg)

## 实机效果

从主题资料库选择背景和配色，在编辑器中调整构图、透明度、遮罩与模糊，然后临时应用到当前 Codex，或在明确确认后启用当前用户级持久化。

<table>
  <tr>
    <td width="50%" align="center">
      <img src="docs/assets/readme/codex-theme-pink.jpg" alt="Codex Desktop 应用粉色主题后的实机效果"><br>
      <sub>示例主题一 · Codex 首页</sub>
    </td>
    <td width="50%" align="center">
      <img src="docs/assets/readme/codex-theme-dark.jpg" alt="Codex Desktop 应用深色主题后的实机效果"><br>
      <sub>示例主题二 · Codex 首页</sub>
    </td>
  </tr>
</table>

![Codex Theme Studio 主题编辑器，左侧调整主题参数，右侧预览 Codex 首页](docs/assets/readme/theme-editor.jpg)

> [!NOTE]
> 截图中的背景由用户提供，仅用于演示自定义主题效果。相关角色、作品、商标和图片版权归各自权利人所有；本仓库与发布包不分发这些壁纸，也不暗示任何授权、合作或背书。请只使用你有权使用的本地图片。

## 核心价值

- **所见即所得**：在独立编辑器中预览首页与任务页，调整背景焦点、安全区、颜色、透明度、遮罩和模糊。
- **本地优先**：主题、图片和索引保存在本机；不会上传背景图片、主题数据、日志或 Codex 页面。
- **可逆且克制**：支持临时应用、一键还原官方外观，以及用户明确启用的当前用户级持久化。
- **便携即用**：Windows x64 self-contained ZIP，无需预装 .NET、Node.js、npm 或 npx，也不要求管理员权限。

## 下载与启动

1. 打开 [GitHub Releases](https://github.com/Aenvo/Codex-Theme-Studio/releases/latest)。
2. 下载 `Codex-Theme-Studio-<version>-win-x64-portable.zip` 和同一 Release 中的 `SHA256SUMS.txt`。
3. 对照 `SHA256SUMS.txt` 校验 ZIP，然后完整解压到普通可写目录，例如 `%USERPROFILE%\Apps\Codex Theme Studio`。
4. 双击 `CodexThemeManager.exe`。

当前源码维护版本为 `1.3.6`，正在进行发布验证；公开稳定版以 [GitHub 最新版](https://github.com/Aenvo/Codex-Theme-Studio/releases/latest)为准。已安装 `1.3.0` 或更高稳定版的用户可以在应用内主动检查更新；本轮发布及更新验收状态见 [1.3.6 验收记录](./docs/testing/1.3.6-release-acceptance.md)。真实程序内升级尚未完成隔离环境验收。

不要直接在压缩包中运行，也不要下载 GitHub 自动附带的 `Source code (zip)` / `Source code (tar.gz)` 作为应用程序。普通用户需要 Release Assets 中名称包含 `win-x64-portable.zip` 的便携包。

便携发布包未进行代码签名，Windows 可能显示未知发布者警告。哈希不一致时不要运行，也不要通过关闭安全软件或盲目加入白名单绕过警告。

## 使用流程

1. **创建或导入**：新建主题，或导入不含可执行代码的 `.cttheme` 主题包。
2. **编辑与预览**：选择 PNG、JPEG 或 WebP 背景，调整构图与六个主题颜色；颜色支持 `#RRGGBB` 和 CSS 顺序的 `#RRGGBBAA`。
3. **临时应用**：选择主题并点击“临时应用”。旧版 Codex 需先启动；统一 ChatGPT/Codex 宿主在需要建立主题通道时，会先请求确认启动或正常重启 ChatGPT。首次遇到新的 EXE 指纹时，应用会完成“应用 → 清理 → 重新应用”闭环验证。
4. **按需持久化**：闭环成功后才开放持久化。持久化会创建当前用户启动项并安装低频 Agent，必须由用户明确启用。
5. **随时还原**：使用“还原外观”停止临时主题；检测到已识别的持久化时，经确认后同时停用启动项和 Agent。

完整操作、数据位置、更新、迁移、彻底清理和故障诊断见[用户指南](./docs/user-guide.md)。

## 功能概览

| 能力 | 当前行为 |
| --- | --- |
| 主题资料库 | 创建、复制、重命名、收藏、搜索、排序、卡片/列表切换和应用回收站 |
| 主题编辑 | 调整背景焦点、安全区、图片适配、首页/任务页透明度、遮罩、模糊与六个主题颜色 |
| 导入与导出 | 校验并处理声明式 `.cttheme` 包，不接受 JavaScript、命令、HTML、远端 CSS 或可执行文件 |
| Codex 连接 | 自动检测当前用户安装的 Microsoft Store Codex，也可手选一个明确的 EXE 并按 SHA-256 确认来源 |
| 临时与持久化 | 临时主题只影响当前 Codex 实例；持久化需要验证闭环和用户明确确认 |
| 外部主题 | 只读发现本机 OkkSkin 当前 Doro 主题，可复制为普通本地主题后编辑 |
| 数据迁移 | 将 DataRoot 迁移到新的空目录，校验文件和 SQLite 完整性，并保留原目录作为恢复点 |
| 应用更新 | `1.3.0` 起支持用户主动触发、三重校验、失败回滚的稳定版自更新，不执行后台静默安装 |

## 安全与兼容边界

- 旧版 Codex 使用短时本机回环 Inspector，操作结束后确认端口 `9229` 无监听。统一 ChatGPT/Codex 宿主使用官方 Store 包的受管启动和随机本机回环 Renderer 端口；如需正常重启，界面会先提示保存未发送输入并请求确认。该随机端口在受管 ChatGPT 进程存续期间保持监听，本机同用户进程可能连接，详见[风险登记 R-26](./docs/risks/risk-register.md)。项目不修改 `WindowsApps`、`app.asar`、EXE、Appx 或数字签名。
- 统一宿主只对当前激活的 Codex 页面应用主题；Chat/Work 页面和辅助窗口保持隔离。慢启动、高负载与恢复中断的扩展实机覆盖仍为 `To be confirmed`。
- 不读取或记录对话正文、认证信息、API Key、模型、MCP 或权限配置。应用就绪后会访问公开 GitHub Release API 检查稳定更新，但不会上传主题、图片或诊断材料。
- 图片只接受按内容识别的 PNG、JPEG 和 WebP，完整解码后重新编码为受管副本，并移除不必要元数据。
- 旧版 Store Codex 的短时 Inspector 路径和 Store ChatGPT/Codex 统一宿主 `26.924.2738.0` 的随机端口路径已有本机实机证据；本轮源码样式已在 `26.928.2636.0` 上进行可见效果核对。版本与验证范围见[兼容性记录](./docs/compatibility/codex-versions.json)及[测试方案](./docs/testing/runtime-compatibility-plan.md)。这些证据不能替代 1.3.6 最终发布实包的应用、清理、重新应用及持久化现场闭环，也不是运行白名单；未知版本仍需完整能力探测。
- 1.3.6 尚未在无预装 .NET/Node 的干净 Windows VM 或有效 Defender 环境完成验收；最终实包的统一宿主现场闭环、真实 updater 进程终止与断电边界也未覆盖。首次使用或更新前请保留可恢复副本。

安全问题请勿通过公开 Issue 披露；报告方式和支持范围见[安全政策](./SECURITY.md)。

## 开发与文档

开发工具链以 .NET SDK `8.0.423` 为基准（允许 `global.json` 指定的补丁前滚），Node.js 固定为 `24.18.0`。在仓库根目录运行：

```powershell
.\build.ps1
.\package.ps1
```

`build.ps1` 执行 locked restore、构建、测试、格式检查与 self-test；`package.ps1` 在完整 Release 验证后生成 Windows x64 self-contained 便携目录、ZIP、哈希与发布清单。

- [用户指南](./docs/user-guide.md)：完整操作、数据位置、清理与故障诊断。
- [构建文档](./docs/building.md)：开发环境、验证命令和源码启动。
- [发布流程](./docs/releasing.md)：版本、tag、Draft Release 与人工发布门禁。
- [设计系统](./design.md)：UI token、组件与交互规范。
- [安全政策](./SECURITY.md)：支持版本和漏洞报告方式。

## 社区

项目曾在 [LINUX DO](https://linux.do/) 社区分享。欢迎通过 GitHub Issues 提交可公开的问题、兼容性反馈和功能建议，也欢迎为 Windows Codex 主题体验贡献改进。

## 许可证

除另有说明外，本仓库的第一方源码依据 [Apache License 2.0](./LICENSE) 开放源代码。第三方组件和素材继续适用各自许可证与归属声明，详见 [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md)。Apache License 2.0 不授予 OpenAI、Codex 或其他第三方商标的使用权。
