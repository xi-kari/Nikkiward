registerSettingsSection('Hotkeys',(ui,s) => {
  for (const [field,label] of [['main','显示主窗口'],['screenshot','游戏截图']]) {
    const b = ui.row(label,ui.button(s[field],() => ui.action('Hotkeys','record',{field})));
    b.setAttribute('aria-label',label+'，'+s[field]+'，录制快捷键');
    b.setAttribute('aria-haspopup','dialog');
  }
  ui.add(ui.element('p','单独使用功能键时，游戏内响应可能需要管理员权限。','hint'));
});
