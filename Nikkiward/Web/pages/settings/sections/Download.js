registerSettingsSection('Download',(ui,s) => {
  ui.group('安装位置');
  ui.command('默认游戏安装路径','选择文件夹','choose',s.path || '尚未指定');
  ui.add(ui.element('p','新建渠道安装时使用此目录，已有游戏文件保留在原处。','hint'));
  if (s.path) {
    ui.command('查看安装位置','打开文件夹','open');
    ui.command('清除默认路径','清除','clear');
  }
  ui.group('文件与速度');
  ui.toggle('使用硬链接','hardLinks',s.hardLinks,'允许内容相同的游戏资源共享磁盘空间。');
  const limit = ui.input('限速（KB/s）','limit','number',s.limit,{min:0,max:10000000,step:100});
  limit.onchange = () => {
    if (limit.validity.badInput) { limit.reportValidity(); return; }
    const value = Math.max(0,Math.min(10000000,Number(limit.value)));
    const lower = Math.floor(value);
    const rounded = value - lower === 0.5 ? lower + lower % 2 : Math.round(value);
    limit.value = String(rounded);
    ui.save('limit',rounded);
  };
  ui.add(ui.element('p','0 表示不限速，更改后用于后续的渠道文件导入。','hint'));
});
