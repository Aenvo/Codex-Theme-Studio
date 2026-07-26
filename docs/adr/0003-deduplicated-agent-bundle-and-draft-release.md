# ADR 0003：Agent 运行时去重与 Draft Release

- 状态：Accepted
- 日期：2026-07-25
- 适用版本：1.2.0

## 背景

Desktop 与持久化 Agent 都以 Windows x64 self-contained 方式发布。1.1.7
便携 ZIP 中两者有 189 个内容相同的运行时文件，重复压缩体积约 39.6 MB。
稳定 Agent 又必须在便携目录移动或删除后继续运行，不能简单依赖原程序目录。

项目准备通过私有 GitHub 仓库验证自动发布，但当前版本未签名，也没有有效的
公开分发病毒扫描证据。

## 决策

- `package.ps1` 继续分别生成 Desktop 和 Agent 的 self-contained 暂存输出。
- 最终便携包只保存一份路径、大小和 SHA-256 均一致的共享运行时文件。
- `agent/agent-bundle-manifest.json` 使用 Schema v1，记录安装源、安装目标、
  字节数和 SHA-256；Agent 独有文件仍位于 `agent/`。
- 启用持久化时，安装器严格校验 manifest，从便携包组合出内容寻址的完整
  Agent 版本目录。完成验证和原子目录切换前不更新启动项。
- 未知 Schema、绝对路径、路径逃逸、重复目标、重解析点、缺失文件、大小或
  哈希不匹配均 fail-closed。
- 发布包仅保留简体中文卫星资源和根目录中性英文资源。
- 首选 ZIP 上限为 100,000,000 字节；超过该值警告，超过 120,000,000 字节
  阻断打包。
- GitHub Actions 对标签构建只创建 Draft Release。有效病毒扫描、哈希、
  未签名披露和干净环境复核完成前不得公开 Release。

## 后果

- 下载体积减少，同时稳定 Agent 仍可脱离便携目录运行。
- 便携包内的 `agent/` 子目录本身不再是完整运行目录；manifest 和包根共享
  文件共同构成安装源。
- manifest 成为发布与持久化安装的安全契约，必须保留专门回归测试。
- 当前仍保留固定 Node Runtime；本决策不引入联网按需下载或 Injector 重写。
