'use strict';

const icon = (name, size = '') => '<svg aria-hidden="true" class="ic ' + size + '"><use href="#i-' + name + '"/></svg>';
const reducedMotion = matchMedia('(prefers-reduced-motion:reduce)');
const dialog = $('#switcher');
const profileButton = $('#profBtn');
let profileKey, frame, px = 0, py = 0, tx = 0, ty = 0;

function moveStars() {
  px += (tx - px) * .06;
  py += (ty - py) * .06;
  win.style.setProperty('--px', px.toFixed(3));
  win.style.setProperty('--py', py.toFixed(3));
  frame = Math.abs(tx-px) + Math.abs(ty-py) > .001 ? requestAnimationFrame(moveStars) : 0;
}
function pointAt(x, y) {
  if (!win.classList.contains('fx') || win.classList.contains('still')) return;
  tx = x;
  ty = y;
  if (!frame) frame = requestAnimationFrame(moveStars);
}
win.onpointermove = e => pointAt(2 * e.clientX / win.clientWidth - 1, 2 * e.clientY / win.clientHeight - 1);
win.onpointerleave = () => pointAt(0, 0);

function setMotion(s) {
  const still = s.still || s.running || reducedMotion.matches;
  const comparison = document.body.classList.contains('bare');
  win.classList.toggle('still', still);
  win.classList.toggle('noblur', still && !comparison);
  win.classList.toggle('fx', comparison ? !s.running && !reducedMotion.matches : !still);
  if (still) {
    cancelAnimationFrame(frame);
    frame = tx = ty = px = py = 0;
    win.style.setProperty('--px', 0);
    win.style.setProperty('--py', 0);
  }
}
reducedMotion.onchange = () => { if (currentState) setMotion(currentState); };

function positionIndicator() {
  const active = $('#nav [aria-current]');
  const indicator = $('#navInd');
  indicator.style.width = active.offsetWidth + 'px';
  indicator.style.transform = 'translateX(' + active.offsetLeft + 'px)';
}
new ResizeObserver(([entry]) => win.style.setProperty('--tail-width', entry.contentRect.width + 'px')).observe($('.tailwrap'));
new ResizeObserver(positionIndicator).observe($('#nav [aria-current]'));
window.mastheadBounds = () => {
  const nav = $('#nav').getBoundingClientRect();
  return [nav.left, nav.right];
};

function switchProfiles(open, focus = true) {
  win.classList.toggle('switching', open);
  dialog.classList.toggle('on', open);
  dialog.inert = !open;
  $('#backdrop').hidden = !open;
  for (const el of document.querySelectorAll('.top,.lead,.launch,#tail,#strip')) el.inert = open;
  profileButton.setAttribute('aria-expanded', open);
  if (focus) (open ? dialog.querySelector('button:enabled') : profileButton).focus({preventScroll:true});
}
profileButton.onclick = () => switchProfiles(true);
$('#backdrop').onclick = () => switchProfiles(false);
document.addEventListener('keydown', e => {
  if (dialog.inert) return;
  if (e.key === 'Escape') switchProfiles(false);
  if (e.key !== 'Tab') return;
  const buttons = dialog.querySelectorAll('button:enabled');
  const first = buttons[0], last = buttons[buttons.length-1];
  if (document.activeElement === (e.shiftKey ? first : last)) {
    e.preventDefault();
    (e.shiftKey ? last : first).focus();
  }
});

function renderProfiles(s) {
  $('#profName').textContent = profileButton.title = s.profile || '选择档案';
  $('.pop-foot span').textContent = s.discoveryTime ? '上次发现安装 · ' + s.discoveryTime : '';
  const key = JSON.stringify(s.profiles);
  if (key === profileKey) return;
  profileKey = key;
  const focusedId = document.activeElement.dataset.profileId;
  const channels = new Set(s.profiles.map(p => p.channel));
  $('.pop h3 small').textContent = channels.size === 3 && s.profiles.length === 3 ? '一个游戏，三个渠道' :
    s.profiles.length ? s.profiles.length + ' 个档案' : '';
  $('#noProfiles').hidden = !!s.profiles.length;
  $('#profList').replaceChildren(...s.profiles.map(p => {
    const b = document.createElement('button');
    b.className = 'prow';
    b.dataset.profileId = p.id;
    b.disabled = !p.available;
    describeProfile(b, p);
    b.setAttribute('role', 'radio');
    b.setAttribute('aria-checked', p.current);
    b.innerHTML = '<i class="pi">' + icon(p.current ? 'check' : 'planet') + '</i><div><b></b><span></span></div><div class="pst"><em></em><span></span></div>';
    b.querySelector('b').textContent = p.name;
    b.querySelector('span').textContent = p.name === p.region + ' · ' + p.channel ? 'Windows' : [p.channel, p.region, 'Windows'].join(' · ');
    const [reason, ...note] = p.reason.split('\n');
    b.querySelector('em').textContent = reason;
    b.querySelector('.pst span').textContent = note.join(' ');
    b.style.setProperty('--c', p.available ? 'var(--ok)' : 'var(--warn)');
    b.onclick = () => {
      bridge?.postMessage({action:'select-profile', profileId:p.id});
      switchProfiles(false);
    };
    return b;
  }));
  if (!dialog.inert && focusedId) {
    const options = [...dialog.querySelectorAll('.prow:enabled')];
    (options.find(b => b.dataset.profileId === focusedId) || $('#rediscover')).focus();
  }
}

