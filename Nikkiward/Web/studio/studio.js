'use strict';
const $=s=>document.querySelector(s), all=s=>[...document.querySelectorAll(s)];
const web=window.chrome?.webview, stage=$('#stage'), canvas=$('#preview'), frame=$('#frame');
let photos=[], selected='', current=null, image=null, edit=initialEdits(), zoom=0, dx=0, dy=0;
let raw=false, queued=false, drag=null, compared=[], compareZoom=1, cx=50, cy=50;
let selectedMask=null, format='PNG', captionKey='';
const thumbnails=new Map(), thumbsRequested=new Set(), flags=new Map();
function send(action,extra={},id=selected) {
  const msg={action,id,...extra};
  if(web) web.postMessage(msg); else window.mockStudio?.(msg);
}
function element(tag,text,cls) {
  const el=document.createElement(tag);
  if(text!=null) el.textContent=text;
  if(cls) el.className=cls;
  return el;
}
function button(text,fn,cls) {
  const b=element('button',text,cls); b.onclick=fn; return b;
}
function status(text) { $('#status').textContent=text || '副本写到 Nikkiward 的导出目录；游戏照片目录一个字节都不动。'; }
function tab(name) {
  all('[data-tab]').forEach(b=>b.setAttribute('aria-selected',b.dataset.tab===name));
  all('[data-pane]').forEach(p=>p.classList.toggle('on',p.dataset.pane===name));
}
function save() { if(current) send('save',{edits:edit}); }
function repaint() {
  if(queued) return;
  queued=true; requestAnimationFrame(()=>{queued=false;paint();});
}
function fit(g) {
  const framed=!raw && edit.frame!=='off';
  return Math.max(.001,Math.min(1,(stage.clientWidth-40)/g.width,(stage.clientHeight-30)/g.height,
    (framed?880:980)/g.w,(framed?520:610)/g.h));
}
function paint() {
  if(!image) return;
  const e=raw?initialEdits():edit, g=geometry(image,e), size=zoom||fit(g);
  const scale=Math.min(1,size*devicePixelRatio), width=g.width*size, height=g.height*size;
  const left=(stage.clientWidth-width)/2+dx, top=(stage.clientHeight-height)/2+dy;
  const ratio=scale/size, x=Math.max(0,Math.floor(-left*ratio)), y=Math.max(0,Math.floor(-top*ratio));
  const w=Math.min(Math.round(g.width*scale),Math.ceil((stage.clientWidth-left)*ratio))-x;
  const h=Math.min(Math.round(g.height*scale),Math.ceil((stage.clientHeight-top)*ratio))-y;
  Object.assign(frame.style,{left:left+'px',top:top+'px',width:width+'px',height:height+'px'});
  frame.dataset.frame=e.frame;
  const caption=$('#frameCaption'), unit=g.unit*size;
  caption.hidden=!g.foot;
  if(g.foot) {
    const rows=captionLayout(g,e,current), key=JSON.stringify(rows.map(r=>[r.text,r.font]));
    if(key!==captionKey) {
      captionKey=key;
      loadCaption(rows).then(()=>{
        if(captionKey===key) paint();
      }).catch(()=>{if(captionKey===key)status('展签字体载入失败，请重新打开照片工坊。');});
    }
    const footerTop=height-g.foot*size;
    const x=Math.max(0,-left), y=Math.max(0,-top-footerTop);
    const visibleWidth=Math.min(width,stage.clientWidth-left)-x;
    const visibleHeight=Math.min(g.foot*size,stage.clientHeight-top-footerTop)-y;
    caption.hidden=visibleWidth<=0 || visibleHeight<=0;
    if(!caption.hidden) {
      const dpi=devicePixelRatio;
      const pixelWidth=Math.ceil(visibleWidth*dpi), pixelHeight=Math.ceil(visibleHeight*dpi);
      if(caption.width!==pixelWidth) caption.width=pixelWidth;
      if(caption.height!==pixelHeight) caption.height=pixelHeight;
      Object.assign(caption.style,{left:x+'px',top:footerTop+y+'px',width:visibleWidth+'px',height:visibleHeight+'px'});
      const ctx=caption.getContext('2d',{alpha:false});
      ctx.resetTransform();
      ctx.fillStyle=framePaper(e.frame);
      ctx.fillRect(0,0,pixelWidth,pixelHeight);
      ctx.translate(-x*dpi,-y*dpi);
      drawCaption(ctx,rows,unit*dpi,0,e.frame);
    }
  }
  const remove=$('#removeMask');
  remove.hidden=raw || !e.masks.includes(selectedMask);
  if(!remove.hidden) {
    remove.style.left=(g.pad+(selectedMask.x+selectedMask.w)*g.w)*size+'px';
    remove.style.top=(g.pad+selectedMask.y*g.h)*size+'px';
  }
  const direct=!e.crop && !e.turns && !e.masks.length && Object.keys(neutral).every(k=>!e[k]);
  image.hidden=!direct;
  if(direct) Object.assign(image.style,{left:g.pad*size+'px',top:g.pad*size+'px',width:g.w*size+'px',height:g.h*size+'px'});
  canvas.hidden=direct || w<=0 || h<=0;
  if(!canvas.hidden) {
    drawPhoto(canvas,image,e,current,scale,{x,y,w,h});
    Object.assign(canvas.style,{left:x/ratio+'px',top:y/ratio+'px',width:w/ratio+'px',height:h/ratio+'px'});
  }
  $('#zVal').textContent=zoom?Math.round(zoom*100)+'%':'适应';
}
function controls() {
  for(const input of all('[data-k]')) {
    const value=edit[input.dataset.k]; input.value=value;
    input.nextElementSibling.textContent=(value>0?'+':'')+value+(input.dataset.k==='rot'?'°':'');
  }
  all('#segCrop button').slice(0,4).forEach((b,i)=>b.setAttribute('aria-pressed',i===edit.crop));
  all('[data-f]').forEach(b=>b.setAttribute('aria-pressed',b.dataset.f===edit.frame));
  all('[data-fld]').forEach(b=>b.checked=edit.fields[b.dataset.fld]);
  all('.code input[id^=c]').forEach((b,i)=>b.value=edit.codes[i]);
  $('#addMask').hidden=!edit.masks.length; $('#maskHint').hidden=!!edit.masks.length;
  $('#title').value=edit.title; $('#xUid').checked=edit.masks.length>0;
  $('#sPre').replaceChildren(...presets.map(([name,values])=>{
    const b=button(name,()=>{Object.assign(edit,neutral,{rot:edit.rot},values);changed();});
    const i=element('i'); if(thumbnails.get(selected)) i.style.backgroundImage='url("'+thumbnails.get(selected)+'")';
    i.style.filter='saturate('+(1+(values.sat||0)/100)+') sepia('+Math.abs(values.tmp||0)/100+')';
    b.prepend(i); b.setAttribute('aria-pressed',Object.keys(neutral).filter(k=>k!=='rot').every(k=>edit[k]===(values[k]||0)));
    return b;
  }));
}
function changed() {controls(); repaint(); save();}
function strip() {
  const n=photos.findIndex(p=>p.id===selected), start=Math.max(0,Math.min(n-4,photos.length-9));
  $('#sStrip').replaceChildren(...photos.slice(start,start+9).map(p=>{
    const b=button('',()=>openPhoto(p.id)); b.setAttribute('aria-label',p.name);
    b.title=p.name; b.classList.toggle('v',!!p.portrait); b.setAttribute('aria-current',p.id===selected); b.dataset.id=p.id;
    if(thumbnails.get(p.id)) b.style.backgroundImage='url("'+thumbnails.get(p.id)+'")';
    else if(!thumbsRequested.has(p.id)) {thumbsRequested.add(p.id);send('thumb',{},p.id);}
    return b;
  }));
}
function openPhoto(id) {
  selectedMask=null;$('#removeMask').hidden=true;
  searchVersion++;
  $('#frameCaption').hidden=true; image?.remove(); frame.dataset.frame='off'; selected=id; current=null; image=null; canvas.hidden=true; zoom=dx=dy=0; raw=false;
  $('.side').inert=true; $('#sHold').disabled=$('#sFav').disabled=true;
  compared=[]; stage.classList.remove('cmp'); $('#sCmp').setAttribute('aria-pressed','false');
  const n=photos.findIndex(p=>p.id===id);
  $('#sName').textContent=photos[n].name; $('#sIdx').textContent=(n+1)+' / '+photos.length.toLocaleString();
  $('#sPrev').disabled=n===0; $('#sNext').disabled=n===photos.length-1;
  status('正在载入…'); strip(); send('open');
}
let searchVersion=0, pendingInspection=null;
async function findNext(field,value) {
  const version=++searchVersion;
  pendingInspection?.resolve({});
  const start=photos.findIndex(p=>p.id===selected);
  status('正在当前列表中查找…');
  for(let step=1;step<photos.length;step++) {
    const p=photos[(start+step)%photos.length], token=version+'-'+step;
    const result=await new Promise(resolve=>{
      pendingInspection={token,resolve}; send('inspect',{field,token},p.id);
    });
    if(version!==searchVersion)return;
    pendingInspection=null;
    let match=false;
    if(field==='palette') {
      let colors=result.facts?.palette;
      if(!colors && result.thumbnail) {
        const thumbnail=new Image(); thumbnail.src=result.thumbnail;
        try {await thumbnail.decode();colors=palette(thumbnail);} catch {continue;}
      }
      match=colors?.some(color=>color===value || similarColor(color,value));
    } else {
      const candidate=result.facts?.[field];
      match=field==='date'?candidate?.slice(0,10)===value.slice(0,10):
        field==='outfit'?candidate?.includes(value):candidate===value;
    }
    if(version!==searchVersion)return;
    if(match){openPhoto(p.id);return;}
  }
  status('当前列表中没有其他符合条件的照片。');
}
function similarColor(a,b) {
  const rgb=color=>color.startsWith('#')?color.slice(1).match(/../g).map(x=>parseInt(x,16)):color.match(/\d+/g).map(Number);
  const first=rgb(a), second=rgb(b);
  return first.every((v,i)=>Math.abs(v-second[i])<=24);
}
async function receive(m) {
  if(m.type==='inspect'){
    if(pendingInspection?.token===m.token) pendingInspection.resolve(m);
    return;
  }
  if(m.type==='error' && pendingInspection){searchVersion++;pendingInspection.resolve({});pendingInspection=null;}
  if(m.type==='list') {
    Object.assign(document.documentElement.dataset,{skin:m.skin,tone:m.tone});
    $('#backLabel').textContent=m.origin;
    photos=m.photos; $('#sCmp').disabled=photos.length<2; openPhoto(m.id);return;
  }
  if(m.type==='exported') {
    status('已导出 '+m.name);
    $('#status').append(button('在文件夹中显示',()=>send('reveal-export'),'lnk'));
    return;
  }
  if(m.type==='status'||m.type==='error') {status(m.message);return;}
  if(m.type==='thumb') {
    thumbnails.set(m.id,m.url);
    for(const b of all('#sStrip button')) if(b.dataset.id===m.id && m.url) b.style.backgroundImage='url("'+m.url+'")';
    return;
  }
  if(m.type==='image') {
    if(m.unreadable) status('这张照片的修图记录读不出来，已按原图打开');
    flags.set(m.id,{favorite:!!m.starred,reject:!!m.rejected});markButtons();
    const cell=all('.cmpv figure').find(f=>f.dataset.id===m.id);
    if(cell) {
      const surface=cell.querySelector('.compare-photo');
      surface.style.backgroundImage='url("'+m.url+'")';
      surface.style.backgroundSize=m.size || 'cover';
      surface.dataset.x=m.x ?? 50;
      syncCompare();
    }
    return;
  }
  if(m.type==='favorite'||m.type==='reject') {
    const f=flags.get(m.id)||{}; f[m.type]=m.value; flags.set(m.id,f); markButtons();return;
  }
  if(m.type!=='photo'||m.id!==selected) return;
  const im=new Image();
  if(web) im.crossOrigin='anonymous';
  im.src=m.url;
  try {await im.decode();} catch {if(selected===m.id) status('照片无法解码，请切换照片。');return;}
  if(selected!==m.id) return;
  photos.find(p=>p.id===selected).portrait=im.naturalHeight>im.naturalWidth;
  strip();
  im.className='source-photo'; im.alt='当前照片'; im.draggable=false; frame.prepend(im); image=im; current=m; canvas.hidden=false; $('.side').inert=false; $('#sHold').disabled=$('#sFav').disabled=false;
  edit=Object.assign(initialEdits(),m.edits||{}); flags.set(selected,{favorite:m.starred,reject:m.rejected});
  const summary=$('#sDim');
  const kilobytes=m.sizeBytes/1024;
  const size=kilobytes<1024?Math.round(kilobytes)+' KB':(kilobytes/1024).toFixed(1)+' MB';
  summary.replaceChildren(element('span',m.dim||im.naturalWidth+' × '+im.naturalHeight,'m'),
    ' · ',element('span',size,'m'),' · ',element('span',m.date,'m'));
  if(m.hasParameters) summary.append(' · 拍照模式');
  summary.title='来源目录 · '+m.folder;
  summary.setAttribute('aria-label',summary.textContent+'，'+summary.title);
  $('#paramHint').hidden=$('#params').hidden=!m.hasParameters;
  $('#outfitHeading').hidden=$('#outfit').hidden=!m.hasParameters || !m.outfit.length;
  $('#outfit').replaceChildren(...m.outfit.map((name,i)=>{
    const tag=button('',()=>findNext('outfit',name));
    if(m.slots?.[i]) tag.append(element('em',m.slots[i]));
    tag.append(name); return tag;
  }));
  const facts=[['焦距',m.lens!=null?m.lens+' mm':''],['光圈',m.aperture!=null?'f/'+m.aperture:''],['滤镜',m.filter],['时间',photos.find(p=>p.id===selected).time]];
  $('#params').replaceChildren(...(m.hasParameters?facts:[]).map(([k,v])=>{
    const field={'焦距':'lens','光圈':'aperture','滤镜':'filter','时间':'date'}[k];
    const cell=button('',()=>findNext(field,m[field])); cell.disabled=!v; cell.append(element('small',k),element('b',v||'—',k==='滤镜'?'':'m'));return cell;
  }));
  $('#missing').textContent=m.hasParameters?'':'这张照片没有拍摄参数';
  $('#sPal').replaceChildren(...(m.palette||palette(im)).map(color=>{
    const sw=button('',()=>findNext('palette',color)); sw.style.background=color;sw.title=color;sw.setAttribute('aria-label','找主色 '+color+' 的照片');return sw;
  }));
  controls();markButtons();paint();status(m.unreadable?'这张照片的修图记录读不出来，已按原图打开':'');
  window.studioOpened?.();
}
function step(n) {
  const p=photos[photos.findIndex(p=>p.id===selected)+n]; if(p) openPhoto(p.id);
}
function markButtons() {
  const favorite=!!flags.get(selected)?.favorite, label=favorite?'已收藏':'收藏';
  $('#sFav').setAttribute('aria-pressed',favorite);
  $('#sFav').setAttribute('aria-label',label);$('#sFav span').textContent=label;
  for(const f of all('.cmpv figure')) for(const b of f.querySelectorAll('[data-mark]'))
    b.setAttribute('aria-pressed',!!flags.get(f.dataset.id)?.[b.dataset.mark]);
}
function comparison() {
  const box=$('.cmpv'); box.replaceChildren(); stage.classList.toggle('cmp',compared.length>0);
  $('#sCmp').setAttribute('aria-pressed',!!compared.length);
  box.style.gridTemplateColumns='repeat('+Math.max(1,compared.length)+',minmax(0,1fr))';
  for(const [i,id] of compared.entries()) {
    const fig=element('figure');fig.dataset.id=id;
    const photo=photos.find(p=>p.id===id);
    const view=element('div'), surface=element('i',null,'compare-photo');
    surface.setAttribute('role','img');surface.setAttribute('aria-label',photo.name);
    view.append(surface);fig.append(view);
    const cap=element('figcaption'), select=element('select'); select.setAttribute('aria-label','选片 '+(i+1));
    for(const p of photos) {
      const opt=new Option(p.name,p.id,false,p.id===id);opt.disabled=compared.includes(p.id)&&p.id!==id;select.add(opt);
    }
    if(compared.length<Math.min(4,photos.length)) select.add(new Option('＋ 加入选片','add'));
    select.onchange=()=>{
      if(select.value==='add') compared.push(photos.find(p=>!compared.includes(p.id)).id);
      else compared[i]=select.value;
      comparison();
    };
    const picker=element('label',null,'compare-picker');
    picker.title='更换或加入选片';
    picker.append(element('b',String.fromCharCode(65+i),'m'),' · ',element('span',photo.time,'m'));
    if(i===1) picker.append(' · 缩放与位置同步');
    picker.append(select);cap.append(picker);
    for(const [key,label] of [['favorite','留下'],['reject','放回']]) {
      const b=button(label,()=>send(key,{value:!flags.get(id)?.[key]},id),key==='favorite'?'btn solid':'btn'); b.dataset.mark=key;cap.append(b);
    }
    fig.append(cap);box.append(fig);send('image',{},id);
  }
  syncCompare();markButtons();
}
function syncCompare() {
  for(const img of all('.compare-photo')) {
    img.style.transform='scale('+compareZoom+')';
    img.style.transformOrigin=cx+'% '+cy+'%';
    img.style.backgroundPosition=clamp(cx+Number(img.dataset.x || 50)-50,0,100)+'% '+cy+'%';
  }
}
function changeZoom(factor) {
  if(compared.length){compareZoom=Math.max(1,Math.min(8,compareZoom*factor));syncCompare();return;}
  if(image){zoom=Math.max(.04,Math.min(8,(zoom||fit(geometry(image,edit)))*factor));repaint();}
}
all('[data-tab]').forEach(b=>{b.setAttribute('role','tab');b.onclick=()=>tab(b.dataset.tab);});
all('[data-k]').forEach(i=>{
  i.oninput=()=>{edit[i.dataset.k]=+i.value;i.nextElementSibling.textContent=i.value+(i.dataset.k==='rot'?'°':'');repaint();};
  i.onchange=()=>changed();
});
all('#segCrop button').forEach((b,i)=>b.onclick=()=>{if(i===4) edit.turns=(edit.turns+1)%4;else edit.crop=i; changed();});
all('[data-format]').forEach(b=>b.onclick=()=>{
  format=b.dataset.format;
  all('[data-format]').forEach(item=>item.setAttribute('aria-pressed',item===b));
});
all('[data-f]').forEach(b=>b.onclick=()=>{edit.frame=b.dataset.f;changed();});
all('[data-fld]').forEach(b=>b.onchange=()=>{edit.fields[b.dataset.fld]=b.checked;changed();});
$('#title').oninput=e=>{edit.title=e.target.value;save();repaint();};
all('.code input[id^=c]').forEach((input,i)=>{
  input.maxLength=256;input.oninput=()=>{edit.codes[i]=input.value;save();repaint();};
  input.nextElementSibling.onclick=()=>send('copy-code',{text:input.value});
});
$('#sReset').onclick=()=>{const codes=edit.codes;edit=initialEdits();edit.codes=codes;changed();};
$('#sFav').onclick=()=>send('favorite',{value:!flags.get(selected)?.favorite});
$('#sCmp').onclick=()=>{
  if(compared.length) compared=[];
  else {const i=photos.findIndex(p=>p.id===selected); compared=[selected,photos[i+1]?.id||photos[i-1].id];compareZoom=1;cx=cy=50;}
  comparison();
};
$('#sBack').onclick=()=>send('back');$('#locate').onclick=()=>send('locate');
// Where the window's title bar lets clicks through: the back button and the action buttons on the right.
window.mastheadBounds=()=>{
  const edges=list=>{const r=list.filter(Boolean).map(el=>el.getBoundingClientRect()).filter(b=>b.width>0);
    return r.length?[Math.min(...r.map(b=>b.left)),Math.max(...r.map(b=>b.right))]:null;};
  const back=edges([$('#sBack')]),tools=edges([$('#sHold'),$('#sCmp'),$('#sFav'),$('#locate')]);
  return back?(tools?[...back,...tools]:back):null;
};
$('#sPrev').onclick=()=>step(-1);$('#sNext').onclick=()=>step(1);
$('#zOut').onclick=()=>changeZoom(1/1.25);$('#zIn').onclick=()=>changeZoom(1.25);
$('#zVal').onclick=()=>{zoom=zoom===1?0:1;dx=dy=0;repaint();};
stage.onwheel=e=>{e.preventDefault();changeZoom(e.deltaY<0?1.12:1/1.12);};
const hold=$('#sHold');
hold.onpointerdown=e=>{hold.setPointerCapture(e.pointerId);raw=true;repaint();};
hold.onlostpointercapture=()=>{raw=false;repaint();};
hold.onkeydown=e=>{if(e.key===' '){e.preventDefault();raw=true;repaint();}};
hold.onkeyup=hold.onblur=()=>{raw=false;repaint();};
function addMask() {
  if(edit.masks.length>=32) {status('最多可添加 32 个遮挡块');return;}
  selectedMask={x:.79-edit.masks.length%4*.04,y:.88-edit.masks.length%6*.08,w:.17,h:.06};
  edit.masks.push(selectedMask);changed();
}
$('#xMeta').onclick=e=>e.preventDefault();
$('#addMask').onclick=addMask;
$('#removeMask').onclick=()=>{
  edit.masks=edit.masks.filter(m=>m!==selectedMask);
  selectedMask=null;changed();
};
$('#xUid').onchange=e=>{if(e.target.checked)addMask();else{edit.masks=[];changed();}};
function point(e) {
  const r=frame.getBoundingClientRect(),g=geometry(image,edit);
  return {x:((e.clientX-r.left)/r.width*g.width-g.pad)/g.w,y:((e.clientY-r.top)/r.height*g.height-g.pad)/g.h};
}
stage.onpointerdown=e=>{
  if(e.target.closest('button,select')||!image)return;
  drag={x:e.clientX,y:e.clientY,dx,dy,cx,cy};
  if(!compared.length && e.target===canvas) {
    const p=point(e), m=edit.masks.findLast(m=>p.x>=m.x && p.x<=m.x+m.w && p.y>=m.y && p.y<=m.y+m.h);
    selectedMask=m||null;repaint();
    if(m) Object.assign(drag,{m,start:{...m},p,resize:p.x>m.x+m.w*.75 && p.y>m.y+m.h*.55});
  }
  stage.setPointerCapture(e.pointerId);
};
const clamp=(x,min,max)=>Math.max(min,Math.min(max,x));
stage.onpointermove=e=>{
  if(!drag)return;
  if(compared.length){cx=clamp(drag.cx-(e.clientX-drag.x)*.15,0,100);cy=clamp(drag.cy-(e.clientY-drag.y)*.15,0,100);syncCompare();return;}
  if(drag.m) {
    const p=point(e),m=drag.m,s=drag.start;
    if(drag.resize){m.w=clamp(s.w+p.x-drag.p.x,.015,1-s.x);m.h=clamp(s.h+p.y-drag.p.y,.015,1-s.y);}
    else{m.x=clamp(s.x+p.x-drag.p.x,0,1-m.w);m.y=clamp(s.y+p.y-drag.p.y,0,1-m.h);}
  } else {dx=drag.dx+e.clientX-drag.x;dy=drag.dy+e.clientY-drag.y;}
  repaint();
};
stage.onlostpointercapture=()=>{if(drag?.m)save();drag=null;};
for(const [id,action] of [['xExport','export'],['xCopy','copy'],['copyAll','copy'],['xSetBg','background']]) {
  $('#'+id).onclick=async()=>{
    if(!image)return;
    const codes=id==='copyAll'?edit.codes.map((value,i)=>value && ['搭配码','染色码','家园码','相机码'][i]+' '+value).filter(Boolean).join('\n'):'';
    const photoId=selected, type=action==='export' && format==='JPEG'?'image/jpeg':'image/png';
    const edits=structuredClone(edit);
    if(action==='background') {edits.frame='off';edits.title='';}
    let out;
    try {
      out=await renderOutput(image,edits,current);
      const blob=await new Promise(resolve=>out.toBlob(resolve,type,.94));
      if(!blob || blob.size>72*1024*1024){status('编码超过 72 MiB，请缩小裁切范围或改用 JPEG。');return;}
      const reader=new FileReader(); reader.onload=()=>send(action,{image:reader.result,text:codes},photoId);reader.readAsDataURL(blob);
    } catch(error) {status(error.message||'图像编码失败，请缩小裁切范围后重试。');}
    finally {if(out)out.width=out.height=0;}
  };
}
document.onkeydown=e=>{
  if(e.key==='Escape')send('back');
  if(!e.target.closest('input,select')){if(e.key==='ArrowLeft')step(-1);if(e.key==='ArrowRight')step(1);}
};
new ResizeObserver(repaint).observe(stage);
if(web)web.addEventListener('message',e=>receive(e.data));
else if(location.protocol==='file:' && new URLSearchParams(location.search).has('mock')) {
  const script=document.createElement('script');script.src='../../Web.dev/studio/cases.js';document.head.append(script);
}
