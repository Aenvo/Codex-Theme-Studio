# 第 7C.1 macOS UI 设计 QA

## 比较证据

- Windows 资料库：
  `codex-clipboard-3392eca1-2457-4e2b-9731-5b5ee1280805.png`
- Windows 回收站：
  `codex-clipboard-302f6607-00c1-4c4c-b772-da0f06acb655.png`
- macOS Headless：
  `$CTS_UI_AUDIT_DIR/17-sidebar-library-1240x780.png`、
  `$CTS_UI_AUDIT_DIR/02-library-selected-1240x780.png`、
  `$CTS_UI_AUDIT_DIR/19-sidebar-trash-1240x780.png`、
  `$CTS_UI_AUDIT_DIR/20-sidebar-trash-960x620.png`
- Windows 编辑器视觉真相：
  `docs/screenshots/task-10-editor.png` 以及用户提供的 Windows 1.2.0
  深色编辑器截图（2048×1120）。
- macOS 编辑器：
  `$CTS_UI_AUDIT_DIR/22-editor-fixture-1240x780.png`、
  `$CTS_UI_AUDIT_DIR/23-editor-fixture-960x620.png`、
  `$CTS_UI_AUDIT_DIR/24-editor-task-1240x780.png`、
  `$CTS_UI_AUDIT_DIR/25-editor-color-picker.png`、
  `$CTS_UI_AUDIT_DIR/26-editor-task-wide-1800x980.png`。
- 并排对照：
  `$CTS_UI_AUDIT_DIR/windows-macos-sidebar-library.png`、
  `$CTS_UI_AUDIT_DIR/windows-macos-sidebar-trash.png`、
  `$CTS_UI_AUDIT_DIR/windows-macos-action-panel.png`、
  `$CTS_UI_AUDIT_DIR/windows-macos-editor-comparison.png`

截图只使用无私人内容的 fixture 图片；不复制用户主题或页面内容。

## Windows 一致性检查

- 产品 Logo：macOS `Assets/app-icon.png` 与 Windows 同名资产逐字节相同，
  SHA-256 一致；窗口与 Sidebar 均使用该资产。
- Sidebar：宽度 240，结构为品牌 Header、工作台/收藏/回收站导航、底部设置和
  当前选择卡。导航项铺满 Sidebar 可用宽度；背景、活动项、间距、圆角和字体
  层级与 WPF 资源一致。
- 图标：工作台、收藏、回收站、设置、搜索、刷新、卡片和列表使用与 WPF 同源的
  Lucide Geometry；24×24 源规格、2 单位描边、圆角端点和连接。
- 资料库：使用全宽卡片区、顶部标题/搜索/筛选/工具栏、卡片/列表切换，以及
  选中主题后出现的底部操作栏。搜索框与三个等宽筛选控件使用固定 12px 间距；
  1240×780 为三列，960×620 为两列。
- 主题卡片：收藏按钮位于预览图左上角，使用与 Windows 相同的 36px 半透明容器、
  白色轮廓星和黄色选中星；副标题统一使用 13px“本地主题”。卡片内不显示
  “背景仅预览”Badge，背景能力边界统一放在所选主题操作区。
- 所选主题操作区：固定为 Windows 的两行状态、居中 Busy 进度和三按钮横向结构；
  正常副标题精确使用“未知版本 · 能力探测兼容”，不再附加首次资格或背景预览
  信息；按钮顺序及文案为“临时应用 / 设为持久主题 / 还原外观”。macOS 首版仅将
  “设为持久主题”保持禁用；应用中、安全收尾和失败状态会禁用所有不安全操作，
  主失败与恢复失败压缩为脱敏的一行说明并保留完整 Tooltip。
- 按钮组件：Windows 与 macOS 的主、次级和 Ghost 按钮已按相同 token 及状态表
  映射。主按钮 Hover/Pressed 同步更新背景和边框；次级及 Ghost Hover 使用
  `#1F1F1F / #404040`，Pressed 使用 0.82，Disabled 使用 0.42；高度、内边距、
  圆角和焦点环保持一致。Disabled 仍保留主/次级/Ghost 各自的默认表面，不退回
  Avalonia Fluent 的通用禁用底色。组件五态证据为
  `$CTS_UI_AUDIT_DIR/21-button-component-states.png`。
- 回收站：入口、标题、工具栏、展示切换和空态层级与 Windows 对齐。第 7C.1
  尚未接 Storage，因此删除、还原和清空保持禁用，不伪装为已具备真实数据能力。
- 设置：Sidebar 和原生 `⌘,` 均请求同一个 Settings 单例窗口；About 继续位于
  macOS App 菜单。
- 编辑器：由资料库显式选择后进入完整窗口级编辑页；左侧保持 Windows 的主题名称、
  背景选择/拖放、图片适配、裁切缩放与焦点、首页/任务页透明度和遮罩、任务页模式、
  图片模糊、六色字段与颜色选择器、恢复默认色和面板毛玻璃；右侧保持首页/任务页
  双预览并实时反馈颜色、透明度、构图、模糊和面板玻璃；底部提供取消编辑、另存为副本、
  保存主题。颜色严格按主题 Schema 的 CSS `#RRGGBBAA` 解释，不使用 Avalonia 的
  `#AARRGGBB` 通道顺序。

## 合理保留的 macOS 差异

- 系统标题栏、交通灯按钮、NativeMenu、`⌘F`、`⌘R`、`⌘,`、`⌘W`。
- Settings/About 使用独立窗口，不复制 Windows 的 DWM 和页内窗口管理。
- 960×620 使用两列卡片并缩短搜索占位文案，避免裁切；1240×780 保持三列。

## 自动验证

- ViewModel 覆盖显式选择、工作台/收藏/回收站导航、设置请求、搜索筛选、
  Apply/Restore、Busy、SafeCleanup、主错误和恢复错误。
- Headless 覆盖 960×620、1240×780、0/1/100 主题、资料库、收藏、回收站、
  Settings、About、卡片/列表、编辑器首页/任务页、六个颜色字段、颜色窗口 Alpha
  滑杆、宽屏任务环境信息和关键控件边界。损坏的本地图片会安全显示为空预览，
  不允许解码异常越过 UI 边界。
- Release build 要求 0 warning、0 error。

## 未覆盖

- 真实 macOS 1×/2× Retina 字体光栅化。
- VoiceOver 实际朗读顺序、中文输入法 composition、系统菜单勾选状态。
- 回收站真实 Storage、删除、还原和清空。
- 正式 Runtime 接入及真机 Apply/Restore。
- 当前编辑器保存/另存为在 fixture 资料库的本次进程内生效；Storage 持久化接入属于
  第 7C.2，不能把当前结果描述为跨启动持久保存。

## final result: passed for the fixture-backed 7C.1 scope

Windows 产品身份、Sidebar、通用图标、资料库层级、所选主题操作栏、回收站空态和
编辑器核心工作流已在代码与 Headless 截图中对齐。编辑器补齐过程中发现并修复了
入口无导航、参数集缺失、预览未联动、颜色透明通道顺序错误、毛玻璃无反馈和首页输入区
尺寸错误；最终又修复了任务页输入区被背景图撑高、图片适配 UI 与草稿状态不同步、
颜色窗口 Alpha 控件裁切及损坏图片解码异常。修复后的 1240×780、960×620、
宽屏任务页与颜色窗口证据在第 7C.1 fake-backed 范围内无剩余 P0/P1/P2。
真实 Retina、VoiceOver、IME 和系统菜单仍需人工真窗验收。
