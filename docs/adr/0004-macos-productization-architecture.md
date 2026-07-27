# ADR 0004：macOS 产品化架构

- 状态：Accepted
- 日期：2026-07-27
- 决策范围：macOS Apple Silicon 产品化

## 背景

Windows 1.2.0 继续以 .NET 8、WPF、SQLite、固定 Node.js Runtime 和
Windows x64 self-contained 便携包为正式维护基线。macOS 可行性分支已经取得
三份独立证据：

- `520a3b49354a927e322b0d55581a26eae1333b34`：可信 Bundle、签名、进程和端口发现。
- `fd71c0e3eb8487957151238fe8ceb9b0fc543e58`：短时 Inspector 生命周期。
- `2f34905e71115cad37af9008f31b28bbd25961e7`：CSS Apply、Cleanup、Reapply
  及手动重启后的重新资格验证。

这些提交只证明当前 Apple Silicon、macOS 和 Codex 版本上的技术可行性。
正式产品不得依赖 `spikes/`，也不得把 spike 的一次性路径、checkpoint 或测试
证据作为生产状态。

## 决策

### 产品与运行时

- macOS 首版目标为 macOS 14+、Apple Silicon、`osx-arm64` self-contained。
- macOS .NET 主程序使用 .NET 10；Windows 继续使用 .NET 8 和 WPF。
- macOS UI 在后续阶段使用独立 Avalonia 前端，不复用 WPF XAML。
- 首版只应用声明式纯 CSS 调色板。主题背景图片保留在主题数据中但不发送到
  macOS Runtime；背景图片渲染继续为 `To be confirmed`。

### 模块

- `CodexThemeStudio.CodexRuntime` 保存平台中立的资格、Apply、Cleanup 和状态机。
- `CodexThemeStudio.CodexAdapter.MacOS` 只负责启动并验证 one-shot Swift Helper。
- Swift Helper 独占 Bundle/签名/进程/端口/SIGUSR1/Inspector 生命周期，并直接
  启动固定 Node Runtime。
- Node 只负责受限 HTTP、WebSocket、CDP 和固定 renderer 表达式。
- macOS Desktop 不拼接 shell、Swift 或 Node 命令。
- Windows Desktop、Agent 和 CodexAdapter 在 macOS 产品化初期保持不变。

### 协议与隐私

- .NET、Swift 和 Node 之间每次请求各使用单一 stdin JSON 和单一 stdout JSON。
- 主题请求只允许 ID、Schema、Variant 和六个调色板字段；不接受任意 CSS、
  JavaScript、HTML、远端 URL、图片或生命周期 Hook。
- Target ID、Browser ID、WebSocket URL、完整页面 URL、Blob URL 和原始命令
  输出不得越过 Swift/Node 边界。
- 不读取或记录 DOM/页面正文、对话、认证信息、Token、argv、环境变量、进程
  内存、模型或权限配置。
- Helper 一旦发送信号，取消和超时路径仍必须优先执行 Cleanup、`_debugEnd()`
  和 9229 最终关闭验证。

### 发布边界

- 最终 App Bundle 嵌入并逐层签名 .NET apphost、Swift Helper、Node 及原生库。
- Runtime 身份采用分阶段、单向构建信任链，不接受由待验证文件目录自行提供
  预期值：
  1. 固定 Node、CDP 脚本和 renderer 脚本先生成版本化
     `runtime-manifest.json`；
  2. manifest 的 SHA-256 生成到 Swift Helper 的编译期常量；
  3. Helper 编译完成后，其 SHA-256 再生成到 .NET Adapter 的编译期常量；
  4. 最后从内到外签名所有 nested code 和 App Bundle。
- 源码 checkout 中两个生成常量故意为空，因此正式 Adapter 和 Helper 在打包
  信任链未生成前 fail-closed。当前 7B.1 只验证 manifest 解析、逐文件哈希、
  manifest 自身哈希和未配置拒绝路径，不生成正式打包 manifest，也不具备
  打包身份资格。
- 生成 runtime manifest、写入两级编译期预期值、验证最终 App Bundle 签名并
  用该产物执行真机闭环，是 7B.2 开始前的独立前置门禁。
- 发布目标为 Developer ID、Hardened Runtime、公证和 stapled ZIP。
- 首版不以 App Sandbox 或 Mac App Store 为目标，不绕过 Gatekeeper、SIP 或
  其他系统安全机制。

## 后果

- Windows 和 macOS UI 将独立维护，但共享 ThemeCore、Contracts 和 Storage。
- macOS 增加 Swift、Node、.NET 三层协议及其供应链和签名成本。
- Swift Helper 成为安全边界；其替换、版本漂移或结构化清理失败必须 fail-closed。
- Intel、Universal Binary、背景图片、Agent、登录项、签名、公证和多版本
  Codex 兼容矩阵仍需独立阶段验证。
