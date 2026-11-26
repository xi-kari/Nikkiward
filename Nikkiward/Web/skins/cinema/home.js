'use strict';

const builtInColors = {
  '海月':['#0a1634','#2c5fc4','#a8d1ff','#eef4ff'],
  '花信':['#fbeef0','#f2a8b3','#b3355d','#9db3dc']
};
const kinds = {ready:'go', launching:'busy', running:'live', checking:'wait', blocked:'stop', notfound:'stop'};
const actions = {status:'查看检查明细', recheck:'重新检查', locate:'定位游戏', rediscover:'重新发现安装'};
const cta = $('#cta');
const note = $('#note');
const overlayButtons = {switcher:$('#profileBtn'), palette:$('#cmdBtn')};
const motion = matchMedia('(prefers-reduced-motion: reduce)');
let recentPhotos = [];
let profileKey, timer, idleAllowed = false;

function element(tag, value, cls) {
  const node = document.createElement(tag);
  if (value != null) node.textContent = value;
  if (cls) node.className = cls;
  return node;
}

function withNumbers(value) {
  return (value || '').split(/(\d+(?:[:.×-]\d+)*%?)/).map((part,i) => i % 2 ? element('span',part,'m') : part);
}

function icon(name, cls = '') {
  const svg = document.createElementNS('http://www.w3.org/2000/svg','svg');
  svg.setAttribute('class','i ' + cls);
  svg.setAttribute('aria-hidden','true');
  const use = document.createElementNS(svg.namespaceURI,'use');
  use.setAttribute('href','#i-' + name);
  svg.append(use);
  return svg;
}

function renderLaunch(s) {
  const kind = kinds[s.visual];
  if (cta.dataset.kind !== kind || cta.querySelector('.lbl').textContent !== s.launchLabel ||
      win.dataset.state !== s.visual) {
    const mark = kind === 'live' ? element('i',null,'rec') :
      ['busy','wait'].includes(kind) ? element('i',null,'spin') :
      kind === 'go' ? icon('play','play') : icon(s.visual === 'notfound' ? 'folder' : 'shield');
    cta.dataset.kind = kind;
    cta.replaceChildren(mark,element('span',s.launchLabel,'lbl'));
    if (kind === 'go') cta.append(element('span','Enter','kbd'));
  }
  cta.disabled = kind !== 'go' || !s.enabled;
  cta.setAttribute('aria-label',s.launchLabel);
  cta.setAttribute('aria-busy',s.busy);
  cta.title = s.detail;
  cta.setAttribute('aria-description',s.detail);
  const stop = $('#stopBtn');
  stop.hidden = !s.closeVisible;
  stop.disabled = !s.closeEnabled;
  stop.lastChild.textContent = s.closeLabel;
  win.dataset.state = s.visual;
  win.classList.toggle('hasnote',kind !== 'go');
  const lines = s.detail.split('\n');
  const prose = lines.length === 1;
  note.classList.toggle('prose',kind !== 'go' && prose);
  if (kind === 'go') return;
  const heading = element('b');
  const title = prose ? (kind === 'live' && s.sessionMinutes != null ? '本次游玩 ' + s.sessionMinutes + ' 分钟' : '') : lines.shift();
  heading.hidden = !title || title === s.launchLabel;
  if (kind === 'stop') heading.append(icon('shield'));
  heading.append(...withNumbers(title));
  const reason = element('p',lines.join('\n'));
  reason.title = reason.textContent;
  const links = prose ? element('div',null,'note-actions') : reason;
  const next = s.state === 'SelectionRequired' && !s.nextActions.includes('locate') ? ['locate', ...s.nextActions] : s.nextActions;
  for (const action of next) {
    const button = element('button',actions[action],'lnk');
    button.dataset.action = action;
    links.append(button);
  }
  const focused = note.contains(document.activeElement) && document.activeElement.dataset.action;
  note.replaceChildren(heading,reason);
  if (prose && links.childElementCount) note.append(links);
  if (focused) note.querySelector('[data-action="'+focused+'"]')?.focus({preventScroll:true});
}

