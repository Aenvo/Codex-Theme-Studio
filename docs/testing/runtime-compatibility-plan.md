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
5. 所有路径结束后确认旧版 Inspector `9229` 无监听；统一宿主还需核对随机 Renderer 端口仅绑定 `127.0.0.1`、端口所有者与目标身份一致，并记录其在受管 ChatGPT 进程存续期间的监听状态。
6. 统一宿主需要受管启动或正常重启时，确认 GUI 提示保存未发送输入、用户确认与实际进程身份一致；暂态 Renderer 未就绪时确认保留同一可信进程重试，且无重复启动。

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

## 2026-09-30 统一宿主适配结果

- Store ChatGPT `26.924.1866.0` 的包内静态资源保留旧版 surface/composer 标记，并新增
  `data-app-shell-active-page`；兼容契约 v2 采用增量选择器，不删除旧版单页路径。新版回归
  覆盖 Codex 激活页应用、inactive Chat 页隔离、切页暂停/恢复和辅助窗口隔离。
- 旧固定 9229 路径的诊断继续有效：统一宿主不提供 Node Windows debug-handler，固定
  `--inspect` AUMID 激活没有进入 Electron 主进程，精确 WindowsApps EXE 启动被 MSIX
  拒绝。上述结果不再被解释为所有 Renderer 通道均已穷尽。
- 新通道只接受当前用户注册的官方 Store 包；Theme Studio 通过 AUMID 传入
  `--remote-debugging-address=127.0.0.1` 和随机高位端口，正式主题流程继续使用原 Profile。
  端口操作校验精确 EXE、PID、创建时间、主进程命令行、Browser ID、Page Target、
  `app://` 路由和 Windows 端口所有者；旧版本继续使用短时 9229 Inspector。
- GUI 在关闭现有 ChatGPT 前要求用户确认并提示保存未发送输入；关闭只针对精确身份，
  优先主窗口正常关闭，再使用 Windows Restart Manager 正常关机请求。持久化 Agent 只接管
  启动不超过 2 分钟的新实例，运行更久的无端口实例保持 fail-closed。
- `26.924.2738.0` 已完成随机端口 Canary、临时主题可见、持久主题切换、正常重启恢复和
  电脑重启后从菜单启动恢复。暂态 `port_renderer_unavailable`、
  `port_renderer_unqualified` 与 `renderer_port.open_timeout` 保留同一可信进程重试后，
  修复版真实冷启动一次成功；最终 Agent、Run 项、配置与运行时状态一致，9229 无监听。
- 随机 Renderer 端口在该受管 ChatGPT 进程存续期间保持监听，不等同于可短时关闭的旧
  9229 Node Inspector；其本机同用户调试面记录为 R-26 Open。Electron 精确版本、非 Store
  实例、慢启动/系统高负载扩展实机覆盖和本轮旧版真实实例复验仍为 `To be confirmed`。
- WindowsApps/`app.asar` 修改、包调试设置、原生 DLL/进程注入仍不属于兼容范围，也不得
  作为回退方案。

## 1.3.5 正式候选包复验边界

- 上述 2026-09-30 现场证据属于当时的统一宿主适配构建，不自动转为 `1.3.5` 正式候选包的现场验收。本地候选包已完成完整构建，并核对 Build Info、Agent 与安装清单、ZIP 摘要及包内文档链接；最终 tag 构建的提交身份、远程 CI 和 Draft Release 状态仍为 `To be confirmed`。
- `1.3.5` 候选包在统一宿主上的临时主题、持久主题、正常重启及电脑重启恢复须重新记录目标版本、精确 EXE 指纹、应用和清理结果、Agent 与 Run 项状态；在实际执行前均为 `To be confirmed`。不得发送消息或记录私人对话正文。
- R-26 的随机回环端口存续及本机同用户连接风险、R-27 的正常重启与未发送输入提示已在 README 和用户指南披露；候选包内文档与源码一致性已核对，候选包实机行为、慢启动、高负载与恢复中断的扩展覆盖为 `To be confirmed`。
