(() => {
  const query = new URLSearchParams(location.search);
  const variant = query.get('mock').slice('settings'.length + 1);
  const skin = ['atelier','stitch','journal','wishstar'].includes(variant) ? variant : 'cinema';
  const real = variant === 'real';
  const sections = {
    appearance:{skin:'Cinema',theme:'FollowArtwork',adaptive:true,accentMode:'Adaptive',delay:8,recent:true,motion:'Full',serif:real ? true : null,density:'Standard',
      preset:real ? null : 'background2',source:real ? 'D:\\我的壁纸与背景收藏\\'+ '秋日相册与旅行记录\\'.repeat(12)+'花信与白鸽的午后.png' : null,
      interval:15,carousel:false,parallax:false,hologram:true,video:false,videoAvailable:false,blur:false,glass:1,pan:false,zoom:1,fixedAccent:'Blush',customAccent:'#e8a0b4'},
    general:{language:'zh-CN',close:'Exit',afterLaunch:'HideToTray',resetTime:'04:00',playTime:false},
    about:{version:real ? '0.1.0-preview.5' : '0.9.0',runtime:'win-x64',distribution:'便携版',commit:'90c5481d',channel:'Stable',available:true}
  };
  const library = 'D:\\游戏与旅行记录\\无限暖暖\\奇迹大陆\\X6Game\\Saved\\GamePlayPhotos';
  Object.assign(sections,{
    Screenshot:{folder:library+'\\游戏截图',canOpen:true,hotkey:'Ctrl+Shift+F9',format:'Avif',quality:'High',color:true,copy:true,sdr:true},
    Gallery:{customRoot:true,profile:'无限暖暖 · 官服',root:library,exportRoot:'E:\\摄影作品\\照片工坊\\搭配手记\\导出',
      enabled:true,protection:'E:\\收藏备份\\奇迹大陆',protectionStatus:'356 条记录 · 1.8 GiB',cache:'2,186 个缩略图 · 328 MiB',
      cachePath:'D:\\应用数据\\Nikkiward\\Gallery\\Thumbnails',tool:'E:\\摄影工具\\NikkiGallery\\NikkiGallery.exe',toolStatus:'已关联',
      actions:['refresh','choose','reset','open','exports','change','chooseProtection','openProtection','verify','clean','clearCache','associate','openTool','disconnect']},
    Plugins:{status:'已安装',version:'1.2.0',canImport:true,canOpen:true,canUninstall:true},
    Hotkeys:{main:'Ctrl+Alt+N',screenshot:'Ctrl+Shift+F9'},
    Gamepad:{enabled:true,longPress:true,guideAction:'None',shareAction:'MapKeys',shareKeys:'Ctrl Shift F9',
      status:'已启用，检测到手柄。',canEdit:true,runtimeMissing:false}
  });
  const navigation = [{"id":"appearance","label":"外观"},{"id":"general","label":"通用"},{"id":"FileManagement","label":"文件管理"},{"id":"Screenshot","label":"游戏截图"},{"id":"Gallery","label":"相册"},{"id":"Hotkeys","label":"键盘快捷键"},{"id":"Gamepad","label":"手柄增强"},{"id":"Advanced","label":"高级"},{"id":"about","label":"关于"},{"id":"Download","label":"下载"},{"id":"Overview","label":"概览"},{"id":"Plugins","label":"插件"},{"id":"Journal","label":"奇想手账与缓存"},{"id":"Files","label":"本地资源"},{"id":"Status","label":"运行状态"},{"id":"Components","label":"组件验证"},{"id":"Diagnostics","label":"脱敏诊断"},{"id":"Contract","label":"隔离契约"}];
  const initial = variant === 'hotkeys-recording' ? 'Hotkeys' :
    Object.keys(sections).find(id => id.toLowerCase() === variant) ?? 'appearance';
  if (!real && initial !== 'Plugins') navigation.length = 9;
  const skins = [['Cinema','熄灯放映'],['Atelier','高定画册'],['Stitch','穿针引线'],['Journal','搭配师的手账'],['WishStar','心愿星']].map(([id,label]) => ({id,label}));
  sections.appearance.skin = skins.find(item => item.id.toLowerCase() === skin).id;
  if (variant === 'hotkeys-recording') {
    const observer = new MutationObserver(() => {
      const content = document.querySelector('#settings .content');
      if (!content?.querySelector('[aria-haspopup="dialog"]')) return;
      observer.disconnect();
      const dialog = document.createElement('dialog');
      dialog.className = 'hotkey-preview';
      dialog.setAttribute('aria-labelledby','record-title');
      dialog.innerHTML = '<h2 id="record-title">显示主窗口</h2><p>请按下新的快捷键，按 Esc 取消。</p><form method="dialog"><button class="btn">取消</button></form>';
      content.append(dialog);
      dialog.showModal();
    });
    observer.observe(document.querySelector('#page'),{childList:true,subtree:true});
  }
  receivePage({type:'state',page:'settings',module:'settings',skin,tone:query.get('tone') ?? (skin === 'cinema' ? 'light':'dark'),
    state:{sections,navigation,skins,initial,
      canClearTime:true,mainKey:initial === 'appearance' ? (real ? 'Ctrl+Shift+Alt+F12' : 'Alt+S') : sections.Hotkeys.main,
      screenshotKey:initial === 'appearance' ? 'Alt+D' : sections.Hotkeys.screenshot}});
})();
