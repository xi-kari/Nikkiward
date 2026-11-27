'use strict';
(() => {
  const assets = '../../../Web.dev/assets/';
  const cases = {
    'home-tide':['tide','ready'],
    'home-blossom':['blossom','ready'],
    'home-tide-running':['tide','running'],
    'home-blossom-blocked':['blossom','blocked'],
    'x-home-photo05-launching':['photo05','launching'],
    'x-home-tide-notfound':['tide','notfound'],
    'x-home-blossom-checking':['blossom','checking'],
    'switcher':['tide','ready'],
    'real-ready':['tide','ready'],
    'real-blocked':['blossom','blocked'],
    'real-notfound':['tide','notfound'],
    'real-running':['tide','running'],
    'real-long':['photo05','blocked']
  };
  const overlayCase = window.prepareOverlayCase?.({config:'home-tide',diag:'home-blossom-blocked'});
  const showCase = overlayCase?.show ?? show;
  const key = overlayCase?.homeCase ?? window.panelHomeCase ?? new URLSearchParams(location.search).get('mock');
  if (!Object.hasOwn(cases,key)) return;
  const [bg,visual] = cases[key];
  const covers = {
    tide:['bg-tide.jpg','海月','内置原画','dark',['#c7dbff','#27468f','#0c162e','#f5f2ea']],
    blossom:['bg-blossom.jpg','花信','内置原画','light',['#a13a5c','#f7ccd5','#fff6f4','#8fb0e0']],
    photo05:['photo-05.jpg','夜色荷塘','我的照片','dark',['#aae8f2','#16677f','#071420','#f3c9d0']]
  };
  const states = {
    ready:['Ready','启动游戏','官方启动器 1.6.2 · 签名已核对','通过','今天 14:32'],
    launching:['Launching','正在启动','请在 Windows 的管理员确认里选择「是」','通过','今天 14:32'],
    running:['Running','游戏运行中','已运行 12 分钟 · 启动器已安静下来','通过','今天 14:32'],
    blocked:['SignatureUntrusted','签名验证未通过','这是安全拒绝，不是故障','签名未通过','14:32'],
    notfound:['NotInstalled','未发现安装','常见位置里没有找到「国服 · 官方」','未发现安装','14:32'],
    checking:['Checking','检查安装中','正在核对安装目录、渠道身份与签名…','检查中…','']
  };
  const [file,artName,artSource,theme,palette] = covers[bg];
  const [state,launchLabel,subtitle,check,checkTime] = states[visual];
  const reasons = {
    blocked:'游戏主程序的数字签名没有通过核对，所以 Nikkiward 不会启动它。请用官方启动器校验或重装，然后回来重新检查。',
    notfound:'把游戏所在的文件夹指给 Nikkiward，或者换一个已经装好的档案。'
  };
  const profiles = [
    ['cn','国服 · 官方','官方','可启动 · F:\\InfinityNikki · 官方启动器 1.6.2'],
    ['bili','国服 · 哔哩哔哩','哔哩哔哩','未发现安装 · 可在游戏设置里手动定位'],
    ['steam','国际服 · Steam','Steam','需要选择安装 · 发现 2 个 Steam 库位置']
  ].map(([id,name,channel,reason]) => ({id,name,channel,reason,current:id === 'cn',available:id === 'cn'}));
  const photos = [
    ['p05','photo-05.jpg','21-14-08'], ['p04','photo-04.jpg','20-52-31'],
    ['tide','bg-tide.jpg','19-30-12'], ['fae','photo-fae.jpg','18-05-44']
  ].map(([id,src,time],i) => ({
    id,session:i < 2 ? 'current' : null,thumbnail:assets + src,fileName:'2026-11-26_' + time + '.jpg',time:'2026.11.26  ' + time.slice(0,5).replace('-',':')
  }));
  if (key.startsWith('real-')) {
    $('#btnTools').hidden = true;
    const readySummary = '官方启动器 1.3.2 · 签名已核对';
    const recent = [
      ['p05','photo-05.jpg','2026-11-26_19-00-08.png','今天 19:00'],
      ['p04','photo-04.jpg','2026-11-25_23-40-12.png','昨天 23:40'],
      ['p01','photo-01.jpg','2026-11-21_19-00-08.png','11-21 19:00'],
      ['fae','photo-fae.jpg','2026-11-03_04-45-12.png','11-03 04:45']
    ].map(([id,src,fileName,time]) => ({id,thumbnail:assets + src,fileName,time}));
    const selected = key === 'real-long' ? 'steam' : key === 'real-notfound' ? null : 'cn';
    const options = profiles.map(p => ({
      ...p, current:p.id === selected, available:p.id === selected,
      reason:p.id === selected ? '已发现安装' : '未发现安装'
    }));
    const region = selected === 'steam' ? '国际服' : selected ? '国服' : null;
    const realStates = {
      'real-ready':{detail:'检测到启动器版本 1.3.2，组件签名验证通过；点击后构建瞬时 plan 并请求 UAC 提权。',readySummary,check:'检查通过'},
      'real-blocked':{detail:'游戏组件的数字签名无效或签名者不受信任，为安全起见已拒绝启动。请用官方启动器校验游戏文件或重新安装后重试。',checkTime:'今天 06:45'},
      'real-notfound':{detail:'请在游戏设置的「基本信息」中点「定位游戏」。'},
      'real-running':{detail:'目标进程已在运行，启动按钮已锁定。',sessionMinutes:42,
        taskText:'正在导入共享对象 · 1024/2048',progress:50,officialDuration:'1284 小时',localDuration:'96.5 小时'},
      'real-long':{state:'SelectionRequired',launchLabel:'需要选择安装',check:'需要选择安装',
        detail:'请在游戏设置的「基本信息」中点「定位游戏」，选择要使用的安装目录。',
        artName:'2026-11-26_21-14-08_花愿镇的黄昏与晚风',artSource:'自定义背景',
        taskText:'正在计算 SHA-256 · 20480/40960',progress:50,officialDuration:'1284 小时',localDuration:'96.5 小时',
        palette:['#aae8f2','#16677f'],photos:recent.slice(-1)}
    };
    showCase({
      state,visual,launchLabel,readySummary:null,check:launchLabel,checkTime:'今天 06:43',checkCount:null,
      enabled:visual === 'ready',busy:false,running:visual === 'running',closeVisible:visual === 'running',
      closeEnabled:visual === 'running',closeLabel:'关闭游戏',sessionMinutes:null,
      nextActions:visual === 'blocked' ? ['status','recheck'] : visual === 'notfound' ? ['locate','rediscover'] : [],
      profile:options.find(p => p.current)?.name ?? null,profiles:options,channel:region,platform:[region,'Windows'].filter(Boolean).join(' · '),
      background:{type:'image',url:assets + file},artName,artSource,theme,accent:palette[0],onAccent:theme === 'dark' ? '#0c1530' : '#fff6f4',
      palette:['#ce7b94','#e9a6b9'],contrastPassed:true,still:true,photos:recent,photoTotal:null,lastSessionPhotos:null,
      officialDuration:null,localDuration:null,taskText:null,progress:null,checklist:null,tools:null,...realStates[key]
    });
    return;
  }
  const sheet = document.createElement('link');
  sheet.rel = 'stylesheet';
  sheet.href = '../../../Web.dev/stitch/cases.css';
  document.head.append(sheet);
  $('.top').insertAdjacentHTML('beforeend', '<div class="cap" aria-hidden="true"><span><svg viewBox="0 0 11 11"><path d="M0 5.500h11"/></svg></span><span><svg viewBox="0 0 11 11"><rect x=".5" y=".5" width="10" height="10"/></svg></span><span><svg viewBox="0 0 11 11"><path d="M0 0l11 11M11 0L0 11"/></svg></span></div>');
  showCase({
    state,visual,launchLabel,detail:reasons[visual] || subtitle,readySummary:subtitle,
    enabled:visual === 'ready',busy:['launching','checking'].includes(visual),
    running:visual === 'running',closeVisible:visual === 'running',closeEnabled:true,closeLabel:'关闭游戏',
    nextActions:visual === 'blocked' ? ['recheck','status'] : visual === 'notfound' ? ['locate','rediscover'] : [],
    profiles,profile:profiles[0].name,channel:'国服',platform:'国服 · Windows',check,checkTime,sessionMinutes:12,
    officialDuration:'128h',localDuration:'未开启',taskText:'合并存储',progress:62,photos,currentSessionPhotos:2,
    background:{type:'image',url:assets + file},artName,artSource,theme,accent:palette[0],onAccent:theme === 'dark' ? '#0c1530' : '#fff6f4',
    palette,contrastPassed:true,tools:null,still:true
  }).then(() => {
    if (visual === 'launching') {
      $('#note').classList.add('show');
      $('#reason').hidden = $('#actions').hidden = false;
      $('#reason').textContent = '每次启动都要经过这一步。在确认框里点取消也没关系，按钮会恢复可点。';
      const cancel = document.createElement('button');
      cancel.className = 'btn sm';
      cancel.textContent = '模拟：在确认框里点了取消';
      $('#actions').append(cancel);
    }
    if (visual === 'notfound') {
      $('#cChkLi > span').textContent = '启动检查';
      $('#cChk').textContent = $('#cChk').title = check + ' · ' + checkTime;
    }
    if (key === 'switcher') switchProfiles(true);
  });
})();
