# Codex Theme Studio 用户指南

## 系统要求

- Windows 10 或 Windows 11 x64。
- 当前用户安装且身份校验通过的 Microsoft Store Codex。
- 解压后的普通可写目录。
- 不需要预装 .NET、Node.js、npm 或 npx；正常使用不需要管理员权限。

本产品不是 OpenAI 官方产品。它只在当前用户的本机环境内管理主题。

## 下载、校验、解压和首次启动

1. 下载 `CodexThemeManager-<version>-win-x64-portable.zip` 与 `SHA256SUMS.txt`。
2. 在 PowerShell 中运行：

   ```powershell
   Get-FileHash .\CodexThemeManager-<version>-win-x64-portable.zip -Algorithm SHA256
   ```

3. 确认输出与 `SHA256SUMS.txt` 完全相同。
4. 完整解压 ZIP，不要直接从压缩包内运行。
5. 双击 `CodexThemeManager.exe`。

该版本未进行代码签名，Windows 可能显示未知发布者警告。哈希不一致时不要运行，也不要通过关闭安全软件或盲目加入白名单来绕过警告。

首次启动会明确采用 `%LOCALAPPDATA%\CodexThemeStudio\Data` 作为默认 DataRoot，创建必要的 SQLite 索引和资源目录。首次启动不会启用持久化、不会创建启动项、不会修改 Codex，也不会自动联网。

## 创建和编辑主题

1. 在“主题库”选择“新建主题”。
2. 输入名称并进入编辑页。
3. 选择 PNG、JPEG 或 WebP 背景。
4. 调整颜色、焦点、安全区、布局、首页/任务页透明度、遮罩和模糊。
5. 保存主题或另存副本。

模拟预览只用于编辑参考，不代表当前 Codex 版本已通过真实渲染验证。图片最大 16 MB、单边最大 16384 像素、总像素不超过 5000 万；SVG 不受支持。

## 导入和导出

- “导入主题”选择 `.cttheme` 文件。应用会校验 Schema、条目白名单、路径、数量、大小和 SHA-256 后再写入 DataRoot。
- 选中主题后使用“导出 .cttheme”。导出包只包含声明式主题 JSON 和受管图片，不包含 JavaScript、命令、HTML、远端 CSS、日志、凭证或绝对路径。
- 未知高版本 Schema 会被拒绝，不会由旧版本覆盖。

## 临时显示和切换主题

1. 先启动受支持的 Codex。
2. 在主题库选中主题，点击“临时显示”。
3. 选中另一个主题并再次临时显示即可切换。

临时显示只影响当前 Codex 实例，不创建启动项，不启动持久化 Agent。Codex 完全退出后效果消失。

## 启用持久化

1. 选中要持久化的主题。
2. 点击“设为持久主题”。
3. 阅读确认信息后继续。

应用会在 `%LOCALAPPDATA%\CodexThemeStudio\Agent\versions\<SHA-256>` 安装内容寻址的稳定 Agent 副本，并创建当前用户启动项：

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
CodexThemeStudio.PersistenceAgent
```

默认快照位于 `%LOCALAPPDATA%\CodexThemeStudio\Runtime\Persistence`，配置位于 `%LOCALAPPDATA%\CodexThemeStudio\Agent\config.json`。Agent 只在发现新的可信 Codex PID 时短时应用主题；GUI 可以退出。

## 还原 Codex 外观

点击“还原 Codex 外观”会清理当前 Codex 运行时主题、停止当前进程内未来重注并关闭 Inspector，但不会删除主题库、应用设置或启动项。

如果持久化仍然启用，后续新的 Codex 实例仍会应用持久主题。要停止后续自动应用，请执行“停用持久化”。

## 停用持久化

点击“停用持久化”并确认。应用会：

- 移除当前用户启动项。
- 通知 Agent 停止。
- 还原当前 Codex。
- 清除“当前持久主题”标记。

主题库、历史快照版本和有限诊断日志会保留。停用持久化不等于删除用户数据。

## 更改数据位置

1. 在存储区域查看当前 DataRoot。
2. 点击“迁移数据”并选择不存在或为空的目标目录。
3. 确认源目录和目标目录。
4. 等待复制、文件数量/大小/SHA-256 校验、SQLite 完整性检查和定位切换完成。
5. 按提示重启应用。

迁移成功后原 DataRoot 保持原样，作为恢复点。迁移失败不会切换定位，也不会删除旧数据。外置磁盘离线时应用会 fail-closed，不会静默创建新的默认空库。

## 便携模式与程序目录

“便携”表示程序目录解压即用且可以移动，不表示用户数据随 EXE 存放。第一版不支持 `portable.flag` 或程序目录内 `data/` 优先级。

删除程序目录：

- 不会自动删除 DataRoot。
- 不会自动停用已启用的稳定 Agent。
- 不会自动移除当前用户启动项。

因此在删除程序目录前应先从应用执行“停用持久化”，再退出应用。

## 数据和日志位置

默认位置：

```text
%LOCALAPPDATA%\CodexThemeStudio\
├─ bootstrap.json
├─ Data\
├─ Agent\
├─ Runtime\
└─ Logs\agent.jsonl
```

`bootstrap.json` 记录实际 DataRoot。DataRoot 内包含数据库、主题、缓存、导出暂存和运行状态。日志采用有限大小与最少必要原则，不记录对话正文、账号、Token 或完整页面 URL。

## 完整清理

完整清理是不可逆操作，先导出需要保留的主题并确认备份：

1. 在应用内执行“停用持久化”。
2. 确认 Codex 已还原并退出 Theme Studio。
3. 删除解压后的程序目录。
4. 如确定不再保留主题，再由用户手动删除 `bootstrap.json` 指向的 DataRoot。
5. 如确定不保留 Agent 历史、快照和日志，再由用户手动删除 `%LOCALAPPDATA%\CodexThemeStudio` 的剩余内容。
6. 检查上述 Run 项已不存在。

不要把“还原外观”“停用持久化”和“删除主题库”当成同一个操作。旧迁移目录和内容寻址 Agent 版本不会由应用自动永久删除。

## Codex 更新后的诊断

如果 Codex 更新后主题不再生效：

1. 先点击“还原 Codex 外观”。
2. 在状态区域查看是否显示“Codex 版本未验证”或身份校验失败。
3. 确认使用的是当前用户的 Microsoft Store Codex，而不是其他安装来源。
4. 完全退出并重新启动 Codex，再刷新状态。
5. 不要修改 `WindowsApps`、`app.asar`、Codex EXE 或签名，也不要长期开放 Inspector。
6. 保留 Theme Studio 版本、Codex 版本、操作时间和脱敏后的错误代码用于诊断；不要附带真实对话截图或凭证。

未知 Codex 版本默认 fail-closed。版本号相同但 DOM 结构变化时也可能拒绝注入，这是安全边界而不是需要绕过的错误。

## 已知限制

- 当前真实验证的 Codex 版本为 Microsoft Store `26.715.4045.0` x64。
- 不支持非 Store 安装、多个 Codex 主进程、未知版本、非回环 Inspector 或不完整主窗口。
- 当前版本未签名，未提供安装器、自动更新、在线主题商店或远端主题源。
- 普通任务页真实渲染、刷新恢复、窗口隔离和 Restore 已验证；不同 Codex 更新仍需重新做兼容性验收。
- 程序目录可移动，但自定义 DataRoot 和稳定 Agent 是独立位置。
