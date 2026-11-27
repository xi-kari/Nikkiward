registerSettingsSection('Status',(ui,s) => {
  ui.command('启动检查','查看运行状态','open',s.status);
});
