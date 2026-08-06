# 当前风险登记表

- 更新日期：2026-08-06
- 适用基线：Codex Theme Studio 1.3.0 本地维护发布
- 历史任务风险和当时证据保留在 `task-11-known-issues.md`、`task-12-known-issues.md` 与对应验收记录中。

| ID | 风险 | 当前控制或证据 | 状态 |
| --- | --- | --- | --- |
| R-01 | .NET 8 将于 2026-11-10 结束支持 | SDK 固定为 8.0.423，发布为 self-contained；跨越 EOL 前必须用新 ADR 评估迁移到受支持 LTS | Open |
| R-02 | 便携包未签名 | manifest 和 SHA256SUMS 提供完整性校验，README 明确披露未知发布者警告；不建议绕过安全软件 | Open |
| R-03 | 本机没有有效病毒扫描证据 | Defender 调用曾返回 `0x800106ba`；公开分发前必须在启用且签名库最新的防病毒环境复扫 | Open |
| R-04 | 未在无开发 Runtime 的干净 Windows 用户或 VM 验收 | self-contained 文件、随包 Node、中文/空格路径启动及另一台非完全干净 Windows 机器上的可运行性已验证；仍不能替代无预装 .NET/Node 的干净环境 | Open |
| R-05 | 当前 Codex 完全重启后的 Agent 恢复未在本会话复验 | enable/disable、当前 PID 和自动化新 PID/PID 复用路径已有证据；完整宿主重启仍未覆盖 | Open |
| R-06 | 真实外置磁盘 DataRoot 迁移未覆盖 | 临时真实文件系统测试覆盖复制、哈希、SQLite 完整性、离线、取消和回滚；物理断连仍未覆盖 | Open |
| R-07 | 能力探测可能无法覆盖未来 Codex 或第三方构建的全部行为差异 | 当前真实证据覆盖 Store Codex `26.715.4045.0`、`26.715.10079.0`、`26.721.3404.0`、`26.727.6591.0` 与 `26.730.8199.0` x64；最新版本 Electron 为 `151.0.7922.71`，完整能力探测、应用/清理/重应用、持久化 Agent 升级及 Codex 重启重注入闭环通过。能力缺失、身份变化、错误端口所有者和清理残留仍 fail-closed。非 Store 真实实例验证为 `To be confirmed` | Open |
| R-08 | 第一方与第三方许可证范围可能混淆 | 根目录 `LICENSE` 将第一方源码声明为 Apache-2.0；README 与第三方 Notices 分别说明适用范围和商标边界 | Mitigated |
| R-09 | 图片内容寻址缓存并发提交可能竞态 | 1.0.1 改为缓存先提交、主题资源最后提交，并对移动冲突进行有限重试和哈希复核；由并发压力回归覆盖 | Mitigated |
| R-10 | 打包成功或失败残留大型工作目录 | 1.0.1 使用独立暂存发布目录，成功后回收本次工作目录，失败保留并报告诊断路径 | Mitigated |
| R-11 | 构建使用系统 Node 导致与发布 Runtime 漂移 | `build.ps1` 和 `package.ps1` 共同读取 `eng/runtime-baseline.json`，严格要求 Node `v24.18.0` | Mitigated |
| R-12 | 本地生成物和历史验证副本占用大量空间 | 仅保留当前发布和 1.1.7 回滚归档；固定 Node 缓存继续保留。历史发布目录只能在范围、恢复点和回收站验证完成后处理 | Mitigated |
| R-13 | Injector 运行脚本误用 PowerShell 7/.NET Core API，或原子写入在句柄释放前移动文件 | 运行时发现固定以 Windows PowerShell 5.1 为最低基线；自动化测试真实执行精确 EXE `Discover` 和边界 `Snapshot` 并解析单一 JSON。资格、目标选择及其他原子写入均以流作用域结束后再 `Move/Replace`，关键路径有跨实例读取回归 | Mitigated |
| R-14 | 统一“还原外观”需要修改第三方 OkkSkin 的当前用户启动项、状态和 Agent；身份误判可能影响无关进程，部分失败可能导致下次 Codex 再次应用主题 | 仅接受无 Reparse Point 的已知状态与启动器、精确 Run 命令和精确 `node.exe … agent.mjs` 命令行；状态原子改为禁用并保留未知字段和缓存；任一残留返回 Partial。自动化边界测试已加入，真实 Codex 完整重启验收仍为 `To be confirmed` | Open |
| R-15 | 启动兼容缓存可能被误解为当前进程、窗口或可见效果已经验证 | Schema v2 只缓存构建级资格；启动始终重新发现并计算 EXE SHA-256，不缓存 PID、端口、Target、Renderer 或活动主题；应用与持久化继续执行操作级实时 fail-closed 校验 | Mitigated |
| R-16 | 旧本机交接快照或历史归档可能被误认为当前实施状态 | `Directory.Build.props` 固定维护基线 `1.3.0`；根目录旧交接快照已可恢复移出并由 `.gitignore` 阻止再次误提交，历史验收文档只保留证据边界 | Mitigated |
| R-17 | 去重后的便携包依赖 Agent bundle manifest；路径逃逸、清单篡改或复制中断可能生成不完整稳定 Agent | Schema v1 对路径、大小、SHA-256、重复目标和重解析点 fail-closed；安装先写随机暂存目录，复核全部文件后原子切换，既有内容寻址版本复用前重新校验 | Mitigated |
| R-18 | 自动化 Release 可能在签名、病毒扫描或干净环境验收前公开 | CI 仅有 `contents: read`；Release 构建阶段只读，只有人工推送精确 tag 后的独立 job 取得 `contents: write` 并创建 Draft。公开发布仍需人工完成 SHA-256、有效 Defender、NotSigned 披露和干净 Windows 验收 | Mitigated |
| R-19 | 公开仓库可能意外暴露凭证、个人数据或尚未修复的漏洞 | `.gitignore` 拦截常见环境文件、密钥、日志和数据库；公开前扫描当前树与历史，安全问题转入 Private Vulnerability Reporting。仓库公开后仍需人工启用 Secret Scanning、Push Protection 和私密漏洞报告 | Open |
| R-20 | 编辑器永久删除无引用受管背景时，错误的可达性判断可能造成不可恢复的数据损失 | 删除范围只来自当前编辑会话追踪；存储层再次校验可信 DataRoot、精确主题 UUID、普通文件/目录、无重解析点、`theme.json` 当前 `art.file` 引用和空目录条件。共享缓存、索引主题、应用回收站主题、未知孤立目录及完整未索引主题均排除；临时真实文件系统测试覆盖直接删除和保护分支 | Mitigated |
| R-21 | 更新资产被替换、损坏或构造为路径逃逸 ZIP | 只接受三个精确 Release 资产；GitHub asset digest、Schema v3 release manifest 与 SHA256SUMS 必须一致。ZIP 条目数、压缩/展开大小、绝对路径、`..`、ADS、大小写重复路径及链接均 fail-closed | Mitigated |
| R-22 | 自更新在文件锁、断电或新版启动失败时留下不可运行目录 | 外置 Node runner 只接收随机 token；请求通过 Schema v1 JSON 传递。应用目录在同盘以 rename 切换，旧目录保留到新版完成 120 秒健康检查；失败自动回滚。断电及真实杀进程边界仍需隔离 Windows 验收 | Open |
| R-23 | 更新清理误删用户放入便携目录的文件，或持久化 Agent 升级失败 | `app-install-manifest.json` 逐文件记录大小和 SHA-256；只删除旧清单拥有且哈希仍匹配的文件，其他文件移入 Preserved。Agent 使用事务式切换，失败继续运行旧版并提供重试 | Mitigated |
