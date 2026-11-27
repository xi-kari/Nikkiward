registerSettingsSection('general',(ui,s,state) => {
  ui.select('语言','language',s.language,[['zh-CN','简体中文'],['system','跟随系统']]);
  ui.select('关闭窗口时','close',s.close,[['Exit','完全退出'],['MinimizeToTray','最小化到系统托盘']]);
  ui.select('启动游戏后','afterLaunch',s.afterLaunch,[['KeepWindow','保持窗口'],['Minimize','最小化'],['HideToTray','隐藏到托盘']]);
  ui.input('每日清单重置时间','resetTime','time',s.resetTime);
  ui.toggle('记录本机游玩时长','playTime',s.playTime);
  ui.row('本机游玩记录',ui.button('清除记录',() => ui.action('general','clearTime'))).disabled = !state.canClearTime;
  if (state.playTimeError) ui.add(ui.element('p',state.playTimeError,'hint'));
  ui.row('Windows 透明与动画效果',ui.button('系统设置',() => ui.action('general','effects')));
  ui.add(ui.button('帮助改进 Nikkiward 翻译',() => ui.action('about','link',{value:'project'})));
});
