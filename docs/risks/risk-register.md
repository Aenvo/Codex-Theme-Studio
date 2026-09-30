# 当前风险登记表

- 更新日期：2026-10-01
- 适用基线：Codex Theme Studio 1.3.5 已公开并为 GitHub Latest；本地正式候选包、远程 CI、私有预检、tag 构建、Draft 附件及公开状态均已复核。
- 历史任务风险和当时证据保留在 `task-11-known-issues.md`、`task-12-known-issues.md` 与对应验收记录中。
- 1.3.4 及统一宿主测试包的现场记录只证明对应构建和宿主；1.3.5 最终发布实包的统一宿主现场闭环仍未覆盖。
- 2026-09-10，维护者明确接受 R-03、R-04 与 R-22 中列出的三项缺失门禁，仅用于生成 `v1.3.3` Draft Release；该接受不等于门禁通过、不关闭风险，也不授权自动公开 Release。
- 2026-10-01，维护者针对 `v1.3.5` 明确接受下列未覆盖风险并授权公开本版本：有效病毒扫描、无预装 .NET/Node 的干净 Windows 验收、最终实包的统一宿主应用/清理/重新应用与持久化现场闭环、真实更新中断或断电、慢启动/高负载/恢复中断，以及受管随机回环 Renderer 端口的本机同用户连接面。此授权仅适用于 `v1.3.5`，不表示这些测试通过，也不关闭对应 Open 风险；公开说明须继续披露。

