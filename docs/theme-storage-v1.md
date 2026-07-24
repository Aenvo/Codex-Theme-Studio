# 主题与存储格式 v1

本文记录任务 3 已冻结的持久化契约。图片解码见任务 4 文档；主题导入解包与
DataRoot 迁移见[主题包与数据迁移格式 v1](theme-package-and-migration-v1.md)。
本文不描述 Codex 注入。

## 主题 JSON

- 当前 `schemaVersion` 为 `1`。
- JSON 属性名区分大小写；不允许注释、尾随逗号、整数枚举或未知字段。
- 当前版本遇到未知字段时拒绝读取，不会在再次保存时静默丢失字段。
- 高于当前版本的 Schema 只返回版本与兼容性错误，不反序列化、不覆盖。
- `id` 必须是非空 UUID；目录名固定使用该 UUID，不使用显示名称。
- `name` 必须非空、不得包含控制字符，最长 120 个 UTF-16 字符；名称不唯一。
- `variant`：`auto`、`light`、`dark`。
- `palette` 的六个颜色字段只接受 `#RRGGBB` 或 `#RRGGBBAA`。
- `art.safeArea` 仅作为旧主题兼容字段保留，Renderer 不再使用其值。
- `art.size`：`cover`、`contain`、`crop`；只有 `crop` 使用 `focusX`、`focusY`。
- `art.taskMode`：`ambient`、`hidden`、`full`。
- `focusX`、`focusY`、首页/任务页透明度与遮罩均为 `0..1`。
- `blur` 为 `0..64`。
- `art.file` 只允许相对路径以及 `.png`、`.jpg`、`.jpeg`、`.webp` 扩展名。
  用户图片必须先经过任务 4 的内容识别和安全重编码，主题只引用应用生成的
  受管资源；详见 [图片安全处理与预览管线](image-pipeline-v1.md)。

相对路径统一序列化为 `/` 分隔。路径不得为绝对路径，不得包含空段、
`.`、`..`、控制字符、Windows 非法字符、尾随点/空格或设备保留名。
访问 DataRoot 时还会拒绝根目录和现有路径组件上的符号链接、Junction
等重解析点。

## DataRoot 布局

固定定位文件默认为：

```text
%LOCALAPPDATA%\CodexThemeStudio\bootstrap.json
```

定位文件只保存版本和 DataRoot 绝对路径，采用同目录临时文件与原子替换。
损坏、超限或包含未知字段的定位文件会报错，不会被自动覆盖。已配置的
DataRoot 不可用时会报告离线，不会静默创建新的默认空库。

实际目录为：

```text
<DataRoot>/
├─ database/themes.db
├─ themes/<theme-uuid>/theme.json
├─ cache/
├─ exports/
├─ logs/
├─ recovery/
└─ runtime/
```

背景图、缩略图和主题 JSON 位于文件系统；SQLite 不保存大型 Blob。
主题 JSON 和资源文件使用同目录临时文件、刷新落盘后再原子替换。

## SQLite Schema v1

- `schema_migrations`：已应用数据库迁移版本与时间。
- `themes`：主题索引、名称、Schema 版本、时间、收藏、排序、来源、
  相对目录、缩略图相对路径、内容 SHA-256、当前持久主题、兼容性、
  最近应用摘要和软删除时间。
- `theme_tags`：主题标签及显示顺序，外键指向 `themes`。

迁移在事务内执行且可重复初始化。数据库版本高于当前版本时停止修改。
同名主题通过不同 UUID 区分；当前持久主题由唯一部分索引保证最多一个。
仓储写入与标签更新使用事务。

`DeleteAsync` 在 v1 中执行软删除：从活动查询中隐藏记录、清除其当前持久
标记，但保留主题目录作为恢复点。一致性扫描只报告缺失/无效/哈希不匹配
的索引文件和孤立目录，不自动删除或修复。

## 后续边界

- SQLite 完整性恢复和跨进程写入协调由集成测试与版本验收持续覆盖。
