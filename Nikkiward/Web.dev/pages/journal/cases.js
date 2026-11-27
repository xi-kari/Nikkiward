(() => {
  const params = new URLSearchParams(location.search), variant = params.get('mock').slice(8);
  const metrics = [['登录天数','212','天'],['游戏时长','128','小时'],['服装数量','1,946','件']]
    .map(([label,value,unit]) => ({label,value,unit}));
  const state = {
    date:'2026-11-25 20:14', metrics, modules:[], browserNote:'原型不加载网页', enabled:true, since:'08-30',
    play:{total:96.5,week:9.2,days:[1.2,0,2.6,1.8,0,2.9,.7].map((hours,i) => ({hours,label:'四五六日一二三'[i]})),
      recent:[['11-26 21:05','0:42'],['11-24 20:31','2:36'],['11-23 14:10','1:48']].map(([time,duration]) => ({time,duration,channel:'国服 · 官方'}))},
    items:[{id:'1',text:'每日心愿',done:true},{id:'2',text:'拍一张今天的搭配',done:true},{id:'3',text:'周本素材',done:false,note:'周日前'}],
    links:[{id:'map',text:'互动地图'},{id:'wardrobe',text:'服装图鉴'}]
  };
  if (variant === 'empty') Object.assign(state,{date:null,enabled:false,items:[],links:[],play:null,browserNote:null});
  if (variant === 'real') {
    metrics[2].value = '123,456';
    state.since = state.browserNote = null;
    state.play.total = 19872.6;
    state.items[2].text = '整理本周拍摄的全部搭配，补上每套服装的部件名称和取景地点，再挑出最喜欢的一张';
    state.links[0].text = '互动地图 · 花愿镇、微风绿野与祈愿树林全部收集点';
    state.modules = [{title:'探索总览',text:'祈愿树林',rows:[{label:'奇想星',value:'127/150'}]}];
  }
  const skin = ['atelier','stitch','journal','wishstar'].includes(variant) ? variant : 'cinema';
  receivePage({type:'state',page:'journal',module:'journal',skin,tone:params.get('tone') === 'light' ? 'light' : 'dark',state});
  const controls = document.createElement('div');
  controls.className = 'wc';
  controls.setAttribute('aria-hidden','true');
  controls.innerHTML = ['M0 5.5h10','M.5.5h9v9h-9z','M0 0l10 10M10 0L0 10'].map(d =>
    '<span><svg viewBox="0 0 10 10"><path d="'+d+'"/></svg></span>').join('');
  document.body.append(controls);
})();
