# 任务 12 最终验收结果

> 历史发布证据：本文对应本地版本 1.0.0。后续源码和 1.0.1 发布状态以最新维护验收记录为准。

## 结论

版本 `1.0.0` 的 Windows x64 self-contained 便携目录与 ZIP 已生成。Release 构建、自动化测试、固定 Runtime、自检、哈希、发布内容、中文/空格路径启动、真实持久化 enable/disable 与最终清理状态均通过。

独立干净 Windows 用户/虚拟机、承载本验收会话的 Codex 完全重启，以及有效的本机病毒扫描未覆盖，不能声明为通过；详见[任务 12 已知限制](../risks/task-12-known-issues.md)。

## 自动化验证

- .NET Release 构建：0 警告、0 错误。
- .NET：122 通过；2 个现场持久化测试在普通全量运行中按设计跳过。
- Node Injector：23 通过。
- `dotnet format --verify-no-changes`：通过。
- Agent `self-test`：通过。
- Injector `self-test`：通过，报告 Node `v24.18.0`。
- 新增跨线程 Inspector 操作锁回归测试：通过。

## 便携产物

| 项目 | 结果 |
| --- | --- |
| 版本 | `1.0.0` |
| RID | `win-x64` |
| 目录文件数 | 691 |
| 目录总大小 | 365,821,021 B |
| ZIP 大小 | 151,374,519 B |
| ZIP SHA-256 | `775205b0adb46e0219d6d655733a71d1453f85882d1276812bcac10c0732b239` |
| 主 EXE SHA-256 | `4c62dea2db721e7c05cf81116d22ae165ad9a0f030b57edb8e9b20c329528c58` |
| Agent EXE SHA-256 | `f4af65012c31b17bb3864bc0086bb63049d3454b774da57b7b9182bae81092a5` |
| 主 EXE 签名 | `NotSigned` |
| Agent EXE 签名 | `NotSigned` |

重新计算的哈希、文件数和字节数均与 `release-manifest.json`、`SHA256SUMS.txt` 一致。

## 发布内容检查

- 未发现 `.pdb`、测试文件、fixture、截图、日志、数据库、`.cttheme` 或私人图片。
- 文本扫描未发现开发机用户路径、用户名或凭证形式的值。
- 仅打包生产 Injector 文件。
- 固定 Node Runtime 为 `v24.18.0`，下载压缩包 SHA-256 在解压前校验。
- README、用户指南、构建信息、Notices 和许可证均存在。
- 产品元数据为 `Codex Theme Studio`，文件版本 `1.0.0.0`，产品版本 `1.0.0`。
- 自制抽象图标已写入主 EXE，不使用 OpenAI/Codex 官方标志。

## 路径与启动

最终 ZIP 已解压到同时包含中文和空格的验证路径。`CodexThemeManager.exe` 在无项目工作目录依赖的情况下成功启动，进程名为 `CodexThemeManager`，并创建了真实主窗口；随包 Node 报告 `v24.18.0`。

该验证证明最终解压目录可以直接启动，但当前 Windows 用户仍安装有开发 SDK，因此不能替代真正无 .NET SDK/Node 的干净用户验收。

## 真实持久化

使用最终便携包执行了显式现场测试：

1. enable 成功。
2. 当前用户 Run 值已创建。
3. 稳定 Agent 已启动。
4. 稳定 Agent 使用 Node `v24.18.0`。
5. Inspector 端口无残留监听。
6. disable 成功。
7. Run 值已移除。
8. Agent 进程数恢复为 0。
9. 配置恢复为 `enabled=false`。
10. Inspector 端口监听数为 0。

现场测试首次发现 Inspector 操作锁跨 `await` 释放线程不一致的问题；实现已改为可跨线程释放的命名 Semaphore，并加入回归测试。修复后的 enable/disable 均通过。

测试前对现有 4 个 Agent 运行文件建立并校验了恢复副本。最终没有执行回滚或清理恢复副本。

## 存储与还原

- DataRoot 迁移的复制、数量/大小/SHA-256、SQLite 完整性、失败回滚、离线和取消路径由真实文件系统自动化测试覆盖。
- 本次没有迁移当前用户正在使用的实际 DataRoot。
- Restore 在真实 disable 路径中完成；最终 Run、Agent 和 Inspector 状态均为关闭。
- 没有删除程序目录、用户 DataRoot、旧迁移目录或 Agent 历史版本。

## 病毒扫描

调用 Microsoft Defender 自定义扫描失败，系统报告 Defender Antivirus 与实时保护均未启用（错误 `0x800106ba`）。Windows Security Center 记录了另一款本机防病毒产品，但本任务没有可审计的非交互命令行扫描入口，因此没有把“未发现历史检测”误报为“本次扫描通过”。
