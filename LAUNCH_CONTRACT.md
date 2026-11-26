# 启动契约：机制、脆弱点与维护手册

> **这是本项目最关键的资产。** 任何 UI 重构都不得改动本文描述的行为。
> 修改 `Models/LaunchProviderContract.cs`、`ViewModels/OfficialAssistedLaunchCoordinator.cs`、
> `Services/WindowsLaunchPreflightVerifier.cs` 之前必须先读本文。
>
> **ContractVersion 2（自适应契约）**：自本版起，契约按发现到的启动器版本目录自动实例化，
> 组件身份闸门从「内容哈希相等」改为「Authenticode 签名有效 + 签名者可信」。
> 游戏版本更新不再需要刷新契约。改造动机与决策记录见 §4。

---

## 1. 机制：为什么能启动

《无限暖暖》不能直接运行 `InfinityNikki.exe`。真实进程链（`_trace_agent/TRACE_REPORT.md` 记录）是：

```
launcher.exe  →  <版本目录>\xstarter.exe  →  InfinityNikki.exe  →  X6Game-Win64-Shipping.exe
```

即使从 Steam 启动也要先起官方 launcher。**攻克点在于跳过第一环**：直接以 `-skiplauncher` 参数启动第二环 `xstarter.exe`。

落到代码，全部启动行为就是这四个值（`LaunchProviderCatalog.CreateCnWindows`）：

| 项 | 值 |
|---|---|
| 可执行文件 | `<launcherRoot>\<版本目录>\xstarter.exe`（版本目录按 `^\d+\.\d+\.\d+$` 在磁盘上发现，多个并存时取最高版本） |
| 参数 | `-skiplauncher`（**有且仅有这一个**） |
| 工作目录 | `<launcherRoot>`（**不是** exe 所在的版本目录） |
| 提权 | `UseShellExecute = true` + `Verb = "runas"` |

对应 `OfficialAssistedLaunchCoordinator.Start` 的 ProcessStartInfo 构造。

**这四个值是不可协商的。** 工作目录设成版本目录会失败；漏掉 `runas` 会失败；多加参数会偏离契约被 `Preflight.ContractDrift` 拒绝。

## 2. 执行门那段绕不过去的逻辑

契约里 `ExecutionEnabled = false`（所有版本实例一律如此），静态 verifier 明确「deliberately stops before process creation」，返回 `ExecutionAllowed: false` / `FailureCode: ExecutionGateClosed` / `Plan: null`。

那为什么还能启动？在 `OfficialAssistedLaunchCoordinator.PrepareAsync`：当且仅当门是这个**精确**状态时，`PrepareAsync` 自行合成一份 `LaunchPlan`，返回 `Succeeded = true`，Detail 写作「本次点击形成一次不持久化的瞬时实验启动授权」。

即：**静态层永远拒绝执行，运行层按用户的单次点击合成瞬时授权。** 这个分离是刻意的，`MainPageViewModel.CanAttemptOfficialAssistedLaunch` 里那串看着奇怪的条件（要求 `ExecutionAllowed: false` 才允许点）正是在匹配它。

> ⚠️ **不要「修好」这个看似矛盾的状态。** 把 `ExecutionEnabled` 改成 `true` 会让 `PrepareAsync` 走另一条分支并要求 verifier 提供 `Plan`，而 verifier 不会提供 → `Preflight.ExecutionStateMismatch`，启动直接坏掉。

## 3. 换电脑、换版本还有效吗

**都有效。** 路径与版本全部动态发现，无一硬编码：

- Steam：注册表 + `libraryfolders.vdf` + `appmanifest_3164330.acf`
- 官方：固定目录 + 注册表 Uninstall 键（`InstallLocation`/`DisplayIcon`/`UninstallString`）+ launcher 配置里的 `gameDir`
- 手动选择游戏目录时，若上述来源都失败，还会上溯游戏根的父目录（父目录必须具备完整 launcher 布局才采纳）
- 版本目录：按 `^\d+\.\d+\.\d+$` 正则枚举且必须含 `xstarter.exe`；多个并存（启动器自更新残留旧目录）时**只绑定最高版本**——那是 `launcher.exe` 自己会拉起的那个
- 契约：`LaunchProviderCatalog.TryGet` 对任意合法版本目录返回按该版本实例化的契约；`BackendRelativeExecutablePath` 与 official-backend 组件的相对路径随版本目录参数化，其余全部字段是冻结常量

