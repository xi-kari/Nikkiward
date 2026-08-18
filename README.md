# Nikkiward

Nikkiward 是面向《无限暖暖》PC 版的非官方社区启动与本地管理工具。项目使用
WinUI 3 构建，计划面向 Windows x64 提供安装包和便携 ZIP，目前尚未公开发布。

当前预览版本：`0.1.0-preview.1`

## 当前范围

- 管理国服官方渠道、哔哩哔哩渠道和 Steam 国际服的独立安装 Profile。
- 展示每个渠道的安装、组件、启动能力和运行状态。
- 提供本地相册、收藏、手账、外观与输入设置。
- 保持渠道认证边界：Steam 登录仍依赖 Steam 客户端，界面不会把内容复用等同于独立登录能力。

Nikkiward 不包含游戏本体、官方启动器、账号凭据或渠道令牌，也不会把未经验证的
启动链路标记为可用。

## 构建

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

相册游戏参数解析使用固定版本的可选原生组件。CI 和正式 Release 在构建前通过
固定提交地址下载并校验 SHA-256：

```powershell
.\build\Fetch-Nuan5Dependency.ps1
```

没有该 DLL 时，Nikkiward 仍可构建和运行，相册只会省略对应的游戏内参数。

## 发布与更新

仓库已配置 `v*` 标签触发的 Release 工作流，计划产出：

- `Nikkiward-win-x64.zip`
- `Nikkiward-update.json`
- `SHA256SUMS.txt`

安装包和便携 ZIP 在公开发布前必须通过干净 Windows 环境、路径、渠道、升级与
卸载测试。完整发布门见 [docs/PACKAGING_ACCEPTANCE.md](docs/PACKAGING_ACCEPTANCE.md)。

当前客户端更新能力只读取 GitHub Releases、比较版本并打开发布页，不会静默下载、
执行或覆盖现有程序。未来自动替换程序前，必须补齐签名清单、独立更新器、启动健康
确认和上一版本回滚。详细契约见 [docs/UPDATE_PROTOCOL.md](docs/UPDATE_PROTOCOL.md)。

## 数据与安全

- [隐私说明](PRIVACY.md)
- [安全策略](SECURITY.md)
- [第三方代码声明](THIRD-PARTY-NOTICES.md)

## 许可与声明

Nikkiward 源代码以 [MIT License](LICENSE) 发布。第三方代码与组件继续适用各自的
许可证和版权声明。

Nikkiward 是独立、非官方的社区项目，与叠纸网络、Infold Games、哔哩哔哩、
Valve 或 Steam 不存在隶属、授权或背书关系。《无限暖暖》名称、图标、角色、美术
及其他游戏素材的权利归其各自权利人。
