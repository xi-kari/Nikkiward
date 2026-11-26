# 功能页面

入口为 `Nikkiward/Web/pages/index.html`。六个目的地是 home、gallery、favorites、journal、wish、settings。home 返回启动页。其余目的地在 MainPage 的页面登记表查找适配器，缺项继续走原生入口。官方手账浏览器与照片工坊各有自己的宿主。

## 新增页面

1. 在 `Nikkiward/Features/Pages/<Name>PageBridge.cs` 中实现 MainPage 的 partial 成员，提供一个 PageAdapter。构造参数依次是模块文件夹名、动作白名单、当前状态的快照函数和异步动作处理函数。状态字段用 camelCase。系统文件选择、数据读写、业务校验都在适配器完成。
2. 在 `MainPage.WebPages.cs` 的 Register 调用增加一对路由和适配器，例如 `("gallery", GalleryPageAdapter())`。两个路由可引用同一个模块。顶栏、模块加载器和其他页面均无需修改。
3. 添加 `Nikkiward/Web/pages/<module>/page.css` 和 `page.js`。CSS 选择器限定在模块根元素内。普通脚本用闭包保持私有作用域，通过 `registerPage` 导出生命周期，以兼容直接用浏览器打开 file URL。

```js
(() => {
  let listeners;
  function mount({root, page, send, commands}) {
    listeners = new AbortController();
    const button = document.createElement('button');
    button.textContent = '刷新';
    const refresh = () => send('refresh');
    button.addEventListener('click', refresh, {signal:listeners.signal});
    root.append(button);
    commands.push(['当前页面', '刷新', refresh]);
    return state => { button.disabled = state.busy; };
  }
  function unmount() { listeners.abort(); }
  registerPage('example', {mount, unmount});
})();
```

挂载只发生在进入路由时。返回的函数接收首份及后续状态。切走时 unmount 负责取消页面的订阅、定时器及异步工作，底座随后清空 DOM 和命令。路由、模块、皮肤与明暗由原生发送，底座更新 html 的 data-skin、data-tone。数据变化时原生调用当前宿主的 Refresh。页面用 `send(action, fields)` 发送 `{page, action, …fields}`，原生检查来源、当前页面及该适配器白名单，再交给处理器。顶栏和命令面板直接发送 `{action:'navigate', to:'journal'}`，无需等待页面状态。模块内可使用 `send('navigate', {to:'journal'})`；这类消息带有 `page`，宿主只接受当前就绪页面的请求。可恢复的文件错误显示在当前页面，宿主、模块载入、适配器取状态或序列化失败时，当前目的地退回原生页面，原因在启动检查的「功能页面」中查看。

方块容器（卡片、输入框、下拉面板、菜单和表格容器）的 `border-radius` 使用 `var(--r-box)`，上限为 10px。按钮和小标签使用 `var(--r-pill)`，保留皮肤的胶囊圆角。两项变量均由 components.css 定义。

## 对照与发布

在 `Nikkiward/Web.dev/pages/<路由>/cases.js` 读取 mock 和 tone，调用 `receivePage({type:'state',page,module,skin,tone,state})`，与正式页面共用挂载和渲染。普通浏览器打开 `Nikkiward/Web/pages/index.html?mock=wish`。另有 wish-empty、wish-records、wish-atelier、wish-stitch、wish-journal、wish-wishstar，亮色加 `&tone=light`。正式宿主只接受固定入口地址。Web.dev 不进入发布包。

共用部件位于 Web/shared/components.css、commands.js，主题值来自 theme.css。改完运行 `pwsh -File build/Update-WebManifests.ps1`，发布检查按 WebSkins.sha256 验证。路由和数据逻辑在 Nikkiward.ProfileBuilder.Tests 中验证。

