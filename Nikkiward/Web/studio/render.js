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
  const foot = edit.frame === 'off' ? 0 : 70*unit;
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
  return [
    e.title || (e.fields.date?photo.date:''),
    [e.title && e.fields.date && photo.date,e.fields.filter && photo.filter && '滤镜 · '+photo.filter,e.fields.outfit && photo.outfit[0],e.fields.code && e.codes[0]].filter(Boolean).join('　'),
    e.fields.lens?[photo.lens && photo.lens+'mm',photo.aperture && 'f/'+photo.aperture].filter(Boolean).join('  '):'',
    photo.hasParameters?'INFINITY NIKKI · 拍照模式':''
  ];
}
function drawPhoto(canvas,image,e,photo,scale,clip) {
  const g=geometry(image,e);
  const px=n=>Math.round(n*scale);
  const fullWidth=Math.max(1,px(g.width)), fullHeight=Math.max(1,px(g.height));
  const area=clip || {x:0,y:0,w:fullWidth,h:fullHeight};
  canvas.width=area.w; canvas.height=area.h;
  const ctx=canvas.getContext('2d',{willReadFrequently:true});
  ctx.translate(-area.x,-area.y);
  const pad=px(g.pad), w=Math.max(1,px(g.w)), h=Math.max(1,px(g.h));
  ctx.fillStyle=['bar','ink'].includes(e.frame)?'#0e0f13':'#f6f3ee';
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
  if(g.foot && !clip) {
    const [heading,detail,lens,mode]=frameText(e,photo);
    const u=g.unit*scale, left=pad+4*u, right=fullWidth-left, y=pad+h;
    ctx.fillStyle=['bar','ink'].includes(e.frame)?'#f6f3ee':'#17181c';
    ctx.font='500 '+(13*u)+'px "JetBrains Mono", monospace';
    ctx.fillText(heading,left,y+(g.w<g.h?39:28)*u,w*.6);
    ctx.font=(12*u)+'px "Noto Sans SC", sans-serif';
    ctx.globalAlpha=.7;
    const narrow=g.w<g.h;
    if(!narrow) ctx.fillText(detail,left,y+48*u,w*.6);
    ctx.globalAlpha=1; ctx.textAlign='right'; ctx.font='500 '+((narrow?13:17)*u)+'px "JetBrains Mono", monospace';
    ctx.fillText(lens,right,y+(narrow?39:33)*u,w*.38);
    ctx.globalAlpha=.65; ctx.font=(11*u)+'px Manrope, sans-serif';
    if(!narrow) ctx.fillText(mode,right,y+50*u,w*.38);
  }
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
