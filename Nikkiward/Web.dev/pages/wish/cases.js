(() => {
  const params = new URLSearchParams(location.search), name = params.get('mock');
  function record(date, name, rarity, pull, distance) {
    return {month:'2026-'+date.slice(0,2),time:date,name,rarity,pull,distance};
  }
  const samples = [
    [record('11/20 20:16','星光礼裙','★★★★',120,14),record('11/16 19:48','月下微光','★★★★★',106,20)],
    [record('11/04 18:35','银色花结','★★★',180,null),record('10/27 16:30','秋日手札','★★★★',142,null)],
    [record('10/13 19:42','绮梦','★★★★',142,null),record('09/28 10:28','花影','★★★',55,null)],
    [record('11/18 10:20','晨雾','★★★★',200,8),record('11/11 12:10','晴空','★★★★★',192,null)]
  ];
  const pools = [
    ['限时共鸣（导入文件第 3 池）','11-05 – 11-20',120,6,14],
    ['限时共鸣（导入文件第 2 池）','10-14 – 11-04',180,9,null],
    ['限时共鸣（导入文件第 1 池）','09-20 – 10-13',142,7,null],
    ['常驻共鸣','长期',200,9,8]
  ].map(([name,period,count,five,distance],i) => ({name,period,count,five,distance,records:samples[i]}));
  const skin = name.slice(5);
  const state = name === 'wish-empty' ? {pools:[]} : {
    date:'2026-11-20',dateLabel:'上次导入',total:642,five:31,average:'20.7',pools,
    importHint:'记录来自你用社区工具导出的 JSON 文件。Nikkiward 只读这个文件，按卡池统计；不会向任何服务器请求你的记录。',
    expanded:name === 'wish-records' ? 1 : undefined
  };
  receivePage({type:'state',page:'wish',module:'wish',skin:['atelier','stitch','journal','wishstar'].includes(skin) ? skin : 'cinema',
    tone:params.get('tone') === 'light' ? 'light' : 'dark',state});
  const controls = document.createElement('div');
  controls.className = 'wc';
  controls.setAttribute('aria-hidden','true');
  controls.innerHTML = ['M0 5.5h10','M.5.5h9v9h-9z','M0 0l10 10M10 0L0 10'].map(d =>
    '<span><svg viewBox="0 0 10 10"><path d="'+d+'"/></svg></span>').join('');
  document.body.append(controls);
})();

