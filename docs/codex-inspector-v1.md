# Codex 发现与受控调试通道 v1

## 范围

本通道负责目标发现、主进程身份校验、旧版短时 Inspector 通信、统一宿主的
受管随机回环 Renderer 通信和能力探针。
自动发现仍只查找当前用户注册的 Microsoft Store Codex；用户也可以从设置页
选择一个 EXE 精确路径。它不扫描任意目录或模糊匹配其他 Electron 进程，
不读取页面文本，也不修改 Codex 配置或安装文件。

## 安装与进程身份

自动发现每次动态调用 Windows 包与进程 API，不缓存版本目录，并记录：

- 包名为 `OpenAI.Codex`；
- PackageFamilyName 为 `OpenAI.Codex_2p2nqsd0c76g0`；
- PublisherId 为 `2p2nqsd0c76g0`；
- SignatureKind 与 PackageStatus；
- `ChatGPT.exe` 位于当前注册包 InstallLocation 内。

手选目标记录 EXE 绝对路径和 SHA-256；非 Store、Publisher 或签名不匹配只产生
按文件指纹确认的来源警告，不单独阻断。主进程必须与目标 EXE 路径精确相同，
且命令行不包含任何 `--type=` 子进程参数。多个合格主进程视为目标不明确并阻断。
每个操作保存 PID、UTC 创建时间和可执行文件路径快照；
打开通道前、执行探针前和探针后重新读取并精确比较，任何变化都 fail-closed。

## 旧版短时 Inspector 约束

Inspector 当前固定使用端口 9229，并执行以下门禁：

- 仅接受 `127.0.0.1`、`localhost` 或 `::1`；
- HTTP 只允许 `/json/version` 和 `/json/list`；
- WebSocket 只允许 `/<UUID>`；
- 拒绝凭证、查询字符串、Fragment、重定向、远程 Host、错误端口和错误路径；
- 监听地址必须为回环，OwningProcess 必须等于已校验 Codex PID；
- `/json/version` 必须声明 Node 与协议版本；
- `/json/list` 必须只有一个 Node Target，Target ID 与 WebSocket UUID 必须一致；
- 如果 `/json/version` 也提供 Browser WebSocket，其 UUID 必须与 Page Target ID 一致；
- HTTP/WebSocket 均使用短超时和最大 128 KiB 响应限制。

探针使用 `process._debugProcess(PID)` 短时打开 Node Inspector，并通过
`process._debugEnd()` 关闭。成功与失败路径都尝试清理；成功返回前再次确认
9229 已无监听。开启与关闭等待使用 Node 本地 TCP 探测，只有在端口状态转换时
才调用 Windows 端口 API 核对回环地址和 OwningProcess；不在短时门限内反复启动
PowerShell 进程。TCP 可达本身不构成信任，读取 Inspector 元数据前仍必须完成
权威端口所有者校验。

## 统一宿主随机回环 Renderer 约束

没有 Windows Node debug-handler 的统一 ChatGPT/Codex 宿主只允许使用当前用户注册的
官方 Microsoft Store 包。Theme Studio 通过已注册 AUMID 启动应用，并仅传入：

- `--remote-debugging-address=127.0.0.1`；
- 由本机临时监听器预留后立即释放的随机高位 `--remote-debugging-port`；
- 正式主题流程不传 `--user-data-dir`，继续使用用户原 Profile。

已运行但没有该端口的 ChatGPT 只能在 GUI 明确提示保存未发送输入并经用户确认后请求
正常关闭；持久化 Agent 只可接管启动不超过 2 分钟的新实例，运行更久的实例保持
fail-closed。关闭前后都核对精确 EXE、PID 和 UTC 创建时间；优先请求主窗口正常关闭，
必要时只使用 Windows Restart Manager 的正常关机请求，不调用强制终止。

每次 Renderer 操作都重新确认主进程命令行中的地址和端口、Windows 回环监听地址与
OwningProcess。`/json/version` 必须返回 Chrome Browser 协议及同端口、无凭证、无查询参数的
`/devtools/browser/<UUID>` WebSocket；随后通过 Target 协议枚举页面，只对经过
`app://` 路由、辅助窗口排除和 DOM 能力探测的 Codex 页面执行表达式。操作结束再次复核
进程身份、启动端口和端口所有者；任何变化、端口复用或结构异常都 fail-closed。