function renderPhotos(s) {
  const list = $('#thumbs');
  const more = $('.thumb.more');
  if (s.photos) {
    const previous = new Map([...list.querySelectorAll('[data-photo-id]')].map(b => [b.dataset.photoId, b]));
    s.photos.slice(0,4).forEach((p, index) => {
      let button = previous.get(p.id);
      if (!button) {
        button = document.createElement('button');
        button.className = 'thumb';
        button.dataset.action = 'photo';
        button.dataset.photoId = p.id;
        button.innerHTML = '<img alt=""><i class="new" hidden><svg viewBox="0 0 24 24"><use href="#i-star4"/></svg></i>';
        const image = button.firstElementChild;
        image.onload = () => button.style.setProperty('--photo-width', Math.round(76 * Math.max(.8, image.naturalWidth/image.naturalHeight)) + 'px');
      }
      previous.delete(p.id);
      const img = button.firstElementChild;
      if (p.thumbnail !== img.getAttribute('src')) {
        if (p.thumbnail) img.src = p.thumbnail;
        else {
          img.removeAttribute('src');
          button.style.removeProperty('--photo-width');
        }
      }
      img.alt = p.thumbnail ? '' : p.fileName;
      button.title = p.fileName + ' · ' + p.time;
      button.setAttribute('aria-label', '在相册打开 ' + button.title);
      button.style.setProperty('--k', index);
      if (list.children[index] !== button) list.insertBefore(button, list.children[index] || more);
    });
    previous.forEach(b => b.remove());
    $('.recent').hidden = !s.photos.length;
    more.style.setProperty('--k', Math.min(4,s.photos.length));
  }
  const count = s.currentSessionPhotos ?? s.lastSessionPhotos;
  $('#sessionCount').textContent = count == null ? '' : sessionLabel(s,'本次游玩')+'拍了 ' + count + ' 张';
  $('.recent-h .tag').hidden = count == null;
  if (s.photos) list.querySelectorAll('.new').forEach((mark, i) => mark.hidden = !photoMarked(s.photos[i],i,count));
}

function renderLaunch(s) {
  const visual = s.state === 'ChannelUnsupported' ? 'unsupported' : s.visual;
  const previous = win.dataset.state;
  win.dataset.state = visual;
  win.dataset.live = Number(s.enabled || visual === 'launching');
  win.classList.toggle('quiet', s.running);
  const orb = $('#orb'), label = $('#orbLabel');
  orb.disabled = !s.enabled;
  orb.title = $('#launchDetail').textContent = s.detail;
  orb.setAttribute('aria-label', s.launchLabel);
  orb.setAttribute('aria-busy', s.busy);
  const signature = s.state === 'SignatureUntrusted';
  label.className = 'orb-label ' + (visual === 'ready' ? '' : signature || visual === 'unsupported' || s.launchLabel.length > 6 ? 'xs' : 'sm');
  const title = document.createElement('span');
  title.textContent = s.launchLabel;
  if (signature || visual === 'unsupported') {
    const cut = signature ? 4 : 2;
    title.replaceChildren(s.launchLabel.slice(0,cut), document.createElement('br'), s.launchLabel.slice(cut));
  }
  label.replaceChildren(title);
  if (signature) label.insertAdjacentHTML('afterbegin', icon('shield'));
  if (previous && previous !== visual && !win.classList.contains('still')) {
    label.classList.add('swap');
    if (visual === 'launching') document.querySelectorAll('.ripple').forEach(r => r.classList.add('go'));
  }
  if (visual !== 'launching') document.querySelectorAll('.ripple').forEach(r => r.classList.remove('go'));
  document.querySelectorAll('.sat').forEach(b => b.disabled = visual === 'launching');
  $('#stat').dataset.tone = ({ready:'ok',running:'ok',launching:'busy',checking:'busy',unsupported:'idle'})[visual] || 'warn';
  const heading = $('#statT'), summary = $('#statS');
  heading.textContent = s.sessionMinutes != null && s.running ? elapsedLabel(s) : s.check;
  heading.parentElement.hidden = !heading.textContent || heading.textContent === s.launchLabel;
  summary.textContent = visual === 'ready' ? s.readySummary : s.detail;
  summary.hidden = !summary.textContent || summary.textContent === s.launchLabel || summary.textContent === heading.textContent;
  summary.title = s.detail;
  if (visual === 'notfound' || s.state === 'SelectionRequired') {
    summary.replaceChildren(...s.detail.split(/(「[^」]+」)/).map(part => {
      if (!part.startsWith('「')) return part;
      const label = document.createElement('span');
      label.className = 'action-label';
      label.textContent = part;
      return label;
    }));
  }
  if (s.running && s.sessionMinutes != null && s.detail === '目标进程已在运行，启动按钮已锁定。') summary.hidden = true;
  const action = $('#tailAct');
  const name = s.closeVisible ? 'close' : s.state === 'SelectionRequired' ? 'locate' : s.nextActions.find(a => a === 'locate' || a === 'recheck');
  action.hidden = !name;
  const retry = $('#retry');
  const secondary = s.nextActions.find(a => a !== name && a !== 'status');
  retry.hidden = !secondary;
  if (secondary) {
    retry.dataset.action = secondary;
    retry.textContent = secondary === 'rediscover' ? '重新发现' : '重新检查';
  }
  if (name) {
    action.dataset.action = name;
    action.disabled = name === 'close' && !s.closeEnabled;
    action.innerHTML = icon(({close:'power',locate:'pin',recheck:'refresh'})[name], 'sm');
    action.append(({close:s.closeLabel,locate:'定位游戏',recheck:'重新检查'})[name]);
  }
}

