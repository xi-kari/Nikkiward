# 参与贡献

感谢你帮助改进 Nikkiward。请先搜索现有 Issue，避免重复提交；较大的改动可以先开 Issue 讨论用户场景。

## 提交问题

- 功能问题请使用「问题报告」模板，提供版本、渠道、最小复现步骤和实际结果。
- 功能建议请使用「功能建议」模板，描述要解决的玩家场景和期望结果。
- 安全问题请按照 [安全策略](../SECURITY.md) 私密提交。

提交截图、日志或诊断信息前，请移除账号名、Cookie、令牌、完整本地路径和私人照片。

## 提交代码

1. 从 `main` 创建分支，并保持每个提交只解决一个主题。
2. 修改后运行与变更相关的测试；涉及 WinUI 或安装流程时，在 Windows x64 上验证用户可见路径。
3. 更新用户可见行为时同步更新 README、隐私说明或发布文档。
4. 创建 Pull Request，填写变更内容、验证命令与发布影响。

## 本地验证

```powershell
dotnet restore .\Nikkiward.ProfileBuilder.Tests\Nikkiward.ProfileBuilder.Tests.csproj
dotnet run --project .\Nikkiward.ProfileBuilder.Tests\Nikkiward.ProfileBuilder.Tests.csproj -c Release --no-restore

dotnet restore .\Nikkiward\Nikkiward.csproj -r win-x64
dotnet build .\Nikkiward\Nikkiward.csproj -c Debug -p:Platform=x64 -r win-x64 --no-restore
```

请不要提交构建输出、个人配置、诊断包、游戏本体或任何凭据。