function renderProfiles(s) {
  const key = JSON.stringify(s.profiles);
  $('#discovery').replaceChildren(...withNumbers(s.discoveryTime && '上次发现安装：' + s.discoveryTime));
  if (key === profileKey) return;
  profileKey = key;
  const count = new Set(s.profiles.map(p => p.channel)).size;
  $('#switcher header span').textContent = count === 3 && s.profiles.length === 3 ? '档案 · 同一个游戏的三个渠道' : '档案 · ' + s.profiles.length + ' 个档案';
  const focused = document.activeElement.closest('.pf')?.dataset.id;
  $('#pfList').replaceChildren(...s.profiles.map(p => {
    const b = element('button',null,'pf');
    b.dataset.id = p.id;
    b.disabled = !p.available;
    describeProfile(b, p);
    b.setAttribute('role','radio');
    b.setAttribute('aria-checked',p.current);
    const reason = element('p');
    reason.append(element('i',null,'dot' + (p.available ? '' : p.reason.includes('未发现') ? ' idle' : ' warn')),...withNumbers(p.reason));
    b.append(icon('check','ck'),element('b',p.name),element('span',p.current ? '当前' : '','tagx'),reason);
    b.onclick = () => { bridge?.postMessage({action:'select-profile',profileId:p.id}); setOverlay(''); };
    return b;
  }));
  if (!s.profiles.length) $('#pfList').append(element('p','未发现档案','empty'));
  if (focused) ([...$('#pfList').children].find(b => b.dataset.id === focused && !b.disabled) || $('#pfRefresh')).focus();
}

function renderPhotos(s) {
  const ledge = $('#ledge');
  if (s.photos) {
    recentPhotos = s.photos.slice(0,4);
    const old = new Map([...ledge.querySelectorAll('button')].map(b => [b.dataset.photoId,b]));
    for (const [i,p] of recentPhotos.entries()) {
      const b = old.get(p.id) || element('button');
      if (!b.firstChild) {
        b.dataset.action = 'photo';
        b.dataset.photoId = p.id;
        const img = element('img');
        img.onload = () => b.classList.toggle('v',img.naturalWidth < img.naturalHeight);
        b.append(img,element('i'));
      }
      const img = b.firstChild;
      img.alt = p.thumbnail ? '' : p.fileName;
      img.hidden = !!p.thumbnail;
      if (p.thumbnail && img.getAttribute('src') !== p.thumbnail) {
        img.src = p.thumbnail;
        b.style.backgroundImage = 'url("' + img.src + '")';
      }
      if (!p.thumbnail) {
        img.removeAttribute('src');
        b.style.backgroundImage = '';
        b.classList.remove('v');
      }
      b.setAttribute('aria-label','在相册打开 ' + p.fileName + ' ' + p.time);
      if (ledge.children[i+1] !== b) ledge.insertBefore(b,ledge.children[i+1] || null);
      old.delete(p.id);
    }
    old.forEach(b => b.remove());
  }
  ledge.hidden = !recentPhotos.length;
  const count = s.lastSessionPhotos;
  $('#sessionShots').hidden = count == null;
  if (count != null) $('#sessionShots').replaceChildren(...withNumbers('上次游玩 ' + count + ' 张'));
  ledge.querySelectorAll('button').forEach((b,i) => b.lastChild.hidden = i >= (count ?? 0));
}

