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
- `CodexThemeStudio.Application.MacOS` 是 macOS 产品组合根；未来 Avalonia UI
  与无 UI 验收入口均通过它创建 Adapter、Runtime 和进程内资格存储。
- 第 7B.2B 使用独立 Acceptance Harness 驱动 Application 组合根；Harness
  不直接引用 Helper、Node 或 CDP。RuntimeHost 继续只负责产物身份自检，
  不承载产品业务命令。
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
- 7B.2A 使用仓库外、内容寻址、原子 staging 建立正式单向身份链。固定 Node
  的版本、架构、官方归档哈希以及解包后二进制哈希由独立 macOS 基线固定；
  manifest 只声明 Node、CDP 和 renderer 文件，不声明自身或 Helper。
- Manifest SHA-256 只进入仓库外 Swift Package 副本的生成源码；Helper
  SHA-256 只进入仓库外 .NET 生成源码。源码 checkout 中的两个默认常量继续
  为空并 fail-closed，最终哈希、manifest 实例和二进制不得提交。
- 7B.2A 的无 apphost .NET Host 只用于离线复核：.NET 编译身份验证 Helper，
  Helper 编译身份验证 manifest，manifest 再验证 Node 和两份脚本。只有整条
  staging 链成立时，严格 self-test 才报告
  `packagedRuntimeIdentityConfigured=true`。
- 7B.2A staging 尚未签名、公证，也不构成发布 App Bundle。使用该固定产物
  执行正式真机闭环是 7B.2B；nested signing、Hardened Runtime 和 Gatekeeper
  验收仍属于第 8 阶段。
- 7B.2B 的验收请求只通过 stdin 接受短时明确授权和六色声明式主题。资格仅在
  单次 Harness 进程内存中存在；首次资格、Restore、第二次临时 Apply 和最终
  Restore 必须全部通过 Application 和 Coordinator。调用方取消不得中断一次
  必要的独立最终 Cleanup。
- assembly receipt schema v2 记录受管产品载荷的规范化清单和组合哈希。assembly
  ID 同时绑定 manifest、Helper 及全部 staged DLL/deps/runtimeconfig；receipt
  只是证据，编译进 Helper 和 .NET 的单向哈希仍是信任根。
- 发布目标为 Developer ID、Hardened Runtime、公证和 stapled ZIP。
- 首版不以 App Sandbox 或 Mac App Store 为目标，不绕过 Gatekeeper、SIP 或
  其他系统安全机制。

## 后果

- Windows 和 macOS UI 将独立维护，但共享 ThemeCore、Contracts 和 Storage。
- macOS 增加 Swift、Node、.NET 三层协议及其供应链和签名成本。
- Swift Helper 成为安全边界；其替换、版本漂移或结构化清理失败必须 fail-closed。
- 验收 Harness 只证明正式无 UI 组合链；它不是最终 UI 的长期命令行 API，也不
  建立跨应用或跨 Codex 重启的持久资格。
- Intel、Universal Binary、背景图片、Agent、登录项、签名、公证和多版本
  Codex 兼容矩阵仍需独立阶段验证。
