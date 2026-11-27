window.settingsDeveloperViews = {
  storage(ui,paths) {
    ui.group('本机存储');
    ui.facts(paths);
    ui.add(ui.element('p','登录会话由浏览器单独保存。备份和诊断报告不包含登录凭据。','hint'));
    ui.group('手账与缓存');
    ui.command('奇想手账','打开手账','open');
    ui.command('本地缓存','清除…','clear','清除统计快照及公开图片。');
  },
  diagnostics(ui,s,reportFirst) {
    function report() {
      ui.group('诊断报告');
      ui.add(ui.element('p','导出不含命令行、登录凭据、进程内存或网络载荷。','hint'));
      ui.command('脱敏诊断','导出报告','export',s.exportStatus,!s.canExport);
      ui.command('启动渠道验证','查看详情','details',s.validation);
      ui.facts({'验证记录':s.artifact});
    }
    if (reportFirst) report();
    ui.group('本机组件');
    if (!s.components.length) ui.add(ui.element('p','暂无组件检查结果。','hint'));
    for (const c of s.components) {
      ui.group(c.name);
      ui.facts({'检查状态':c.status,'文件位置':c.path,'文件版本':c.fileVersion,'产品版本':c.productVersion,
        '数字签名':c.signature,'SHA-256':c.sha,'文件信息':c.metadata,'问题':c.issue});
    }
    if (!reportFirst) report();
  }
};
