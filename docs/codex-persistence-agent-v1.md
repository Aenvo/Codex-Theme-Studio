# Codex Persistence Agent v1

## 用户语义

持久化是用户明确启用的当前用户级能力。启用后，GUI 可以退出；Agent 在发现新的
可信 Codex PID 时短时应用当前快照。停用持久化会移除启动项、停止 Agent、还原
当前 Codex 外观并清除当前持久主题标记，但不会删除主题库。

`IPersistenceService` 提供：

- `EnableAsync`：安装稳定 Agent、创建并激活快照、注册启动项。
- `SwitchAsync`：暂停 Agent、原子切换快照、切换当前运行时，再恢复 Agent。
- `DisableAsync`：移除启动项、发出退出信号、Restore 并清除持久主题标记。
- `GetStatusAsync`：交叉检查配置、启动项、快照和当前运行时。

## 稳定路径

默认基目录为 `%LOCALAPPDATA%\CodexThemeStudio`：

```text
Agent/
├─ config.json
├─ state.json
└─ versions/<bundle-sha256>/
   ├─ CodexThemeStudio.Agent.exe
   └─ runtime/
      ├─ node/node.exe
      └─ injector/
Runtime/Persistence/
├─ current.json
└─ snapshots/<snapshot-uuid>/
   ├─ manifest.json
   ├─ theme.json
   └─ background.<ext>
Logs/
├─ agent.jsonl
└─ agent.jsonl.1
```

Agent 安装目录按发布内容寻址，不引用便携 GUI 原路径。发布包必须提供
固定版本 Node Runtime 和 Injector 目录。

## 快照策略

- 稳定模式（默认）：快照位于上述本地运行目录。
- 严格存储模式：快照位于 DataRoot 的 `runtime/persistence`。数据根或快照目录
  离线时 Agent 不应用，也不会创建新的默认主题库。

快照读取拒绝未知 Schema、重解析点、未知字段、超限文件、不安全文件名、主题
校验失败或任一 SHA-256 不匹配。Agent 从不读取 GUI 正在编辑的半成品目录。

## Agent 循环

- 默认每 10 秒检查一次，错误按 10、20、40、60 秒退避。
- 使用 PID、UTC 创建时间和快照指纹识别实例及 PID 复用。
- 同一实例成功应用后默认 60 秒内不打开 Inspector。
- 到达复核间隔后只读取最小 Renderer 状态；标记丢失才重新应用。
- 每轮解析已保存的手选 EXE；路径失效时回退当前用户 Store 自动发现。
- Agent 仅对已验证版本或当前 EXE SHA-256 已完成本机首次闭环的目标注入；指纹变化时暂停，等待 GUI 重新验证。
- Named Mutex 保证单实例；Named Event 用于 GUI 停用或切换时通知退出。
- GUI 与 Agent 的 Inspector 操作共用当前用户命名 Mutex，避免并发打开、查询或
  关闭同一 Inspector；普通安装与 PID 发现不受该锁影响。
- 日志使用结构化字段白名单记录时间、来源、级别、事件、操作结果、会话/关联 ID、
  应用与 Codex 版本、EXE SHA-256、错误码和严格脱敏异常指纹。异常只保留类型、
  HRESULT、最多 10 个方法名栈帧与栈指纹，不保留原始消息、源码路径或行号。
- 单文件 1 MiB，保留当前文件和两个滚动副本。连续相同状态只在变化时记录，
  另每 15 分钟写入一次健康心跳；错误后的首次成功单独记录恢复事件。
- 日志不记录 URL、DOM、对话、账号、Token、主题内容、绝对路径、身份信息或
  Inspector 原始响应。写入失败不会阻断主题功能，但会返回结构化结果并尝试在
  Agent 状态中记录 `diagnostics.write_failed`。

## 命令入口

```text
CodexThemeStudio.Agent.exe self-test
CodexThemeStudio.Agent.exe run --config <absolute-config-path>
CodexThemeStudio.Agent.exe once --config <absolute-config-path>
CodexThemeStudio.Agent.exe signal-stop
```

`run` 会立即隐藏控制台窗口。配置中的 Agent、Node、Injector、状态和日志路径均
执行固定目录白名单校验，防止损坏配置把 Agent 变成任意程序启动器。

## 启动项

Run 值名为 `CodexThemeStudio.PersistenceAgent`。命令中的 Agent 和配置路径始终
单独加双引号，因此包含空格或中文时仍可运行。移除启动项失败时
`DisableAsync` 返回失败，不继续报告“已停用”。
