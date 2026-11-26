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
  const navigation = [{"id":"appearance","label":"外观"},{"id":"general","label":"通用"},{"id":"FileManagement","label":"文件管理"},{"id":"Screenshot","label":"游戏截图"},{"id":"Gallery","label":"相册"},{"id":"Hotkeys","label":"键盘快捷键"},{"id":"Gamepad","label":"手柄增强"},{"id":"Advanced","label":"高级"},{"id":"about","label":"关于"},{"id":"Download","label":"下载"},{"id":"Overview","label":"概览"},{"id":"Plugins","label":"插件"},{"id":"Journal","label":"奇想手账与缓存"},{"id":"Files","label":"本地资源"},{"id":"Status","label":"运行状态"},{"id":"Components","label":"组件验证"},{"id":"Diagnostics","label":"脱敏诊断"},{"id":"Contract","label":"隔离契约"}];
  if (!real) navigation.length = 9;
  const skins = [['Cinema','熄灯放映'],['Atelier','高定画册'],['Stitch','穿针引线'],['Journal','搭配师的手账'],['WishStar','心愿星']].map(([id,label]) => ({id,label}));
  sections.appearance.skin = skins.find(item => item.id.toLowerCase() === skin).id;
  receivePage({type:'state',page:'settings',module:'settings',skin,tone:query.get('tone') ?? (skin === 'cinema' ? 'light':'dark'),
    state:{sections,navigation,skins,initial:['general','about'].includes(variant) ? variant : 'appearance',
      canClearTime:true,mainKey:real ? 'Ctrl+Shift+Alt+F12' : 'Alt+S',screenshotKey:'Alt+D'}});
})();
