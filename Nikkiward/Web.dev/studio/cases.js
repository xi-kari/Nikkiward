(() => {
  const key=new URLSearchParams(location.search).get('mock');
  const skin=key.slice(7);
  const assets='file:///D:/Projects/nikkiward-worktrees/_briefs/redesign/concepts/assets/';
  const fixtures=[
    ['photo-01.jpg','214702',35,'2.8','晨曦','繁花礼裙',['#141a10','#5a6b2a','#e0b64c','#d98aa0','#b8323a']],
    ['photo-fae.jpg','213318',50,'1.8','花雾','花间精灵',['#2a1f2c','#3f6a5c','#c84a5a','#f2b8c0','#f6e8d8']],
    ['photo-05.jpg','211140',28,'4.0','月白','荷塘月色',['#05070d','#16384e','#3f8fa8','#a6e3ee','#f0c8c8']],
    ['photo-04.jpg','224107',85,'1.4','旧金','金纱礼服',['#0c0d12','#4a4636','#a89a78','#e8dcc0','#f4e6e0']]
  ];
  const order=[0,1,2,3,0,2,3,1,0];
  const rows=Array.from({length:1284},(_,i)=>{
    const k=order[i%order.length];
    const [src,time,lens,aperture,filter,outfit,palette]=fixtures[k];
    const edits=initialEdits();
    edits.frame=key==='studio-adjust'?'off':key==='studio-portrait'?'ink':'on';
    edits.codes=['NK7Q-4MXD-2RJP','','','CAM-35-28-CX'];
    return {id:String(i),portrait:k===1,time:i===4?'21:47:04':time.match(/../g).join(':'),name:'20261126_'+time+'.jpg',date:'2026.11.26 '+time.slice(0,2)+':'+time.slice(2,4),
      url:assets+src,hasParameters:true,lens,aperture,filter,outfit:k===0?[outfit,'金色长卷','花冠','薄纱手套']:[outfit],slots:k===0?['连衣裙','发型','头饰','手饰']:[],palette,edits,folder:'ScreenShot',size:'6.2 MB',dim:k===1?'2160 × 3840':'3840 × 2400'};
  });
  window.mockStudio=m=>queueMicrotask(()=>{
    const p=rows.find(p=>p.id===m.id);
    if(m.action==='inspect') receive({type:'inspect',token:m.token,facts:p});
    if(m.action==='open') receive({...p,type:'photo'});
    if(m.action==='thumb'||m.action==='image')receive({type:m.action,id:p.id,url:p.url,size:p.id==='4'?'auto 106%':'cover',x:p.id==='4'?47:50});
    if(m.action==='save')p.edits=m.edits;
    if(m.action==='favorite'||m.action==='reject')receive({type:m.action,id:p.id,value:m.value});
  });
  window.studioOpened=()=>{
    window.studioOpened=null;
    if(key==='studio-adjust')tab('adjust');
    if(key==='studio-portrait')tab('frame');
    if(key==='studio-compare'){tab('codes');compared=['0','4'];comparison();}
  };
  document.querySelector('#codeNote').append('示例里的码是占位，不是真的。');
  receive({type:'list',origin:'相册',skin:['atelier','stitch','journal','wishstar'].includes(skin)?skin:'cinema',
    tone:new URLSearchParams(location.search).get('tone')==='light'?'light':'dark',photos:rows,id:key==='studio-adjust'?'3':key==='studio-portrait'?'1':'0'});
})();
