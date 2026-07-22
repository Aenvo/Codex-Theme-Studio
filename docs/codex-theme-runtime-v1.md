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

注入采用 fail-closed。当前验证版本为 Codex `26.715.4045.0`；其他官方 Store
版本可以被发现并显示为 `Unsupported`，但不会自动注入。后续兼容性复验通过后，
应显式更新 `CodexVersionPolicy` 的验证集合。

## Restore 边界

`renderer-cleanup` 会：

- 停用未来窗口和 `dom-ready` 重注 Hook。
- 移除 Style、主题 Class、CSS 变量和装饰 DOM。
- 断开 Observer、事件监听和 Timer。
- 撤销当前背景 Blob URL。
- 最终请求关闭 Inspector。

Restore 不删除主题库，不修改 Codex 的 API、模型、MCP、权限、身份或会话配置，
也不要求重启 Codex。
