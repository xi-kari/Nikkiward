registerSettingsSection('Advanced',(ui,s,state) => {
  ui.group('开发者工具');
  ui.toggle('开发者模式','developer',s.developer,'显示手账缓存、本地资源、运行状态和诊断工具。');
  if (s.profiles && ui.showChannelTools) {
    const tools = s.profiles;
    const details = ui.element('details');
    details.open = true;
    details.append(ui.element('summary','档案与渠道工具'));
    for (const [label,value] of Object.entries(tools.facts)) details.append(ui.element('p',label + ' · ' + value));
    const actions = ui.element('div',null,'actions');
    function command(label,tool,enabled,id) {
      const button = ui.button(label,() => ui.action('Advanced','profile',{tool,id}));
      button.disabled = !enabled;
      actions.append(button);
    }
    for (const p of tools.candidates) {
      details.append(ui.element('p',[p.name,p.id,p.channel,p.capability,p.root].join(' · ')));
      command(p.name,'select',!p.selected,p.id);
    }
    for (const [label,tool,enabled] of [
      ['自动发现','discover',tools.discover],['游戏根','game',tools.discover],['启动器根','launcher',tools.discover],
      ['存储位置','root',tools.plan],['预演','plan',tools.plan],[tools.buildLabel,'build',tools.build],
      ['激活渠道','activate',tools.activate],['撤销激活','rollback',tools.rollback]
    ]) command(label,tool,enabled);
    details.append(actions);
    for (const e of tools.evidence) details.append(ui.element('p',[e.Title,e.Source,e.State,e.Detail,e.Capability].join(' · ')));
    ui.add(details);
  }
  ui.group('设置概览');
  const links = ui.element('div',null,'actions');
  for (const item of state.navigation) {
    if (item.id !== 'Advanced' && item.id !== 'about') links.append(ui.button(item.label,() => ui.go(item.id)));
  }
  ui.add(links);
  ui.group('渠道与运行边界');
  ui.facts(s.contract);
});
