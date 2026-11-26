'use strict';
(() => {
  const assets = 'file:///D:/Projects/nikkiward-worktrees/_briefs/redesign/concepts/assets/';
  const cases = {
    'home-tide':['tide','ready'],
    'home-blossom':['blossom','ready'],
    'home-tide-running':['tide','running'],
    'home-blossom-blocked':['blossom','blocked'],
    'x-launching':['blossom','launching'],
    switcher:['tide','ready']
  };
  const key = new URLSearchParams(location.search).get('mock');
  if (!Object.hasOwn(cases,key)) return;
  const [art, visual] = cases[key];
  const [state,launchLabel,check,detail] = {
    ready:['Ready','启动游戏','可以启动','官方启动器 1.6.2 · 签名已核对'],
    launching:['Launching','正在启动','正在启动','请在 Windows 管理员确认窗口里选「是」；选「否」会回到这里，不算出错'],
    running:['Running','游戏运行中','游戏运行中','21:44 启动 · 启动器已安静：粒子与视差暂停'],
    blocked:['SignatureUntrusted','签名验证未通过','安全拒绝，不是故障','启动器签名与官方不一致。请用官方启动器校验或重装，再重新检查']
  }[visual];
  const css = document.createElement('link');
  css.rel = 'stylesheet';
  css.href = '../../../Web.dev/wishstar/cases.css';
  document.head.append(css);
  show({
    state, visual, launchLabel, detail, readySummary:detail, check, checkTime:'21:42',
    enabled:visual === 'ready', busy:visual === 'launching', running:visual === 'running',
    closeVisible:visual === 'running', closeEnabled:true, closeLabel:'关闭游戏',
    nextActions:visual === 'blocked' ? ['status','recheck'] : [],
    profile:'国服 · 官方', discoveryTime:'今天 21:42',
    profiles:[
      {id:'cn',name:'国服 · 官方',region:'CN',channel:'Official',available:true,current:true,reason:'可以启动'},
      {id:'bili',name:'国服 · 哔哩哔哩',region:'CN',channel:'Bilibili',available:false,current:false,reason:'未发现安装'},
      {id:'steam',name:'国际服 · Steam',region:'Global',channel:'Steam',available:false,current:false,reason:'渠道暂不支持启动\n相册与手账可用'}
    ],
    officialDuration:'128 小时', localDuration:null, taskText:'合并存储', progress:62,
    artSource:'内置原画', artName:art === 'tide' ? '海月' : '花信', theme:art === 'tide' ? 'dark' : 'light',
    background:{type:'image',url:assets + 'bg-' + art + '.jpg'}, still:true,
    photos:[['p05','photo-05.jpg','21:07'],['p04','photo-04.jpg','20:52'],['fae','photo-fae.jpg','19:12'],['p01','photo-01.jpg','18:40']]
      .map(([id,fileName,time]) => ({id,fileName,time,thumbnail:assets + fileName})),
    lastSessionPhotos:2
  }).then(() => {
    if (key === 'switcher') switchProfiles(true, false);
    const frameTime = {'home-tide':2150,'home-blossom':1833.33,'home-blossom-blocked':1500,switcher:1866.67}[key] ?? 1666.67;
    for (const animation of win.getAnimations({subtree:true})) {
      if (animation.effect.getTiming().iterations === Infinity) {
        animation.currentTime = frameTime;
        animation.playbackRate = 0;
        animation.play();
      }
    }
    if (win.classList.contains('fx')) {
      const album = $('.thumb.more');
      const y = parseFloat(getComputedStyle(album).translate.split(' ')[1]);
      const glassY = Math.round(y);
      album.style.animation = 'none';
      album.style.translate = '0 ' + glassY + 'px';
      album.firstElementChild.style.transform = 'translate3d(0,' + (y - glassY) + 'px,0)';
    }
  });
})();
