(() => {
  let cleanup;
  const positions = new Map();
  const shapes = {
    compare:'<rect x="3.5" y="5" width="7.5" height="14" rx="1"/><rect x="13" y="5" width="7.5" height="14" rx="1"/>',
    refresh:'<path d="M19.5 12a7.5 7.5 0 1 1-2.2-5.3M19.5 4v4h-4"/>',
    down:'<path d="M6 9l6 6 6-6"/>', search:'<use href="#i-search"/>',
    star:'<path d="M12 3.5l2.6 5.5 6 .8-4.4 4.2 1.1 6-5.3-2.9-5.3 2.9 1.1-6L3.4 9.8l6-.8z"/>'
  };
  const icon = name => '<svg class="i" aria-hidden="true" viewBox="0 0 24 24">'+shapes[name]+'</svg>';
  function el(tag,cls,text) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text != null) e.textContent = text;
    return e;
  }
  function button(text, action, cls='btn') {
    const b = el('button',cls,text);
    b.type='button'; b.onclick=action;
    return b;
  }
  function mount({root,page,send,commands}) {
    const favorites = page==='favorites';
    const panel = el('section','gallery-module');
    panel.innerHTML = '<header class="ph"><h1></h1><div class="meta"></div><div class="sp"></div><label class="search">'+icon('search')+
      '<input aria-label="搜索照片" placeholder="搜索文件名、滤镜、服装部件"></label><button class="btn compare">'+icon('compare')+
      '连拍选片</button><button class="btn refresh" aria-label="刷新相册">'+icon('refresh')+'刷新</button></header>'+
      '<div class="tb"><div class="tabs" role="tablist" aria-label="照片分类"></div><div class="tools"></div></div>'+
      '<div class="gscroll"><div class="notice" role="status"></div><div class="empty" role="status" hidden></div><div class="groups"></div><div class="sentinel"></div></div>';
    root.append(panel);
    const find = s=>panel.querySelector(s), search=find('input'), scroller=find('.gscroll'), groups=find('.groups'),
      sentinel=find('.sentinel'), tabs=find('.tabs'), tools=find('.tools'), notice=find('.notice');
    find('h1').textContent=favorites?'收藏':'相册';
    let state, version, sent, lastOffset=-1, timer, selecting=false, menu=null, trigger=null;
    let pending=null, sequence=0, bookmark=null;
    const sections=new Map(), abort=new AbortController();
    const dispatch=(action,fields={})=>send(action,{version,visit:state?.visit,...fields});
    const fields=()=>({tab:state.tab,text:search.value,filter:state.filter,clothing:state.clothing,color:state.color,sort:state.sort});
    function change(values,clear=false) {
      clearTimeout(timer);closeMenu();scroller.scrollTop=0;bookmark=null;
      pending=clear?{tab:'all',text:'',filter:null,clothing:null,color:null,sort:'newest'}:{...fields(),...pending,text:search.value,...values};
      dispatch(clear?'clear':'query',{...pending,sequence:++sequence});
    }
    function closeMenu(restore=false) {menu?.remove();menu=null;if(restore)trigger?.focus();trigger=null;}
    function popup(anchor,choices) {
      if(menu && trigger===anchor){closeMenu();return;}
      closeMenu();trigger=anchor;menu=el('div','gallery-menu');menu.setAttribute('aria-label',anchor.textContent);
      for(const [label,action,swatch] of choices) {
        const b=button(label,()=>{closeMenu(true);action();},'option');
        if(swatch!=null){const dot=el('i','swatch');dot.style.backgroundColor=swatch;b.prepend(dot);}
        menu.append(b);
      }
      panel.append(menu);
      const r=anchor.getBoundingClientRect();
      menu.style.right=Math.max(8,innerWidth-r.right)+'px';
      menu.style.top=Math.min(r.bottom+4,innerHeight-menu.offsetHeight-8)+'px';
      menu.querySelector('button')?.focus();
    }
    document.addEventListener('pointerdown',e=>{if(menu&&!menu.contains(e.target)&&!trigger?.contains(e.target))closeMenu();},{signal:abort.signal});
    panel.addEventListener('keydown',e=>{if(e.key==='Escape'&&menu){closeMenu(true);e.stopPropagation();}},{signal:abort.signal});
    function dropdown(label,options){
      const b=button(label,()=>popup(b,options()),'btn ghost');
      b.insertAdjacentHTML('beforeend',icon('down'));return b;
    }
    search.oninput=()=>{clearTimeout(timer);timer=setTimeout(()=>change({text:search.value}),180);};
    find('.refresh').onclick=()=>dispatch('refresh');
    find('.compare').onclick=()=>{selecting=true;change({tab:'bursts'});};
    commands.push(['相册','搜索照片',()=>search.focus()],['相册','刷新相册',()=>dispatch('refresh')]);
    function navigation(){
      if(!state)return;
      const counts=state.tabs, available=Math.max(1,Math.min(8,Math.floor((panel.clientWidth-480)/110)));
      const visible=counts.length<=available?counts:counts.slice(0,Math.max(1,available-1));
      tabs.replaceChildren();
      for(const t of visible){
        const b=button(t.name,()=>{selecting=false;change({tab:t.id});},'');
        b.append(el('span','m',t.count.toLocaleString('en-US')));b.setAttribute('role','tab');b.setAttribute('aria-selected',t.id===state.tab);tabs.append(b);
      }
      if(visible.length<counts.length){
        const more=counts.slice(visible.length);
        tabs.append(dropdown(more.find(t=>t.id===state.tab)?.name||'更多',()=>more.map(t=>[t.name+'  '+t.count,()=>{selecting=false;change({tab:t.id});}])));
      }
      tools.replaceChildren();
      if(state.text || state.filter || state.clothing || state.color!=null || state.tab!=='all' || state.sort!=='newest')
        tools.append(button('清空筛选',()=>{search.value='';selecting=false;change({},true);},'btn ghost'));
      for(const [key,label,list] of [['filter','滤镜',state.filters],['clothing','服装部件',state.clothes],['color','主色',state.colors]]){
        const hex=n=>'#'+Number(n).toString(16).padStart(6,'0');
        const current=state[key];
        const b=dropdown(current==null||key==='color'?label:current,()=>[
          ['全部'+label,()=>change({[key]:null})],
          ...Object.entries(list||{}).map(([v,n])=>[(key==='color'?hex(v):v)+'  '+n,()=>change({[key]:v}),key==='color'?hex(v):null]),
          ...(state.indexing?[['还在索引',()=>{}]]:[])
        ]);
        if(key==='color' && current!=null){
          const dot=el('i','swatch');dot.style.backgroundColor=hex(current);b.prepend(dot);
        }
        b.setAttribute('aria-label',label+(current!=null?' · '+(key==='color'?hex(current):current):'')+(state.indexing?' · 还在索引':''));tools.append(b);
      }
      const sorts={newest:'最新在前',oldest:'最早在前',name:'按名称',category:'按分类'};
      tools.append(dropdown(sorts[state.sort],()=>Object.entries(sorts).map(([v,n])=>[n,()=>change({sort:v})])));
      if(state.plugin)tools.append(button('相册插件',()=>dispatch('plugin'),'btn ghost'));
    }

    const nearby=new Set(), loading=new Set();
    function loadNearby(){
      if(loading.size>=3)return;
      const bounds=scroller.getBoundingClientRect();
      const queued=Array.from(nearby).filter(tile=>tile.isConnected && !tile.querySelector('img').hasAttribute('src'))
        .map(tile=>{
          const r=tile.getBoundingClientRect();
          return {tile,distance:Math.max(0,bounds.top-r.bottom,r.top-bounds.bottom)};
        }).sort((a,b)=>a.distance-b.distance);
      for(const {tile} of queued.slice(0,3-loading.size)){
        loading.add(tile);
        tile.querySelector('img').src=tile.dataset.src;
      }
    }
    const images=new IntersectionObserver(entries=>{
      for(const {target,isIntersecting} of entries){
        if(!target.isConnected)continue;
        if(isIntersecting)nearby.add(target);
        else {
          nearby.delete(target);
          if(!loading.has(target))target.querySelector('img').removeAttribute('src');
        }
      }
      loadNearby();
    },{root:scroller,rootMargin:'900px 0px'});
    function releaseImages(){
      images.disconnect();nearby.clear();loading.clear();
    }
    function photoTile(p) {
      const tile=el('div','tile'), ratio=Math.max(.12,Math.min(8,p.ratio||1.5)), width=Math.round(184*ratio);
      tile.style.flex=ratio+' 1 '+width+'px';tile.style.maxWidth=Math.round(width*1.5)+'px';
      tile.dataset.photoId=p.id;
      if(!p.src){
        tile.dataset.src=p.thumbnail||('https://page-image.nikkiward.invalid/'+version+'/'+p.id);
        const img=el('img','thumbnail');img.alt='';img.decoding='async';
        const unavailable=el('span','unavailable','缩略图不可用');unavailable.hidden=true;
        function finished(ok){
          if(!loading.delete(tile))return;
          unavailable.hidden=ok;
          if(!nearby.has(tile))img.removeAttribute('src');
          loadNearby();
        }
        img.onerror=()=>finished(false);
        img.onload=()=>finished(true);
        tile.append(img,unavailable);
      }
      const open=button('',()=>dispatch(selecting&&p.burst?'burst':'open',{id:p.id}),'open');
      if(p.src){
        open.style.backgroundImage='url("'+p.src+'")';
        if(p.zoom)open.style.backgroundSize=p.zoom;
        if(p.position)open.style.backgroundPosition=p.position;
      }
      open.setAttribute('aria-label','打开照片 '+p.name);
      const cap=el('span','cap');cap.append(el('span','m',p.time),el('span','',[p.filter,p.lens?p.lens+'mm':''].filter(Boolean).join(' · ')));
      open.append(cap);tile.append(open);
      if(p.protectedCopy)tile.append(el('span','bd','保护副本'));
      const star=button('',()=>dispatch('star',{id:p.id}),'st');star.innerHTML=icon('star');
      star.setAttribute('aria-label',p.favorite?'取消收藏':'收藏照片');star.setAttribute('aria-pressed',!!p.favorite);tile.append(star);
      const actions=button('···',()=>popup(actions,[['复制图片',()=>dispatch('copy',{id:p.id})],['在文件夹中定位',()=>dispatch('locate',{id:p.id})],
        ...(p.burst?[['连拍选片',()=>dispatch('burst',{id:p.id})]]:[])]),'actions');
      actions.setAttribute('aria-label',p.name+' 的操作');tile.append(actions);
      if(!p.src)images.observe(tile);
      return tile;
    }
    function next(){
      if(state?.next==null || sent===state.next)return;
      if(sentinel.getBoundingClientRect().top>scroller.getBoundingClientRect().bottom+250)return;
      sent=state.next;dispatch('next',{offset:sent});
    }
    const paging=new IntersectionObserver(next,{root:scroller,rootMargin:'250px'});paging.observe(sentinel);
    const resize=new ResizeObserver(()=>{if(!menu)navigation();});resize.observe(panel);
    cleanup=()=>{
      if(state){
        const bounds=scroller.getBoundingClientRect();
        const first=Array.from(groups.querySelectorAll('.tile')).find(t=>t.getBoundingClientRect().bottom>bounds.top);
        positions.set(page,{scope:state.scope,top:scroller.scrollTop,id:first?.dataset.photoId,
          delta:first?first.getBoundingClientRect().top-bounds.top:0});
      }
      clearTimeout(timer);abort.abort();resize.disconnect();paging.disconnect();releaseImages();closeMenu();
    };
    return s=>{
      const reset=version!==s.version;
      if(state?.visit!==s.visit || !state){
        const saved=positions.get(page);bookmark=saved?.scope===s.scope?saved:null;
        pending=null;sequence=0;
      }
      if(s.sequence>=sequence)pending=null;
      state=s;
      if(reset){version=s.version;sent=null;lastOffset=-1;releaseImages();groups.replaceChildren();sections.clear();closeMenu();}
      if(document.activeElement!==search)search.value=s.text||'';
      const meta=find('.meta');meta.replaceChildren(el('span','m',(s.tabs[0]?.count||0).toLocaleString('en-US')),
        favorites?' 张 · '+(s.protection?'收藏保护已开：原图被删后保留一份副本':'收藏保护已关闭'):' 张 · 按拍摄日期');
      if(s.indexing)meta.append(' · 正在索引 '+s.indexed+' / '+s.total);
      if(!menu)navigation();
      notice.textContent=[s.error,s.overlap?'收藏保护因目录重叠暂停。请在相册设置中选择其他保护目录。':'',
        s.skipped?'已跳过 '+s.skipped+' 张无法读取的照片。':'',
        selecting?'请选择一张连拍照片进行比较。':''].filter(Boolean).join(' ');
      const empty=find('.empty');
      const filtered=!!(s.text||s.filter||s.color!=null||s.clothing);
      const unlocated=s.missingRoot && s.tab==='all' && !favorites && !filtered;
      empty.hidden=s.indexing || (!unlocated && s.matches>0);
      scroller.classList.toggle('is-empty',!empty.hidden && !s.matches);
      if(!empty.hidden){
        const noFavorites=!filtered && (s.tab==='favorites'||favorites && s.tab==='all');
        const noMatch=filtered || s.tab!=='all' && !noFavorites;
        empty.replaceChildren(el('p','',unlocated?'尚未定位游戏图库。':noFavorites?'还没有收藏的照片。':
          noMatch?'没有符合条件的照片。':'图库中还没有照片。'));
        if(unlocated)empty.append(button('去定位游戏',()=>dispatch('locate-game')));
        else if(noFavorites){
          empty.append(el('p','hint','在相册里点照片上的星标，即可收藏。'),
            button('去相册',()=>favorites?send('navigate',{to:'gallery'}):change({},true)));
        }else if(noMatch)empty.append(button('清空筛选',()=>{search.value='';selecting=false;change({},true);}));
        else empty.append(button('刷新相册',()=>dispatch('refresh')));
      }
      if(s.offset!==lastOffset){
        lastOffset=s.offset;
        for(const day of s.days){
          let row=sections.get(day.date);
          if(!row){
            const h=el('div','gh');h.append(el('span','m',day.date),el('span','',day.weekday+' · '+day.count+' 张'));
            if(day.current&&!favorites)h.append(el('em','','本次游玩拍的'));
            row=el('div','grow');groups.append(h,row);sections.set(day.date,row);
          }
          const frag=document.createDocumentFragment();for(const p of day.photos)frag.append(photoTile(p));row.append(frag);
        }
      }
      if(bookmark && s.matches){
        const target=Array.from(groups.querySelectorAll('.tile')).find(t=>t.dataset.photoId===bookmark.id);
        scroller.scrollTop=target?scroller.scrollTop+target.getBoundingClientRect().top-scroller.getBoundingClientRect().top-bookmark.delta:bookmark.top;
        if(target || !s.indexing)bookmark=null;
      }
      requestAnimationFrame(next);
    };
  }
  registerPage('gallery',{mount,unmount(){cleanup?.();}});
})();
