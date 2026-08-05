# Codex 发现与短时 Inspector 通道 v1

## 范围

本通道负责目标发现、主进程身份校验、短时 Inspector 通信和能力探针。
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

## 通道约束

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
9229 已无监听。

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
```

标准输出或错误输出均为单个 JSON 文档，包含服务版本、协议版本、状态、
稳定错误码和 `retryable`。错误响应不记录命令行、完整调试响应、页面 DOM、
认证信息或用户对话。

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

## 启动组合检查

启动状态刷新通过一次 `discover` 返回安装身份、EXE SHA-256、进程快照和观察时间。
缓存失效时使用 `inspect-status` 在同一个短时 Inspector 会话中完成能力 Canary 与
Renderer 状态读取；仅需核对已有活动主题时使用 `renderer-status`，不重复能力 Canary。
两种路径仍在执行前后校验 PID、创建时间、EXE 路径、端口所有者和目标身份，并在返回前
确认 9229 已关闭。

组合结果不得缓存 PID、创建时间、Browser/Page Target、端口、窗口、路由、Renderer
marker 或页面内容。失败、取消、身份变化或关闭确认失败继续 fail-closed。
