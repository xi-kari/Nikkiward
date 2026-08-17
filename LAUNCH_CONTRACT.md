# 启动契约：机制、脆弱点与维护手册

> **这是本项目最关键的资产。** 任何 UI 重构都不得改动本文描述的行为。
> 修改 `Models/LaunchProviderContract.cs`、`ViewModels/OfficialAssistedLaunchCoordinator.cs`、
> `Services/WindowsLaunchPreflightVerifier.cs` 之前必须先读本文。

---

## 1. 机制：为什么能启动

《无限暖暖》不能直接运行 `InfinityNikki.exe`。真实进程链（`_trace_agent/TRACE_REPORT.md` 记录）是：

```
launcher.exe  →  1.3.1\xstarter.exe  →  InfinityNikki.exe  →  X6Game-Win64-Shipping.exe
```

即使从 Steam 启动也要先起官方 launcher。**攻克点在于跳过第一环**：直接以 `-skiplauncher` 参数启动第二环 `xstarter.exe`。

落到代码，全部启动行为就是这四个值（[LaunchProviderContract.cs:78](Nikkiward/Models/LaunchProviderContract.cs:78)）：

| 项 | 值 |
|---|---|
| 可执行文件 | `<launcherRoot>\1.3.1\xstarter.exe` |
| 参数 | `-skiplauncher`（**有且仅有这一个**） |
| 工作目录 | `<launcherRoot>`（**不是** exe 所在的 `1.3.1\`） |
| 提权 | `UseShellExecute = true` + `Verb = "runas"` |

对应 [OfficialAssistedLaunchCoordinator.cs:250](Nikkiward/ViewModels/OfficialAssistedLaunchCoordinator.cs:250)。

**这四个值是不可协商的。** 工作目录设成 `1.3.1\` 会失败；漏掉 `runas` 会失败；多加参数会偏离契约被 `Preflight.ContractDrift` 拒绝。

## 2. 执行门那段绕不过去的逻辑

冻结契约里 `ExecutionEnabled = false`（[LaunchProviderContract.cs:90](Nikkiward/Models/LaunchProviderContract.cs:90)），静态 verifier 明确「deliberately stops before process creation」，返回 `ExecutionAllowed: false` / `FailureCode: ExecutionGateClosed` / `Plan: null`。

那为什么还能启动？在 [OfficialAssistedLaunchCoordinator.cs:177](Nikkiward/ViewModels/OfficialAssistedLaunchCoordinator.cs:177)：当且仅当门是这个**精确**状态时，`PrepareAsync` 自行合成一份 `LaunchPlan`，返回 `Succeeded = true`，Detail 写作「本次点击形成一次不持久化的瞬时实验启动授权」。

即：**静态层永远拒绝执行，运行层按用户的单次点击合成瞬时授权。** 这个分离是刻意的，`MainPageViewModel.CanAttemptOfficialAssistedLaunch` 里那串看着奇怪的条件（要求 `ExecutionAllowed: false` 才允许点）正是在匹配它。

> ⚠️ **不要「修好」这个看似矛盾的状态。** 把 `ExecutionEnabled` 改成 `true` 会让 `PrepareAsync` 走另一条分支并要求 verifier 提供 `Plan`，而 verifier 不会提供 → `Preflight.ExecutionStateMismatch`，启动直接坏掉。

## 3. 换电脑还有效吗

**有效。** 路径全部动态发现，无一硬编码：

- Steam：注册表 + `libraryfolders.vdf` + `appmanifest_3164330.acf`
- Epic：注册表
- 官方：注册表 + `AppData` + launcher 配置里的 `gameDir`
- 版本目录：按 `^\d+\.\d+\.\d+$` 正则枚举

装在 D 盘、E 盘、任意目录名都能找到。`MainPageViewModel.cs:13` 那个硬编码的 `E:\InfinityNikki\_evidence\...` 只是证据文件路径，与启动无关（仍应移除）。

## 4. 什么会让它失效

**游戏或启动器更新。** 契约把 5 个二进制的身份钉死了：

| 组件 | 钉住的东西 |
|---|---|
| `launcher.exe` | SHA-256 + 文件版本 1.3.1 + 签名指纹 |
| `1.3.1\xstarter.exe` | SHA-256 + 文件版本 1.3.1 + 签名指纹 |
| `InfinityNikki.exe` | SHA-256 + 签名指纹 |
| `X6Game-Win64-Shipping.exe` | SHA-256 + 文件版本 `2,8,1,2828` + 签名指纹 |
| `ACE-Service64.exe` | SHA-256 + 版本 `24.0.2510.212` + 签名指纹 |

加上 `CnLauncherVersion = "1.3.1"` 这个字符串常量：`LaunchProviderCatalog.TryGet` 只在版本目录名**精确等于** `1.3.1` 时返回契约（[LaunchProviderContract.cs:162](Nikkiward/Models/LaunchProviderContract.cs:162)）。

失效场景，按发生频率排：

| 事件 | 后果 | 频率 |
|---|---|---|
| 游戏版本更新 | `game-client` 哈希与文件版本不匹配 → `Preflight.HashDrift` | 每个大版本（约 6 周） |
| ACE 反作弊更新 | `anti-cheat-artifact` 不匹配 | 不定，较频繁 |
| 启动器更新到 1.3.2 | 版本目录不匹配 → **根本找不到契约**，最严重 | 少见 |
| 官方换签名证书 | 全部指纹失效 | 罕见 |

**结论：换电脑不影响，游戏打补丁就会坏。** 这是当前设计最大的维护负担。

## 5. 更新后怎么修

我加了一个刷新工具，不用手算哈希：

```bash
dotnet run --project Nikkiward.ProfileBuilder.Tests -- --emit-contract
```

它扫描本机安装，算出 5 个组件的实际 SHA-256、文件版本、产品版本、签名指纹，按 `LaunchProviderContract.cs` 的语法直接打印出可粘贴的 `RequiredComponents` 块，并给出新的 `CnLauncherVersion`。

流程：
1. 游戏更新后启动一次失败，看到 `Preflight.HashDrift` 或找不到契约
2. 跑上面那条命令
3. 把输出粘回 `LaunchProviderCatalog`（若版本目录变了，同时改 `CnLauncherVersion`，并把契约 id / `ContractVersion` 递增）
4. 跑 `dotnet run --project Nikkiward.ProfileBuilder.Tests -- --current-machine` 确认全过

> 工具只**读取并打印**，绝不自动改源码。契约必须由人确认后手工粘贴——这是安全边界，不要自动化掉。

## 6. 不许碰的行为清单

以下每一条都有特征测试锁定（`--launch-contract`）。改动导致测试失败即为回归，不是测试过时。

1. 参数**恰好**是 `["-skiplauncher"]`
2. 工作目录 = launcher 根，**不是** exe 所在目录
3. 后端相对路径 = `<版本>\xstarter.exe`
4. `Verb = "runas"`（提权），`UseShellExecute = true`
5. 契约 id `OfficialXStarterSkipLauncherCn131`，`ArgumentPresetId` `cn-win-xstarter-skiplauncher-v1`
6. 契约里 `ExecutionEnabled = false`
7. `ExecutionGateClosed` + `Plan: null` + `ExecutionAllowed: false` 时**必须**合成瞬时 plan 并 `Succeeded = true`
8. 其他任何执行门状态组合 → `Preflight.ExecutionStateMismatch`
9. 5 个组件必须各返回**唯一且通过**的回执，否则 `Preflight.ComponentReceiptIncomplete`
10. 目标组件已在运行时拒绝启动（`Preflight.BaselineDirty`）
11. 无法读取活动进程路径时拒绝启动（`Preflight.ObserverUnavailable`，fail-closed）
12. 用户取消 UAC → `Runtime.UserCancelledElevation`（NativeErrorCode 1223），不重试
13. 绝不出现 SteamOS 的 `_SD` 参数
14. 绝不读取 `PaperLauncherToken` / `PaperStartupToken`（每次启动由官方生成，读它就是在碰凭据）

## 7. UI 层的义务

启动按钮必须**如实**映射状态，不许伪装可用：

| 内部状态 | 按钮 |
|---|---|
| 未检测 | 禁用 + 骨架，「检查安装中…」 |
| 无安装 / 渠道不支持 | 禁用 + 说明原因 |
| 哈希漂移（更新后） | 禁用 + 明确提示「游戏已更新，启动契约需要刷新」+ 指向本文 |
| 执行门关闭（正常可启动态） | **可点击**，「启动游戏」 |
| 目标已在运行 | 禁用，「游戏已在运行」 |
| UAC 被取消 | 恢复可点击，不弹错误对话框 |

「哈希漂移」这一态尤其重要：用户会以为启动器坏了，实际是游戏更新了。文案必须说清并给出可操作路径。
