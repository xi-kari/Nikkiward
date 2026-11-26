'use strict';
(async () => {
  const cases = {
    'home-tide':['tide','ready'], 'home-blossom':['blossom','ready'],
    'home-tide-running':['tide','running'], 'home-blossom-blocked':['blossom','blocked'],
    'home-tide-launching':['tide','launching'], 'home-photo05-notfound':['photo05','notfound'],
    switcher:['tide','ready']
  };
  const key = new URLSearchParams(location.search).get('mock');
  if (!Object.hasOwn(cases,key)) return;
  const [cover,visual] = cases[key];
  const assets = 'file:///D:/Projects/nikkiward-worktrees/_briefs/redesign/concepts/assets/';
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
  await show({
    state, visual, launchLabel, detail, check, checkTime:visual === 'notfound' ? '' : '21:02',
    enabled:visual === 'ready', busy:visual === 'launching', readySummary:detail,
    closeVisible:visual === 'running', closeEnabled:visual === 'running', closeLabel:'关闭游戏',
    nextActions:visual === 'blocked' ? ['status','recheck'] : visual === 'notfound' ? ['locate','rediscover'] : [],
    date:'2026-11-26', profile:profiles[0].name, profiles, discoveryTime:'今天 21:02',
    officialDuration:'128 小时', taskText:'后台 · 合并三渠道存储', progress:62, photos,
    background:{type:'image',url:assets+file}, theme, accent, onAccent:'#fbf6ea',
    palette:['#134455',accent], artSource:cover === 'photo05' ? '自定义背景' : '内置原画', artName, still:true
  });
  if (visual === 'running') {
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
