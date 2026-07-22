# Third-party notices

Codex Theme Studio 是独立实现的非 OpenAI 官方产品。发布包不包含参考项目 `Codex Dream Skin`、`okkmax-web` 或 `okkskin` 的源码、CSS、脚本或素材。

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

实际发布包的 `LICENSES/` 目录包含：

- `dotnet-LICENSE.txt` 与 `dotnet-THIRD-PARTY-NOTICES.txt`
- `Node.js-LICENSE.txt`
- `MIT.txt`
- `Apache-2.0.txt`
- `SourceGear.sqlite3-LICENSE.txt`
- `SkiaSharp-LICENSE.txt` 与 `SkiaSharp-THIRD-PARTY-NOTICES.txt`

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

许可证名称来自锁定的 NuGet 包元数据和上游随附许可证。各许可证只适用于对应的第三方组件，不表示 OpenAI 或任何第三方对 Codex Theme Studio 提供背书。
