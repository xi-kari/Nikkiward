'use strict';
(() => {
  const assets = 'file:///D:/Projects/nikkiward-worktrees/_briefs/redesign/concepts/assets/';
  const cases = {
    'home-tide':['tide','ready'], 'home-blossom':['blossom','ready'],
    'home-tide-running':['tide','running'], 'home-blossom-blocked':['blossom','blocked'],
    'home-tide-launching':['tide','launching'], 'home-photo05-notfound':['photo05','notfound'],
    'real-ready':['tide','ready'], 'real-blocked':['blossom','blocked'],
    'real-notfound':['tide','notfound'], 'real-running':['tide','running'], 'real-long':['photo05','blocked'],
    'home-tide-idle':['tide','ready'], switcher:['tide','ready'], palette:['tide','ready']
  };
  const key = new URLSearchParams(location.search).get('mock');
  if (!Object.hasOwn(cases,key)) return;
  const [bg,visual] = cases[key];
  const covers = {
    tide:['bg-tide.jpg','海月','内置原画','dark','#a8d1ff'],
    blossom:['bg-blossom.jpg','花信','内置原画','light','#b3355d'],
    photo05:['photo-05.jpg','11月24日 22:41','你的照片','dark','#a6e3ee']
  };
  const [src,artName,artSource,theme,accent] = covers[bg];
  const summary = '官方启动器 1.6.2 · 签名已核对';
  const states = {
    ready:['Ready','启动游戏',summary,'全部通过',summary],
    launching:['Launching','正在启动','等待 Windows 管理员确认\n在确认框里点「否」不算出错，按钮会恢复可点。','全部通过',summary],
    running:['Running','游戏运行中','本次游玩 00:42:17\n启动器已安静下来：动效收敛，画面静止。','全部通过',summary],
    blocked:['SignatureUntrusted','签名验证未通过','这是安全拒绝，不是故障\n签名和官方发布的对不上。请用官方启动器校验或重装。','1 项未通过','官方启动器 1.6.2 · 签名未通过'],
    notfound:['NotInstalled','未发现安装','没有找到这个档案的安装\n常见位置都看过了。装在别的盘的话，手动指给我。','等待安装目录','未发现安装']
  };
  const [state,launchLabel,detail,check,readySummary] = states[visual];
  const profiles = [
    ['cn','国服 · 官方','官方',true,'可启动 · '+summary],
    ['bili','国服 · 哔哩哔哩','哔哩哔哩',false,'未发现安装 · 可手动定位'],
    ['steam','国际服 · Steam','Steam',false,'渠道暂不支持启动 · 相册与手账可用']
  ].map(([id,name,channel,available,reason]) => ({id,name,channel,available,reason,current:id === 'cn'}));
  const photos = [
    ['photo-01.jpg','20261126_214702.jpg','2026.11.26 21:47:02'],
    ['photo-fae.jpg','20261126_213318.jpg','2026.11.26 21:33:18'],
    ['photo-05.jpg','20261126_211140.jpg','2026.11.26 21:11:40'],
    ['photo-04.jpg','20261124_224107.jpg','2026.11.24 22:41:07']
  ].map(([id,fileName,time]) => ({id,fileName,time,thumbnail:assets+id}));
  if (key.startsWith('real-')) {
    const long = key === 'real-long';
    const ready = visual === 'ready';
    const absent = visual === 'notfound';
    const running = visual === 'running';
    const times = ['今天 19:00','昨天 23:40','11-21 19:00','11-03 04:45'];
    const files = ['2026-11-26_19-00-08.png','2026-11-25_23-40-18.png','2026-11-21_19-00-08.png','2026-11-03_04-45-07.png'];
    const recent = photos.map((p,i) => ({...p,time:times[i],fileName:files[i]}));
    const options = profiles.map(p => {
      const current = !absent && p.id === (long ? 'steam' : 'cn');
      return {...p,current,available:current,reason:current ? '已发现安装' : '未发现安装'};
    });
    const reasons = {
      'real-ready':'检测到启动器版本 1.3.2，组件签名验证通过；点击后构建瞬时 plan 并请求 UAC 提权。',
      'real-blocked':'游戏组件的数字签名无效或签名者不受信任，为安全起见已拒绝启动。请用官方启动器校验游戏文件或重新安装后重试。',
      'real-notfound':'请在游戏设置的「基本信息」中点「定位游戏」。',
      'real-running':'目标进程已在运行，启动按钮已锁定。',
      'real-long':'请在游戏设置的「基本信息」中点「定位游戏」，选择要使用的安装目录。'
    };
    const label = long ? '需要选择安装' : launchLabel;
    show({
      state:long ? 'SelectionRequired' : state, visual, launchLabel:label, detail:reasons[key],
      enabled:ready, busy:false, closeVisible:running, closeEnabled:running, closeLabel:'关闭游戏',
      readySummary:ready ? '官方启动器 1.3.2 · 签名已核对' : null, check:ready ? '检查通过' : label,
      checkTime:key === 'real-blocked' ? '今天 06:45' : '今天 06:43', checkCount:null,
      nextActions:absent ? ['locate','rediscover'] : visual === 'blocked' ? ['status','recheck'] : [],
      profile:options.find(p => p.current)?.name ?? null, profiles:options, channel:long ? '国际服' : '国服',
      platform:'Windows', discoveryTime:null, profileOpen:false, running, sessionMinutes:running ? 42 : null,
      officialDuration:running || long ? '1284 小时' : null, localDuration:running || long ? '96.5 小时' : null,
      journalSnapshotDate:null, taskText:running ? '正在导入共享对象 · 1024/2048' : long ? '正在计算 SHA-256 · 20480/40960' : null,
      progress:running || long ? 50 : null, checklist:null, tools:null, photoTotal:null, lastSessionPhotos:null,
      photos:long ? recent.slice(-1) : recent, artName:long ? '2026-11-26_21-14-08_花愿镇的黄昏与晚风' : artName,
      artSource:long ? '自定义背景' : artSource, theme, accent, contrastPassed:true, palette:['#1d5a78','#a6e3ee'],
      background:{type:'image',url:assets+src}, still:true
    });
    return;
  }
  if (key === 'palette') commands.find(([, , action]) => action === 'photo')[1] = '照片工坊 · 最近一张';
  show({
    state,visual,launchLabel,detail,readySummary,check,checkTime:'今天 14:02',
    nextActions:visual === 'blocked' ? ['recheck'] : visual === 'notfound' ? ['locate'] : [],
    enabled:visual === 'ready',busy:visual === 'launching',running:visual === 'running',
    sessionMinutes:visual === 'running' ? 42 : null,closeVisible:visual === 'running',
    closeEnabled:visual === 'running',closeLabel:'关闭游戏',profile:profiles[0].name,profiles,
    discoveryTime:'今天 14:02',officialDuration:'128h',localDuration:'本机 96.5 h',journalSnapshotDate:'2026-11-25',
    photos,lastSessionPhotos:3,taskText:'合并存储',progress:62,artName,artSource,theme,accent,
    palette:['#05070d','#1d5a78','#a6e3ee','#f0c8c8'],contrastPassed:true,
    background:{type:'image',url:assets+src},still:true
  }).then(() => {
    $('#sessionShots').replaceChildren(...withNumbers('本次游玩 3 张'));
    $('#taskSub').textContent = '不影响启动';
    if (key === 'home-tide-idle') setIdle(true);
    if (key === 'palette' || key === 'switcher') setOverlay(key,false);
  });
})();
