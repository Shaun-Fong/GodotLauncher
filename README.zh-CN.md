![项目页（深色主题）](docs/images/projects.png)

<p align="center">
  <a href="README.md">ENGLISH</a> | 中文
</p>

<p align="center">
  <a href="https://github.com/Shaun-Fong/GodotLauncher/actions/workflows/build.yml"><img src="https://github.com/Shaun-Fong/GodotLauncher/actions/workflows/build.yml/badge.svg" alt="build" /></a>
</p>

<p align="center">
一个面向 Windows、Linux 和 macOS 的轻量、快速的 <a href="https://godotengine.org/">Godot Engine</a> 桌面启动器。
</p>

## 功能

- **项目库** —— 打开过的 Godot 项目会被记住，并记录使用的编辑器版本。可通过文件夹选择器新建项目、
  导入已有的 `project.godot`、在每一行用下拉框选择引擎版本，点击即可打开。当项目要求的版本尚未安装时，
  该行会给出提示。
- **完整的版本目录** —— 包含 Godot 4.x 的 stable、RC、beta、dev 版本以及 3.x stable。列表缓存在本地，
  在任何网络请求之前就先渲染，并在后台每 6 小时刷新一次。新版本会显示 `NEW` 徽标；开启对应通道的
  通知后，应用内还会显示横幅提示。
- **离线优先** —— 有缓存时即使没有网络也能立即显示版本列表和已安装编辑器，并使用
  `ETag`/`If-None-Match` 条件请求降低刷新开销；当上游返回的目录明显残缺时，会保留旧缓存而不是覆盖它。
- **并排安装** —— 每个版本都有独立目录，位于 `editors/std` 或 `editors/dotnet` 下。自动为当前平台
  解析 Standard 与 .NET 版本，以及 x86_64、x86_32、ARM64、ARM32 架构。
- **断点续传下载** —— 使用 `.part` 文件和 HTTP Range 请求，显示速度与剩余时间，支持取消、重试，
  重启后仍可继续。
- **已安装管理** —— 启动、置顶、设置别名、设为默认、编辑每个版本的启动参数、打开安装目录、卸载、
  清理旧预览版，或登记手动下载的编辑器。
- **启动方式** —— 编辑器以分离进程启动，启动器默认隐藏（可配置）而不阻塞；Windows 上可选显示
  控制台窗口（使用 `_console.exe`）。
- **托盘与单实例** —— 关闭窗口后驻留托盘，默认编辑器和最近使用的 5 个版本一键可达；再次启动
  只会唤回已有窗口，不会开启第二份。
- **主题与多语言** —— 支持亮色、暗色或跟随系统，启动时不会白屏闪烁；内置英文和简体中文。
  新增一门语言只需往 `src/GodotLauncher.App/Localization` 放一个 JSON 文件。
- **网络选项** —— 可跟随系统代理、直连，或填写自定义 HTTP 代理。版本刷新、下载和更新检查都使用
  同一设置；刷新失败时显示非阻塞提示，并提供跳转到网络设置的入口。
- **启动器更新** —— 从 GitHub Releases 检查新版本，下载进度与实时速度直接显示在更新按钮上，再次点击
  可取消；下载完成后自动重启完成更新。若 GitHub API 触发速率限制，会改用发布页面继续检查。
- **可编辑配置** —— 人类可读的 TOML 配置、原子写入；配置损坏时会自动备份并重建，而不是崩溃。

## 下载

预构建的压缩包发布在 [Releases](https://github.com/Shaun-Fong/GodotLauncher/releases) 页面：

| 平台 | 压缩包 |
| --- | --- |
| Windows x64 | `GodotLauncher-win-x64.zip` |
| Linux x64 | `GodotLauncher-linux-x64.tar.gz` |
| macOS Intel | `GodotLauncher-osx-x64.tar.gz` |
| macOS Apple silicon | `GodotLauncher-osx-arm64.tar.gz` |

这些构建是自包含的，不需要额外安装 .NET。每个版本还附带 `SHA256SUMS.txt`。目前尚未做代码签名，
因此 Windows SmartScreen 和 macOS Gatekeeper 可能在首次运行时提示。

## 从源码构建

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)。Avalonia 界面可在 Windows、Linux 和 macOS 上构建。

```bash
dotnet build GodotLauncher.slnx
dotnet run --project src/GodotLauncher.App
```

运行测试：

```bash
dotnet test tests/GodotLauncher.Core.Tests/GodotLauncher.Core.Tests.csproj
```

生成自包含单文件版本：

```bash
dotnet publish src/GodotLauncher.App/GodotLauncher.App.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=true
```

## 命令行参数

| 参数 | 说明 |
| --- | --- |
| `--background` | 隐藏启动并驻留托盘，供开机自启使用。 |
| `--bench` | 测量冷启动到首帧的耗时，把 `startup-ms=<n>` 写入日志后退出。 |

## 文件位置

| 路径 | 内容 |
| --- | --- |
| `%APPDATA%\GodotLauncher`（Windows） | 数据目录 |
| `~/Library/Application Support/GodotLauncher`（macOS） | 数据目录 |
| `$XDG_DATA_HOME/GodotLauncher`（Linux） | 数据目录 |
| `config.toml` | 设置，首次保存时创建 |
| `installed.json` | 已安装编辑器登记表 |
| `projects.json` | 项目库 |
| `catalog-cache.json` | 最近一次获取的版本目录 |
| `logs/launcher.log` | 滚动日志，2 MB × 3 个文件 |
| `editors/<std\|dotnet>/<version-id>/` | 已安装的编辑器（位置可配置） |
| `downloads/`、`.staging/` | 下载中的压缩包与解压暂存目录 |

## 当前状态

当前版本为 0.5.0。已实现：版本目录、下载与安装、启动编辑器、项目库、已安装管理、托盘、主题、
多语言、设置、代理支持、启动器自更新、单实例和可选开机自启。

已知差距记录在 [docs/DECISIONS.md](docs/DECISIONS.md#known-gaps)，包括冷启动约 0.9 秒（目标是 300 毫秒），
以及校验和验证目前仅在发布方提供摘要时才会执行。

## 文档

- [架构说明](docs/ARCHITECTURE.md) —— 项目结构、数据流和存储布局。
- [设计决策](docs/DECISIONS.md) —— 技术选型、数据来源与取舍。

## 许可证

[MIT](LICENSE)
