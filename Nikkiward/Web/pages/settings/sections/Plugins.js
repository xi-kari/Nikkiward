registerSettingsSection('Plugins',(ui,s) => {
  function button(label,op,enabled) {
    const b = ui.button(label,() => ui.action('Plugins',op));
    b.disabled = !enabled;
    return b;
  }
  ui.row('相册插件',button('导入插件','import',s.canImport),[s.status,s.version].filter(Boolean).join(' · '));
  ui.row('运行插件',button('打开插件','open',s.canOpen));
  ui.row('移除安装文件',button('卸载插件','uninstall',s.canUninstall));
});
