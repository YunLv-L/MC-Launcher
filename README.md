# MC-Launcher

一个基于 **C#** 和 **Avalonia UI** 构建的现代化、原生 Minecraft 下载器与启动器。

[![Build MC-Launcher](https://github.com/YunLv-L/MC-Launcher/actions/workflows/build.yml/badge.svg)](https://github.com/YunLv-L/MC-Launcher/actions/workflows/build.yml)

## 📖 简介

**MC-Launcher** 致力于提供一个轻量、快速、且无需依赖浏览器的原生 Minecraft 启动体验。它放弃了基于 WebView（如 Electron/Tauri）的臃肿方案，采用 Avalonia 构建原生跨平台界面，完美契合 Windows 11 的 Fluent Design 风格。

> **🚧 当前状态：** 项目处于早期开发阶段，目前仅实现了**游戏核心文件及资源下载**功能，尚未接入启动逻辑。

## ✨ 核心特性

- **原生 UI 体验**：使用 Avalonia 构建，支持硬件加速与原生 Fluent 主题，告别 WebView 的高内存占用。
- **轻量且纯净**：通过 GitHub Actions 编译为**单文件、自包含 (Self-contained)** 的 `.exe`，目标机器无需预装 .NET 运行时。
- **安全可靠的下载**：基于 Mojang 官方 Piston Meta API 解析版本，所有下载文件均强制进行 **SHA1 哈希校验**，杜绝文件损坏。
- **自动化的构建链路**：完全依赖 GitHub Actions 进行 CI/CD，推送代码即产出最新可执行文件。

## 🗺️ 开发进度 (Roadmap)

- [x] 初始化 Avalonia UI 框架与现代化界面
- [x] 获取官方 Version Manifest 并解析指定版本的 JSON
- [x] 下载 Client.jar 与 Libraries 依赖库（自动过滤适用平台）
- [x] 下载 Assets 资源文件与索引 (Asset Index)
- [x] 文件完整性与 SHA1 校验
- [ ] 原生库 (Natives) 的下载与解压逻辑
- [ ] Java 运行环境 (JRE) 的本地检测与自动下载
- [ ] 拼装启动参数并拉起游戏进程 (`CreateProcess`)

## 🚀 如何使用 (下载最新构建)

由于项目尚未发布正式的 Release 版本，你可以直接通过 GitHub Actions 获取最新的测试版：

1. 点击仓库顶部的 **[Actions](https://github.com/YunLv-L/MC-Launcher/actions)** 标签页。
2. 在左侧选择 **Build MC-Launcher**，点击最新一次成功运行（带有绿色勾 ✅）的记录。
3. 滚动到页面底部的 **Artifacts** 区域。
4. 下载 **`MC-Launcher-win-x64`** 压缩包。
5. 解压后，双击运行 `MC-Launcher.exe` 即可。

## 🛠️ 本地开发与构建

如果你希望参与开发，你需要安装 [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

```bash
# 克隆仓库
git clone https://github.com/YunLv-L/MC-Launcher.git
cd MC-Launcher

# 本地运行调试
dotnet run --project MCLauncher.csproj

# 本地打包单文件 exe (输出至 publish 目录)
dotnet publish MCLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

## 💻 技术栈

- **UI 框架**：[Avalonia UI 11](https://avaloniaui.net/) (Fluent Theme)
- **开发语言**：C# 12 / .NET 8
- **网络与序列化**：`System.Net.Http` / `System.Text.Json`
- **构建部署**：GitHub Actions

## ⚠️ 免责声明 (Disclaimer)

1. 本项目为开源学习项目，与 Mojang Studios 或 Microsoft 没有任何从属关系。
2. **版权提醒**：内置游戏下载分发功能可能在部分地区面临合规风险。若将本项目用于商业或公开分发，请务必遵守当地法律法规及 Minecraft EULA 协议。

## 📄 许可证

本项目基于 [MIT License](LICENSE) 开源。
