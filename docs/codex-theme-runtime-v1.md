# Codex Theme Runtime v1

## 范围

任务 7 将任务 3 至 6 的主题存储、图片资产、Codex 身份校验、短时 Inspector
和 Renderer 生命周期编排为 `ICodexThemeRuntime`。本阶段只管理当前 Codex
实例，不创建启动项、不启动 Agent，也不写入持久化主题快照。

GUI 后续通过以下四个异步入口调用：

- `ApplyTemporaryAsync`：首次临时应用。
- `SwitchTemporaryAsync`：切换临时主题，失败时尝试恢复旧主题。
- `GetStatusAsync`：读取安装、进程、会话和 Renderer 标记状态。
- `RestoreAsync`：停止重注 Hook 并完整清除主题运行时。

底层 Injector 命令 `renderer-apply`、`renderer-status` 和
`renderer-cleanup` 保留为结构化测试入口。主题及图片只通过 UTF-8 JSON
标准输入传递，不进入命令行。

## 状态机

```text
NotInstalled / NotRunning / Unsupported
                    |
                    v
Default / Ready -> Applying -> Temporary
                         |          |
                         |          +-> Applying (Switch)
                         |                    |
                         +-> Partial          +-> Temporary / safe rollback
                                              |
Temporary / Partial -> Restoring ------------+-> Default
```

额外诊断状态：

- `Mismatch`：Renderer 主题与会话所选主题不一致，或旧会话对应的 Codex
  进程已经变化。
- `InspectorResidual`：查询前发现 9229 端口已处于监听，或端口被异常占用。
- `Unavailable`：例如同时发现多个可信主进程，无法安全选择实例。
- `Failed`：保留给无法归入上述状态的运行时失败。

`ThemeRuntimeEvidence` 明确区分进程存在、Renderer 标记和用户可见效果。
当前自动状态查询最多返回 `RuntimeMarkers`；只有独立的真实视觉验证才能声明
`VisibleEffect`。

## 并发、取消与故障恢复

- Apply、Switch 和 Restore 共用一个非阻塞写锁；第二个写操作返回
  `runtime.operation_busy`，并在用户消息中提供当前操作 ID。
- 每个写操作生成 UUID，内存中的进行中状态和会话文件都带有该 ID。
- Injector 自身使用短时 Inspector，应用服务另设 60 秒总超时。
- Renderer 已成功改变后，会话文件使用独立的短恢复令牌完成提交，避免用户取消
  导致“运行时已切换、状态未切换”。
- Switch 在执行新主题前预取旧主题和图片。新主题应用失败或会话文件提交失败时，
  先尝试重放旧主题；旧主题不可恢复时执行完整 cleanup 并写回 Default。
- Apply 完整成功并原子提交会话文件后，才更新 SQLite 的 `last_used_utc` 和
  `last_apply_result`。最近使用元数据写入失败不会谎报运行时应用失败。
- Restore 可重复调用。Renderer 不存在时 cleanup 返回无操作成功；Codex
  未运行时只清除安全的当前会话状态。

## 当前会话文件

文件位于 DataRoot 下的 `runtime/current-session.json`，Schema v1 只包含：

- 状态、主题 UUID。
- Codex PID 与 UTC 创建时间。
- Renderer generation。
- 操作 UUID 与更新时间。

写入使用同目录临时文件、落盘刷新和原子替换。文件不包含图片、绝对路径、窗口
URL、DOM、页面文本、对话、认证信息或 Inspector 原始响应。该文件不是任务 8
的持久化主题快照。

## 版本策略

注入采用能力驱动的 fail-closed 策略。当前验证版本为 Codex `26.715.4045.0`、
`26.715.10079.0`、`26.721.3404.0`、`26.727.6591.0`、`26.730.8199.0`、
`26.803.5235.0` 和 `26.803.10989.0`，验证记录只决定状态显示为
`Verified`，不作为运行白名单。
未知版本完整能力探测通过后显示为 `CompatibleByProbe`，并可以立即临时应用；
能力缺失、目标不明确、Inspector 残留或注入闭环失败时显示为 `Incompatible`。

每个尚未取得本机资格的 EXE SHA-256（包括已验证版本）的首次临时应用执行“应用并验证 → 清理并验证 → 重新应用并验证”，
最终保持主题可见；闭环成功后写入本机资格记录并开放持久化。EXE 内容变化会使原
资格失效，Agent 暂停注入，直到新指纹再次完成临时闭环。机器可读兼容证据见
`docs/compatibility/codex-versions.json`；后续真实复验只需追加证据记录和对应测试。

## Restore 边界

`renderer-cleanup` 会：

- 停用未来窗口和 `dom-ready` 重注 Hook。
- 移除 Style、主题 Class、CSS 变量和装饰 DOM。
- 断开 Observer、事件监听和 Timer。
- 撤销当前背景 Blob URL。
- 最终请求关闭 Inspector。

Restore 不删除主题库，不修改 Codex 的 API、模型、MCP、权限、身份或会话配置，
也不要求重启 Codex。

## 启动缓存与实时状态

`compatibility-qualifications.json` Schema v2 保存构建级兼容资格：EXE SHA-256、
Codex 版本、包完整名、来源与身份评估、探测协议版本、必需能力版本、Canary 结果和
探测时间。Schema v1 的哈希仅保留原有持久化资格含义，不能直接作为启动能力缓存。

缓存命中只说明相同构建曾完成兼容探测，不代表当前进程、窗口或可见主题状态。启动仍
执行一次实时发现并重新计算 EXE SHA-256；哈希、包身份、来源、探测协议或必需能力版本
任一变化都会使缓存失效。默认外观且缓存有效时不打开 Inspector；会话记录存在活动主题
时执行一次 Renderer-only 核对；缓存失效时执行一次组合能力与状态检查。

Desktop 将状态区分为 `Cached`、`Confirming`、`Live` 和 `Error`。缓存阶段普通主题管理
可用；临时应用必须等待实时身份确认，已取得精确指纹资格的持久化配置和离线 Restore
可以在 Codex 未运行时执行。窗口重新激活会先做不打开 Inspector 的轻量进程发现；发现
进程身份变化后才刷新完整运行状态，并在未就绪时按 2 秒间隔短时重试最多 20 秒。手动刷新
强制完整探测；任何主题应用仍执行独立的操作级实时校验，不直接信任启动缓存。
