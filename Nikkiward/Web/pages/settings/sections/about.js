registerSettingsSection('about',(ui,s) => {
  for (const [name,value] of [['作者 / 维护者','Xikari · @xi-kari'],['当前版本',s.version],['运行环境',[s.runtime,s.distribution].filter(Boolean).join(' · ')],['构建提交',s.commit]])
    if (value) ui.row(name,ui.element('span',value,'value'));
  ui.select('更新通道','channel',s.channel,[['Stable','稳定版'],['Preview','预览版']],null,value => ui.action('about','channel',{value})).disabled = s.checking;
  ui.row('更新',ui.check());
  if (s.status) ui.add(ui.element('p',s.status,'hint'));
  if (s.release) ui.add(ui.button('查看发布版本',() => ui.action('about','release')));
  const links = ui.element('div',null,'actions');
  for (const [value,label] of [['project','项目主页'],['issues','问题反馈'],['releases','发行记录'],['privacy','隐私说明'],['license','源代码许可'],['notices','第三方声明']])
    links.append(ui.button(label,() => ui.action('about','link',{value})));
  ui.add(links);
});