装在 D 盘、E 盘、任意目录名都能找到；启动器从 1.3.1 升到任何 `x.y.z` 也不需要改代码。

## 4. 组件身份怎么验（以及为什么不再钉哈希）

ContractVersion 1 曾把 5 个二进制的 SHA-256、文件版本与签名指纹全部钉死。结果是**每个游戏版本日全网用户 fail-closed**，
必须由维护者手工刷新契约、发版，用户升级后才能启动——对公开分发的启动器这是不可接受的维护负担，也是大量
「更新后启动不了」「选择游戏目录没反应」反馈的根因。ContractVersion 2 改为**发行商身份验证**：

每个组件的闸门（`WindowsLaunchPreflightVerifier.VerifyComponentAsync`，顺序固定）：

1. 文件存在（`RequiredComponentMissing`）
2. 读取期间文件未被改动——长度与修改时间读前读后一致（`BinaryIdentityDrift`，只防哈希读取窗口内的内容漂移）
3. WinVerifyTrust 签名状态必须 `Valid`（`SignatureInvalid`）
4. 签名者可信（`SignerMismatch`）：叶证书指纹命中 `TrustedSignerRequirement.Thumbprints` 允许列表；
   或证书主体**整串等值**匹配（CN 取自 2.5.4.3 RDN 本身而非 SimpleName 回退，加 subject serialNumber）——
   后者让官方例行换证书时不至于全网熄火
5. launcher 侧组件（launcher.exe、xstarter.exe）额外钉 `OriginalFilename`（构建常量、版本无关），
   防止**另一个**官方签名的二进制被改名塞进提权槽位；游戏本体不钉（InfinityNikki.exe 没有版本资源）

除静态预检外，`OfficialAssistedLaunchCoordinator.Start` 在提权前对即将被执行的 xstarter.exe
**持句柄重验**：以 `FileShare.Read`（拒绝写入/改名/删除共享）打开文件并在持有句柄期间重跑
签名 + 签名者 + OriginalFilename 校验，句柄保持到 `Process.Start` 返回——通过复核的字节
就是 UAC 同意后 ShellExecute 读到的字节，验签与启动之间的顶层 EXE 换文件窗口由此关闭
（失败码 `Runtime.BackendIdentityDrift`）。

SHA-256 与文件版本**仍会计算并写入预检组件回执**，用于诊断显示；预检失败原因（含签名者
指纹与 CN）会并入诊断快照的 LastFailureReason，随脱敏报告导出，但都不再阻断启动。

B 服与 Steam 的实验直启沿用这五个组件及其签名者要求。两个渠道激活后按本次计划的启动器根和游戏根验签。Steam 的安装发现只检查四个 EXE，直启预检还要求游戏根内的 `ACE-Service64.exe` 通过验证。请求提权前，渠道入口在只读共享句柄下复核 `xstarter.exe` 的签名、签名者和原始文件名，并比较同一次点击内两次读取的 SHA-256。文件发生变化会返回 `BackendIdentityDrift`，下一次点击会重新采样。两个渠道的实物签名者尚待确认。

当前可信签名者（`LaunchProviderCatalog`）：

| 签名者 | 适用组件 | 锚点 |
|---|---|---|
| Suzhou Nikki Co.,Ltd.（叠纸/苏州暖暖） | launcher.exe、xstarter.exe、InfinityNikki.exe、Shipping | 指纹 `6A57…015A`；CN + 统一社会信用代码 `91320594076301379M` |
| ACEVILLE PTE LTD（ACE 反作弊） | ACE-Service64.exe | 指纹 `30D5…1466`；CN |

