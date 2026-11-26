# 给网页皮肤任务的说明

## 文件和登记

工作副本内新增 `Nikkiward/Web/skins/<id>/index.html`、自己的样式和脚本，以及 `Nikkiward/Web.dev/<id>/cases.js`。文件夹名固定为 `cinema`（熄灯放映）、`stitch`（穿针引线）、`journal`（搭配师的手账）、`wishstar`（心愿星）。已有的 `atelier` 是可运行示例。登记只认五个既有 ID，重建应用后有入口页的皮肤会出现在设置中。

沿用 Atelier 的 CSP、顶部拖拽区和右上角原生窗口按钮留白。引用字体时选择 `../../fonts/<族名>.css`，使用该文件的 font-family。可用文件名为 bodoni-moda、noto-serif-sc、zcool-xiaowei、cormorant、manrope、lxgw-wenkai-tc、caveat、jetbrains-mono、noto-sans-sc、fraunces。

```html
<link rel="stylesheet" href="../../fonts/cormorant.css">
<link rel="stylesheet" href="home.css">
<script defer src="../../shared/home.js"></script>
<script defer src="home.js"></script>
<link rel="stylesheet" href="../../shared/theme.css">
<link rel="stylesheet" href="../../shared/planner.css">
<script defer src="../../shared/planner.js"></script>
<link rel="stylesheet" href="../../shared/overlays.css">
<script defer src="../../shared/overlays.js"></script>
```

共用脚本在 DOM 解析后运行，提供 `$`、`win`、`bridge` 和 `show(state)`。页面需提供 `#win.pending`、无替代文字的 `img#coverImg`、`video#coverVideo[muted][loop][playsinline][hidden]` 和普通脚本全局函数 `render(state)`。根节点 pending 时不可见；首次封面和字体就绪后，共用脚本移除 pending，有动效时加 enter。皮肤把 still 应用到自己的动画，按设计稿设置背景层尺寸、裁切和遮罩。视频、静态帧、后台暂停及原生接管由共用脚本处理。顶部有交互元素时实现 `window.mastheadBounds()`，返回它们在页面坐标中的左右边界数组，单位为 CSS 像素。若导航以外还有独立的顶部工具按钮，在数组后追加其左右边界；两块之间仍可拖动窗口。

## 状态契约

宿主以 camelCase JSON 下发状态，每次调用 render。除 photos 的增量规则外，可空字段均以 null 表示暂无数据。

| 字段 | 用途 |
|---|---|
| state、visual | 原生启动状态枚举名、合并后的视觉状态 ready / launching / running / blocked / notfound / checking。 |
| launchLabel、detail、readySummary | 原生主按钮文案、完整原因和帮助文本、可启动态的短副行。 |
| enabled、busy、closeVisible、closeEnabled、closeLabel | 启动请求是否可点、忙碌标志、关闭游戏是否显示和可点、关闭按钮文案。 |
| check、checkTime、checkCount、nextActions | 启动检查短句、检查时间、可空检查项数，以及该状态下的辅助动作名。 |
| profile、channel、platform | 当前档案友好名称、区服、带 Windows 的平台显示名。 |
| profiles、discoveryTime | 档案数组和最近发现时间。每项含 id、name、region（区服）、channel（发行渠道）、available（可选择）、reason（发现结果）、current。可选择不代表通过启动门。 |
| profileOpen | 原生档案浮层是否打开。网页切换器由皮肤管理自己的开关、焦点、Escape 和遮罩。 |
| running、sessionMinutes、still | 游戏运行标志、可确定的本次运行分钟数、是否收敛动效。 |
| officialDuration、journalSnapshotDate、localDuration | 官方手账时长、快照日期、本机时长。 |
| taskText、progress | 后台任务短句和可空的 0–100 进度。 |
| artName、artSource、background | 画名、来源，及 {type,url,poster}；type 为 image/video，视频 poster 是静态帧地址。 |
| theme、accent、onAccent、palette、contrastPassed | dark/light、织标底色、织标文字色、原生取色的亮/暗主题强调色数组、当前织标文字对底色的对比度是否达到 4.5。 |
| photos、photoTotal、lastSessionPhotos | 最近照片的 id、fileName、time（今天 HH:mm、昨天 HH:mm、MM-dd HH:mm 或往年 yyyy-MM-dd）、thumbnail，以及可空的总数和上次游玩拍摄数。**photos 缺省时保留上次列表，空数组才清空。** |
| skin、theme、checklistReset | 宿主当前皮肤 ID、画面明暗、设置中的本地清单重置时刻。共用脚本在 html 根元素设置 data-skin 和 data-tone，面板使用 theme.css 的 --nk-* 变量。 |
| checklist | 无条目时为 null。其余为 {completed,total,next,items}，next 是首个未完成项的文字，全完成时为 null。items 按玩家顺序给出 {id,text,done}。 |
| reminders、reminderCount、nextReminder | 全部提醒、条数、下一条待触发提醒。每项含 id、text、repeat（once/daily/weekly）、本地 time、一次性 date、周 weekday（0 为周日）、带时区 due。已结束提醒的 due 为空。 |
| links | 玩家排序的固定链接 {id,text,url}，仅接受 HTTPS。 |
| sessionStarted、currentSessionPhotos | 当前档案关联进程的启动时刻（含时区）及本次记录内的拍摄数。photos 每项的 session 为 current、previous 或 null。照片归属及本次、上次数量仅来自已启用的本机游玩记录。已绑定游戏进程的启动时刻可独立显示，未能确认时留空。 |
| date、issue | 本机日期 YYYY-MM-DD、中文刊号。 |