该随机回环端口由 ChatGPT 启动参数创建，在受管 ChatGPT 进程存续期间保持监听；它不是
旧版可由 Theme Studio 单独关闭的 9229 Node Inspector。其本机同用户调试面记录在
风险登记 R-26，不得把随机端口描述为认证或零风险边界。

## 能力探针

能力表达式访问 Electron 主进程公开运行时信息并执行隔离 Canary：

- `process.versions.electron`；
- `BrowserWindow.getAllWindows()` 的窗口数量和合格主窗口；
- `webContents` 与 `executeJavaScript` 是否可用；
- 每个窗口 URL 的协议和脱敏路由类型；
- 隔离 CSS Canary 的添加、生效、移除和残留检查。

返回结果不包含完整 URL。`initialRoute=/avatar-overlay` 只返回
`avatar-overlay`，其他 `app://` 地址只保留首个路径段分类；非 `app://`
地址统一返回 `non-app`。

## 结构化命令

```text
self-test
discover
probe --pid <PID> [--executable <absolute-exe-path>]
close-inspector --pid <PID> [--executable <absolute-exe-path>]
renderer-port-probe --pid <PID> --executable <absolute-exe-path> --port <PORT>
renderer-port-apply --pid <PID> --executable <absolute-exe-path> --port <PORT>
renderer-port-status --pid <PID> --executable <absolute-exe-path> --port <PORT>
renderer-port-cleanup --pid <PID> --executable <absolute-exe-path> --port <PORT>
```

标准输出或错误输出均为单个 JSON 文档，包含服务版本、协议版本、状态、
稳定错误码和 `retryable`。错误响应不记录命令行、完整调试响应、页面 DOM、
认证信息或用户对话。

Desktop 启动 Node 子进程时必须把重定向的 stdout 与 stderr 显式解码为 UTF-8；不得依赖
GUI 进程的系统代码页。否则中文 Windows 可能按 CP936 解码 Injector 的 UTF-8 错误 JSON，
破坏字符串边界并把明确的兼容性错误误报为 `injector_response_invalid`。

`windows-discovery.ps1` 由系统 `powershell.exe` 执行，以 Windows PowerShell 5.1
为最低运行基线。精确 EXE 路径不得依赖 PowerShell 7 或 .NET Core 专属 API；
进程不存在、PID 复用或无法取得映像路径时返回结构化空结果并由上层 fail-closed，
不得让 PowerShell 异常文本污染 JSON 协议。对应回归会真实调用 Windows
PowerShell 5.1，而不是只在当前开发 Shell 中解析脚本。

## 当前版本验证

2026-07-20 在 Codex Store 版本 `26.715.4045.0` 上完成只读实测：

- 主进程：PID 仅作为当次测试证据，不写入长期配置；
- Electron：`150.0.7871.124`；
- 窗口数：2；
- 脱敏路由类型：`app:index.html`、`avatar-overlay`；
- Inspector 从打开到确认关闭共约 14.091 秒；
- 探针结束后端口 9229 无监听。

版本号和窗口形态属于当次验证证据，不作为未来版本白名单。Codex 更新后重新解析
保存目标或回退 Store 自动发现；未知版本以完整能力探测决定临时可用性，未知响应
结构、能力缺失、Inspector 残留或清理失败仍默认拒绝。

2026-07-24 在 Store Codex `26.721.3404.0` / Electron `150.0.7871.128`
上完成精确 EXE 发现、能力探测和“应用 → 清理 → 重新应用”闭环；当前指纹
取得本机持久化资格，最终端口 9229 无监听。该次验收未执行持久化 enable/disable。

2026-08-05 在 Store Codex `26.727.6591.0` / Electron `150.0.7871.182`
上复验同一闭环。新版宿主的 Inspector 在端口开始监听后可能尚未完成 HTTP
元数据就绪，关闭后也需要更长时间才能稳定重开；Injector 因此在总门限内重试
暂态连接、等待端口稳定关闭，协议或端口所有者异常仍立即拒绝。最终清理为
`active=false`、`hookCount=0`，端口 9229 无监听；未执行持久化 enable/disable。

