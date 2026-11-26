registerSettingsSection('Gallery',(ui,s) => {
  function button(label,op) {
    const b = ui.button(label,() => ui.action('Gallery',op));
    b.disabled = !s.actions.includes(op);
    return b;
  }
  function controls(...items) {
    const group = ui.element('div',null,'actions');
    group.append(...items);
    return group;
  }
  ui.group(s.profile || '当前图库');
  ui.row(s.customRoot ? '自定义图库目录' : '默认图库目录',controls(button('选择目录','choose'),button('恢复默认','reset')),s.root);
  ui.row('浏览照片',button('打开相册','open'));
  ui.row('照片工坊导出目录',button('选择目录','exports'),s.exportRoot);
  ui.group('收藏保护');
  ui.toggle('保存收藏照片的保护副本','protection',s.enabled,s.protectionStatus).disabled = !s.actions.includes('change');
  ui.row('保护目录',controls(button('选择目录','chooseProtection'),button('打开','openProtection')),s.protection);
  ui.row('保护副本',controls(button('校验','verify'),button('清理未收藏副本','clean')));
  ui.group('缩略图缓存');
  ui.row(s.cache,controls(button('刷新统计','refresh'),button('清除缓存','clearCache')),s.cachePath);
  ui.group('外部工具');
  ui.row('NikkiGallery',button('选择程序','associate'),s.tool);
  ui.row(s.toolStatus,controls(button('打开工具','openTool'),button('解除关联','disconnect')));
  if (ui.entering) ui.action('Gallery','refresh');
});
