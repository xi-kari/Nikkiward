'use strict';
(() => {
  const assets = 'file:///D:/Projects/nikkiward-worktrees/_briefs/redesign/concepts/assets/';
  const covers = {
    tide: ['bg-tide.jpg', '海月', '内置原画', 'dark', '#A9DBFF', '#0A1322'],
    blossom: ['bg-blossom.jpg', '花信', '内置原画', 'light', '#B43B56', '#FFF8F2'],
    photo05: ['photo-05.jpg', '荷塘夜色', '来自相册 · 11 月 26 日', 'dark', '#F3BFAE', '#1D1310']
  };
  const cases = {
    'real-ready': ['tide','ready'],
    'real-blocked': ['blossom','blocked'],
    'real-notfound': ['tide','notfound'],
    'real-running': ['tide','running'],
    'real-long': ['photo05','blocked'],
    'switcher': ['tide','ready'],
    'home-tide': ['tide','ready'],
    'home-blossom': ['blossom','ready'],
    'home-photo05': ['photo05','ready'],
    'home-tide-running': ['tide','running'],
    'home-tide-launching': ['tide','launching'],
    'home-tide-notfound': ['tide','notfound'],
    'home-blossom-blocked': ['blossom','blocked']
  };
  const key = window.panelHomeCase ?? new URLSearchParams(location.search).get('mock');
  if (!Object.hasOwn(cases, key)) return;
  const [cover, visual] = cases[key];
  const [src, artName, artSource, theme, accent, onAccent] = covers[cover];
  const states = {
    ready: ['Ready', '启动游戏', '启动时会弹出 Windows 管理员确认', '全部通过', '今天 21:40 检查'],
    launching: ['Launching', '正在启动', '请在 Windows 的管理员确认里点「是」；点「否」会回到这里', '全部通过', '今天 21:40 检查'],
    running: ['Running', '游戏运行中', '已运行 42 分钟 · 启动器已安静', '全部通过', '游戏运行中 · 动效已收敛'],
    blocked: ['SignatureUntrusted', '签名验证未通过', '这是安全拒绝，不是故障。请用官方启动器校验或重装，再回来重新检查。', '1 项未通过', '签名验证 · 今天 21:40 检查'],
    notfound: ['NotInstalled', '未发现安装', '常见位置里没有找到这个档案的游戏。可以手动指给 Nikkiward。', '未发现安装', '今天 21:40 检查']
  };
  const [state, launchLabel, detail, check, checkTime] = states[visual];
  const photos = [
    ['p01', 'photo-01.jpg', '21:31:52'], ['p05', 'photo-05.jpg', '21:14:07'],
    ['p04', 'photo-04.jpg', '20:58:40'], ['tide', 'bg-tide.jpg', '20:41:15']
  ].map(([id, file, time]) => ({
    id, thumbnail: assets + file, fileName: '2026_11_26_' + time.replaceAll(':','_') + '.jpeg',
    time: '2026 年 11 月 26 日 ' + time.slice(0,5)
  }));
  const css = document.createElement('link');
  css.rel = 'stylesheet';
  css.href = '../../../Web.dev/atelier/cases.css';
  document.head.append(css);
  $('.mast').insertAdjacentHTML('beforeend', `<div class="winbtns" aria-hidden="true">
      <button tabindex="-1"><svg viewBox="0 0 10 10"><path d="M0 5h10"/></svg></button>
      <button tabindex="-1"><svg viewBox="0 0 10 10"><rect x=".5" y=".5" width="9" height="9"/></svg></button>
      <button tabindex="-1"><svg viewBox="0 0 10 10"><path d="M0 0l10 10M10 0L0 10"/></svg></button>
    </div>`);
  const sample = {
    nextActions:visual === 'blocked' ? ['status','recheck'] : visual === 'notfound' ? ['locate','rediscover'] : [],
    discoveryTime:"今天 21:40", profiles:[
      {id:'cn',channel:'官方',name:'国服 · 官方',available:true,current:true,reason:'可启动 · 官方启动器 1.6.2'},
      {id:'bili',channel:'哔哩哔哩',name:'国服 · 哔哩哔哩',available:false,current:false,reason:'未发现安装 · 可在游戏设置里定位'},
      {id:'steam',channel:'Steam',name:'国际服 · Steam',available:true,current:false,reason:'可启动 · 经 Steam 启动'}
    ],
    background:{type:"image",url:assets + src}, state, visual, launchLabel, detail, readySummary:'官方启动器 1.6.2 · 签名已核对',
    enabled:visual === 'ready', busy:visual === 'launching', closeVisible:visual === 'running',
    closeEnabled:visual === 'running', closeLabel:'关闭游戏', profile:'国服 · 官方', profileOpen:false,
    channel:'国服', check, checkTime, checkCount:['ready','launching'].includes(visual) ? 6 : null,
    officialDuration:'128h', journalSnapshotDate:'2026-11-25', localDuration:null,
    checklist:{completed:2,total:3,next:'拍一张晨曦滤镜'}, photoTotal:93, lastSessionPhotos:3,
    taskText:'后台 · 正在合并三渠道存储', progress:62,
    artName, artSource, theme, accent, onAccent, still:true, photos, date:'2026-11-26', issue:'二〇二六年十一月号'
  };
  if (key.startsWith('real-')) {
    const missing = key === 'real-notfound', custom = key === 'real-long', running = key === 'real-running';
    const times = ['今天 19:00','昨天 23:40','11-21 19:00','11-03 04:45'];
    const files = ['2026-11-26_19-00-08.png','2026-11-25_23-40-08.png','2026-11-21_19-00-08.png','2026-11-03_04-45-08.png'];
    const recent = photos.map((p,i) => ({...p,fileName:files[i],time:times[i]}));
    const reasons = {
      'real-ready':'检测到启动器版本 1.3.2，组件签名验证通过；点击后构建瞬时 plan 并请求 UAC 提权。',
      'real-blocked':'游戏组件的数字签名无效或签名者不受信任，为安全起见已拒绝启动。请用官方启动器校验游戏文件或重新安装后重试。',
      'real-notfound':'请在游戏设置的「基本信息」中点「定位游戏」。',
      'real-running':'目标进程已在运行，启动按钮已锁定。',
      'real-long':'请在游戏设置的「基本信息」中点「定位游戏」，选择要使用的安装目录。'
    };
    Object.assign(sample, {
      detail:reasons[key], readySummary:visual === 'ready' ? '官方启动器 1.3.2 · 签名已核对' : null,
      profile:missing ? null : custom ? '国际服 · Steam' : '国服 · 官方', channel:custom ? '国际服' : missing ? '' : '国服',
      profiles:sample.profiles.map(p => {
        const current = !missing && p.id === (custom ? 'steam' : 'cn');
        return {...p,current,available:current,reason:current ? '已发现安装' : '未发现安装'};
      }),
      state:custom ? 'SelectionRequired' : state, launchLabel:custom ? '需要选择安装' : launchLabel,
      check:custom ? '需要选择安装' : visual === 'ready' ? '检查通过' : launchLabel,
      checkTime:key === 'real-blocked' ? '今天 06:45' : '今天 06:43', checkCount:null,
      officialDuration:running || custom ? '1284 小时' : null, localDuration:running || custom ? '96.5 小时' : null,
      sessionMinutes:running ? 42 : null, journalSnapshotDate:null, discoveryTime:null, checklist:null,
      photoTotal:null, lastSessionPhotos:null, photos:custom ? recent.slice(-1) : recent,
      taskText:running ? '正在导入共享对象 · 1024/2048' : custom ? '正在计算 SHA-256 · 20480/40960' : null,
      progress:running || custom ? 50 : null,
      artName:custom ? '2026-11-26_21-14-08_花愿镇的黄昏与晚风' : artName, artSource:custom ? '自定义背景' : artSource
    });
  }
  if (key === 'home-tide-running') win.classList.add('quiet-sample');
  show(sample).then(() => { if (key === 'switcher') switchProfiles(true); });
})();
