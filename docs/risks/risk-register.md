# 当前风险登记表

- 更新日期：2026-07-24
- 适用基线：Codex Theme Studio 1.1.7 本地维护发布
- 历史任务风险和当时证据保留在 `task-11-known-issues.md`、`task-12-known-issues.md` 与对应验收记录中。

| ID | 风险 | 当前控制或证据 | 状态 |
| --- | --- | --- | --- |
| R-01 | .NET 8 将于 2026-11-10 结束支持 | SDK 固定为 8.0.423，发布为 self-contained；跨越 EOL 前必须用新 ADR 评估迁移到受支持 LTS | Open |
| R-02 | 便携包未签名 | manifest 和 SHA256SUMS 提供完整性校验，README 明确披露未知发布者警告；不建议绕过安全软件 | Open |
| R-03 | 本机没有有效病毒扫描证据 | Defender 调用曾返回 `0x800106ba`；公开分发前必须在启用且签名库最新的防病毒环境复扫 | Open |
| R-04 | 未在无开发 Runtime 的干净 Windows 用户或 VM 验收 | self-contained 文件、随包 Node 和中文/空格路径启动分别验证；仍不能替代干净环境 | Open |
| R-05 | 当前 Codex 完全重启后的 Agent 恢复未在本会话复验 | enable/disable、当前 PID 和自动化新 PID/PID 复用路径已有证据；完整宿主重启仍未覆盖 | Open |
| R-06 | 真实外置磁盘 DataRoot 迁移未覆盖 | 临时真实文件系统测试覆盖复制、哈希、SQLite 完整性、离线、取消和回滚；物理断连仍未覆盖 | Open |
| R-07 | 能力探测可能无法覆盖未来 Codex 或第三方构建的全部行为差异 | 当前真实证据覆盖 Store Codex `26.715.4045.0`、`26.715.10079.0` 与 `26.721.3404.0` x64；`26.721.3404.0` 的应用内更新将 Electron 从 `150.0.7871.124` 更新到 `150.0.7871.128`，完整能力探测与首次应用/清理/重应用闭环通过。能力缺失和清理残留仍 fail-closed；非 Store 真实实例验证为 `To be confirmed` | Open |
| R-08 | 第一方与第三方许可证范围可能混淆 | 根目录 `LICENSE` 将第一方源码声明为 Apache-2.0；README 与第三方 Notices 分别说明适用范围和商标边界 | Mitigated |
| R-09 | 图片内容寻址缓存并发提交可能竞态 | 1.0.1 改为缓存先提交、主题资源最后提交，并对移动冲突进行有限重试和哈希复核；由并发压力回归覆盖 | Mitigated |
| R-10 | 打包成功或失败残留大型工作目录 | 1.0.1 使用独立暂存发布目录，成功后回收本次工作目录，失败保留并报告诊断路径 | Mitigated |
| R-11 | 构建使用系统 Node 导致与发布 Runtime 漂移 | `build.ps1` 和 `package.ps1` 共同读取 `eng/runtime-baseline.json`，严格要求 Node `v24.18.0` | Mitigated |
| R-12 | 本地生成物和历史验证副本占用大量空间 | 保留源码、固定 Node 缓存及各版本 ZIP/校验和/manifest；回收 build/work/validation 和已解压发布目录。批量清理前记录 Git 基线、建立项目外校验恢复副本并使用可恢复机制 | Mitigated |
| R-13 | Injector 运行脚本误用 PowerShell 7/.NET Core API，或原子写入在句柄释放前移动文件 | 运行时发现固定以 Windows PowerShell 5.1 为最低基线；自动化测试真实执行精确 EXE `Discover` 和边界 `Snapshot` 并解析单一 JSON。资格、目标选择及其他原子写入均以流作用域结束后再 `Move/Replace`，关键路径有跨实例读取回归 | Mitigated |
| R-14 | 统一“还原外观”需要修改第三方 OkkSkin 的当前用户启动项、状态和 Agent；身份误判可能影响无关进程，部分失败可能导致下次 Codex 再次应用主题 | 仅接受无 Reparse Point 的已知状态与启动器、精确 Run 命令和精确 `node.exe … agent.mjs` 命令行；状态原子改为禁用并保留未知字段和缓存；任一残留返回 Partial。自动化边界测试已加入，真实 Codex 完整重启验收仍为 `To be confirmed` | Open |
| R-15 | 启动兼容缓存可能被误解为当前进程、窗口或可见效果已经验证 | Schema v2 只缓存构建级资格；启动始终重新发现并计算 EXE SHA-256，不缓存 PID、端口、Target、Renderer 或活动主题；应用与持久化继续执行操作级实时 fail-closed 校验 | Mitigated |
| R-16 | 高于源码基线的归档版本可能被误认为已验收发布 | `Directory.Build.props` 继续固定维护基线 `1.1.7`；发布文档和交接 manifest 将 `1.1.8`–`1.1.10` 明确标为无对应验收记录的归档，不据此声明发布通过 | Mitigated |
