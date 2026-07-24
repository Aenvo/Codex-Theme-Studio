# ADR 0002：当前用户持久化 Agent 与启动机制

- 状态：Accepted
- 日期：2026-07-20

## 背景

持久主题必须在便携 GUI 退出或移动后继续工作，同时不能要求用户通过特殊 Codex
快捷方式启动，也不能长期开放 Inspector。主题库可能位于移动磁盘，因此 Agent
不能直接依赖 GUI 正在编辑的主题目录。

## 决策

第一版采用以下结构：

- Agent 安装到 `%LOCALAPPDATA%\CodexThemeStudio\Agent\versions\<SHA-256>`。
- 当前用户启动使用
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 中固定名称
  `CodexThemeStudio.PersistenceAgent`。
- Run 命令只包含完整引用的稳定 Agent 路径、固定 `run --config` 参数和完整
  引用的稳定配置路径；不经过 Shell 拼接。
- Agent 使用当前用户命名 Mutex 保证单实例，使用命名 Event 接收退出信号。
- 默认快照位于 `%LOCALAPPDATA%\CodexThemeStudio\Runtime\Persistence`。
- 严格存储模式将快照放到 DataRoot 的 `runtime/persistence`；DataRoot
  不可用时 fail-closed，不创建替代数据根。
- Inspector 只在新可信 PID 应用主题或低频完整性复核时短时打开，操作后关闭。

## 快照与状态

每次快照写入新的不可变 UUID 目录，包含 `manifest.json`、最小 `theme.json`
和受管背景副本。主题与图片分别计算 SHA-256，再计算组合指纹。完成全部写入后，
通过原子替换 `current.json` 切换当前快照。

Agent 状态记录 PID、UTC 创建时间、主题 UUID、快照指纹和最后复核时间。同一
PID、创建时间和指纹匹配时不会重复注入；默认每 60 秒才允许一次运行时复核。

## 本地诊断与支持材料

Desktop 与 Agent 使用同一强类型诊断 Schema，但分别写入 `desktop.jsonl` 和
`agent.jsonl`，避免跨进程竞争。事件模型不提供任意属性字典，所有可持久化字段
均经过白名单校验。连续状态只在变化时记录，并使用低频心跳证明 Agent 仍在运行。

诊断写入采用 fail-open：日志不可用不得破坏主题功能，但写入失败必须作为结构化
状态暴露，不能静默假装已有证据。用户可以在本地生成 Issue Markdown 或脱敏 ZIP；
导出过程重新序列化允许字段，不直接复制原始日志，也不自动上传或创建远端 Issue。

## 后果

- 移动或删除便携 GUI 不会破坏已经安装的 Agent。
- 本地稳定模式会保留少量 Agent、日志和当前主题运行数据，界面必须如实说明。
- 内容寻址版本目录暂不自动删除，版本清理必须提供明确、可恢复的后续流程。
- Run 项、Agent 配置或快照损坏时返回结构化错误，不假装持久化仍然正常。
- 停用持久化会移除 Run 项、通知 Agent、还原当前 Codex 并清除数据库中的当前
  持久主题标记，但保留主题库、快照版本和有限诊断日志。
