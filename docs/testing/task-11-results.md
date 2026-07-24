# 任务 11 测试结果

> 历史证据：本文记录任务 11 当时的测试边界，已由任务 12 和后续维护验收取代。

## 自动化

- Release 构建：0 警告、0 错误。
- .NET：121 通过、2 跳过、0 失败。
- Node Injector：23 通过、0 跳过、0 失败。
- `dotnet format --verify-no-changes`：通过。
- Agent `self-test` 与 Injector `self-test`：通过。
- 两个跳过项均为真实持久化测试，原因由 `LiveFact` 明确输出。

## 真实 Codex

- 只读 probe：Store `26.715.4045.0`、Electron `150.0.7871.124`，
  1 个主窗口和 1 个 `avatar-overlay`。
- 连续 10 次临时切换全部成功：平均 `11943.943 ms`，0 个 Renderer 失败。
- 切换前工作集 `221757440 B`，切换后 `220250112 B`，差值 `-1507328 B`；
  未观察到持续增长。
- 任务页为 `task-ambient`；菜单、内容滚动、Composer 聚焦均可用。
- `Ctrl+R` 后自动恢复为 1 个主窗口应用、1 个宠物窗口隔离。
- Restore 后 `active=false`、`hookCount=0`、端口 9229 无监听。
- 未发送任何消息。

## 性能记录

| 项目 | 实测 |
| --- | --- |
| GUI 冷启动到主窗口 | `2848.356 ms` |
| GUI 启动后工作集 | `161882112 B` |
| GUI 启动后私有内存 | `97628160 B` |
| 100 主题 ViewModel 初始化 | `0.401 ms`（热运行） |
| 500 主题 ViewModel 初始化 | `176.625 ms`（冷运行） |
| 500 主题过滤 | `18.304 ms` |
| 2400×1200 JPEG 受管导入 | `434.281 ms`；源文件 `17647 B` |
| 10 次真实主题切换平均 | `11943.943 ms` |
| Desktop Release 工程目录 | 56 文件，`387241334 B` |
| Agent Release 工程目录 | 56 文件，`387102433 B` |
| 现有稳定 Agent 工程目录 | 70 文件，`479403926 B` |

工程目录体积不是最终发布包大小；最终 self-contained 目录和 ZIP 留待任务 12。

## 安全检查

- 源码未发现 `WindowsApps` 写入、`app.asar` 修改、API Key 或 Token 访问。
- 非测试网络端点仅有 WPF XML namespace 和 `127.0.0.1` Inspector。
- `auth` 字样来自 `UnauthorizedAccessException` 与只读 DOM 属性选择器，不是认证读取。
- 当前 Run 值不存在、Agent 进程数为 0、配置 `enabled=false`。
- 最终 Inspector 监听数为 0。

## 截图

- `docs/screenshots/task-11-desktop-cold-start.jpg`
- SHA-256：
  `708fc17d40db53cd56c03a4e5c13b6284c595b7ac869d5b68eeb06bc7dc881d0`

真实 Codex 截图因含私人任务内容未保存。
