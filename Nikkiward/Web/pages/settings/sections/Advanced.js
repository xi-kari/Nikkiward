registerSettingsSection('Advanced',(ui,s,state) => {
  ui.group('开发者工具');
  ui.toggle('开发者模式','developer',s.developer,'显示手账缓存、本地资源、运行状态和诊断工具。');
  ui.group('设置概览');
  const links = ui.element('div',null,'actions');
  for (const item of state.navigation) {
    if (item.id !== 'Advanced' && item.id !== 'about') links.append(ui.button(item.label,() => ui.go(item.id)));
  }
  ui.add(links);
  ui.group('渠道与运行边界');
  ui.facts(s.contract);
});
