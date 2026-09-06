# Codex Theme Studio 设计系统

本文件是 Codex Theme Studio 的项目级视觉与交互真相源。它约束应用自身界面，不约束用户创建或导入的 Codex 主题。

## 1. 设计方向

- **Confirmed**：应用固定使用以 `#111111` 为主背景的中性黑桌面视觉，并以蓝色 `#2563EB` 表达主要动作、组件规则明确要求的选中描边和键盘焦点；不跟随 Windows 明暗模式。
- **Confirmed**：Doro 等用户主题只作为内容展示，不得替换应用品牌 token。
- **Confirmed**：界面采用 shadcn/ui 的语义分层与组件状态思想，由原生 WPF 实现；不引入 React、WebView 或第三方 WPF UI 框架。
- **Confirmed**：保留 Windows 原生标题栏、拖动、缩放、贴靠和辅助功能，并通过 DWM 使用深色标题栏。

## 2. 语义颜色

| Token | 值 | 用途 |
|---|---:|---|
| `background` | `#111111` | 窗口与主内容背景 |
| `foreground` | `#F5F5F5` | 主文字 |
| `card` | `#181818` | 卡片与分组容器 |
| `popover` | `#1C1C1C` | Popup、菜单、Tooltip、Dialog |
| `secondary` | `#242424` | 次要按钮与交互表面 |
| `muted` | `#1F1F1F` | 弱化表面 |
| `muted-foreground` | `#A3A3A3` | 辅助文字 |
| `border` | `#303030` | 卡片与分隔线 |
| `input` | `#404040` | 输入边界、滑块、滚动条和需高辨识度的文本型分段器选中面 |
| `primary` | `#2563EB` | 主要动作与选中描边 |
| `primary-hover` | `#1D4ED8` | 主要动作悬停 |
| `primary-pressed` | `#1E40AF` | 主要动作按下 |
| `primary-foreground` | `#FFFFFF` | 主要动作上的高对比文字与图标 |
| `ring` | `#60A5FA` | 键盘焦点环 |
| `loading` | `#287EFF` | 加载中状态的文字与图标 |
| `destructive` | `#F87171` | 破坏性动作与错误 |
| `sidebar` | `#0D0D0D` | 侧栏背景 |
| `sidebar-accent` | `#202020` | 侧栏活动表面 |
| `accent-soft` | `#232323` | 选中卡片与列表项的中性底色 |

颜色通过 `Themes/DesignTokens.xaml` 暴露为 WPF 资源。组件不得在能够使用语义 token 时另写相近颜色；状态底色可使用明确的深色成功、警告和错误色。
大面积表面必须保持 `R=G=B` 的中性色；蓝色只用于主要按钮、选中描边、Focus、Slider、Progress 和小面积状态提示，不得重新作为卡片或页面背景。主要按钮使用白色前景，并在 default、hover 和 pressed 状态保持至少 `4.5:1` 的文字对比度。

## 3. 排版、间距与形状

- 字体：`Segoe UI, Microsoft YaHei UI`，正文默认 13 px。
- 页面标题：27 px、SemiBold；卡片标题：17 px、SemiBold；辅助信息：11–12 px。
- 基础间距单位为 4 px；常用控件间距为 8、12、16、24 px。
- 输入和按钮最小高度 36–38 px；卡片圆角 12 px，按钮、输入和 Popup 内部控件圆角 8 px，Badge 圆角 6 px。
- 标准窗口为 `1240×780` 级别，最小窗口为 `960×620`；操作不得因缩放或窄窗口变得不可达。

## 4. 组件规则

