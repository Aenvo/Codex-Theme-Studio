# Codex 发现与短时 Inspector 通道 v1

## 范围

任务 5 只实现官方 Microsoft Store Codex 的安装发现、主进程身份校验、
短时 Inspector 通信和只读探针。此阶段不注入 CSS、背景、DOM 或生命周期钩子，
不读取页面文本，也不修改 Codex 配置。

## 安装与进程身份

每次操作动态调用 Windows 包与进程 API，不缓存版本目录。安装必须同时满足：

- 包名为 `OpenAI.Codex`；
- PackageFamilyName 为 `OpenAI.Codex_2p2nqsd0c76g0`；
- PublisherId 为 `2p2nqsd0c76g0`；
- SignatureKind 为 `Store`，PackageStatus 为 `Ok`；
- `ChatGPT.exe` 位于当前注册包 InstallLocation 内。

主进程必须位于同一包目录，文件名为 `ChatGPT.exe`，且命令行不包含任何
`--type=` 子进程参数。每个操作保存 PID、UTC 创建时间和可执行文件路径快照；
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

## 只读探针

只读表达式仅访问 Electron 主进程公开运行时信息：

- `process.versions.electron`；
- `BrowserWindow.getAllWindows()` 的窗口数量；
- 每个窗口 URL 的协议和脱敏路由类型。

返回结果不包含完整 URL。`initialRoute=/avatar-overlay` 只返回
`avatar-overlay`，其他 `app://` 地址只保留首个路径段分类；非 `app://`
地址统一返回 `non-app`。

## 结构化命令

```text
self-test
discover
probe --pid <PID>
close-inspector --pid <PID>
```

标准输出或错误输出均为单个 JSON 文档，包含服务版本、协议版本、状态、
稳定错误码和 `retryable`。错误响应不记录命令行、完整调试响应、页面 DOM、
认证信息或用户对话。

## 当前版本验证

2026-07-20 在 Codex Store 版本 `26.715.4045.0` 上完成只读实测：

- 主进程：PID 仅作为当次测试证据，不写入长期配置；
- Electron：`150.0.7871.124`；
- 窗口数：2；
- 脱敏路由类型：`app:index.html`、`avatar-overlay`；
- Inspector 从打开到确认关闭共约 14.091 秒；
- 探针结束后端口 9229 无监听。

版本号和窗口形态属于当次验证结果，不作为未来版本白名单。Codex 更新后仍从
当前注册包重新发现；未知响应结构默认拒绝，需在任务 11 做兼容性复验。
