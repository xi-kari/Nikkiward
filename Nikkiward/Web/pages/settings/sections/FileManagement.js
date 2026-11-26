registerSettingsSection('FileManagement',(ui,s) => {
  ui.group('数据目录');
  if (s.protection) ui.add(ui.element('p',s.protection,'hint'));
  ui.command('当前数据目录','打开文件夹','openFolder',s.folder,s.busy);
  ui.command('切换数据目录','选择文件夹','chooseFolder','选择已有设置的目录。确认后重新启动，原目录中的文件保留。',s.busy);
  ui.group('备份与恢复');
  ui.command('备份设置','创建备份','backup',s.saved ? '上次备份于 '+s.saved : '尚无备份记录',s.busy || !!s.protection);
  ui.command(s.protection ? '恢复设置' : '最近的备份',s.protection ? '选择备份' : '打开备份','openBackup',s.backup,s.busy || (!s.protection && !s.canOpenBackup));
  ui.command(s.protection ? '重置设置' : '删除设置',s.protection ? '重置…' : '删除…','delete','确认后应用将关闭，下次启动时使用默认设置。',s.busy);
  ui.group('日志与缓存');
  ui.command('应用日志','打开文件夹','logs',null,s.busy);
  const labels = ['日志缓存','图片缓存','浏览器缓存','游戏资源缓存','启动器背景缓存'];
  if (s.sizes) ui.facts(Object.fromEntries(labels.map((label,i) => {
    const bytes = s.sizes[i];
    const unit = bytes >= 1048576 ? 1048576 : bytes >= 1024 ? 1024 : 1;
    return [label,(bytes / unit).toLocaleString('zh-CN',{maximumFractionDigits:1})+' '+({1:'B',1024:'KiB',1048576:'MiB'})[unit]];
  })));
  ui.command('缓存占用','刷新','refresh',null,s.busy);
  ui.toggle('同时清理启动器背景缓存','backgrounds',s.backgrounds).disabled = s.busy || !!s.protection;
  ui.command('清理缓存','清理…','clearCache','清理以上缓存，启动器背景缓存按开关处理。',s.busy);
});
