# Nikkiward

> 为《无限暖暖》PC 玩家准备的非官方社区启动与本地管理工具。

[![Preview](https://img.shields.io/badge/status-preview--4-cb718b?style=flat-square)](https://github.com/xi-kari/Nikkiward/releases)
[![Windows](https://img.shields.io/badge/platform-Windows%20x64-3d6e66?style=flat-square)](https://github.com/xi-kari/Nikkiward/releases)
[![.NET](https://img.shields.io/badge/.NET-10-5b536d?style=flat-square)](https://dotnet.microsoft.com/)
[![Build](https://github.com/xi-kari/Nikkiward/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/xi-kari/Nikkiward/actions/workflows/build.yml)
[![License](https://img.shields.io/badge/license-MIT-8b6b57?style=flat-square)](LICENSE)

Nikkiward 把多渠道安装档案、启动前检查、游戏内照片和个人记录放进一个安静的暖纸界面里。它的设计重点不是替玩家隐藏复杂度，而是把每个渠道当前的状态、可执行动作和不可用原因讲清楚。GitHub Releases 提供 Windows x64 安装包和便携 ZIP。

**[查看项目介绍页](https://xi-kari.github.io/Nikkiward/)** · **[下载 Releases](https://github.com/xi-kari/Nikkiward/releases)** · **[查看发布门](docs/PACKAGING_ACCEPTANCE.md)**

![Nikkiward preview](docs/assets/hero-blossom.jpg)

## 它解决什么

### 一套入口，三份渠道档案

为国服官方渠道、哔哩哔哩渠道和 Steam 国际服保留独立 Profile。每份档案都展示安装位置、组件状态、启动能力和当前运行状态，切换档案不会把“共用内容”误报成“共用登录”。

### 把本地内容留在本地

- **相册与收藏**：按日期浏览照片，查看基础元数据，复制或定位文件。
- **奇想手账**：保存已同步的本地快照，展示登录天数、游戏时长和衣橱等摘要。
- **心愿共鸣记录**：导入并增量合并记录，重复同步不会覆盖已有历史。
- **外观与输入**：调整背景、界面风格和控制方式，让启动器更像自己的桌面。

## 诚实的能力边界

Nikkiward 是独立的社区工具，不包含游戏本体、官方启动器、账号凭据或渠道令牌。

- Steam 登录仍由 Steam 客户端负责。
- 内容复用、安装档案切换和独立登录是不同能力，界面会分别呈现。
- 更新检查只读取 GitHub Releases 并打开发布页，不会静默下载、执行或覆盖程序。
- 相册原生页面保持只读；需要编辑、批处理或其他重型能力时，交给已安装的外部工具。

## 开始使用

从 [Releases](https://github.com/xi-kari/Nikkiward/releases) 下载 Windows x64 安装包，或选择便携 ZIP。首次启动后，在档案页确认各渠道的安装路径，再从启动页执行对应的预检与操作。

当前预览版本：`0.1.0-preview.4`

## 本地构建

环境要求：

- Windows 10 1809 或更高版本
- .NET SDK 10
- 支持 WinUI 3 / Windows App SDK 的 Visual Studio Build Tools

```powershell
dotnet restore .\Nikkiward.ProfileBuilder.Tests\Nikkiward.ProfileBuilder.Tests.csproj
dotnet run --project .\Nikkiward.ProfileBuilder.Tests\Nikkiward.ProfileBuilder.Tests.csproj -c Release --no-restore

dotnet restore .\Nikkiward\Nikkiward.csproj -r win-x64
dotnet build .\Nikkiward\Nikkiward.csproj -c Debug -p:Platform=x64 -r win-x64 --no-restore
```

相册的游戏参数解析使用固定版本的可选原生组件。CI 和正式 Release 会在构建前下载并校验 SHA-256：

```powershell
.\build\Fetch-Nuan5Dependency.ps1
```

没有该 DLL 时，Nikkiward 仍可构建和运行，相册只会省略对应的游戏内参数。

## 发布产物

推送 `v*` 标签会触发 Release 工作流，生成：

- `Nikkiward-win-x64.zip`
- `Nikkiward-Setup-win-x64.exe`
- `Nikkiward-update.json`
- `SHA256SUMS.txt`

发布前必须通过干净 Windows 环境、路径、渠道、升级和卸载测试。完整验收条件见 [docs/PACKAGING_ACCEPTANCE.md](docs/PACKAGING_ACCEPTANCE.md)，更新协议见 [docs/UPDATE_PROTOCOL.md](docs/UPDATE_PROTOCOL.md)。

## 参与项目

欢迎提交 Issue、改进文案和 UI 建议。请先阅读 [贡献指南](.github/CONTRIBUTING.md)，再选择合适的 Issue 模板或提交 Pull Request。涉及启动链路、渠道认证、网页抓取或外部插件的改动，请同时说明：

1. 实际验证过的环境、版本和渠道；
2. 观察到的结果与仍未验证的部分；
3. 对用户数据、令牌和安装文件的影响。

## 隐私与许可

- [隐私说明](PRIVACY.md)
- [安全策略](SECURITY.md)
- [第三方代码声明](THIRD-PARTY-NOTICES.md)
- [MIT License](LICENSE)

Nikkiward 是独立、非官方的社区项目，与叠纸网络、Infold Games、哔哩哔哩、Valve 或 Steam 不存在隶属、授权或背书关系。《无限暖暖》名称、图标、角色、美术及其他游戏素材的权利归其各自权利人。
