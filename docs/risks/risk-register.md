# 当前风险登记表

- 更新日期：2026-07-27
- 适用基线：Codex Theme Studio 1.2.0 本地维护发布
- 历史任务风险和当时证据保留在 `task-11-known-issues.md`、`task-12-known-issues.md` 与对应验收记录中。

| ID | 风险 | 当前控制或证据 | 状态 |
| --- | --- | --- | --- |
| R-01 | .NET 8 将于 2026-11-10 结束支持 | SDK 固定为 8.0.423，发布为 self-contained；跨越 EOL 前必须用新 ADR 评估迁移到受支持 LTS | Open |
| R-02 | 便携包未签名 | manifest 和 SHA256SUMS 提供完整性校验，README 明确披露未知发布者警告；不建议绕过安全软件 | Open |
| R-03 | 本机没有有效病毒扫描证据 | Defender 调用曾返回 `0x800106ba`；公开分发前必须在启用且签名库最新的防病毒环境复扫 | Open |
| R-04 | 未在无开发 Runtime 的干净 Windows 用户或 VM 验收 | self-contained 文件、随包 Node、中文/空格路径启动及另一台非完全干净 Windows 机器上的可运行性已验证；仍不能替代无预装 .NET/Node 的干净环境 | Open |
| R-05 | 当前 Codex 完全重启后的 Agent 恢复未在本会话复验 | enable/disable、当前 PID 和自动化新 PID/PID 复用路径已有证据；完整宿主重启仍未覆盖 | Open |
| R-06 | 真实外置磁盘 DataRoot 迁移未覆盖 | 临时真实文件系统测试覆盖复制、哈希、SQLite 完整性、离线、取消和回滚；物理断连仍未覆盖 | Open |
| R-07 | 能力探测可能无法覆盖未来 Codex 或第三方构建的全部行为差异 | 当前真实证据覆盖 Store Codex `26.715.4045.0`、`26.715.10079.0` 与 `26.721.3404.0` x64；`26.721.3404.0` 的应用内更新将 Electron 从 `150.0.7871.124` 更新到 `150.0.7871.128`，完整能力探测与首次应用/清理/重应用闭环通过。能力缺失和清理残留仍 fail-closed；非 Store 真实实例验证为 `To be confirmed` | Open |
| R-08 | 第一方与第三方许可证范围可能混淆 | 根目录 `LICENSE` 将第一方源码声明为 Apache-2.0；README 与第三方 Notices 分别说明适用范围和商标边界 | Mitigated |
| R-09 | 图片内容寻址缓存并发提交可能竞态 | 1.0.1 改为缓存先提交、主题资源最后提交，并对移动冲突进行有限重试和哈希复核；由并发压力回归覆盖 | Mitigated |
| R-10 | 打包成功或失败残留大型工作目录 | 1.0.1 使用独立暂存发布目录，成功后回收本次工作目录，失败保留并报告诊断路径 | Mitigated |
| R-11 | 构建使用系统 Node 导致与发布 Runtime 漂移 | `build.ps1` 和 `package.ps1` 共同读取 `eng/runtime-baseline.json`，严格要求 Node `v24.18.0` | Mitigated |
| R-12 | 本地生成物和历史验证副本占用大量空间 | 仅保留当前发布和 1.1.7 回滚归档；固定 Node 缓存继续保留。历史发布目录只能在范围、恢复点和回收站验证完成后处理 | Mitigated |
| R-13 | Injector 运行脚本误用 PowerShell 7/.NET Core API，或原子写入在句柄释放前移动文件 | 运行时发现固定以 Windows PowerShell 5.1 为最低基线；自动化测试真实执行精确 EXE `Discover` 和边界 `Snapshot` 并解析单一 JSON。资格、目标选择及其他原子写入均以流作用域结束后再 `Move/Replace`，关键路径有跨实例读取回归 | Mitigated |
| R-14 | 统一“还原外观”需要修改第三方 OkkSkin 的当前用户启动项、状态和 Agent；身份误判可能影响无关进程，部分失败可能导致下次 Codex 再次应用主题 | 仅接受无 Reparse Point 的已知状态与启动器、精确 Run 命令和精确 `node.exe … agent.mjs` 命令行；状态原子改为禁用并保留未知字段和缓存；任一残留返回 Partial。自动化边界测试已加入，真实 Codex 完整重启验收仍为 `To be confirmed` | Open |
| R-15 | 启动兼容缓存可能被误解为当前进程、窗口或可见效果已经验证 | Schema v2 只缓存构建级资格；启动始终重新发现并计算 EXE SHA-256，不缓存 PID、端口、Target、Renderer 或活动主题；应用与持久化继续执行操作级实时 fail-closed 校验 | Mitigated |
| R-16 | 旧本机交接快照或历史归档可能被误认为当前实施状态 | `Directory.Build.props` 固定维护基线 `1.2.0`；根目录旧交接快照已可恢复移出并由 `.gitignore` 阻止再次误提交，历史验收文档只保留证据边界 | Mitigated |
| R-17 | 去重后的便携包依赖 Agent bundle manifest；路径逃逸、清单篡改或复制中断可能生成不完整稳定 Agent | Schema v1 对路径、大小、SHA-256、重复目标和重解析点 fail-closed；安装先写随机暂存目录，复核全部文件后原子切换，既有内容寻址版本复用前重新校验 | Mitigated |
| R-18 | 自动化 Release 可能在签名、病毒扫描或干净环境验收前公开 | CI 仅有 `contents: read`；Release 构建阶段只读，只有人工推送精确 tag 后的独立 job 取得 `contents: write` 并创建 Draft。公开发布仍需人工完成 SHA-256、有效 Defender、NotSigned 披露和干净 Windows 验收 | Mitigated |
| R-19 | 公开仓库可能意外暴露凭证、个人数据或尚未修复的漏洞 | `.gitignore` 拦截常见环境文件、密钥、日志和数据库；公开前扫描当前树与历史，安全问题转入 Private Vulnerability Reporting。仓库公开后仍需人工启用 Secret Scanning、Push Protection 和私密漏洞报告 | Open |
| R-20 | 编辑器永久删除无引用受管背景时，错误的可达性判断可能造成不可恢复的数据损失 | 删除范围只来自当前编辑会话追踪；存储层再次校验可信 DataRoot、精确主题 UUID、普通文件/目录、无重解析点、`theme.json` 当前 `art.file` 引用和空目录条件。共享缓存、索引主题、应用回收站主题、未知孤立目录及完整未索引主题均排除；临时真实文件系统测试覆盖直接删除和保护分支 | Mitigated |
| R-21 | 正式 macOS Helper 与已验证 spike 行为漂移，导致身份、端口或清理门禁缺失 | 正式代码不依赖 `spikes/`；ADR 记录证据 SHA，生产协议为独立版本化实现。第 7B.2B 验收入口强制经 Application → Coordinator → Adapter 调用链并由 fixture 验证命令顺序；真实端口和清理仍待真机闭环 | Open |
| R-22 | macOS Swift Helper、固定 Node、runtime manifest 或 renderer script 被替换 | 7B.2A 在仓库外原子 staging 中实际建立 Node/脚本 → manifest → Swift Helper → .NET 的单向内容身份链；源码常量保持空并 fail-closed，只有严格 staging self-test 可报告身份已配置。产物仍未签名或公证，7B.2B 真机闭环及第 8 阶段 nested signing、Gatekeeper 验收前不得声明发布身份资格 | Open |
| R-23 | Helper 崩溃、取消或超时后 Inspector 仍监听 9229 | Helper 独占信号、Node 和 Inspector 生命周期；Application 验收流程为必要清理保留独立 25 秒 token，调用方取消不得跳过 Cleanup 与 `_debugEnd()`，最终端口未知或残留均 fail-closed。真机有效性仍待 7B.2B | Open |
| R-24 | macOS Codex 更新后错误复用旧资格 | 资格只缓存安装组合指纹、协议和能力版本，不缓存 PID、Target 或活动主题；第 7B.2B 资格限定为单一 Harness 进程内存，每次操作重新发现并要求进程身份稳定。跨应用重启持久资格尚未实现 | Open |
| R-25 | 背景图片本地预览被误解为已在 Codex 中生效 | macOS MVP 只投影调色板，UI 必须显示背景未应用；Blob 背景渲染和撤销前后可访问性继续标记 `To be confirmed` | Open |
| R-26 | .NET 或 Node 在 Hardened Runtime 下需要过宽 entitlement | 第 8 阶段从 `allow-jit` 最小候选开始逐项验证；不预设 Apple Events、DYLD、disable-library-validation 或 unsigned executable memory | Open |
| R-27 | macOS 根构建或 Contracts 变更破坏 Windows 1.2.0 | Windows WPF、Agent 和现有 Adapter 初期保持不变；跨平台 DTO 使用新增 v2 类型，所有共享变更要求 Windows完整构建和测试证据 | Open |
| R-28 | Intel、Universal Binary 或多个 macOS/Codex 版本被当前 arm64 证据错误覆盖 | 首版只声明 macOS 14+ Apple Silicon；Intel、Universal 和兼容矩阵均保持 `To be confirmed` 并使用独立 Go/No-Go | Open |
| R-29 | Avalonia 在 macOS 14/15 的 Tier 2 支持不足以保证真实窗口、Retina、中文输入法和 VoiceOver 质量 | 第 7C 先以 fixture、Headless 和真实窗口分层验证；VoiceOver、IME 和 App Bundle 辅助功能必须保留真机验收，不以编译或 Headless 结果替代 | Open |
| R-30 | macOS UI 取消、关闭窗口或退出应用可能中断已开始的 Runtime Cleanup/Inspector finally | UI 只允许在状态操作开始前取消；开始后进入“安全收尾”，Application 持有独立 deadline/cleanup token，UI 不得终止 Helper 或 Node | Open |
| R-31 | macOS UI 可能把背景本地预览、资格状态或 Inspector 关闭证明误报为真实 Runtime 能力 | 背景永久显示“macOS 当前不应用”；资格和恢复使用结构化状态；`inspectorClosedProofCount` 只表示产品操作关闭证明，不显示为会话数量 | Open |
