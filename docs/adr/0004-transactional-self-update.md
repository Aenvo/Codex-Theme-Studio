# ADR 0004：用户触发的事务式自更新

- 状态：Accepted
- 日期：2026-08-06
- 适用版本：1.3.0

## 背景

便携目录没有安装器，运行中的 WPF 与 self-contained Runtime 文件也不能由自身安全替换。项目当前未签名，更新链路不能只依赖单一下载 URL 或普通 ZIP 解压；同时必须保留 Draft Release 与人工公开门禁。

## 决策

- 更新发现位于无 WPF 的 `CodexThemeStudio.Update` 模块。应用就绪 3 秒后检查公开 GitHub 最新稳定 Release，成功结果缓存 12 小时；手动检查绕过缓存。
- 更新只由用户点击“下载更新”触发，不进行后台静默安装。Release Notes 作为受限纯文本显示。
- 只接受便携 ZIP、`release-manifest.json` 与 `SHA256SUMS.txt` 三项资产。GitHub asset digest、Schema v3 release manifest 与 SHA256SUMS 的 ZIP SHA-256 必须一致。
- ZIP 校验拒绝路径逃逸、ADS、大小写重复路径、符号链接、Junction 和超限内容。打包生成 Schema v1 `app-install-manifest.json`，Schema v3 release manifest 记录其 SHA-256。
- 固定 Node Runtime 与 `runtime/updater/apply-update.mjs` 被复制到程序目录外。命令行只传随机 token；路径、版本、哈希和状态经 Schema v1 JSON 交换。
- 新目录先准备在应用同盘。runner 等待旧进程退出，将旧根改名为备份，再原子切换新根。新版在服务、窗口和后台初始化完成后写健康标记；120 秒内失败则恢复旧根并重新启动旧版。
- 成功后只删除旧 install manifest 拥有且当前哈希仍匹配的文件；未知或被修改的文件移入当前用户 Updates/Preserved。清理失败保留旧目录并允许重试。
- 持久化 Agent 在应用更新后事务升级；失败保留并重启旧 Agent，不回滚已健康的 Desktop。
- GitHub workflow 仍只在维护者显式推送精确 tag 后创建 Draft；公开发布继续由维护者人工完成。

## 后果

- `1.2.2` 没有 updater，必须手动安装 `1.3.0`；后续版本才可使用自更新。
- 更新缓存、runner、结果、备份引用和 Preserved 文件成为新的恢复证据，不得用通用缓存清理误删。
- 未签名风险仍存在；完整性校验不能替代代码签名、有效病毒扫描和干净 Windows 验收。
- 原子目录切换、健康超时、回滚、清理重试和 Agent 事务升级必须保留自动化与真实便携目录回归。