失效场景，按发生频率排：

| 事件 | 后果 | 频率 |
|---|---|---|
| 游戏版本更新 | **不失效**——新文件仍由官方签名，直接放行 | 每个大版本（约 6 周） |
| 启动器更新换版本目录 | **不失效**——契约按发现的版本目录实例化 | 少见 |
| 官方续签证书（主体不变） | **不失效**——subject 匹配兜底，指纹允许列表可事后补录 | 罕见 |
| 官方更换签名主体（公司主体变更） | 全量 fail-closed（`SignerMismatch`），需人工确认新主体后更新允许列表并发版 | 极罕见 |
| 组件未签名 / 签名被破坏 / 第三方篡改 | fail-closed（`SignatureInvalid` / `SignerMismatch`），**这正是想要的行为** | — |

**结论：换电脑不影响，打补丁也不影响；只有官方更换签名主体这一极罕见事件需要发版。**

> ⚠️ 安全边界的取舍要诚实地写在这里。本策略保证的是：**在临启动复核时点、就顶层 EXE 而言**，
> 被提权的二进制必须能归属可信签名者。以下残余风险明确不在保证范围内，接受它们是有意的取舍：
>
> - **无反降级**：任何由官方签名的旧版二进制同样能过门。Nikkiward 只把 `-skiplauncher` 交给官方
>   xstarter，游戏内容一致性由官方链与服务器自行校验。
> - **被启动进程加载的模块不在验证范围**：预检只验 5 个 EXE，不验它们加载的 DLL。工作目录与
>   exe 目录都在 DLL 搜索路径上，对可写安装目录的 DLL 侧载无法由启动器消除（v1 哈希钉死同样不验 DLL）。
> - **吊销检查关闭**：WinVerifyTrust 以 `WTD_REVOKE_NONE` + cache-only 运行（避免预检发网络请求、
>   避免无缓存 CRL 的机器 fail-closed）。官方证书被吊销时本应用察觉不到。
> - **信任本机根存储**：管理员级攻击者可安装流氓根证书骗过链校验——该前提已越过本应用的威胁模型
>   （攻击者已是管理员）。普通自签证书（未装根）会被 `Untrusted` 状态正确拒绝。

## 5. 什么时候还需要维护（签名审计工具）

日常**不需要**。只有 §4 表中「官方更换签名主体」发生时才要动手：

```bash
dotnet run --project Nikkiward.ProfileBuilder.Tests -- --emit-contract
```

它扫描本机安装，打印每个组件的签名状态、指纹、证书主体与可信判定；只有出现「签名有效但不在可信列表」的签名者时，
才会生成可直接粘贴进 `LaunchProviderCatalog` 的 `TrustedSignerRequirement` 片段。

流程：
1. 用户反馈启动被拒，界面显示「签名验证未通过」
2. 跑上面那条命令，看是官方换了签名主体，还是文件真的被篡改
3. 若是前者：人工核实证书归属（DigiCert 签发链、主体公司注册信息），把输出片段合入允许列表，递增 `ContractVersion`
4. 跑 `dotnet run --project Nikkiward.ProfileBuilder.Tests -- --current-machine` 确认全过

> 工具只**读取并打印**，绝不自动改源码。可信签名者必须由人确认后手工粘贴——这是安全边界，不要自动化掉。

## 6. 不许碰的行为清单

以下每一条都有特征测试锁定（`dotnet run --project Nikkiward.ProfileBuilder.Tests -- --launch-contract`，
该套件也无条件包含在默认测试模式中）。改动导致测试失败即为回归，不是测试过时。

