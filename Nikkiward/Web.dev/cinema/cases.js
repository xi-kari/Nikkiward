'use strict';
(() => {
  const assets = 'file:///D:/Projects/nikkiward-worktrees/_briefs/redesign/concepts/assets/';
  const cases = {
    'home-tide':['tide','ready'], 'home-blossom':['blossom','ready'],
    'home-tide-running':['tide','running'], 'home-blossom-blocked':['blossom','blocked'],
    'home-tide-launching':['tide','launching'], 'home-photo05-notfound':['photo05','notfound'],
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