2026-09-09 在 Store Codex `26.903.8094.0` / Electron `152.0.7977.83`
上复现 Windows 端口查询单次约需 3.7 秒，旧逻辑在 6 秒开启门限内先执行查询、
再发送调试信号，导致 Inspector 已可开启但操作误报超时。改为先发送信号并使用
本地 TCP 等待后，真实只读 probe 通过：2 个窗口中 1 个主窗口合格，Canary 应用
与清理均成功，最终端口 9229 无监听。本次未执行持久化 enable/disable，也未据此
更新持久化资格记录。

## 启动组合检查

启动状态刷新通过一次 `discover` 返回安装身份、EXE SHA-256、进程快照和观察时间。
旧版缓存失效时使用 `inspect-status` 在同一个短时 Inspector 会话中完成能力 Canary 与
Renderer 状态读取；仅需核对已有活动主题时使用 `renderer-status`，不重复能力 Canary，
并在返回前确认 9229 已关闭。统一宿主若已由 Theme Studio 以随机回环端口受管启动，则
使用 `renderer-port-probe/apply/status/cleanup`；若当前进程没有端口，则按上节规则请求
确认后受管重启，或由持久化 Agent 在受限启动窗口内接管。

两条路径都在执行前后校验 PID、创建时间、EXE 路径、启动参数、端口所有者和目标身份；
随机 Renderer 端口在进程存续期间保持监听，不纳入“9229 已关闭”的旧 Inspector 结论。

组合结果不得缓存 PID、创建时间、Browser/Page Target、端口、窗口、路由、Renderer
marker 或页面内容。失败、取消、身份变化或关闭确认失败继续 fail-closed。

## 2026-09-27 旧通道限制与 2026-09-30 受管通道验收

Store ChatGPT `26.924.1866.0` 的当前主进程没有暴露 Node 在 Windows 上用于
`process._debugProcess(PID)` 的 `node-debug-handler-<PID>` 映射。受控源码探测在
`OpenFileMappingW` 返回 errno 2，端口 9229 始终未进入监听；这与 DOM 选择器不匹配
属于不同阶段。Injector 将该稳定事实返回为 `unsupported_version` /
`inspector_activation_unavailable`，上层保留本地化说明并 fail-closed。

关闭 ChatGPT 后，由用户手动启动的独立探测器重新校验了目标 EXE 的 SHA-256，随后通过
已注册 AUMID 调用 Store 应用激活接口并附带 `--inspect=127.0.0.1:9229`。应用能够重新
启动，但参数没有成为 Electron 主进程命令行，9229 未进入监听；该路径稳定报告
`inspect_cli_activation_unavailable`。直接以精确 WindowsApps EXE 创建进程则被 MSIX
应用模型以 Access Denied 拒绝，包清单也没有为 ChatGPT 主程序注册可用的 execution alias。
这些结论证明固定 9229 Node Inspector 与精确 EXE 启动不可用，但不再被解释为所有安全
Renderer 通道都已穷尽。后续实现增加官方 Store AUMID 的受管随机端口启动，并保留原
Profile；旧版仍先走原有短时 debug-handler 通道，不受新分支影响。整个方案不使用进程内
原生 DLL 注入，不修改 WindowsApps、`app.asar`、EXE 或数字签名。

2026-09-30，Store ChatGPT `26.924.2738.0` 完成随机回环端口 Canary、临时主题可见、
持久主题切换、正常重启恢复以及电脑重启后从菜单启动恢复。暂态
`port_renderer_unavailable`、`port_renderer_unqualified` 和 `renderer_port.open_timeout`
保留同一可信受管进程继续重试，不再触发永久熔断；修复后的真实冷启动一次成功。
最终 Agent、Run 项、配置与运行时状态一致，9229 无监听。Electron 精确版本、非 Store
实例、慢启动和系统高负载下的扩展实机覆盖仍为 `To be confirmed`；随机 Renderer 端口的
进程存续期本机调试面继续按 R-26 保持 Open。
