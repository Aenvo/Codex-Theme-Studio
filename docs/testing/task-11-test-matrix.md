# 任务 11 测试矩阵

## 执行入口

- 全量自动化：`.\build.ps1`
- 真实 Codex 临时主题与隔离验证：
  `.\tests\task-11\Invoke-RealCodexValidation.ps1 -SwitchCount 10`
- 真实持久化：仅在已准备受审计的便携 Runtime 后，分别设置
  `CTS_LIVE_ACTION=enable` / `disable`、`CTS_LIVE_BUNDLE` 和
  `CTS_LIVE_DATA_ROOT`，再单独运行 `Integration.Tests`。

真实测试脚本只接受当前用户注册的官方 Store Codex，要求恰好一个可信主进程。
无论成功或失败，脚本都会执行 Renderer cleanup、请求关闭 Inspector，并要求
端口 9229 最终无监听。

## 自动化覆盖

| 领域 | 覆盖 |
| --- | --- |
| Theme Core | Schema、未知字段、未知高版本、枚举、颜色、数值、中文、Emoji、长名称、草稿、复制、指纹 |
| 图片 | PNG/JPEG/WebP、伪扩展名、空/畸形/超大、尺寸/像素、EXIF/ICC 清理、并发缓存、取消清理、符号链接/Junction |
| 存储 | SQLite 初始化/迁移、20 路并发写入、事务回滚、只读/空间不足、DataRoot 损坏/离线、迁移取消、Zip Slip、包炸弹、Hash |
| Codex Adapter | Store 身份、动态路径、非回环、错误端口/路径、Browser ID、PID 复用、未知版本 fail-closed、超时关闭 Inspector |
| Renderer | 主窗口、折叠侧栏、宠物排除、不完整窗口、DOM 迟到、路由变化、幂等替换、Blob 回收、刷新恢复、cleanup、pointer-events |
| Agent | 单实例、新 PID、PID 复用、重启去重、快照损坏、DataRoot 离线、配置路径、启动项安装/移除错误 |
| Desktop | 100/500 主题、搜索/标签、状态区分、异步互斥、编辑草稿、截图与最小尺寸 |

## 证据分级

- 自动化通过只证明对应输入和夹具。
- Renderer 状态只证明运行时标记、窗口计数和隔离结果，不单独等同视觉正确。
- 真实 Codex 可视复核单独记录在任务 11 结果中。
- `LiveFact` 未满足环境条件时必须显示为 Skip，不允许直接返回并伪装为通过。