function render(s) {
  const preset = s.artSource === '内置原画' && builtInColors[s.artName];
  win.dataset.theme = s.theme;
  win.classList.toggle('still',s.still || s.running);
  win.style.setProperty('--accent',preset ? preset[2] : s.accent);
  $('.col.title').classList.toggle('custom-art',s.artSource === '自定义背景');
  $('#artSrc').textContent = s.artSource;
  for (const id of ['#artName','#plName']) {
    $(id).textContent = id === '#artName' && s.artSource === '自定义背景'
      ? s.artName.replace(/^\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}_(?=\S)/,'') : s.artName;
    $(id).title = s.artName;
    $(id).classList.toggle('m',!preset);
  }
  $('#artSw').replaceChildren(...(preset || s.palette).map(color => {
    const chip = element('i');
    chip.style.backgroundColor = color;
    return chip;
  }));
  $('#artTone').textContent = (s.theme === 'light' ? '亮调' : '暗调') + (s.contrastPassed ? ' · 取色已过对比度闸门' : '');
  renderPlate();
  $('#pfName').textContent = s.profile || '未选择档案';
  $('#pfName').title = s.profile || '';
  $('#pfSub').replaceChildren(...withNumbers(s.readySummary || s.profiles.find(p => p.current)?.reason));
  const sameCheck = s.check === s.launchLabel;
  $('#ckVal').replaceChildren(...(sameCheck ? withNumbers(s.checkTime) : [s.check]));
  $('#ckDot').className = 'dot ' + ({blocked:'warn',notfound:'idle',checking:'idle'}[s.visual] || '');
  $('#ckSub').hidden = sameCheck && s.nextActions.includes('status');
  $('#ckSub').replaceChildren(...withNumbers(sameCheck ? '看明细' : s.checkTime + ' · 看明细'));
  $('#official').replaceChildren(...withNumbers(s.officialDuration?.replace(/^(\d+(?:\.\d+)?)h$/,'$1 小时') || '未同步'));
  $('#durationSub').replaceChildren(...withNumbers([s.journalSnapshotDate && s.journalSnapshotDate.slice(5)+' 快照',s.localDuration].filter(Boolean).join(' · ')));
  $('.col.task').hidden = !s.taskText;
  const counts = s.taskText?.match(/^(.+?)\s*·\s*(\d+\/\d+)$/);
  const percent = s.progress == null ? '' : Math.round(s.progress)+'%';
  const fullTask = [s.taskText,percent].filter(Boolean).join(' ');
  $('#taskValue').replaceChildren(...withNumbers(counts ? counts[1] : fullTask));
  $('#taskValue').title = fullTask;
  $('#taskSub').replaceChildren(...(counts ? [element('span',[counts[2],percent].filter(Boolean).join(' · '),'m')] : []));
  $('.bar').hidden = s.progress == null;
  if (s.progress != null) $('.bar b').style.width = s.progress+'%';
  renderLaunch(s);
  renderProfiles(s);
  renderPhotos(s);
  if (win.dataset.ov === 'palette') renderPalette();
  updateIdle();
}

function renderPlate() {
  const s = currentState;
  const media = s.background.type === 'video' && video.videoWidth ? video : cover;
  const width = media.videoWidth || media.naturalWidth;
  const height = media.videoHeight || media.naturalHeight;
  $('#plMeta').replaceChildren(s.artSource,...(width ? [' · ',element('span',width+' × '+height,'m')] : []));
}
cover.addEventListener('load',renderPlate);
video.addEventListener('loadedmetadata',renderPlate);

function setIdle(on) {
  win.classList.toggle('idle',on);
  $('.plate').setAttribute('aria-hidden',!on);
  for (const selector of ['.top','.band','.ledge']) $(selector).inert = on || !!win.dataset.ov;
}
function wake() {
  if (win.classList.contains('idle')) setIdle(false);
  clearTimeout(timer);
  if (idleAllowed) timer = setTimeout(() => setIdle(true),8000);
}
function updateIdle() {
  const s = currentState;
  const allowed = !!s && s.enabled && s.visual === 'ready' && !s.running && !s.still &&
    !motion.matches && !win.dataset.ov && !document.hidden;
  if (allowed === idleAllowed) return;
  idleAllowed = allowed;
  wake();
}
document.addEventListener('visibilitychange',updateIdle);
motion.addEventListener('change',updateIdle);
for (const event of ['pointermove','pointerdown','wheel']) document.addEventListener(event,wake,{passive:true});

window.mastheadBounds = () => [$('.nav').getBoundingClientRect().left,$('#cmdBtn').getBoundingClientRect().right];

let returnFocus;
function setOverlay(name, focus = true) {
  if (!win.dataset.ov && name) returnFocus = document.activeElement === document.body ? overlayButtons[name] : document.activeElement;
  win.dataset.ov = name;
  for (const [id,button] of Object.entries(overlayButtons)) {
    button.setAttribute('aria-expanded',id === name);
    $('#'+id).inert = id !== name;
  }
  updateIdle();
  setIdle(false);
  if (name === 'palette') {
    $('#pIn').value = '';
    selected = 0;
    renderPalette();
    if (focus) $('#pIn').focus();
  } else if (name === 'switcher') {
    if (focus) $('#switcher button:not(:disabled)').focus();
  } else returnFocus?.focus({preventScroll:true});
}
for (const [name,button] of Object.entries(overlayButtons)) button.onclick = () => setOverlay(name);
$('#veil').onclick = () => setOverlay('');

