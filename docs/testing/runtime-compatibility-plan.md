# Windows 主机与 Codex 更新兼容性测试方案

## 目标

本方案用于发现“开发环境可以运行，但 Windows 用户机器或 Codex 更新后失败”的兼容问题。判断以目标身份、能力探测和注入闭环为准，不以 Codex 版本号、Store 身份或签名单独阻断。

## 测试层级

### 1. Windows 主机静态兼容

- 使用 Windows PowerShell 5.1 AST 解析仓库中的 `.ps1` 文件。
- 审查由 `powershell.exe` 进入的运行时路径，不使用 PowerShell 7 或 .NET Core 专属 API。
- 检查运行时子进程的标准输出和错误输出，确保跨进程协议只产生一个结构化 JSON 文档。
- 区分开发脚本与便携包运行路径；`build.ps1`、`package.ps1` 的问题不得被误报为最终用户运行依赖。

### 2. 文件与资源生命周期

- 检查 `FileStream`、ZIP、临时文件和 Inspector 租约的作用域。
- 原子写入必须在释放临时文件句柄后执行 `File.Move` 或 `File.Replace`。
- 取消、异常和并发失败路径必须清理本次临时资源，但不得删除其他操作共享的文件。
- 资格记录、目标选择、当前会话、持久化指针和 DataRoot 写入保留专门回归测试。

### 3. 自动化回归

- `WindowsDiscoveryCompatibilityTests` 真实启动系统 Windows PowerShell 5.1，验证精确 EXE 的 `Discover` 和无可用映像路径进程的 `Snapshot` 均返回可解析 JSON。
- `CodexCompatibilityQualificationStoreTests` 验证资格记录原子写入后可由新实例读取。
- 完整 `build.ps1 -Configuration Release` 必须通过 locked restore、构建、全部 .NET 测试、格式检查、Agent/Injector self-test 和 Node Injector 测试。

### 4. 真实 Codex 更新验收

1. 记录 Codex 包版本、Electron 版本、目标来源和 EXE SHA-256 摘要。
2. 执行自动发现或精确 EXE 路径发现，确认 PID、启动时间、路径和主进程命令行一致。
3. 运行能力探测，确认主窗口、Renderer 协议、Canary 添加与清理均通过。
4. 新指纹执行“应用 → 清理 → 重新应用”闭环，确认最终主题仍可见且本机持久化资格已保存。
5. 所有路径结束后确认 Inspector `9229` 无监听。

真实验收不得发送消息、记录对话或凭证，也不得自动启用/停用持久化。非 Store 真实实例仍为 `To be confirmed`。

## 2026-07-24 审计结果

- 修复 Windows PowerShell 5.1 不支持 `Path.IsPathFullyQualified` 导致精确 EXE 发现返回非结构化异常的问题。
- 修复兼容资格临时文件在句柄释放前移动导致 `compatibility.qualification.write_failed` 的问题。
- 增加无 `ExecutablePath` 进程的 fail-closed JSON 返回，避免 PID 复用或系统进程边界破坏协议。
- 静态检查其余原子文件写入点，均在 `Move/Replace` 前结束写入流作用域；未发现第二处同类句柄占用缺陷。
- Store Codex `26.721.3404.0`、Electron `150.0.7871.128` 已完成能力探测及首次应用闭环，最终 `9229` 无监听；未执行持久化 enable/disable。

## 2026-08-05 审计结果

- Store Codex `26.727.6591.0`、Electron `150.0.7871.182` 已完成能力探测、主窗口与 `avatar-overlay` 隔离，以及“应用 → 清理 → 重新应用”闭环。
- 修复验证主题 fixture 缺少 `panelBlur`、`cropScale` 导致 `art_fields_invalid`；Node 测试现在直接加载该 fixture，防止白名单再次漂移。
- Inspector 端口开始监听后可能短暂拒绝 HTTP 元数据连接；暂态连接在固定总门限内重试，协议、身份或端口所有者错误仍立即 fail-closed。
- Inspector 关闭后等待端口稳定收敛，并放宽关闭确认门限以适配新版宿主时序；最终清理为 `active=false`、`hookCount=0`，端口 `9229` 无监听。
- 未执行持久化 enable/disable、Codex 完整重启恢复或带私人内容的截图保存。