function render(s) {
  setMotion(s);
  win.dataset.theme = s.theme;
  const builtin = s.artSource === '内置原画';
  win.dataset.bg = builtin ? (s.artName === '海月' ? 'tide' : 'blossom') : 'custom';
  cover.parentElement.style.backgroundImage = builtin ? 'url(' + JSON.stringify(s.background.url) + ')' : '';
  for (const [name,value] of [['--acc',s.accent],['--acc-ink',s.onAccent]]) {
    if (builtin) win.style.removeProperty(name);
    else win.style.setProperty(name,value);
  }
  const datedName = !builtin && /^(\d{4}-\d{2}-\d{2})_(\d{2})-(\d{2})-(\d{2})_(.+)$/.exec(s.artName);
  $('#artName').title = s.artName;
  $('#artName').textContent = datedName ? datedName[5] : s.artName;
  $('#artEn').textContent = builtin ? (s.artName === '海月' ? 'Moonlit Tide' : 'Blossom Letter') : '';
  $('#bgCap').textContent = datedName
    ? s.artSource + ' · ' + datedName[1] + ' ' + datedName.slice(2,5).join(':')
    : builtin ? '启动页原画 · 内置' : s.artSource;
  $('#bgSwap').textContent = builtin ? '换一张' : '恢复默认背景';
  $('#bgSwap').dataset.action = builtin ? (s.artName === '海月' ? 'blossom' : 'tide') : 'reset';
  renderLaunch(s);
  renderProfiles(s);
  renderPhotos(s);
  $('#checked').previousSibling.textContent = s.checkTime === '尚未刷新' ? '' : '检查于 ';
  $('#checked').textContent = s.checkTime;
  $('#official').textContent = s.officialDuration?.replace(/^([\d.]+)h$/, '$1 小时') || '未同步';
  $('#local').textContent = s.localDuration?.replace(/^本机\s*/, '') || '';
  $('#taskLabel').textContent = s.taskText;
  $('.tailwrap').classList.toggle('with-task-detail', !!s.taskText && (!!s.localDuration || s.taskText.length > 12));
  for (const [selector,value] of [['#local',s.localDuration],['#progress',s.taskText]]) {
    const group = $(selector).parentElement;
    group.hidden = group.previousElementSibling.hidden = !value;
  }
  $('#progress').hidden = $('.miniring').hidden = s.progress == null;
  $('#progress').textContent = s.progress == null ? '' : Math.round(s.progress) + '%';
  $('.miniring').style.setProperty('--progress', s.progress + '%');
  const info = [s.check, s.checkTime, '手账时长 ' + $('#official').textContent,
    s.localDuration && '本机时长 ' + $('#local').textContent, s.taskText, $('#progress').textContent].filter(Boolean).join(' · ');
  $('#strip').title = info;
  $('#strip').setAttribute('aria-description', info);
  positionIndicator();
}
