# Nikkiward 更新协议

## 目标

更新链路分成三个相互独立的层次：

1. GitHub 源代码同步与 CI 构建。
2. GitHub Release 便携包和不可变更新清单。
3. 客户端检查、下载、验证、切换与回滚。

源码推送成功不等于客户端已经获得可安装更新。只有 Release 资产完整发布后，客户端
才能发现新版本。

## 当前阶段

`0.1.x` 只实现只读检查：

1. 从 GitHub Releases API 选择稳定版或预览版。
2. 读取该 Release 中的 `Nikkiward-update.json`。
3. 校验清单结构、通道、语义版本、包长度和 SHA-256 格式。
4. 比较当前程序集版本。
5. 显示结果，并由用户主动打开 Release 页面。

客户端不会下载、执行或覆盖程序。清单的哈希在这一阶段用于建立发布契约，不构成已
完成的安装验证。

## Release 资产

每个 Release 必须包含：

```text
Nikkiward-win-x64.zip
Nikkiward-update.json
SHA256SUMS.txt
```

标签与版本必须一一对应：

- 稳定版：`v0.1.0`
- 预览版：`v0.2.0-preview.1`

稳定通道忽略 prerelease；预览通道选择最新的非 draft Release。

## 清单约束

Schema 见 `docs/Nikkiward-update.schema.json`。客户端接受清单前至少验证：

- `schemaVersion == 1`
- `channel` 与请求通道一致
- `version` 是有效语义版本，且与 Release 标签一致
- `publishedAtUtc` 是 UTC 时间
- `size > 0`
- `sha256` 是 64 位十六进制字符串
- `runtimeIdentifier == win-x64`
- `format == zip`

清单不接受下载 URL、命令行、安装目录或待执行文件。客户端必须从同一个 GitHub
Release 的资产元数据中找到唯一同名 ZIP，并交叉核对 API `size`、可用时的 `digest`
和清单 SHA-256。Release 页面地址同样来自 GitHub API 的 `html_url`。

`signature` 在只读检查阶段可以为 `null`。这意味着该清单不能授权自动安装。

## 自动更新启用条件

后续自动更新需要单独的更新器进程，并完成以下顺序：

1. 使用内置公钥验证清单签名和 `keyId`。
2. 确认版本递增、架构和最低支持版本。
3. 下载到同一磁盘的临时目录，支持断点续传。
4. 验证文件长度和 SHA-256。
5. 验证 Authenticode 发布者或独立包签名。
6. 解压到新的 `app-{version}` 目录，不覆盖当前运行目录。
7. 原子切换版本指针并启动新版本。
8. 等待新版本写入健康确认。
9. 保留上一版本一次；健康确认失败时恢复旧指针。

在签名或健康确认缺失时，客户端只能提供手动下载入口。

## 私有仓库边界

私有 GitHub 仓库的 Releases 不能作为匿名客户端更新源。仓库保持私有时，About 页的
检查更新会显示没有公开发布源。准备面向其他用户分发前，应公开该仓库，或把 Release
资产和签名清单发布到独立的公开更新仓库。