- Button、Input、Select、Card、Badge、Alert、Tooltip、Slider、Checkbox、Progress、Empty State、Toast、Dialog、ContextMenu 与 Popup 均复用项目资源和状态规则。
- Hover 提高边界或表面层级；Pressed 降低视觉强度；Disabled 降低透明度且不可触发；Focus 必须使用 `ring`，不能只依赖颜色变化。
- Primary Button 的 Default、Hover、Pressed 分别使用 `primary`、`primary-hover`、`primary-pressed`，不得退回普通按钮的中性 Hover 底色。
- Sidebar 分为 Header、Content、Menu、Footer。活动项使用 `sidebar-accent`，不使用整块高亮白底。
- **Confirmed**：侧栏以单一“工作台”承载主题资料库入口；“全部主题/当前主题”属于工作台内容区的范围筛选，不再作为并列导航项。
- 状态信息放在内容顶部 Alert；短时结果使用非阻塞 Toast；当前主题的高频操作位于卡片或顶部操作区，低频操作进入更多菜单。
- **Confirmed**：主题资料库提供互斥的“卡片展示”和“列表展示”，默认使用卡片展示。两种展示共享搜索、筛选、排序和当前选择，不得因切换布局改变业务状态。
- 搜索框使用 9 px 圆角、左侧搜索图标和清晰占位文案；Hover 提升表面与边界，键盘 Focus 使用 2 px `ring`。
- Select 默认圆角为 8 px；Hover 使用较高层级表面，展开与键盘 Focus 使用 2 px 语义边界。展开触发器必须由透明的定制命中层承载，不得显示平台默认 `ToggleButton` 的大面积高亮底色。
- 卡片展示以背景预览为主要视觉入口，标题、标签/来源、主题配色和最近使用时间位于卡片信息区；当前选择、当前临时、当前持久和外部持久使用不同 Badge。
- 列表展示用于更紧凑地扫描主题，保留与卡片展示相同的主题信息和状态语义。两种展示都必须保留 Recycling 虚拟化与可滚动性；不得为实现换行网格而退回一次性创建全部卡片的普通 `WrapPanel`。
- 图标型分段切换器由相邻的等尺寸选项组成；选中项使用 `accent-soft` 表面和 `ring` 前景/焦点边界，Hover 使用 `secondary`。每个选项必须设置可访问名称，并在约 350 ms 后显示说明用途的 Tooltip；不得只依赖图标形状或颜色表达当前模式。
- **Confirmed**：设置页使用“通用 / 诊断 / 关于”文字型分段器。选中项必须使用 `input`（`#404040`）作为填充、`foreground` 作为文字色，以明显浅于 `muted` 的中性背景表达状态；不得使用蓝色描边或蓝色文字作为选中的唯一表现。未选中项悬浮时使用 `border`（`#303030`）表面和 `foreground` 文字色，且显示手型光标；未选中项获得键盘焦点时仍使用 `ring`，已选中项获得焦点时保留选中填充且不叠加蓝色描边。
- 编辑器采用左侧参数 Card 与右侧固定预览 Card；首页和任务页使用可切换的预览语义。

## 5. 图标系统

- **Confirmed**：应用内通用界面图标固定采用通过 `icons0/i0` 选取的 Lucide 线性图标；只内置实际使用的图标，不引入整套 React、SVG Runtime 或第三方 WPF 图标框架。
- `icons0/i0` 只用于设计与开发阶段的图标检索、选型和来源确认，不是应用运行依赖。选定图标必须转换并固化到仓库；构建、测试、运行和便携发布均不得联网请求 i0、Iconify 或其他图标服务。
- 图标以 `24×24` viewBox、2 单位描边、圆角端点和圆角连接为源规格，转换为 `Themes/Icons.xaml` 中的原生 WPF Geometry；常规控件显示为 16 px，紧凑状态为 14 px，空态可放大到 22–30 px。
- WPF 资源键采用 `Lucide<Name>Geometry`，显示统一复用 `LucideIconStyle`。新增图标时只添加当前界面实际使用的 Geometry，并同步第三方 Notices 中的快照数量；不得复制整套图标库。
- 图标颜色继承控件 `Foreground` 或使用既有语义 brush，不另建图标专用颜色；图标不得成为传达选中、错误或破坏性状态的唯一手段。
- 带文字的可交互控件保留可见标签和 `AutomationProperties.Name`；装饰性图标不接收焦点或命中测试。
- 产品 `app-icon.ico` / `app-icon.png` 是自制品牌资产，不由 Lucide 通用图标替换。

当前实现映射（**Inferred**，依据 `Themes/Icons.xaml` 与实际控件引用；新增或替换图标时同步维护）：

| Lucide 图标 | WPF 资源键 | 当前用途 |
|---|---|---|
| `layout-grid` | `LucideLayoutGridGeometry` | 工作台导航、卡片展示 |
| `list` | `LucideListGeometry` | 列表展示 |
| `search` | `LucideSearchGeometry` | 工作台搜索框 |
| `star` | `LucideStarGeometry` | 收藏与收藏状态 |
| `import` | `LucideImportGeometry` | 导入主题 |
| `settings` | `LucideSettingsGeometry` | 设置导航 |
| `plus` | `LucidePlusGeometry` | 新建主题 |
| `image-off` | `LucideImageOffGeometry` | 缺少背景图片 |
| `diamond` | `LucideDiamondGeometry` | 主题空态 |
| `chevron-down` | `LucideChevronDownGeometry` | 下拉选择器 |

## 6. 主题色与透明度

- **Confirmed**：主题文件的唯一颜色格式是 CSS 顺序 `#RRGGBB` 或 `#RRGGBBAA`。
- Alpha 为 `FF` 时保存六位大写格式；其他 Alpha 保存八位大写格式。
- WPF 原生 `#AARRGGBB` 只能存在于颜色转换层内部，主题值不得直接交给 WPF `ColorConverter`。
- 六个主题颜色字段均使用可复用 Color Field。色块显示在透明棋盘上，并同时显示规范化 Hex 与 Alpha 百分比。
- Color Picker 使用锚定 Popup，包含饱和度/明度平面、Hue、Alpha、精确 Hex、原色/当前色、主题快速色板、基础色板及应用/取消操作。
- 拖动应实时更新模拟预览；取消、`Esc` 或外部关闭必须恢复打开前的颜色；`Enter` 应用。
- OkkSkin 的 `rgba()` 只允许在受控导入时转换，不能作为主题存储值。

## 7. 对比度与可访问性

