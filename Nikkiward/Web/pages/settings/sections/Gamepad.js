registerSettingsSection('Gamepad',(ui,s) => {
  const enabled = ui.toggle('启用手柄增强','enabled',s.enabled,s.status);
  enabled.disabled = !s.canEdit;
  enabled.closest('.row').querySelector('p').dataset.gamepadStatus = '';
  if (s.runtimeMissing)
    ui.row('缺少手柄运行组件',ui.button('获取运行组件',() => ui.action('Gamepad','runtime')));
  ui.toggle('长按导航键唤出主窗口','longPress',s.longPress,'按住 0.6 秒唤出主窗口。关闭后，短按映射立即生效。').disabled = !s.canEdit;
  ui.group('按键映射');
  for (const [prefix,label] of [['guide','导航键'],['share','分享键']]) {
    const field = prefix+'Action';
    ui.select(label,field,s[field],[['None','无操作'],['MapKeys','模拟键盘按键']],prefix === 'guide' ? '启用映射会接管 Xbox Game Bar 的导航键。' : '分享键仍可能触发 Xbox Game Bar 截图。').disabled = !s.canEdit;
    if (s[field] === 'MapKeys')
      ui.input(label+'对应按键',prefix+'Keys','text',s[prefix+'Keys'] || '',{placeholder:'如 Ctrl Alt F1',disabled:!s.canEdit});
  }
});
