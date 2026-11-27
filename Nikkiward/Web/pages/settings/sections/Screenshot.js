registerSettingsSection('Screenshot',(ui,s) => {
  const action = (label,op) => ui.button(label,() => ui.action('Screenshot',op));
  ui.group('截图保存');
  ui.row('截图目录',action('选择目录','choose'),s.folder);
  ui.row('已保存的截图',action('打开文件夹','open')).disabled = !s.canOpen;
  const key = ui.row('截图快捷键',ui.button(s.hotkey,() => ui.action('Hotkeys','record',{field:'screenshot'})));
  key.setAttribute('aria-label','截图快捷键，'+s.hotkey+'，录制快捷键');
  key.setAttribute('aria-haspopup','dialog');
  ui.select('图片格式','format',s.format,[['Png','PNG'],['Avif','AVIF'],['JpegXl','JPEG XL']]);
  if (s.format !== 'Png')
    ui.select('图片质量','quality',s.quality,[['Medium','中'],['High','高'],['Lossless','无损']]);
  ui.group('截图处理');
  ui.toggle('色彩管理','color',s.color,'应用于 SDR 输出，HDR 原图保留其色彩信息。');
  ui.toggle('复制截图到剪贴板','copy',s.copy);
  ui.toggle('同时保存 SDR 图片','sdr',s.sdr,'截取 HDR 画面时，额外保存一张 SDR 图片。');
  ui.row('测试截图',action('截取当前显示器','test'),'将保存启动器所在显示器上的所有可见内容，包括其他窗口。开启自动复制后，截图文件也会复制到剪贴板。');
  ui.row('相册缩略图缓存',action('清除缓存','clear'));
});
