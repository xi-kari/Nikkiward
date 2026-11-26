const presets = [
  ['原片', {}], ['暖调', {tmp:45,sat:10}], ['冷调', {tmp:-45,con:8}],
  ['胶片', {con:-18,sat:-22,exp:6}], ['黑白', {sat:-100,con:14}]
];
const neutral = {rot:0,exp:0,con:0,hi:0,sh:0,sat:0,tmp:0};
function initialEdits() {
  return {...neutral, turns:0, crop:0, frame:'off', fields:{date:true,lens:true,filter:true,outfit:true,code:false},
    title:'', codes:['','','',''], masks:[]};
}
function geometry(image, edit) {
  const sideways = edit.turns % 2;
  let w = sideways ? image.naturalHeight : image.naturalWidth;
  let h = sideways ? image.naturalWidth : image.naturalHeight;
  const ratio = [w/h,1,4/5,16/9][edit.crop];
  w = Math.min(w,h*ratio);
  h = Math.min(h,w/ratio);
  const unit = Math.max(w/880,h/520);
  const pad = ['on','ink'].includes(edit.frame) ? 16*unit : 0;
  const foot = edit.frame === 'off' ? 0 : (edit.frame === 'bar' ? 54 : 70)*unit;
  return {w,h,unit,pad,foot,width:w+2*pad,height:h+pad+foot};
}
function grade(ctx,x,y,w,h,e) {
  if (!Object.keys(neutral).some(k => k !== 'rot' && e[k])) return;
  const pixels = ctx.getImageData(x,y,w,h), data = pixels.data;
  const exposure = 2 ** (e.exp/75), contrast = 1+e.con*.008, saturation = 1+e.sat*.01;
  for (let i=0; i<data.length; i+=4) {
    const luma=.2126*data[i]+.7152*data[i+1]+.0722*data[i+2], level=luma/255;
    const lift=e.hi*level**2+e.sh*(1-level)**2;
    for (let ch=0; ch<3; ch++) {
      const color=luma+(data[i+ch]-luma)*saturation;
      data[i+ch]=((color+lift)*exposure-128)*contrast+128+e.tmp*(1-ch)*.48;
    }
  }
  ctx.putImageData(pixels,x,y);
}
function frameText(e,photo) {
  const date=photo.date.replaceAll('-','.');
  return [
    e.title || (e.fields.date?date:''),
    [e.title && e.fields.date && date,e.fields.filter && photo.filter && '滤镜 · '+photo.filter,e.fields.outfit && photo.outfit[0],e.fields.code && e.codes[0]].filter(Boolean).join('　'),
    photo.hasParameters && e.fields.lens?[photo.lens && photo.lens+'mm',photo.aperture && 'f/'+photo.aperture].filter(Boolean).join(' '):'',
    photo.hasParameters?'INFINITY NIKKI · 拍照模式':'INFINITY NIKKI'
  ];
}
function captionLayout(g,e,photo) {
  const style=getComputedStyle(document.documentElement);
  const num=style.getPropertyValue('--nk-font-num').trim().replace(/,\s*(monospace|sans-serif)$/,', "Noto Serif SC", $1');
  const ui='Manrope, "Noto Sans SC", sans-serif';
  const [heading,detail,lens,mode]=frameText(e,photo);
  const right=(g.width-g.pad)/g.unit-4, left=g.pad/g.unit+4;
  const width=right-left, mid=g.foot/g.unit/2;
  if(g.w<g.h && !e.title && lens && heading) {
    const font='500 13px '+num;
    return [
      {text:heading,font,x:left,y:mid+4,max:width*.58},
      {text:lens,font,x:right,y:mid+4,max:width*.40,right:true}
    ];
  }
  return [
    {text:heading,font:'500 13px '+num,x:left,y:mid-7,max:width*.58},
    {text:detail,font:'400 12px '+ui,x:left,y:mid+13,max:width*.58,alpha:.7},
    {text:lens,font:'500 '+(g.w<g.h?13:17)+'px '+num,x:right,y:mid-2,max:width*.40,right:true},
    {text:mode,font:'400 11px '+ui,x:right,y:mid+15,max:width*.40,right:true,alpha:.65,spacing:'.66px'}
  ];
}
function loadCaption(rows) {
  return Promise.all(rows.filter(row=>row.text).map(row=>document.fonts.load(row.font,row.text)));
}
function framePaper(frame) {return frame==='on'?'#f6f3ee':'#0e0f13';}
function drawCaption(ctx,rows,unit,top,frame) {
  ctx.save();
  ctx.translate(0,top);ctx.scale(unit,unit);
  ctx.fillStyle=frame==='on'?'#1b1418':'#f1f1f1';
  for(const row of rows) {
    ctx.font=row.font;ctx.textAlign=row.right?'right':'left';ctx.globalAlpha=row.alpha??1;
    ctx.letterSpacing=row.spacing||'0px';
    if(row.max>0) ctx.fillText(row.text,row.x,row.y,row.max);
  }
  ctx.restore();
}
async function renderOutput(image,e,photo) {
  const g=geometry(image,e);
  if(g.width*g.height>32*1024*1024 || Math.max(g.width,g.height)>16384)
    throw new Error('导出上限为 3355 万像素，最长边 16384 像素。');
  const rows=g.foot?captionLayout(g,e,photo):[];
  await loadCaption(rows);
  const output=document.createElement('canvas');
  drawPhoto(output,image,e,photo,1,null,rows);
  return output;
}
function drawPhoto(canvas,image,e,photo,scale,clip,rows) {
  const g=geometry(image,e);
  const px=n=>Math.round(n*scale);
  const fullWidth=Math.max(1,px(g.width)), fullHeight=Math.max(1,px(g.height));
  const area=clip || {x:0,y:0,w:fullWidth,h:fullHeight};
  canvas.width=area.w; canvas.height=area.h;
  const ctx=canvas.getContext('2d',{willReadFrequently:true});
  ctx.translate(-area.x,-area.y);
  const pad=px(g.pad), w=Math.max(1,px(g.w)), h=Math.max(1,px(g.h));
  ctx.fillStyle=framePaper(e.frame);
  if(e.frame!=='off') ctx.fillRect(0,0,fullWidth,fullHeight);
  ctx.save(); ctx.beginPath(); ctx.rect(pad,pad,w,h); ctx.clip();
  ctx.translate(pad+w/2,pad+h/2);
  const angle=e.rot*Math.PI/180, c=Math.abs(Math.cos(angle)), s=Math.abs(Math.sin(angle));
  const iw=e.turns%2?image.naturalHeight:image.naturalWidth, ih=e.turns%2?image.naturalWidth:image.naturalHeight;
  const fill=Math.max((g.w*c+g.h*s)/iw,(g.w*s+g.h*c)/ih);
  ctx.rotate(angle+e.turns*Math.PI/2); ctx.scale(fill*scale,fill*scale);
  ctx.drawImage(image,-image.naturalWidth/2,-image.naturalHeight/2); ctx.restore();
  const x=Math.max(0,pad-area.x), y=Math.max(0,pad-area.y);
  const visibleW=Math.min(area.w,pad+w-area.x)-x, visibleH=Math.min(area.h,pad+h-area.y)-y;
  if(visibleW>0 && visibleH>0) grade(ctx,x,y,visibleW,visibleH,e);
  ctx.fillStyle='#17181c';
  for(const m of e.masks) ctx.fillRect(pad+m.x*w,pad+m.y*h,m.w*w,m.h*h);
  if(g.foot && !clip) drawCaption(ctx,rows,g.unit*scale,pad+h,e.frame);
  return g;
}
function palette(image) {
  const sample=document.createElement('canvas'); sample.width=sample.height=48;
  const ctx=sample.getContext('2d'); ctx.drawImage(image,0,0,48,48);
  const data=ctx.getImageData(0,0,48,48).data, bins=new Map();
  for(let i=0;i<data.length;i+=4) {
    const key=(data[i]>>5)*64+(data[i+1]>>5)*8+(data[i+2]>>5);
    const bin=bins.get(key)||[0,0,0,0]; bin[0]++;
    for(let ch=0;ch<3;ch++) bin[ch+1]+=data[i+ch];
    bins.set(key,bin);
  }
  return [...bins.values()].sort((a,b)=>b[0]-a[0]).slice(0,5)
    .map(bin=>'rgb('+bin.slice(1).map(sum=>Math.round(sum/bin[0])).join(',')+')');
}
