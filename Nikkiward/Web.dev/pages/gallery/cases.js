(async () => {
const PH=[
  {src:'photo-01.jpg',v:0,name:'20261126_214702.jpg',date:'2026.11.26',tm:'21:47:02',dim:'3840 × 2400',fl:35,ap:'2.8',ft:'晨曦',outfit:'繁花礼裙',pal:['#141a10','#5a6b2a','#e0b64c','#d98aa0','#b8323a'],acc:'#e9c66a'},
  {src:'photo-fae.jpg',v:1,name:'20261126_213318.jpg',date:'2026.11.26',tm:'21:33:18',dim:'2160 × 3840',fl:50,ap:'1.8',ft:'花雾',outfit:'花间精灵',pal:['#2a1f2c','#3f6a5c','#c84a5a','#f2b8c0','#f6e8d8'],acc:'#f2b8c0'},
  {src:'photo-05.jpg',v:0,name:'20261126_211140.jpg',date:'2026.11.26',tm:'21:11:40',dim:'3840 × 2160',fl:28,ap:'4.0',ft:'月白',outfit:'荷塘月色',pal:['#05070d','#16384e','#3f8fa8','#a6e3ee','#f0c8c8'],acc:'#a6e3ee'},
  {src:'photo-04.jpg',v:0,name:'20261124_224107.jpg',date:'2026.11.24',tm:'22:41:07',dim:'3840 × 2400',fl:85,ap:'1.4',ft:'旧金',outfit:'金纱礼服',pal:['#0c0d12','#4a4636','#a89a78','#e8dcc0','#f4e6e0'],acc:'#e8dcc0'}
];
const G=[
 {d:'2026.11.26',t:'今天',n:3,ses:1,it:[[0,1.6],[1,.5625,0,0,1],[2,1.78]]},
 {d:'2026.11.24',t:'周二',n:12,it:[[3,1.6,0,0,1],[2,1.2,'170%','82% 55%'],[0,.8,'260%','46% 52%'],[3,1.5,'150%','30% 20%'],[1,.75,'150%','40% 30%',1],[0,1.3,'190%','78% 60%'],[2,1.78,'130%','10% 40%'],[3,.8,'220%','55% 30%'],[1,.5625],[0,1.78,'120%','50% 20%'],[2,1,'210%','85% 50%',1],[3,1.6,'125%','60% 70%'],[0,1.2,'230%','20% 30%']]},
 {d:'2026.11.20',t:'上周五',n:8,it:[[2,1.6,'150%','60% 60%'],[0,1.78,'140%','50% 60%'],[1,.75,'180%','50% 60%'],[3,1.2,'180%','40% 40%'],[2,.8,'240%','80% 50%',1],[0,1.6,'160%','30% 80%'],[3,1.78,'130%','50% 30%']]}
];
  const params=new URLSearchParams(location.search), name=params.get('mock'), variant=name.slice(8);
  const favorites=['gallery-blossom','gallery-favorites','gallery-favorites-empty'].includes(name);
  const empty=['gallery-empty','gallery-favorites-empty','gallery-unlocated'].includes(name), noMatch=name==='gallery-nomatch', filtered=name==='gallery-filtered', real=name==='gallery-real';
  const galleryTabs=[['all','全部',1284],['category:mode','拍照模式',986],['category:screen','游戏截图',241],['bursts','连拍',57]];
  const favoriteCounts=[86,71,12,3];
  const tabs=galleryTabs.map(([id,name,count],i)=>({id,name,count:empty?0:favorites?favoriteCounts[i]:count}));
  if(name==='gallery-more')tabs.push(...[['category:low','低清副本',45],['category:cloud','云端照片',8],
    ['category:magazine','杂志照片',4],['category:collage','拼贴照片',3],['favorites','收藏',86],
    ['current','本次游玩',3],['exports','导出的副本',2]].map(([id,name,count])=>({id,name,count})));
  const days=G.map((g,gi)=>{
    let photos=g.it.map(([n,ratio,zoom,position,star],i)=>{
      const p=PH[n];
      return {id:gi+'-'+i,name:p.name,time:p.tm.slice(0,5),filter:p.ft,lens:p.fl,ratio,zoom,position,
        favorite:!!star,src:'../../Web.dev/pages/gallery/assets/'+p.src,burst:gi===1};
    }).filter(p=>(!favorites||p.favorite)&&(!filtered||p.filter==='晨曦'));
    if(favorites&&gi===1)photos[0].protectedCopy=true;
    if(real&&gi===0)photos=Array.from({length:148},(_,i)=>({...photos[i%photos.length],id:'long-'+i,
      name:'绮想手记_第'+i+'张_花间精灵与夜色下的水畔漫游_超长照片文件名.jpg',ratio:[.38,2.5,1,1.6,.8][i%5]}));
    if(real)photos=photos.map(({src,...p})=>({...p,thumbnail:src}));
    return {date:g.d,weekday:g.t,count:favorites||filtered||real?photos.length:g.n,current:!!g.ses,photos};
  }).filter(g=>g.photos.length);
  const state={version:'mock-'+name,tab:'all',text:noMatch?'未匹配的滤镜':'',sort:'newest',filter:filtered?'晨曦':null,
    color:filtered?'14726732':null,filters:{'晨曦':24,'花雾':15,'月白':36,'旧金':12},clothes:{'105602':21},colors:{14726732:18},
    missingRoot:name==='gallery-unlocated',protection:true,tabs,offset:0,next:null,indexing:name==='gallery-indexing',indexed:160,total:1284,
    matches:empty||noMatch?0:days.reduce((sum,g)=>sum+g.photos.length,0),days:empty||noMatch?[]:days};
  if(name==='gallery-by-name'){
    state.sort='name';
    const photos=[days[0].photos[0],days[1].photos[0],days[0].photos[1]].map((p,i)=>({...p,name:['A.jpg','B.jpg','C.jpg'][i]}));
    state.days=[{date:null,photos}];state.matches=photos.length;
    state.tabs=[{id:'all',name:'全部',count:photos.length}];
  }
  await receivePage({type:'state',page:favorites?'favorites':'gallery',module:'gallery',
    skin:name==='gallery-more'?'stitch':['atelier','stitch','journal','wishstar'].includes(variant)?variant:'cinema',
    tone:params.get('tone')==='light'||name==='gallery-blossom'?'light':'dark',state});
  if(name==='gallery-more')Array.from(document.querySelectorAll('.gallery-module .tabs button'))
    .find(button=>button.textContent==='更多')?.click();
  const wc=document.createElement('div');wc.className='wc';wc.setAttribute('aria-hidden','true');
  wc.innerHTML=['M0 5.5h10','M.5.5h9v9h-9z','M0 0l10 10M10 0L0 10'].map(d=>'<span><svg viewBox="0 0 10 10"><path d="'+d+'"/></svg></span>').join('');
  document.body.append(wc);
})();
