# Third-party notices

Codex Theme Studio 是独立实现的非 OpenAI 官方产品。发布包不包含参考项目 `Codex Dream Skin`、`okkmax-web` 或 `okkskin` 的源码、CSS、脚本或素材。

除另有说明外，本项目第一方源码依据仓库根目录的 Apache License 2.0 提供。本文件只记录第三方组件、素材及其许可证，不改变或替代任何第三方条款。

## 随发布包分发的运行组件

| 组件 | 版本 | 许可证 |
| --- | --- | --- |
| Microsoft .NET / Windows Desktop Runtime | 8.0.29 | MIT 与 .NET Runtime 第三方 Notices |
| Node.js Windows x64 Runtime | 24.18.0 | Node.js License（MIT 与随附第三方条款） |
| Microsoft.Data.Sqlite | 8.0.29 | MIT |
| Microsoft.Data.Sqlite.Core | 8.0.29 | MIT |
| SQLitePCLRaw.bundle_e_sqlite3 | 3.0.3 | Apache-2.0 |
| SQLitePCLRaw.config.e_sqlite3 | 3.0.3 | Apache-2.0 |
| SQLitePCLRaw.core | 3.0.3 | Apache-2.0 |
| SQLitePCLRaw.provider.e_sqlite3 | 3.0.3 | Apache-2.0 |
| SourceGear.sqlite3（SQLite 原生二进制） | 3.50.4.5 | SQLite blessing / public-domain dedication，见随包文本 |
| SkiaSharp | 4.150.1 | MIT |
| SkiaSharp.NativeAssets.Win32 | 4.150.1 | MIT；另含 Skia 第三方 Notices |
| Lucide Icons（15 个内置 WPF 矢量图标） | 2026-07-23 固定快照 | ISC；部分 Feather 衍生图标适用 MIT |
| Avalonia UI Desktop / Native / Skia / Themes.Fluent | 12.1.0 | MIT；macOS UI 产品化依赖 |

实际发布包的 `LICENSES/` 目录包含：

- `dotnet-LICENSE.txt` 与 `dotnet-THIRD-PARTY-NOTICES.txt`
- `Node.js-LICENSE.txt`
- `MIT.txt`
- `Apache-2.0.txt`
- `SourceGear.sqlite3-LICENSE.txt`
- `SkiaSharp-LICENSE.txt` 与 `SkiaSharp-THIRD-PARTY-NOTICES.txt`
- `Lucide-LICENSE.txt`

界面图标通过 `icons0/i0` 检索并从 Lucide 集合导出，随后转换为仓库内固定的原生 WPF Geometry。发布包不包含 `i0` 服务端、React 组件、Iconify 数据库或在线图标加载代码；图标运行时不访问网络。

## 仅开发和测试使用

以下组件不会作为应用运行依赖打入便携目录：

| 组件 | 版本 | 许可证 |
| --- | --- | --- |
| Microsoft.NET.Test.Sdk / TestPlatform / CodeCoverage | 18.8.1 | MIT |
| xunit / xunit.assert / xunit.core / xunit.extensibility.* | 2.9.3 | Apache-2.0 |
| xunit.runner.visualstudio | 3.1.5 | Apache-2.0 |
| xunit.abstractions | 2.0.3 | xUnit License（MIT 风格） |
| xunit.analyzers | 1.18.0 | Apache-2.0 |
| System.Collections.Immutable | 8.0.0 | MIT |
| System.Reflection.Metadata | 8.0.0 | MIT |
| SkiaSharp.NativeAssets.macOS | 4.150.1 | MIT（锁文件中的跨平台传递依赖，未打入 win-x64 发布包） |
| Avalonia.Headless | 12.1.0 | MIT（仅 macOS UI 布局、绑定与输入测试） |

许可证名称来自锁定的 NuGet 包元数据和上游随附许可证。各许可证只适用于对应的第三方组件，不表示 OpenAI 或任何第三方对 Codex Theme Studio 提供背书。
