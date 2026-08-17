# Security Policy

## Supported Versions

安全修复只面向最新的稳定版和最新的预览版。旧版本可能不再接收修复。

## Reporting a Vulnerability

请优先使用 GitHub 仓库的 Private vulnerability reporting / Security Advisory 提交
安全问题。不要在公开 Issue 中粘贴账号凭据、Cookie、渠道令牌、完整诊断包、私人
照片或本机目录信息。

报告应尽量包含：

- 受影响的 Nikkiward 版本和架构
- 可重复的最小步骤
- 预期结果与实际结果
- 已脱敏的日志或堆栈
- 对本地文件、更新链路或账号会话的影响

## Update Trust Boundary

当前版本只检查公开更新并打开 Release 页面，不执行自替换。Release 包同时发布
SHA-256 清单。未来自动更新只有在实现清单签名、包长度与 SHA-256 校验、发布者验证、
独立更新进程、启动健康确认和回滚后才会启用。

## Scope

以下内容应分别报告给对应维护者：

- 《无限暖暖》游戏或官方启动器自身的漏洞
- Steam、哔哩哔哩或其他渠道平台的账号与支付问题
- 上游第三方组件中能够独立复现的问题
