'use strict';
(async () => {
  const cases = {
    'home-tide':['tide','ready'], 'home-blossom':['blossom','ready'],
    'home-tide-running':['tide','running'], 'home-blossom-blocked':['blossom','blocked'],
    'home-tide-launching':['tide','launching'], 'home-photo05-notfound':['photo05','notfound'],
    switcher:['tide','ready'],
    'real-ready':['tide','ready'], 'real-blocked':['blossom','blocked'],
    'real-notfound':['tide','notfound'], 'real-running':['tide','running'],
    'real-long':['photo05','blocked']
  };
  const overlayCase = window.prepareOverlayCase?.({config:'home-tide',diag:'home-blossom-blocked'});
  const showCase = overlayCase?.show ?? show;
  const key = overlayCase?.homeCase ?? window.panelHomeCase ?? new URLSearchParams(location.search).get('mock');
  if (!Object.hasOwn(cases,key)) return;
  const [cover,visual] = cases[key];
  const assets = '../../../Web.dev/assets/';
  const covers = {
    tide:['bg-tide.jpg','海月','dark','#263f7c'],
    blossom:['bg-blossom.jpg','花信','light','#b3445c'],
    photo05:['photo-05.jpg','荷塘夜色','dark','#1d5b70']
  };
  const [file,artName,theme,accent] = covers[cover];
  const states = {
    ready:['Ready','启动游戏','官方启动器 1.6.2 · 签名已核对','通过'],
    running:['Running','游戏运行中','启动器已安静下来：动效暂停，照片照常归档。','通过'],
    launching:['Launching','正在启动','请在 Windows 管理员确认里点「是」。点「否」只是取消。','通过'],
    blocked:['SignatureUntrusted','签名验证未通过','这是安全拒绝，不是故障。请用官方启动器校验或重装后再试。','未通过'],
    notfound:['NotInstalled','未发现安装','这个档案下没有找到游戏。告诉我它装在哪里就可以。','未发现安装']
  };
  const [state,launchLabel,detail,check] = states[visual];
  const profiles = [
    {id:'cn',region:'国服',channel:'官方',name:'国服 · 官方',reason:'官方启动器 1.6.2 · D:\\Games\\InfinityNikki',available:true,current:true},
    {id:'bili',region:'国服',channel:'哔哩哔哩',name:'国服 · 哔哩哔哩',reason:'未发现安装 · 切换后可在「游戏设置」里定位',available:false,current:false},
    {id:'steam',region:'国际服',channel:'Steam',name:'国际服 · Steam',reason:'Steam 版 1.6.2 · E:\\SteamLibrary\\Infinity Nikki',available:true,current:false}
  ];
  const photos = [
    ['photo-05.jpg','2026-11-26_21-14-08.jpg','今天 21:14'],
    ['photo-04.jpg','2026-11-26_20-51-33.jpg','今天 20:51'],
    ['photo-01.jpg','2026-11-25_23-40-12.jpg','昨天 23:40']
  ].map(([id,fileName,time]) => ({id,fileName,time,thumbnail:assets+id}));
  function realState() {
    const long = key === 'real-long';
    const missing = key === 'real-notfound';
    const running = visual === 'running';
    const current = missing ? null : long ? 'steam' : 'cn';
    const captures = [
      ['photo-01.jpg','2026-11-26_19-00-08.png','今天 19:00'],
      ['photo-04.jpg','2026-11-25_23-40-12.png','昨天 23:40'],
      ['photo-02.jpg','2026-11-21_19-00-08.png','11-21 19:00'],
      ['photo-05.jpg','2026-11-03_04-45-08.png','11-03 04:45']
    ];
    const details = {
      ready:'检测到启动器版本 1.3.2，组件签名验证通过；点击后构建瞬时 plan 并请求 UAC 提权。',
      blocked:'游戏组件的数字签名无效或签名者不受信任，为安全起见已拒绝启动。请用官方启动器校验游戏文件或重新安装后重试。',
      notfound:'请在游戏设置的「基本信息」中点「定位游戏」。',
      running:'目标进程已在运行，启动按钮已锁定。'
    };
    return {
      state:long ? 'SelectionRequired' : state, launchLabel:long ? '需要选择安装' : launchLabel,
      readySummary:visual === 'ready' ? '官方启动器 1.3.2 · 签名已核对' : null,
      detail:long ? '请在游戏设置的「基本信息」中点「定位游戏」，选择要使用的安装目录。' : details[visual],
      check:long ? '需要选择安装' : visual === 'ready' ? '检查通过' : launchLabel,
      checkTime:key === 'real-blocked' ? '今天 06:45' : '今天 06:43', checkCount:null,
      artName:long ? '2026-11-26_21-14-08_花愿镇的黄昏与晚风' : artName,
      profile:profiles.find(p => p.id === current)?.name ?? null, platform:'Windows',
      profiles:profiles.map(p => ({...p, current:p.id === current, available:p.id === current,
        reason:p.id === current ? '已发现安装' : '未发现安装'})), discoveryTime:'今天 06:43',
      officialDuration:running || long ? '1284 小时' : null, localDuration:running || long ? '96.5 小时' : null,
      running, sessionMinutes:running ? 42 : null, checklist:null, photoTotal:null, lastSessionPhotos:null,
      taskText:long ? '正在计算 SHA-256 · 20480/40960' : running ? '正在导入共享对象 · 1024/2048' : null,
      progress:running || long ? 50 : null,
      photos:(long ? captures.slice(-1) : captures).map(([id,fileName,time]) => ({id,fileName,time,thumbnail:assets+id}))
    };
  }
  const css = document.createElement('link');
  css.rel = 'stylesheet';
  css.href = '../../../Web.dev/journal/cases.css';
  await new Promise(resolve => {
    css.onload = resolve;
    document.head.append(css);
  });
  document.body.dataset.bg = cover;
  $('.titlebar').insertAdjacentHTML('beforeend', `<div class="caption" inert>
    <button><svg viewBox="0 0 11 11"><path d="M0 5.5h11"/></svg></button>
    <button><svg viewBox="0 0 11 11"><rect x=".5" y=".5" width="10" height="10"/></svg></button>
    <button><svg viewBox="0 0 11 11"><path d="M0 0l11 11M11 0L0 11"/></svg></button></div>`);
  await showCase({
    state, visual, launchLabel, detail, check, checkTime:visual === 'notfound' ? '' : '21:02',
    enabled:visual === 'ready', busy:visual === 'launching', readySummary:detail,
    closeVisible:visual === 'running', closeEnabled:visual === 'running', closeLabel:'关闭游戏',
    nextActions:visual === 'blocked' ? ['status','recheck'] : visual === 'notfound' ? ['locate','rediscover'] : [],
    date:'2026-11-26', profile:profiles[0].name, profiles, discoveryTime:'今天 21:02',
    officialDuration:'128 小时', taskText:'后台 · 合并三渠道存储', progress:62, photos,
    background:{type:'image',url:assets+file}, theme, accent, onAccent:'#fbf6ea',
    palette:['#134455',accent], artSource:cover === 'photo05' ? '自定义背景' : '内置原画', artName, still:true,
    ...(key.startsWith('real-') ? realState() : {})
  });
  if (key === 'home-tide-running') {
    [...document.querySelectorAll('.pola')].slice(0,2).forEach(b => {
      b.classList.add('now');
      b.insertAdjacentHTML('beforeend','<b>本次游玩</b>');
    });
  }
  if (key === 'switcher') {
    $('#profs [data-profile-id="bili"] i').textContent = '未安装';
    $('#profs [data-profile-id="steam"] i').textContent = '可启动';
    switchProfiles(true, false);
  }
})();
