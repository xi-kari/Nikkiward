'use strict';

const switcher = $('#switcher');
const trigger = $('#btnProfile');
function switchProfiles(open) {
  switcher.inert = !open;
  switcher.classList.toggle('open', open);
  $('#backdrop').hidden = !open;
  trigger.setAttribute('aria-expanded', open);
  for (const el of document.querySelectorAll('.top,.hem,.care,.recent,.cl-top,.note,.hero')) el.inert = open;
  (open ? $('#btnRediscover') : trigger).focus({preventScroll:true});
}
trigger.onclick = () => switchProfiles(true);
document.addEventListener('click', event => {
  if (!switcher.inert && !switcher.contains(event.target) && !trigger.contains(event.target)) {
    switchProfiles(false);
  }
});
switcher.onkeydown = event => {
  if (event.key === 'Escape') switchProfiles(false);
  if (event.target.matches('.prof') && ['ArrowUp','ArrowDown','ArrowLeft','ArrowRight'].includes(event.key)) {
    event.preventDefault();
    const options = [...switcher.querySelectorAll('.prof:enabled')];
    const step = ['ArrowDown','ArrowRight'].includes(event.key) ? 1 : -1;
    options[(options.indexOf(event.target) + step + options.length) % options.length].focus();
  }
  if (event.key !== 'Tab') return;
  const buttons = switcher.querySelectorAll('button:enabled');
  const first = buttons[0], last = buttons[buttons.length - 1];
  if (event.shiftKey ? document.activeElement === first : document.activeElement === last) {
    event.preventDefault();
    (event.shiftKey ? last : first).focus();
  }
};
let profileKey;
function renderProfiles(items) {
  const key = JSON.stringify(items);
  if (key === profileKey) return;
  profileKey = key;
  const focusedId = document.activeElement.dataset.profileId;
  $('#switcher header small').textContent = items.length === 3 && new Set(items.map(p => p.channel)).size === 3
    ? '同一个游戏的三个渠道' : items.length + ' 个档案';
  $('#profs').replaceChildren(...items.map(p => {
    const button = document.createElement('button');
    button.className = 'prof';
    button.dataset.profileId = p.id;
    button.setAttribute('role','radio');
    button.setAttribute('aria-checked',p.current);
    button.disabled = !p.available;
    describeProfile(button, p);
    button.innerHTML = '<i class="dot"></i><strong></strong><svg class="i ck"><use href="#i-ck"/></svg><small></small>';
    if (!p.available) button.querySelector('.dot').classList.add(p.reason.startsWith('需要选择安装') ? 'w' : 'n');
    button.querySelector('strong').textContent = p.name;
    button.querySelector('small').textContent = p.reason;
    button.onclick = () => {
      bridge?.postMessage({action:'select-profile', profileId:p.id});
      switchProfiles(false);
    };
    return button;
  }));
  if (!switcher.inert && focusedId) {
    const buttons = [...$('#profs').querySelectorAll('button:enabled')];
    (buttons.find(b => b.dataset.profileId === focusedId) || $('#btnRediscover')).focus();
  }
}

function fitSwitcher() {
  const room = $('.cluster').getBoundingClientRect().top - 62;
  switcher.classList.toggle('short-window',room < 180);
  switcher.style.setProperty('--room',room + 'px');
}
new ResizeObserver(fitSwitcher).observe($('.cluster'));

function sizeSeam() {
  fitSwitcher();
  const w = win.clientWidth, h = win.clientHeight;
  const opening = $('.hole');
  const y = opening.offsetTop + opening.offsetHeight + 6, x = w - 230;
  const path = `M${x} ${y}H42A24 24 0 0 1 18 ${y-24}V70A24 24 0 0 1 42 46H${w-42}A24 24 0 0 1 ${w-18} 70V${y-24}A24 24 0 0 1 ${w-42} ${y}H${x}`;
  $('.seam').setAttribute('viewBox', `0 0 ${w} ${h}`);
  $('#mk').setAttribute('width',w);
  $('#mk').setAttribute('height',h);
  for (const el of document.querySelectorAll('#rev,.stitch,.run')) el.setAttribute('d',path);
  $('.needle').style.offsetPath = `path("${path}")`;
  $('.tail').setAttribute('transform', `translate(${w-1440} ${y-778})`);
  const nav = $('.nav [aria-current]');
  $('#tack').style.transform = `translateX(${nav.offsetLeft + nav.offsetWidth/2 + nav.offsetParent.offsetLeft}px)`;
}
new ResizeObserver(sizeSeam).observe($('.hole'));
new ResizeObserver(sizeSeam).observe($('.brand'));
window.mastheadBounds = () => {
  const {left,right} = $('.nav').getBoundingClientRect();
  const tool = $('#btnTools').getBoundingClientRect();
  return [left,right,tool.left,tool.right];
};

