# Codex Theme Studio

Codex Theme Studio 是面向 Windows 10/11 x64 的本地 Codex 桌面端主题管理器。它用于创建、导入、预览、临时应用和持久化本地主题；不是 OpenAI 官方产品，也不会修改 Codex 安装文件。

## 下载与启动

1. 获取 `CodexThemeManager-<version>-win-x64-portable.zip`。
2. 对照同目录的 `SHA256SUMS.txt` 校验 ZIP。
3. 将 ZIP 完整解压到普通可写目录，例如 `D:\Apps\Codex Theme Studio`。不要直接在压缩包中运行。
4. 双击 `CodexThemeManager.exe`。

应用为 Windows x64 self-contained 便携包，不要求预装 .NET、Node.js、npm 或 npx，不要求管理员权限。首个版本尚未进行代码签名，因此 Windows 可能显示未知发布者警告；只应使用哈希与发布记录匹配的包。

首次启动会使用 `%LOCALAPPDATA%\CodexThemeStudio\Data` 作为明确的默认数据目录，不会自动启用持久化，也不会自动联网。程序目录可以移动，但用户数据和已安装的持久化 Agent 不存放在程序目录内。

## 主要能力

- 创建、复制、重命名、收藏、搜索、排序和删除本地主题。
- 导入与导出不含可执行代码的 `.cttheme` 主题包。
- 选择 PNG、JPEG 或 WebP 背景并调整颜色、焦点、安全区、遮罩、透明度和模糊。
- 临时显示主题，或由用户明确启用当前用户级持久化。
- 在主题间切换，单独还原 Codex 外观，或停用持久化。
- 将 DataRoot 安全迁移到新的空目录，并保留原目录作为恢复点。

完整操作、数据位置、彻底清理和故障诊断见[用户指南](./docs/user-guide.md)。

## 支持边界

- 只支持当前用户安装、身份与签名校验通过的 Microsoft Store Codex。
- 当前经过真实验证的 Codex 版本为 `26.715.4045.0`；未知版本默认拒绝注入，Restore 仍可使用。
- 主题通过本机回环 Inspector 短时应用。操作结束后会关闭 Inspector，但 Codex 更新仍可能改变兼容性。
- 第一版是解压即用的便携目录，不是单文件 EXE，不包含安装器和自动更新。
- 该版本未签名，也未通过公开下载渠道发布。

## 隐私与安全

主题编辑、切换和持久化不依赖外部服务器。应用不会上传背景图片、主题数据、日志或 Codex 页面，不读取对话正文、认证信息、API Key、模型、MCP 或权限配置。它不修改 `WindowsApps`、`app.asar`、Codex EXE、Appx 或数字签名。

主题包只接受声明式白名单字段，不能携带 JavaScript、命令、可执行文件、HTML 或远端 CSS。图片按内容识别，完整解码后重新编码，并移除不必要元数据。

## 开发与构建

开发环境固定使用 .NET SDK `8.0.423` 与 Node.js `24.18.0`。在仓库根目录运行：

```powershell
.\build.ps1
.\package.ps1 -Version 1.0.0
```

第二条命令会执行 Release 验证、下载并校验固定 Node Runtime、生成 Windows x64 self-contained 目录、ZIP、哈希和发布清单。完整复现与验收步骤见[构建文档](./docs/building.md)和[发布流程](./docs/releasing.md)。

## 第三方组件

依赖版本、许可证与归属见 [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md)。便携包附带对应 `LICENSES/` 文本。

本仓库当前没有声明面向源代码的开放源代码许可证；第三方许可证只覆盖各自组件。