1. 参数**恰好**是 `["-skiplauncher"]`
2. 工作目录 = launcher 根，**不是** exe 所在目录
3. 后端相对路径 = `<版本>\xstarter.exe`，`<版本>` 为发现到的版本目录名
4. `Verb = "runas"`（提权），`UseShellExecute = true`
5. 契约 id `OfficialXStarterSkipLauncherCn131`（历史名，冻结不改——协调器、verifier 与启动按钮三处按它做精确相等门），`ArgumentPresetId` `cn-win-xstarter-skiplauncher-v1`
6. 契约里 `ExecutionEnabled = false`（所有版本实例）
7. `ExecutionGateClosed` + `Plan: null` + `ExecutionAllowed: false` 时**必须**合成瞬时 plan 并 `Succeeded = true`
8. 其他任何执行门状态组合 → `Preflight.ExecutionStateMismatch`
9. 5 个组件必须各返回**唯一且通过**的回执，否则 `Preflight.ComponentReceiptIncomplete`
10. 目标组件已在运行时拒绝启动（`Preflight.BaselineDirty`）
11. 无法读取活动进程路径时拒绝启动（`Preflight.ObserverUnavailable`，fail-closed）
12. 用户取消 UAC → `Runtime.UserCancelledElevation`（NativeErrorCode 1223），不重试
13. 绝不出现 SteamOS 的 `_SD` 参数
14. 绝不读取 `PaperLauncherToken` / `PaperStartupToken`（每次启动由官方生成，读它就是在碰凭据）
15. **任意 `^\d+\.\d+\.\d+$` 版本目录都能解析出契约**；畸形版本名与非 Official 渠道必须拒绝（`CatalogResolvesDiscoveredVersions`）
16. **签名者不在可信列表或身份不可读 fail-closed 于 `SignerMismatch`**（`UntrustedSignerFailsClosed`、`UnreadableSignerIdentityFailsClosed`）；**未签名/签名无效 fail-closed 于 `SignatureInvalid`**（默认套件的 `TestPreflightUnsignedArtifact`，用真 WinVerifyTrust）；可信签名者（指纹或 subject 整串匹配）无论文件内容如何都放行（`TrustGatePassesTrustedSigner`、`TrustGateSurvivesCertificateRotation`）
17. 更新后的安装（新版本目录 + 未知哈希）必须能完成 Prepare（`UpdatedVersionStillPrepares`）
18. **提权前必须持句柄重验 backend 身份**：Start 以拒绝写入/改名/删除的共享模式锁定 xstarter.exe、用生产探针复核签名与签名者，失败 → `Runtime.BackendIdentityDrift`、绝不 `Process.Start`（`StartRefusesDriftedBackend`）

## 7. UI 层的义务

启动按钮必须**如实**映射状态，不许伪装可用：

| 内部状态 | 按钮 |
|---|---|
| 未检测 | 禁用 + 骨架，「检查安装中…」 |
| 未发现安装 / 等待手动选择安装 | 禁用，分别显示「未发现安装」「需要选择安装」，提示前往「定位游戏」 |
| 渠道不支持 | 禁用 + 说明原因 |
| 签名无效或签名者不可信 | 禁用 + 「签名验证未通过」+ 提示用官方启动器校验或重装 |
| 渠道身份不符（product.db 与所选渠道不一致） | 禁用 + 「渠道身份不符」 |
| 执行门关闭（正常可启动态） | **可点击**，「启动游戏」，detail 回显检测到的启动器版本 |
| 目标已在运行 | 禁用，「游戏运行中」 |
| UAC 被取消 | 恢复可点击，不弹错误对话框 |

「签名验证未通过」这一态尤其重要：它是新策略下唯一的安全 fail-closed 态，文案必须说清是安全拒绝而非故障，并给出可操作路径（官方校验/重装）。

手动选择目录同样必须如实反馈：`DiscoverFromManualRootAsync` 的每条失败路径都会返回 `ManualDiscoveryOutcome`、
弹出对话框并更新「定位游戏」旁的状态文案——**被静默丢弃的选择与坏掉的按钮无法区分**，这是 preview.4 时代
「选择游戏目录没反应」投诉的直接根因，不许回归。启动准备失败时（`PrepareOfficialAssistedLaunchAsync`），
失败的 preflight 结果必须回写按钮状态，不许停留在「启动游戏」。
