'use strict';
(() => {
  const assets = 'file:///D:/Projects/nikkiward-worktrees/_briefs/redesign/concepts/assets/';
  const cases = {
    'home-tide':['tide','ready'],
    'home-blossom':['blossom','ready'],
    'home-tide-running':['tide','running'],
    'home-blossom-blocked':['blossom','blocked'],
    'x-launching':['blossom','launching'],
    switcher:['tide','ready'],
    'real-ready':['tide','ready'],
    'real-blocked':['blossom','blocked'],
    'real-notfound':['tide','notfound'],
    'real-running':['tide','running'],
    'real-long':['custom','blocked']
  };
  const overlayCase = window.prepareOverlayCase?.({config:'home-tide',diag:'home-blossom-blocked'});
  const showCase = overlayCase?.show ?? show;
  const key = overlayCase?.homeCase ?? window.panelHomeCase ?? new URLSearchParams(location.search).get('mock');
  if (!Object.hasOwn(cases,key)) return;
  const [art, visual] = cases[key];
  const [state,launchLabel,check,detail] = {
    ready:['Ready','启动游戏','可以启动','官方启动器 1.6.2 · 签名已核对'],
    launching:['Launching','正在启动','正在启动','请在 Windows 管理员确认窗口里选「是」；选「否」会回到这里，不算出错'],
    notfound:['NotInstalled','未发现安装','未发现安装','请在游戏设置的「基本信息」中点「定位游戏」。'],
    running:['Running','游戏运行中','游戏运行中','21:44 启动 · 启动器已安静：粒子与视差暂停'],
    blocked:['SignatureUntrusted','签名验证未通过','安全拒绝，不是故障','启动器签名与官方不一致。请用官方启动器校验或重装，再重新检查']
  }[visual];
  const real = key.startsWith('real-');
  if (!real) {
    const css = document.createElement('link');
    css.rel = 'stylesheet';
    css.href = '../../../Web.dev/wishstar/cases.css';
    document.head.append(css);
  }
  const sample = {
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
  };
  if (real) {
    const notFound = visual === 'notfound', custom = art === 'custom';
    const running = visual === 'running', ready = visual === 'ready';
    const dates = ['2026-11-26_19-00-08','2026-11-25_23-40-08','2026-11-21_19-00-08','2026-11-03_04-45-08'];
    const times = ['今天 19:00','昨天 23:40','11-21 19:00','11-03 04:45'];
    const photos = sample.photos.map((p,i) => ({...p,fileName:dates[i]+'.png',time:times[i]}));
    Object.assign(sample, {
      readySummary:ready ? '官方启动器 1.3.2 · 签名已核对' : null,
      profile:notFound ? null : custom ? '国际服 · Steam' : '国服 · 官方',
      channel:custom ? '国际服' : '国服', platform:(custom ? '国际服' : '国服') + ' · Windows',
      profiles:sample.profiles.map((p,i) => {
        const current = !notFound && i === (custom ? 2 : 0);
        return {...p,region:i === 2 ? '国际服' : '国服',channel:['官方','哔哩哔哩','Steam'][i],
          current,available:current,reason:current ? '已发现安装' : '未发现安装'};
      }),
      checkTime:visual === 'blocked' && !custom ? '今天 06:45' : '今天 06:43',
      checkCount:null, discoveryTime:null, checklist:null, photoTotal:null, lastSessionPhotos:null,
      officialDuration:running || custom ? '1284 小时' : null, localDuration:running || custom ? '96.5 小时' : null,
      taskText:running ? '正在导入共享对象 · 1024/2048' : custom ? '正在计算 SHA-256 · 20480/40960' : null,
      progress:running || custom ? 50 : null, sessionMinutes:running ? 42 : null,
      photos:custom ? photos.slice(-1) : photos,
      nextActions:notFound ? ['locate','rediscover'] : sample.nextActions
    });
    if (custom) Object.assign(sample, {
      state:'SelectionRequired',launchLabel:'需要选择安装',detail:'请在游戏设置的「基本信息」中点「定位游戏」，选择要使用的安装目录。',
      artName:'2026-11-26_21-14-08_花愿镇的黄昏与晚风',artSource:'自定义背景',theme:'dark',
      accent:'#a4e9f2',onAccent:'#072a3c',background:{type:'image',url:assets+'photo-05.jpg'}
    });
    else if (ready) sample.detail = '检测到启动器版本 1.3.2，组件签名验证通过；点击后构建瞬时 plan 并请求 UAC 提权。';
    else if (running) sample.detail = '目标进程已在运行，启动按钮已锁定。';
    else if (visual === 'blocked') sample.detail = '游戏组件的数字签名无效或签名者不受信任，为安全起见已拒绝启动。请用官方启动器校验游戏文件或重新安装后重试。';
    sample.check = ready ? '检查通过' : sample.launchLabel;
  }
  showCase(sample).then(() => {
    if (!real && (visual === 'launching' || visual === 'running')) {
      $('#statT').textContent = sample.launchLabel;
      $('#statT').parentElement.hidden = false;
    }
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
