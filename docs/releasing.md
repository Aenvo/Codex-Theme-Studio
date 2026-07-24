# 便携发布复现与验收

## 固定输入

- Windows 10/11 x64。
- .NET SDK `8.0.423`，运行时补丁 `8.0.29`。
- Node.js Windows x64 `v24.18.0`。
- Node 压缩包 SHA-256：
  `0ae68406b42d7725661da979b1403ec9926da205c6770827f33aac9d8f26e821`。
- 当前维护 Release 版本号：`1.1.7`。

Node Runtime 只从 `https://nodejs.org/download/release/v24.18.0/` 获取。脚本会在解压前校验固定 SHA-256，并在打包前执行 `node.exe --version`。

## 生成发布包

在仓库根目录运行：

```powershell
.\package.ps1
```

未传入 `-Version` 时，脚本以 `Directory.Build.props` 的 `Version` 作为发布版本；显式传入版本仅用于有意覆盖，并且仍必须使用 `major.minor.patch` 格式。

脚本按顺序执行：

1. `build.ps1` 的 Release 构建、测试、格式、自检和 Injector 测试。
2. 下载并校验固定 Node Runtime（已存在且哈希正确时复用缓存）。
3. 发布 Desktop 与 Agent 的 `win-x64` self-contained 目录。
4. 合并相同运行时文件，冲突文件一律失败。
5. 只复制生产 Injector 文件，不包含测试、fixture、日志、数据库或用户数据。
6. 附带第一方 Apache-2.0 `LICENSE`、README、用户指南、自制应用图标、Lucide WPF 矢量资源、第三方 Notices 和对应许可证。
7. 生成 ZIP、`SHA256SUMS.txt` 和 `release-manifest.json`。

脚本不会覆盖已有的同版本发布目录。需要重建同一版本时，应先由维护者将旧产物移到可恢复的归档位置，或使用新的版本号；不要用破坏性清理命令。

完成 ZIP、`SHA256SUMS.txt` 和 `release-manifest.json` 校验后，可以按 `docs/building.md` 中的 `ArchivesOnly` 清理策略回收已解压便携目录，同时保留每个版本的三项归档文件。需要再次检查 EXE 签名或目录内容时，应把对应 ZIP 解压到独立临时目录，不要假定历史发布目录仍保留解压副本。

## 产物

```text
artifacts/release/1.1.7/
├─ CodexThemeManager-1.1.7-win-x64-portable/
├─ CodexThemeManager-1.1.7-win-x64-portable.zip
├─ SHA256SUMS.txt
└─ release-manifest.json
```

便携目录中的主程序是 `CodexThemeManager.exe`，产品元数据名称为 `Codex Theme Studio`。持久化组件位于 `agent/CodexThemeStudio.Agent.exe`，独立子目录用于隔离 WPF Desktop 与非 WPF Agent 的 self-contained 运行时；稳定安装后仍使用已冻结的 Agent 文件名、进程与启动项契约，不代表第二个产品。

## 归档状态

- `1.0.1`、`1.1.0` 至 `1.1.7` 在 `docs/testing/` 中保留对应本地验收记录。
- `1.1.8`、`1.1.9` 和 `1.1.10` 仅保留 ZIP、`SHA256SUMS.txt` 与 `release-manifest.json`，没有对应本地验收记录，属于未验收归档。
- 当前源码维护基线仍为 `1.1.7`。归档版本号高于源码配置不代表源码基线已升级，也不构成签名、病毒扫描、干净环境或真实运行验收。

## 发布前检查

```powershell
Get-Content .\artifacts\release\1.1.7\SHA256SUMS.txt
Get-AuthenticodeSignature `
  .\artifacts\release\1.1.7\CodexThemeManager-1.1.7-win-x64-portable\CodexThemeManager.exe
Get-AuthenticodeSignature `
  .\artifacts\release\1.1.7\CodexThemeManager-1.1.7-win-x64-portable\agent\CodexThemeStudio.Agent.exe
```

该版本预期为 `NotSigned`，必须在用户文档中如实披露。

以上 EXE 命令适用于刚生成、尚未按 `ArchivesOnly` 清理的便携目录。若只保留归档文件，应先校验 ZIP，再解压到项目外的独立临时目录并对其中相同相对路径执行签名、许可证和内容检查；检查结束后按可恢复策略处理临时目录。

同时确认便携目录根部包含第一方 `LICENSE`，`LICENSES/` 包含 `Lucide-LICENSE.txt` 等第三方许可证，并且界面图标不需要联网加载。

病毒扫描使用本机 Microsoft Defender 的自定义扫描：

```powershell
Start-MpScan -ScanType CustomScan -ScanPath `
  .\artifacts\release\1.1.7\CodexThemeManager-1.1.7-win-x64-portable.zip
```

出现检测时停止分发并审查原因，不建议用户关闭安全软件或盲目加白。

## 最终场景

应在无系统 .NET 和 Node 的临时 Windows 用户或干净虚拟机执行：

1. 把 ZIP 解压到同时含空格和中文的路径。
2. 双击主 EXE。
3. 创建主题并导入本地图片。
4. 临时显示、切换和 Restore。
5. 启用持久化，确认启动项和稳定 Agent 路径。
6. 完全退出并重启 Codex，确认主题由 Agent 恢复。
7. 停用持久化，确认启动项移除、Agent 退出和 Codex 还原。
8. 将 DataRoot 迁移到新的空目录，核对报告并确认旧目录保留。
9. 删除程序目录，确认文档所述的数据与 Agent 行为。

验收结论必须区分源码自动化、当前用户现场验证和真正干净环境验证；后两者不能由构建成功替代。