- 对比度计算必须包含 Alpha 合成：Panel 合成到 Background，Text/Muted 再合成到 Panel。
- Dark 使用黑色底，Light 使用白色底，Auto 取两种结果中较差值。
- 正文低于 `4.5:1` 或辅助文字低于 `3:1` 时显示 Alert，但不得静默改色。
- 支持 Tab、方向键、`Ctrl+F`、`F5`、`Enter` 与 `Esc`；可交互控件应设置 Automation Name。
- 高 DPI 下不得出现横向溢出、被遮挡操作或不可滚动区域；减少动态效果设置下不依赖动画传达状态。

## 8. 验证基线

UI 变更至少检查默认窗口与最小窗口下的空态、单主题、100 主题、编辑器、设置、加载、错误、禁用、Hover、Focus、Popup 和滚动。主题资料库还必须检查卡片/列表默认态与双向切换、分段器 Tooltip、共享选择状态，以及两种布局的虚拟化；设置页还必须检查文字型分段器的默认、各选中项、Hover 与键盘 Focus。源码与 ViewModel 测试不能替代真实渲染截图和人工目视检查。

## 9. macOS 产品规则

- macOS 使用 Avalonia 独立实现，不复用 WPF XAML；视觉 token、信息架构、主题卡片、
  本地预览和 Apply/Restore 语义保持一致。
- macOS 必须直接复用 Windows 的第一方 `app-icon.png` / `app-icon.svg` 字节，
  不得用通用图标或重新绘制的近似图替代产品标识。Sidebar 的工作台、收藏、
  回收站和设置使用与 WPF 同源的 Lucide Geometry、2 单位圆角描边和相同语义色。
- macOS Sidebar 固定遵循 Windows 的 Header、导航、底部设置和当前选择卡结构；
  活动项使用 `sidebar-accent`。资料库页使用全宽卡片区和底部所选主题操作栏，
  不增加会改变 Windows 信息层级的永久右侧预览栏。
- 保留 macOS 系统标题栏、窗口按钮、原生菜单、触控板滚动和系统文件交互。应用菜单
  提供 About 与 `⌘,` Settings；主窗口支持 `⌘F`、`⌘R` 和 `⌘W`。
- 内容区继续固定使用本文件的深色产品视觉，不随系统主题切换；字体使用系统 UI
  字体并允许 PingFang SC 等 CJK 回退，不内嵌 SF Pro。
- 首个 macOS MVP 的主流程为主题列表、选择、本地预览、临时应用和恢复。为保持
  产品导航一致，回收站入口保留；在 Storage 接入前只显示明确的 fixture 空态，
  还原、永久删除和清空操作保持禁用。Persistent Apply、完整编辑器、Agent 和
  登录项不显示为可用能力。
- 背景可以在 Studio 中本地预览，但主题卡片不增加 Windows 中不存在的能力
  Badge；“macOS 当前不应用背景”的边界放在所选主题操作区的 Runtime 能力摘要，
  并且背景不得发送给 Runtime。
- 所选主题操作区复用 Windows 的固定横向结构：左侧为单行 Codex 状态和单行
  兼容性/操作说明，中间只在操作进行时显示进度，右侧依次为“临时应用”、
  “设为持久主题”和“还原外观”。macOS 首版的持久主题按钮保留原位置但保持
  禁用并提供能力说明；Busy、安全收尾及失败状态不得继续允许 Apply。
- 正常、应用中和临时应用完成时，所选主题操作区的副标题与 Windows 一致，仅显示
  Codex 兼容性摘要（例如“未知版本 · 能力探测兼容”）；首次资格说明、背景能力
  提示和恢复建议不得拼接进该行。只有安全收尾或失败状态可暂时用结构化操作说明
  替代兼容性摘要。
- macOS 的按钮组件必须映射 Windows `ButtonBaseStyle`、`PrimaryButtonStyle` 和
  `GhostButtonStyle`：三者均为最小高度 36、内边距 14×7、8px 圆角和 2px
  `#60A5FA` 键盘焦点环。主按钮为 `#2563EB / #1D4ED8 / #1E40AF`；
  次级按钮默认 `#242424`、Hover `#1F1F1F` 配 `#404040` 边框；
  Ghost 默认透明、Hover 与次级按钮一致；次级和 Ghost 的 Pressed 使用 0.82
  透明度，Disabled 统一使用 0.42 且保留各自 Default 的背景、边框和前景；
  平台差异只能改变字体渲染和原生焦点输入方式，不得改变这些产品色和状态语义。
- macOS UI 不显示 PID、Target、完整 URL、Inspector 原始数据或私人内容。
  `inspectorClosedProofCount` 只能描述为产品操作关闭证明，不能显示为 Inspector
  会话数量。
- 状态操作开始后，关闭窗口或停止等待不得终止必要 Cleanup；UI 使用“正在安全
  收尾”表达不可中断阶段。
- 第 7C 验证至少覆盖 960×620、1240×780、0/1/100 主题、Retina、完整键盘导航、
  VoiceOver、中文输入法和 fixture-only 截图。
