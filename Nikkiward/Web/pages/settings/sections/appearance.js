registerSettingsSection('appearance',(ui,s,state) => {
  ui.group('首页皮肤');
  const cards = ui.element('div',null,'skins');
  for (const skin of state.skins) {
    const b = ui.button(skin.label,() => ui.save('skin',skin.id));
    const img = ui.element('img');
    img.src = (location.protocol === 'file:' ? '../../Assets/HomeSkins/' : '../art/HomeSkins/')+skin.id+'.jpg';
    img.alt = '';
    b.prepend(img);
    b.dataset.focus = skin.id;
    b.setAttribute('aria-pressed',String(skin.id === s.skin));
    cards.append(b);
  }
  ui.add(cards);

  ui.group('主题与强调色');
  ui.toggle('界面随画取色','adaptive',s.adaptive,'按背景判明暗、取强调色；对比度不达标时回退到固定配色。');
  ui.select('主题','theme',s.theme,[['FollowArtwork','跟随美术'],['WarmLight','暖亮'],['WarmDark','暖暗']]);
  ui.select('强调色模式','accentMode',s.accentMode,[['Adaptive','自适应'],['Fixed','固定']]);
  ui.select('固定强调色','fixedAccent',s.fixedAccent,[['Blush','腮红'],['Gold','金色'],['Mint','薄荷'],['Lilac','丁香'],['Clay','陶土'],...(s.fixedAccent === 'custom' ? [['custom','自定义']] : [])]).disabled = s.accentMode === 'Adaptive';
  ui.input('自定义强调色','customAccent','color',s.customAccent,{disabled:s.accentMode === 'Adaptive'});

  ui.group('背景');
  const options = [['background1','海月'],['background2','花信']];
  if (!s.preset) options.push(['',s.source ?? '自定义背景']);
  ui.select('背景美术','preset',s.preset ?? '',options);
  if (s.source) ui.add(ui.element('p',s.source,'hint source'));
  const controls = ui.element('div',null,'actions');
  for (const [op,label] of [['choose','选择图片或视频'],['restore','恢复默认背景']])
    controls.append(ui.button(label,() => ui.action('appearance',op)));
  ui.add(controls);
  ui.toggle('背景轮播','carousel',s.carousel);
  ui.input('间隔（分钟）','interval','number',s.interval,{min:1,max:1440,disabled:!s.carousel});
  ui.toggle('背景视差','parallax',s.parallax);
  ui.toggle('光栅卡效果','hologram',s.hologram);
  ui.toggle('视频背景','video',s.video).disabled = !s.videoAvailable;
  ui.toggle('视频视差','pan',s.pan).disabled = !s.videoAvailable || !s.video;
  ui.input('视频缩放','zoom','range',s.zoom,{min:1,max:2.8,step:0.01,disabled:!s.videoAvailable || !s.video});
  ui.toggle('静态背景实时模糊','blur',s.blur);
  ui.input('玻璃强度','glass','range',s.glass,{min:0,max:1,step:0.01});

  ui.group('首页行为');
  ui.select('鉴赏模式','delay',s.delay,[...new Set([0,8,15,30,60,s.delay])].map(n => [n,n ? n+' 秒后' : '关闭']), '五套首页在启动就绪且闲置时收起界面，只留画和展签。移动鼠标或按键恢复，游戏运行中保持静止。');
  ui.toggle('最近拍摄','recent',s.recent,'在首页画面左下摆出最新几张照片。');
  ui.select('启动游戏后','afterLaunch',state.sections.general.afterLaunch,[['KeepWindow','保持窗口'],['Minimize','最小化'],['HideToTray','隐藏到托盘']],'保持窗口、最小化，或隐藏到托盘。',v => ui.save('afterLaunch',v,'general'));

  ui.group('动效、密度与字体');
  ui.select('减少动效','motion',s.motion,[['Full','跟随系统'],['Reduced','减少'],['Off','关闭']],'跟随 Windows「动画效果」设置；游戏运行时自动收敛。');
  ui.select('界面密度','density',s.density,[['Compact','紧凑'],['Standard','标准'],['Comfortable','舒适']]);
  ui.toggle('标题衬线','serif',s.serif);
});
