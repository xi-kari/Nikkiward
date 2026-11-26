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
  const routes = {general:'general',about:'about',download:'Download','file-management':'FileManagement',advanced:'Advanced',
    developer:'Advanced','journal-cache':'Journal',files:'Files',status:'Status',components:'Components',diagnostics:'Diagnostics',contract:'Contract',
    screenshot:'Screenshot',gallery:'Gallery',plugins:'Plugins',hotkeys:'Hotkeys','hotkeys-recording':'Hotkeys',gamepad:'Gamepad'};
  const initial = routes[variant] ?? 'appearance';
  const developerItems = [['Journal','奇想手账与缓存'],['Files','本地资源'],['Status','运行状态'],['Components','组件验证'],['Diagnostics','脱敏诊断'],['Contract','隔离契约']];
  const developer = variant === 'developer' || developerItems.some(([id]) => id === initial);
  const navigation = [['appearance','外观'],['general','通用'],['Download','下载'],['FileManagement','文件管理'],['Screenshot','游戏截图'],
    ['Gallery','相册'],['Plugins','插件'],['Hotkeys','键盘快捷键'],['Gamepad','手柄增强'],['Advanced','高级'],
    ...(developer ? developerItems : []),['about','关于']].map(([id,label]) => ({id,label}));
  const folder = 'D:\\Nikkiward\\Data';
  const contract = {'渠道文件':'仅内容完全相同的游戏资源可共用硬链接。','启动参数':'-skiplauncher 是国服 Windows 的候选参数。','Steam 应用编号':'3164330（公开记录）',
    '订阅编号':'未读取','内容仓库编号':'未读取','构建编号':'未读取','清单编号':'未读取','安装来源':'尚未确定',
    '平台条件':'Windows 入口为 launcher.exe。','进程通信':'公开资料未确认通信流程。'};
  sections.Download = {path:'D:\\Games\\Infinity Nikki',hardLinks:true,limit:0};
  sections.FileManagement = {folder,backup:folder+'\\Backups\\settings-backup.zip',saved:'2026-06-01 14:22:00',
    canOpenBackup:true,busy:false,backgrounds:false,sizes:[44800,6041600,887424,0,204800]};
  sections.Advanced = {developer,contract};
  if (developer) {
    sections.Contract = contract;
    sections.Journal = sections.Files = {'手账登录会话':folder+'\\Journal\\WebView','统计快照':folder+'\\Journal\\Snapshots','公开图片缓存':folder+'\\Journal\\Assets'};
    sections.Status = {status:'已完成启动检查'};
    sections.Components = sections.Diagnostics = {components:[{name:'游戏启动器',status:'已找到',path:'D:\\Games\\Infinity Nikki\\launcher.exe',
      fileVersion:'1.0.0.0',productVersion:'1.0.0.0',signature:'已验证',sha:'65e182af'.repeat(8),metadata:'8.4 MiB'}],
      canExport:true,exportStatus:'尚未导出诊断报告。',validation:'尚无验证记录。',artifact:'当前状态以实时启动检查为准。'};
  }
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