| ID | 风险 | 当前控制或证据 | 状态 |
| --- | --- | --- | --- |
| R-01 | .NET 8 将于 2026-11-10 结束支持 | SDK 固定为 8.0.423，发布为 self-contained；跨越 EOL 前必须用新 ADR 评估迁移到受支持 LTS | Open |
| R-02 | 便携包未签名 | manifest 和 SHA256SUMS 提供完整性校验，README 明确披露未知发布者警告；不建议绕过安全软件 | Open |
| R-03 | 本机没有有效病毒扫描证据 | 上次验收时 Defender 服务、实时保护和签名库不可用；维护者已明确选择不在本机恢复 Defender。`v1.3.5` 未取得有效扫描结果；已在本版本风险接受中承认该缺口，发布说明须披露，后续仍需在有效环境复扫 | Open |
| R-04 | 未在无开发 Runtime 的干净 Windows 用户或 VM 验收 | 既有 self-contained 文件、随包 Node、中文/空格路径启动及当前主机可运行性证据不能替代 `v1.3.5` 在无预装 .NET/Node 干净环境的验收；本版本未覆盖，已在本版本风险接受中承认并须公开披露 | Open |
| R-05 | Codex 完全重启后的 Agent 恢复 | `1.3.1-rc.1` 在 Store Codex `26.803.5235.0` 与 `26.803.10989.0` 上完成旧 Inspector 路径的完整重启恢复。2026-09-30 又在统一宿主 `26.924.2738.0` 上完成随机回环 Renderer 路径的持久主题切换、正常重启恢复及电脑重启后从菜单启动恢复；最终持久主题、Agent、Run 项与配置一致，`9229` 无监听 | Mitigated |
| R-06 | 真实外置磁盘 DataRoot 迁移未覆盖 | 临时真实文件系统测试覆盖复制、哈希、SQLite 完整性、离线、取消和回滚；物理断连仍未覆盖 | Open |
| R-07 | 能力探测可能无法覆盖未来 Codex 或第三方构建的全部行为差异 | 旧短时 9229 Inspector 路径的真实闭环证据覆盖 Store Codex `26.715.4045.0`、`26.715.10079.0`、`26.721.3404.0`、`26.727.6591.0`、`26.730.8199.0`、`26.803.5235.0`、`26.803.10989.0` 与 `26.903.8094.0` x64；统一宿主 `26.924.2738.0` 另以官方 Store 包激活参数和随机回环 Renderer 端口完成临时、持久、正常重启及电脑重启恢复验收。两条路径都继续校验精确 EXE、PID、创建时间、启动参数、回环端口所有者和 Renderer 结构；能力缺失、身份变化、错误端口所有者和应用/清理验证失败仍默认拒绝。非 Store 真实实例验证为 `To be confirmed` | Open |
| R-08 | 第一方与第三方许可证范围可能混淆 | 根目录 `LICENSE` 将第一方源码声明为 Apache-2.0；README 与第三方 Notices 分别说明适用范围和商标边界 | Mitigated |
| R-09 | 图片内容寻址缓存并发提交可能竞态 | 1.0.1 改为缓存先提交、主题资源最后提交，并对移动冲突进行有限重试和哈希复核；由并发压力回归覆盖 | Mitigated |
| R-10 | 打包成功或失败残留大型工作目录 | 1.0.1 使用独立暂存发布目录，成功后回收本次工作目录，失败保留并报告诊断路径 | Mitigated |
| R-11 | 构建使用系统 Node 导致与发布 Runtime 漂移 | `build.ps1` 和 `package.ps1` 共同读取 `eng/runtime-baseline.json`，严格要求 Node `v24.18.0` | Mitigated |
| R-12 | 本地生成物和历史验证副本占用大量空间 | 当前 `artifacts/release/` 仍保留多个历史版本及 `1.3.4-chatgptcompat1` 至 `1.3.4-chatgptcompat8` 测试目录；固定 Node 缓存继续保留。清理前须重新盘点每个目标的用途、精确范围和恢复点，并验证回收站机制，不直接删除既有归档 | Open |
| R-13 | Injector 运行脚本误用 PowerShell 7/.NET Core API，或原子写入在句柄释放前移动文件 | 运行时发现固定以 Windows PowerShell 5.1 为最低基线；自动化测试真实执行精确 EXE `Discover` 和边界 `Snapshot` 并解析单一 JSON。资格、目标选择及其他原子写入均以流作用域结束后再 `Move/Replace`，关键路径有跨实例读取回归 | Mitigated |
| R-14 | 统一“还原外观”需要修改第三方 OkkSkin 的当前用户启动项、状态和 Agent；身份误判可能影响无关进程，部分失败可能导致下次 Codex 再次应用主题 | 仅接受无 Reparse Point 的已知状态与启动器、精确 Run 命令和精确 `node.exe … agent.mjs` 命令行；状态原子改为禁用并保留未知字段和缓存；任一残留返回 Partial。自动化边界测试已加入，真实 Codex 完整重启验收仍为 `To be confirmed` | Open |
| R-15 | 启动兼容缓存可能被误解为当前进程、窗口或可见效果已经验证 | Schema v2 只缓存构建级资格；启动始终重新发现并计算 EXE SHA-256，不缓存 PID、端口、Target、Renderer 或活动主题；应用与持久化继续执行操作级实时 fail-closed 校验 | Mitigated |
| R-16 | 旧本机交接快照或历史归档可能被误认为当前实施状态 | `Directory.Build.props` 决定源码维护版本；1.3.5 本地候选包已核对 FileVersion、Build Info、Agent/安装清单与 ZIP 摘要。最终 tag `v1.3.5` 指向 `084e26c`，tag 工作流与三项正式附件已独立复核；公开 Latest API 返回本版本。根目录旧交接快照已可恢复移出并由 `.gitignore` 阻止再次误提交，历史验收文档只保留证据边界 | Mitigated |
| R-17 | 去重后的便携包依赖 Agent bundle manifest；路径逃逸、清单篡改或复制中断可能生成不完整稳定 Agent | Schema v1 对路径、大小、SHA-256、重复目标和重解析点 fail-closed；安装先写随机暂存目录，复核全部文件后原子切换，既有内容寻址版本复用前重新校验 | Mitigated |
| R-18 | 自动化 Release 可能在签名、病毒扫描或干净环境验收前公开 | CI 仅有 `contents: read`；Release 构建阶段只读，只有人工推送精确 tag 后的独立 job 取得 `contents: write` 并创建 Draft。`v1.3.5` 在附件 SHA-256 与 NotSigned 复核、缺失验收披露及维护者本版本风险接受后，才按该次授权公开；风险接受不等于门禁通过 | Mitigated |
| R-19 | 公开仓库可能意外暴露凭证、个人数据或尚未修复的漏洞 | 2026-10-01 只读 GitHub API 核对：Private Vulnerability Reporting 为 `enabled: true`，`security_and_analysis.secret_scanning` 与 `security_and_analysis.secret_scanning_push_protection` 均为 `enabled`。当前 Git 历史 Gitleaks 唯一命中为旧 XAML 图标的 `x:Key` 误报，未发现凭证；`.gitignore` 拦截常见环境文件、密钥、日志和数据库。防护不能排除后续误提交或历史泄露；安全问题使用私密漏洞报告，不通过公开 Issue 披露 | Open |
| R-20 | 编辑器永久删除无引用受管背景时，错误的可达性判断可能造成不可恢复的数据损失 | 删除范围只来自当前编辑会话追踪；存储层再次校验可信 DataRoot、精确主题 UUID、普通文件/目录、无重解析点、`theme.json` 当前 `art.file` 引用和空目录条件。共享缓存、索引主题、应用回收站主题、未知孤立目录及完整未索引主题均排除；临时真实文件系统测试覆盖直接删除和保护分支 | Mitigated |
| R-21 | 更新资产被替换、损坏或构造为路径逃逸 ZIP | 只接受三个精确 Release 资产；GitHub asset digest、Schema v3 release manifest 与 SHA256SUMS 必须一致。ZIP 条目数、压缩/展开大小、绝对路径、`..`、ADS、大小写重复路径及链接均 fail-closed | Mitigated |
| R-22 | 自更新在文件锁、断电或新版启动失败时留下不可运行目录 | 外置 Node runner 只接收随机 token；请求通过 Schema v1 JSON 传递。应用目录在同盘以 rename 切换，旧目录保留到新版完成 120 秒健康检查；自动化已覆盖文件锁、启动超时、回滚及回滚失败。`v1.3.5` 的真实终止与断电边界未覆盖，已在本版本风险接受中承认并须公开披露 | Open |
| R-23 | 更新清理误删用户放入便携目录的文件，或持久化 Agent 升级失败 | `app-install-manifest.json` 逐文件记录大小和 SHA-256；只删除旧清单拥有且哈希仍匹配的文件，其他文件移入 Preserved。Agent 使用事务式切换，失败继续运行旧版并提供重试 | Mitigated |
| R-24 | 持久化 Agent 频繁打开 Inspector 可能放大新版 Codex/Electron 的 browser 主进程不稳定 | 2026-09-17 在 Store Codex `26.911.7940.0` 上观察到多次 browser crash；ProcDump 确认 `0x80000003` 与 `chrome.dll`，但终止转储缺少原始异常上下文，不能把 Theme Studio 写为已确认唯一根因。源码审计确认 1.3.3 在 10 秒轮询的安全间隔判断前执行能力探测，并在复核时再次打开 Inspector。1.3.4 将无侵入身份短路提前、把复核安全下限提高到 15 分钟、合并为单次 Renderer 状态检查，并在当前 PID 的 Inspector/应用失败后 fail-closed 熔断；`1.3.4-rc.1` 已在该 Codex 构建完成 Agent 事务升级、完整 Codex 重启、原主题恢复及跨旧 60 秒间隔验证，期间无重复 Inspector、9229 残留或新增应用错误 | Mitigated |
| R-25 | ChatGPT 统一宿主未注册 Node Windows debug-handler，旧短时 9229 Inspector 通道无法触达 Renderer | `26.924.1866.0` 的运行中 debug-handler、固定 9229 参数激活和精确 EXE 启动失败证据继续保留。后续实现改为仅对当前用户注册的官方 Store 包，在用户确认或持久化 Agent 的受限启动窗口内通过 AUMID 传入 `--remote-debugging-address=127.0.0.1` 与随机高位端口，并继续使用原 Profile；旧版仍走既有短时 Inspector。`26.924.2738.0` 已完成 Canary、临时主题、持久主题、正常重启和电脑重启恢复真实验收；暂态 `port_renderer_unavailable`、`port_renderer_unqualified` 与 `renderer_port.open_timeout` 不再永久熔断同一可信进程。未修改 WindowsApps、`app.asar`、EXE 或签名，9229 无监听。未来统一宿主参数或 Renderer 结构变化仍由 R-07 fail-closed 控制 | Mitigated |
| R-26 | 统一宿主受管启动的随机回环 Renderer 端口在 ChatGPT 进程存续期间保持开放，可能扩大本机同用户进程的调试面 | 只绑定 `127.0.0.1` 随机高位端口；每次操作都核对官方 Store 身份、精确 EXE、PID、创建时间、主进程命令行中的端口、Browser ID、Page Target、`app://` 路由和 Windows 端口所有者，拒绝远程地址、端口复用和身份变化。随机端口不是认证机制，其他本机同用户进程仍可能发现并连接该 CDP 端点；README 与用户指南已披露端口存续和本机连接风险。`v1.3.5` 实包端口复验与不依赖进程存续端口的替代方案未覆盖；维护者已针对本版本接受已披露的本机连接风险 | Open |
| R-27 | 统一宿主首次应用或恢复主题需要受管关闭并重新启动官方 ChatGPT；未保存输入或暂态 Renderer 未就绪可能造成可见重启或重复启动 | GUI 在关闭前明确提示保存未发送输入并要求确认；关闭只针对精确 PID、创建时间与 EXE 匹配的主进程，优先 `CloseMainWindow`，随后只使用 Windows Restart Manager 正常关机请求，不调用强制终止。持久化 Agent 只允许对启动不超过 2 分钟的无端口实例自动接管，运行更久的实例 fail-closed 并要求回到 GUI；暂态 Renderer 就绪失败保留同一受管进程继续重试。README 与用户指南已披露受管重启，`26.924.2738.0` 修复后真实冷启动一次成功；`v1.3.5` 实包现场闭环及慢启动、系统高负载和恢复中断的扩展覆盖未完成，维护者已针对本版本接受并须公开披露 | Open |
