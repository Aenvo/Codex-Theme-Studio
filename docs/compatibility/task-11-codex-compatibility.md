# 任务 11 Codex 兼容性报告

## 当前验证记录

| 项目 | 结果 |
| --- | --- |
| Codex | Microsoft Store `26.715.4045.0` x64 |
| Package Family | `OpenAI.Codex_2p2nqsd0c76g0` |
| Electron | `150.0.7871.124` |
| Renderer compatibility | v1 |
| 窗口 | 1 个主窗口、1 个 `avatar-overlay` |
| 临时切换 | 连续 10 次成功，generation 1–10 |
| 隔离 | 每次 1 个主窗口应用、1 个宠物窗口保持辅助状态 |
| 未知版本 | 精确版本白名单；未知版本警告并 fail-closed，Restore 仍可用 |

机器可读记录位于 `docs/compatibility/codex-versions.json`。新增验证版本时，必须：

1. 动态确认 Store 包身份和当前安装路径。
2. 执行只读 probe、自动化矩阵与真实窗口隔离。
3. 记录 Electron、窗口形态、Renderer compatibility 和验证日期。
4. 更新 `CodexVersionPolicy` 的精确白名单并运行版本策略测试。

## 本次发现并修复的兼容性问题

主窗口左侧栏折叠后不再存在 `aside`、`nav` 或旧 `data-*` 特征，只保留带
`aria-label` 的侧栏切换控件。旧规则把主窗口误判为辅助窗口。

修复后同时接受英文 `sidebar` 和中文“侧边栏/边栏”的 `aria-label` 特征。
宠物窗口仍先由 `initialRoute=/avatar-overlay` 硬排除；不完整 `app://` 窗口仍
fail-closed。

## 支持边界

- 只支持当前用户注册且身份、签名和进程均通过校验的官方 Store 包。
- 不支持非 Store 安装、多个主进程、未知版本、非回环 Inspector 或不完整 Shell。
- 版本号相同但 DOM 特征变化时，真实 probe 仍可能 fail-closed；不得仅凭版本号
  宣称兼容。