## 动作

普通按钮使用 `data-action`，共用脚本发送 `{action}`。可用动作如下。

- launch、close 请求启动或关闭游戏。所有决定留在原生入口。
- settings、background、status 在当前网页打开游戏设置、背景分区、启动检查。reset 请求原生恢复默认背景。tide、blossom 选择海月和花信。
- select-profile 发送 `{action:'select-profile',profileId:p.id}`，使用当前列表给出的 ID。rediscover 重新发现安装，locate 选择游戏目录，recheck 重新执行检查。profile 是原生浮层兼容动作，附 rect {x,y,width,height}；网页档案按钮直接打开自己的切换器。
- gallery、journal、download、time 打开相册、手账、下载状态、时长。photo 需附 photoId；按钮可使用 `data-action="photo" data-photo-id="…"`。
- nav-launcher、nav-library、nav-gallery、nav-gallery-favorites、nav-resonance、nav-settings 对应首页、手账、相册、收藏、共鸣、设置。
- 设计稿的命令面板组合这些动作。熄灯鉴赏、打开或收起命令面板等页面内交互由皮肤自行完成。

面板由共享 planner.js 渲染，用 data-planner="check"、"reminder" 或 "link" 打开对应区段。编辑发送 {action:"planner",planner:{action,id,text,...}}。check 支持 add/edit/toggle/delete/move，link 支持 add/edit/delete/move/open，reminder 支持 add/edit/delete/move，以连字符拼接动作名。新增省略 id，编辑和新增带 text，链接带 url，提醒带 repeat、time 以及相应的 date 或 weekday；移动带 step（-1 或 1）。打开链接只发送已保存的 id。

## 游戏设置与启动检查

五套首页共用 overlays.js 和 overlays.css。宿主随首页状态下发 overlay（安装身份、只读契约、静态证据、回执、执行门、背景画名、导入模式及操作权限）、components（组件身份、版本、路径和状态）与 officialFlow。诊断原因和下一步沿用 launchLabel、detail。官方启动动作同时要求 Ready 和原生辅助启动能力。

原生入口可向网页发 open-settings、open-background、open-status。关闭、Esc 和分区选择由网页处理。网页可发送 locate、open-root、import-background、reset、art-title、recheck、export-diagnostics、official-flow。原生按所选文件识别背景类型。art-title 附当前 artKey 和最长 120 字的 title，空标题恢复原名。资源管理器只打开原生当前安装路径；导入文件、定位目录、诊断导出目录和启动确认沿用原生流程。

五套皮肤均提供 config、diag 对照。cinema 另有 config-blossom、config-real、diag-real、diag-real-blocked、config-real-pkg。真实状态示例由测试项目的 `--emit-overlay-samples` 参数生成到 Web.dev/overlay-samples.js。原生检查的原始值位于 overlay.technical。

## 对照与发布

普通浏览器以 `file:///…/Nikkiward/Web/skins/<id>/index.html?mock=<用例名>` 打开。共用脚本在无 WebView2 时从 `../../../Web.dev/<id>/cases.js` 加载测试数据。脚本读取 mock，按设计稿调用 `show(state)`；需要展开切换器等终态时，用 `show(state).then(...)`。背景和示例照片使用设计素材的 file URL，仍走正式渲染逻辑。保留设计稿 bare 的 CSS 和确定性粒子位置；对照入口设置 bare、noanim、still，跳过入场，并在初始布局后固定动画时间。

每套的 `?mock=panel` 和 `?mock=panel-light` 分别使用 home-tide、home-blossom 的首页示例及固定动画帧。面板单独读取 Web.dev/panel.js。高定画册另有 `?mock=panel-edit`，对应海月背景下的整理模式。

修改完页面后，从仓库根目录运行 `pwsh -File build/Update-WebManifests.ps1`，将生成的 `build/WebSkins.sha256` 与页面一起交付。清单包含 shared 和所有皮肤，合并并行分支时在合并后的文件树上再生成一次。Web.dev 只用于对照，不进入发布载荷。

各皮肤任务只新增自己的两个目录并重新生成清单。共用宿主、白名单、XAML 登记、Web/shared、字体、项目文件和发布校验脚本由底座维护。
