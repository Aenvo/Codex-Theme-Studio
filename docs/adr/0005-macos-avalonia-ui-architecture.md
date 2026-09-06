# ADR 0005：macOS Avalonia UI 架构

- 状态：Accepted
- 日期：2026-07-29
- 决策范围：第 7C macOS UI 产品化

## 背景

第 7B 已通过正式 `Application.MacOS → CodexRuntimeCoordinator →
CodexAdapter.MacOS → Swift Helper → 固定 Node Runtime` 无 UI 真机闭环。
Windows 1.2.0 继续由 WPF 独立维护。macOS 需要使用相同产品身份和主题资料库
语义，同时遵循 macOS 的窗口、菜单、键盘、滚动和辅助功能惯例。

## 决策

- macOS UI 使用 Avalonia 12.1.0、.NET 10、`osx-arm64`，首版支持 macOS 14+。
- 新项目为 `CodexThemeStudio.Desktop.MacOS`；不复用 WPF XAML，不修改 Windows
  Desktop，不创建共享 Presentation 层。
- Desktop 只直接引用 `Application.MacOS`、Contracts 和 Avalonia。它不得直接
  引用 Runtime、Adapter、Swift、Node、CDP 或 staging 路径。
- ViewModel 使用纯 .NET 状态，不持有 Avalonia Window、Control、StorageProvider
  或 macOS 原生对象。第 7C.1 使用 fake `IMacThemeStudioClient`，不连接 Storage
  或正式 Runtime。
- 主流程固定为主题列表、选择、本地预览、临时应用和恢复。完整编辑器、导入
  导出、删除、Agent、持久化和发布工程延期。
- 产品身份直接复用 Windows 的第一方 app icon；Sidebar 保留工作台、收藏、
  回收站、设置和当前选择的产品结构，通用图标使用与 WPF 同源的 Lucide
  Geometry。第 7C.1 的回收站只是明确标记的 fixture 空态，不宣称已具备删除、
  还原或清空能力。
- 资料库页面采用与 Windows 一致的全宽卡片区、顶部工具栏和底部所选主题操作栏。
  macOS 仍保留系统标题栏、NativeMenu 和 `⌘` 快捷键，不逐像素复制 Windows
  系统交互。
- UI 只展示纯调色板能力。背景图可以本地预览，但卡片不增加 Windows 中不存在
  的 Badge；所选主题操作区必须显示“macOS 当前不应用背景”的 Runtime 能力摘要，
  并且背景不得发送给 Runtime。
- 用户取消只在正式状态操作开始前生效。状态操作开始后，UI 可以停止等待或隐藏
  窗口，但 Application 必须继续完成 Cleanup 和 Inspector finally；UI 不得终止
  Helper 或 Node。
- `inspectorClosedProofCount=4` 表示四个产品操作取得 Inspector 关闭证明，不是
  内部 Inspector 会话总数。普通 UI 不显示该数值；诊断 UI 只能将其描述为产品
  操作关闭证明。
- App 使用原生 macOS 菜单和系统标题栏；产品内容保持 `design.md` 的固定深色
  视觉。Settings 使用 `⌘,` 独立单例窗口，About 位于应用菜单。

## 后果

- Windows 和 macOS UI 独立演进，但共享 Contracts、ThemeCore、Storage 和产品
  运行边界。
- Avalonia Native、Skia、Retina、中文输入法、VoiceOver、签名及 entitlement
  需要后续真实 App Bundle 验证。
- 第 7C.1 的 fake UI 不构成真实 Apply/Restore 证据。真实 Runtime 接入必须使用
  独立授权并继续满足第 7B 的身份、隐私和 Inspector 关闭门禁。
