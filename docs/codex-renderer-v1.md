# Codex Renderer Runtime v1

## 范围

本模块实现声明式主题 Payload、renderer 窗口识别、Style/Blob 装饰层、
首页与任务页强度、主进程生命周期钩子及完整清理；应用层已将这些能力封装为
用户可见的临时应用、状态查询和完整还原语义。

## Payload

Injector 只从 UTF-8 JSON 标准输入读取主题，主题值不进入命令行。Payload
执行严格字段白名单，并再次校验：

- Schema v1、UUID、名称、枚举、颜色和数值范围；
- PNG/JPEG/WebP Content-Type、Base64、32 MiB 受管图片上限和文件签名；
- crop focus、cover/contain/crop、opacity、overlay、blur 和 variant；
- 旧 `safeArea` 字段继续校验以兼容 Schema v1，但不参与渲染。

任务 3 的持久化枚举保持不变。Renderer 层确定映射：

| Schema v1 | Renderer |
| --- | --- |
| `ambient` | `ambient` |
| `full` | `banner` |
| `hidden` | `off` |

Payload 由 `JSON.stringify` 整体序列化到固定程序，不把颜色、路径、图片或主题名
拼接成可执行代码。标准输入 JSON 上限为 48 MiB，可容纳 32 MiB 图片的 Base64
膨胀和主题元数据，同时继续阻断无界输入。

## 窗口识别

兼容配置版本为 1，集中定义在 `renderer-runtime.mjs`。判定顺序：

1. 主进程仅考虑 `app://` 窗口；
2. 排除 `initialRoute=/avatar-overlay`、`/avatar-overlay` 和已知宠物路径；
3. Renderer 等待 DOM 不再处于 `loading`；
4. 同时要求 Shell、侧栏、主内容和 Composer 四类特征；
5. 不完整或未知窗口保持原样。

当前版本的 task/home 判断优先使用 Codex 提供的语义化 `data-*` 特征，包括
主内容中的 conversation、timeline 和 thread footer 标记；不读取页面文本，
也不把单个易变 class 作为唯一依据。

## Renderer 层

每个已识别主窗口最多创建：

- 一个 `#codex-theme-studio-style`；
- 一个 `#codex-theme-studio-layer`；
- 一个背景 Blob URL；
- 根元素 Class、运行版本、generation 和 pageMode 标记。

装饰层和子层固定使用 `pointer-events: none`。背景图片从白名单字节创建 Blob，
支持裁切焦点、cover/contain/crop 和 blur。只有 crop 使用 focus；首页使用 home opacity/overlay；
任务页支持 ambient、banner、off。

Renderer 监听 DOM 变化、hash/popstate 和低频页面模式检查。重复 ensure 在相同
themeId/generation 下只修复缺失节点和同步 pageMode，不创建新 Style 或 Blob。

## 主进程生命周期

主进程状态保存在固定的 v1 全局键中，生命周期为：

```text
prepare → probe → apply → ensure → replace → cleanup
```

- `apply`：枚举已有窗口并安装 `dom-ready` Guard；
- `ensure`：幂等复核全部窗口；
- `replace`：递增 generation，先停用旧 listener，再清理旧 renderer；
- `browser-window-created`：只为未来窗口安装同一套双重识别；
- `cleanup`：先使 runtime inactive，再移除所有 listener，清理 renderer、
  Style、Class、CSS 变量、DOM、observer、timer 和 Blob URL。

Renderer 与主进程清理都校验 generation。旧代清理句柄在新代生效后返回 false，
不能删除新主题。

## 状态与隐私

最小状态只返回 runtimeVersion、active、generation、themeId、主/辅助窗口数、
hookCount、pageModes 和脱敏失败计数。它不返回 URL、DOM、页面文本、用户对话、
认证信息或完整 Inspector 响应。

## 当前机器验证

2026-07-20 在 Codex `26.715.4045.0` / Electron `150.0.7871.124` 上完成：

- DOM probe：1 个完整主窗口、1 个 `avatar-overlay` 辅助窗口；
- 应用与两次 replace：generation 1 → 2 → 3，始终只有 1 个主窗口应用、
  1 个辅助窗口隔离、2 个窗口 Guard、0 个失败；
- 首页识别为 `home`，现有任务识别为 `task-ambient`；
- 任务菜单打开/关闭、Composer 聚焦和主内容滚动正常；
- `Ctrl+R` 后 generation 保持 3，`dom-ready` 自动重应用；
- cleanup 后 active=false、hookCount=0；再次 `Ctrl+R` 未重注；
- 每次短时 Inspector 操作后端口 9229 均无监听。

真实截图已用于本机视觉核对，但因截图包含本地任务名和账号信息，没有保存到
仓库或作为交付物附带。机器上存在任务 6 之外的旧皮肤背景，因此截图只用于
确认本任务的 Style/Class/交互和窗口隔离；图片 Blob 的创建、替换与撤销由 VM
测试和运行时状态验证，不把旧皮肤素材归因于本任务。

2026-08-05 在 Codex `26.727.6591.0` / Electron `150.0.7871.182` 上复验：
1 个完整主窗口应用、1 个 `avatar-overlay` 隔离、2 个窗口 Guard、0 个失败；
应用、状态确认、清理、重新应用闭环通过，最终清理后 `active=false`、
`hookCount=0`，端口 9229 无监听。未保存含本地任务信息的真实截图。