let photoKey;
function renderPhotos(items) {
  const key = JSON.stringify(items);
  if (key === photoKey) return;
  photoKey = key;
  $('.recent').hidden = !items.length;
  $('#shots').replaceChildren(...items.slice(0,4).map(p => {
    const b = document.createElement('button');
    b.className = 'shot';
    b.dataset.action = 'photo';
    b.dataset.photoId = p.id;
    b.title = [p.fileName,p.time].filter(Boolean).join(' · ');
    b.setAttribute('aria-label', '在相册打开 ' + b.title);
    const img = document.createElement('img');
    img.alt = p.thumbnail ? '' : p.fileName;
    if (p.thumbnail) {
      img.onload = () => b.style.width = Math.round(74 * Math.min(img.naturalWidth / img.naturalHeight, 1.55)) + 'px';
      img.src = p.thumbnail;
    }
    b.append(img);
    if (p.session) b.append(Object.assign(document.createElement('b'),{textContent:p.session === 'current' ? '本次' : '上次'}));
    return b;
  }));
}

const artworkPalettes = {
  tide:['#c7dbff','#27468f','#0c162e','#f5f2ea'],
  blossom:['#a13a5c','#f7ccd5','#fff6f4','#8fb0e0']
};

function render(s) {
  const tools = $('#btnTools');
  tools.title = s.nextReminder ? '下次提醒：'+s.nextReminder.text+' · '+new Date(s.nextReminder.due).toLocaleString('zh-CN') : '工具与提醒';
  const visual = s.visual;
  win.dataset.state = visual;
  win.classList.toggle('light',s.theme === 'light');
  win.classList.toggle('quiet',s.running);
  win.classList.toggle('still',s.still);
  win.dataset.bg = s.artSource === '内置原画' ? ({'海月':'tide','花信':'blossom'}[s.artName] || 'custom') : 'custom';
  if (win.dataset.bg === 'custom') {
    const disc = s.palette.find(color => color.toLowerCase() !== s.accent.toLowerCase()) || s.onAccent;
    win.style.setProperty('--th',s.accent.slice(1).match(/../g).map(hex => parseInt(hex,16)).join(','));
    win.style.setProperty('--disc',disc);
    win.style.setProperty('--hole',s.onAccent);
  } else {
    for (const name of ['--th','--disc','--hole']) win.style.removeProperty(name);
  }
  $('#bgName').textContent = [s.artName,s.artSource].filter(Boolean).join(' · ');
  $('#bgName').title = $('#bgName').textContent;
  const palette = artworkPalettes[win.dataset.bg] || s.palette;
  $('#pal').replaceChildren(...palette.map(color => {
    const chip = document.createElement('i');
    chip.style.background = color;
    return chip;
  }));
  $('#bgNote').textContent = [palette.length ? '已按画面取色' : '', s.contrastPassed === true ? '对比度达标' : ''].filter(Boolean).join(' · ');

  $('#hT').textContent = s.launchLabel;
  const blocked = visual === 'blocked' || visual === 'notfound';
  let subtitle = s.detail;
  if (visual === 'ready') subtitle = s.readySummary;
  if (visual === 'running' && s.detail === '目标进程已在运行，启动按钮已锁定。') subtitle = '';
  if (visual === 'blocked') subtitle = s.state === 'SignatureUntrusted' ? '这是安全拒绝，不是故障' : '';
  if (visual === 'notfound') subtitle = s.profile ? '常见位置里没有找到「' + s.profile + '」' : '';
  $('#hS').textContent = subtitle;
  $('#hS').hidden = !subtitle;
  $('#hero').title = s.detail;
  $('#hero').disabled = !s.enabled;
  $('#hero').setAttribute('aria-label', s.launchLabel + '。' + s.detail);
  $('#hero').setAttribute('aria-busy', s.busy);
  const reason = $('#reason');
  const comma = s.detail.indexOf('，');
  if (visual === 'blocked' && comma > 0) {
    const lead = document.createElement('b');
    lead.textContent = s.detail.slice(0,comma);
    reason.replaceChildren(lead,s.detail.slice(comma));
  } else reason.textContent = s.detail;
  $('#reason').hidden = !blocked;
  $('#runningNote').hidden = visual !== 'running';
  const duration = elapsedLabel(s);
  $('#runningNote').textContent = duration && !s.detail.includes(duration) ? duration : '游戏正在运行，动效和粒子已关闭。';
  const chooseInstall = s.state === 'SelectionRequired';
  const actions = chooseInstall ? ['locate',...s.nextActions] : s.nextActions;
  $('#actions [data-action="recheck"]').classList.toggle('pri',!chooseInstall);
  for (const b of $('#actions').querySelectorAll('button')) {
    b.hidden = b.dataset.action === 'close' ? !s.closeVisible : !actions.includes(b.dataset.action);
  }
  const close = $('#actions [data-action="close"]');
  close.disabled = !s.closeEnabled;
  close.querySelector('span').textContent = s.closeLabel;
  $('#actions').hidden = !(s.closeVisible || actions.length || visual === 'running');
  $('#note').classList.toggle('show',blocked || !$('#actions').hidden);

  $('#pName').textContent = s.profile || '选择档案';
  const channel = $('#cChan');
  channel.textContent = s.profile ? s.profile + ' / Windows' : '未配置';
  const repeatedCheck = s.check === s.launchLabel;
  $('#cChkLi > span').textContent = repeatedCheck ? '检查时间' : '启动检查';
  $('#cChk').textContent = repeatedCheck ? s.checkTime : [s.check,s.checkTime].filter(Boolean).join(' · ');
  $('#cChkLi').hidden = !$('#cChk').textContent;
  $('#cChkLi').classList.toggle('bad',blocked);
  $('#pDot').className = 'dot' + (visual === 'blocked' ? ' w' : ['notfound','checking'].includes(visual) ? ' n' : '');
  renderProfiles(s.profiles);
  const hours = s.officialDuration?.match(/^([\d.]+)h$/);
  if (hours) {
    const num = document.createElement('b');
    num.className = 'num';
    num.textContent = hours[1];
    $('#official').replaceChildren(num, ' 小时 · 官方快照');
  } else $('#official').textContent = s.officialDuration || '未同步';
  $('#local').textContent = s.localDuration;
  $('#local').closest('li').hidden = !s.localDuration;
  const task = $('#taskText').parentElement;
  const summary = s.progress == null ? s.taskText : s.taskText?.replace(/ · [\d,]+\/[\d,]+$/, '');
  task.classList.toggle('counted-task',summary != null && summary !== s.taskText);
  $('#taskText').textContent = summary;
  $('#taskText').closest('li').hidden = !s.taskText;
  $('#progress').hidden = $('.bar').hidden = s.progress == null;
  if (s.progress != null) {
    $('#progress').textContent = Math.round(s.progress) + '%';
    $('.bar').style.setProperty('--progress',s.progress + '%');
  }
  for (const em of document.querySelectorAll('.care em')) em.title = em.textContent;
  if (s.profile && channel.scrollWidth > channel.clientWidth) channel.textContent = s.profile;
  task.title = [s.taskText,s.progress == null ? '' : Math.round(s.progress) + '%'].filter(Boolean).join(' · ');
  if (s.photos) renderPhotos(s.photos);
  const count = s.currentSessionPhotos ?? s.lastSessionPhotos;
  $('.recent header span').textContent = count == null ? '' : sessionLabel(s,'上次游玩')+'拍了 ' + count + ' 张';
}