const commands = [
  ['启动','启动游戏','launch','Enter'], ['启动','关闭游戏','close'], ['启动','切换档案…','switcher'],
  ['启动','游戏设置','settings'], ['启动','定位游戏（手动选择安装目录）','locate'], ['启动','启动检查与诊断','status'],
  ['前往','相册','nav-gallery'], ['前往','收藏','nav-gallery-favorites'], ['前往','最近一张照片','photo'],
  ['前往','奇想手账','nav-library'], ['前往','心愿共鸣记录','nav-resonance'], ['前往','设置','nav-settings'],
  ['背景','换成「花信」','blossom'], ['背景','换成「海月」','tide'], ['背景','恢复默认背景','reset'],
  ['背景','进入鉴赏模式','idle','闲置 8 秒也会进入'], ['背景','更换背景','background'], ['工具','后台任务','download']
];
let selected = 0;
function renderPalette() {
  const query = $('#pIn').value.trim().toLowerCase();
  const items = commands.filter(([group,label,action]) => (group+label).toLowerCase().includes(query) &&
    (action !== 'photo' || recentPhotos.length) && (action !== 'download' || currentState.taskText));
  const list = $('#plist');
  list.replaceChildren();
  selected = Math.min(selected,Math.max(0,items.length-1));
  let previous;
  for (const [group,label,action,hint] of items) {
    if (group !== previous) list.append(element('h4',group));
    previous = group;
    const b = element('button');
    b.id = 'command-'+action;
    b.setAttribute('role','option');
    b.tabIndex = -1;
    b.setAttribute('aria-selected','false');
    b.disabled = (action === 'launch' && cta.disabled) || (action === 'close' && (!currentState.closeVisible || !currentState.closeEnabled)) ||
      (action === 'idle' && (currentState.visual !== 'ready' || !currentState.enabled || currentState.still || currentState.running || motion.matches));
    b.append(element('span',label));
    if (hint) b.append(element('small',hint,hint === 'Enter' ? 'kbd' : ''));
    b.onmousedown = e => e.preventDefault();
    b.onclick = () => {
      if (action === 'switcher') return setOverlay(action);
      setOverlay('');
      if (action === 'idle') { clearTimeout(timer); setIdle(true); }
      else if (action === 'launch') cta.click();
      else if (action === 'photo') $('#ledge button').click();
      else bridge?.postMessage({action});
    };
    list.append(b);
  }
  if (!items.length) list.append(element('h4','没有匹配的命令'));
  const buttons = [...list.querySelectorAll('button')];
  const active = buttons.slice(selected).find(b => !b.disabled) || buttons.slice(0,selected).reverse().find(b => !b.disabled);
  if (active) {
    selected = buttons.indexOf(active);
    active.classList.add('sel');
    active.setAttribute('aria-selected','true');
    $('#pIn').setAttribute('aria-activedescendant',active.id);
    active.scrollIntoView({block:'nearest'});
  } else $('#pIn').removeAttribute('aria-activedescendant');
}
$('#pIn').oninput = () => { selected = 0; renderPalette(); };
document.addEventListener('keydown',e => {
  if (e.isComposing || !currentState) return;
  const ov = win.dataset.ov;
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
    e.preventDefault();
    if (!e.repeat) setOverlay(ov === 'palette' ? '' : 'palette');
    return;
  }
  if (ov) {
    if (e.key === 'Escape') { e.preventDefault(); setOverlay(''); }
    if (ov === 'palette') {
      if (e.key === 'Tab') e.preventDefault();
      if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
        e.preventDefault();
        const buttons = [...$('#plist').querySelectorAll('button')];
        const choices = e.key === 'ArrowDown' ? buttons.slice(selected+1) : buttons.slice(0,selected).reverse();
        const next = choices.find(b => !b.disabled);
        if (next) { selected = buttons.indexOf(next); renderPalette(); }
      }
      if (e.key === 'Enter') { e.preventDefault(); if (!e.repeat) $('#plist .sel')?.click(); }
    } else if (['ArrowDown','ArrowUp'].includes(e.key) && e.target.matches('.pf')) {
      const options = [...$('#pfList').querySelectorAll('button:enabled')];
      e.preventDefault();
      options[(options.indexOf(e.target) + (e.key === 'ArrowDown' ? 1 : -1) + options.length) % options.length].focus();
    } else if (e.key === 'Tab') {
      const buttons = [...$('#switcher').querySelectorAll('button:enabled')];
      if (document.activeElement === buttons[e.shiftKey ? 0 : buttons.length-1]) {
        e.preventDefault(); buttons[e.shiftKey ? buttons.length-1 : 0].focus();
      }
    }
    return;
  }
  const launchKey = e.key === 'Enter' && (win.classList.contains('idle') || e.target === document.body || e.target === win || e.target === cta);
  wake();
  if (launchKey) {
    e.preventDefault();
    if (!e.repeat && !cta.disabled) cta.click();
  }
});
