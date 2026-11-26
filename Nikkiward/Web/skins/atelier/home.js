'use strict';
const tag = $('#tag');
const photos = $('#recent');
const kicks = {running:'本期进行中', blocked:'暂不能启动', notfound:'暂不能启动', checking:'请稍候'};

function number(value) {
  const span = document.createElement('span');
  span.className = 'num';
  span.textContent = value;
  return span;
}

function showPhotos(items, lastSessionPhotos) {
  const previous = new Map([...photos.children].map(b => [b.dataset.photoId, b]));
  items.forEach((p, index) => {
    let button = previous.get(p.id);
    if (!button) {
      button = document.createElement('button');
      button.dataset.action = 'photo';
      button.dataset.photoId = p.id;
      button.append(document.createElement('img'));
    }
    const image = button.firstElementChild;
    if (p.thumbnail && image.getAttribute('src') !== p.thumbnail) image.src = p.thumbnail;
    let marker = button.querySelector('.new');
    if (photoMarked(p,index,lastSessionPhotos) && !marker) {
      marker = document.createElement('span');
      marker.className = 'new';
      button.append(marker);
    } else if (!photoMarked(p,index,lastSessionPhotos)) marker?.remove();
    image.alt = p.thumbnail ? '' : p.fileName;
    button.title = p.fileName + ' · ' + p.time;
    button.setAttribute('aria-label', '在相册打开 ' + button.title);
    if (photos.children[index] !== button) photos.insertBefore(button, photos.children[index] || null);
    previous.delete(p.id);
  });
  previous.forEach(button => button.remove());
  $('.recent').hidden = !items.length;
}

window.mastheadBounds = () => {
  if (win.classList.contains('pending')) return 'loading';
  const brand = $('.wordmark').getBoundingClientRect();
  const nav = $('#nav').getBoundingClientRect();
  return [brand.width ? brand.left : nav.left, nav.right];
};

function render(s) {
  win.dataset.theme = s.theme;
  win.style.setProperty('--accent', s.accent);
  win.style.setProperty('--on-accent', s.onAccent);
  win.classList.toggle('custom-art', s.artSource === '自定义背景');
  win.classList.toggle('still', s.still);
  if (win.dataset.state && win.dataset.state !== s.visual && !s.still) {
    tag.classList.remove('swap');
    void tag.offsetWidth;
    tag.classList.add('swap');
  }
  win.dataset.state = tag.dataset.state = s.visual;
  tag.dataset.kind = ['ready','launching'].includes(s.visual) ? 'solid' : 'hollow';
  $('#tagTitle').textContent = s.launchLabel;
  $('#tagSub').textContent = s.visual === 'ready' ? s.readySummary : s.sessionMinutes != null && s.visual === 'running' ? elapsedLabel(s) : s.detail;
  $('#launchDetail').textContent = $('#launch').title = s.detail;
  $('#tagKick').textContent = s.state === 'SignatureUntrusted' ? '暂不能启动 · 安全拒绝' : kicks[s.visual] || '';
  $('#launch').disabled = !s.enabled;
  $('#launch').setAttribute('aria-label', s.launchLabel);
  $('#launch').setAttribute('aria-busy', s.busy);
  $('#closeGame').hidden = !s.closeVisible;
  $('#closeGame').disabled = !s.closeEnabled;
  $('#closeGame').textContent = s.closeLabel;
  $('#status').hidden = !s.nextActions.includes('status');
  $('#retry').dataset.action = s.visual === 'notfound' ? 'rediscover' : 'recheck';
  $('#retry').textContent = s.visual === 'notfound' ? '重新发现' : '重新检查';
  $('#locate').hidden = !s.nextActions.includes('locate') && s.state !== 'SelectionRequired';
  $('#tagActs').hidden = !['blocked','notfound'].includes(s.visual);
  $('#profileName').textContent = s.profile || '选择档案';
  renderProfiles(s);
  $('#platform').textContent = [s.channel, 'Windows'].filter(Boolean).join(' · ');
  const checkSummary = {SignatureUntrusted:'签名异常', SelectionRequired:'待选目录', Running:'已在运行',
    NotInstalled:s.profile ? s.check : '待定位'};
  $('#chkV').textContent = s.check === s.launchLabel ? checkSummary[s.state] || s.check : s.check;
  $('#chkS').textContent = [s.checkCount == null ? '' : s.checkCount + ' 项', s.checkTime].filter(Boolean).join(' · ');
  const hours = s.officialDuration?.match(/^(\d+(?:\.\d+)?)\s*(?:h|小时)$/);
  const official = $('#official');
  if (hours) {
    official.replaceChildren(number(hours[1]), ' 小时');
  } else {
    official.textContent = s.officialDuration || '未同步';
  }
  const snapshot = s.journalSnapshotDate && new Date(s.journalSnapshotDate + 'T12:00:00');
  $('#journalInfo').textContent = !s.officialDuration ? '官方时长 · 去手账页同步' :
    snapshot ? '官方时长 · ' + (snapshot.getMonth() + 1) + ' 月 ' + snapshot.getDate() + ' 日快照' : '官方时长';
  $('#local').textContent = s.localDuration && !s.localDuration.startsWith('本机') ? '本机 ' + s.localDuration : s.localDuration;
  $('#local').hidden = !s.localDuration;
  if (s.officialDuration && s.localDuration && !snapshot) {
    $('#journalInfo').textContent = '官方时长 · ' + $('#local').textContent;
    $('#journalInfo').title = $('#journalInfo').textContent;
    $('#local').hidden = true;
  } else $('#journalInfo').removeAttribute('title');
  if (s.checklist) {
    $('#checklist').replaceChildren(number(s.checklist.completed), ' / ', number(s.checklist.total));
    $('#nextTask').textContent = s.checklist.next ? '还剩：' + s.checklist.next : '今日已完成';
    $('#nextTask').title = s.checklist.next || '';
  } else $('#checklist').textContent = '还没有清单';
  $('#nextTask').hidden = !s.checklist;
  $('#allPhotos').textContent = s.photoTotal == null ? '全部' : '全部 ' + s.photoTotal + ' 张';
  const last = $('#lastSession');
  const count = s.currentSessionPhotos ?? s.lastSessionPhotos;
  last.hidden = count == null;
  if (!last.hidden) last.replaceChildren(document.createElement('b'), sessionLabel(s,'上次游玩')+'拍的 ' + count + ' 张');
  $('#coverKick').textContent = s.artSource;
  const title = $('#coverTitle');
  const caption = s.artSource === '自定义背景'
    ? s.artName.replace(/^\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}_(?=\S)/, '') : s.artName;
  if (title.title !== s.artName) {
    title.dataset.len = Math.min(4, [...caption].length);
    title.classList.toggle('long-title', [...caption].length > 4);
    if (title.classList.contains('long-title')) title.textContent = caption;
    else title.replaceChildren(...[...caption].map(ch => {
      const span = document.createElement('span');
      span.append(document.createElement('b'));
      span.firstChild.textContent = ch;
      return span;
    }));
    title.title = s.artName;
  }
  const task = $('#bgtask');
  task.hidden = !s.taskText;
  task.firstElementChild.textContent = task.title = s.taskText || '';
  task.querySelector('.num').textContent = Math.round(s.progress);
  task.style.setProperty('--progress', s.progress + '%');
  task.lastElementChild.hidden = task.querySelector('.bar').hidden = s.progress == null;
  $('#issueDate').textContent = s.issue;
  if (s.photos) showPhotos(s.photos, s.lastSessionPhotos);
}


function switchProfiles(open) {
  $('#switcher').classList.toggle('on', open);
  $('#veil').classList.toggle('on', open);
  $('#profileBtn').setAttribute('aria-expanded', open);
  document.querySelectorAll('#win > header,#win > section').forEach(el => el.inert = open);
  if (bridge) (open ? $('#switcher button:not(:disabled)') : $('#profileBtn')).focus({preventScroll:true});
}
$('#profileBtn').onclick = () => switchProfiles(true);
$('#veil').onclick = () => switchProfiles(false);
document.addEventListener('keydown', e => {
  if (!$('#switcher').classList.contains('on')) return;
  if (e.key === 'Escape') switchProfiles(false);
  if (e.key === 'Tab') {
    const buttons = [...$('#switcher').querySelectorAll('button:not(:disabled)')];
    if (document.activeElement === buttons[e.shiftKey ? 0 : buttons.length-1]) {
      e.preventDefault();
      buttons[e.shiftKey ? buttons.length-1 : 0].focus();
    }
  }
});
let profileKey;
function renderProfiles(s) {
  $('#switcher .ft span').textContent = s.discoveryTime ? '上次发现：' + s.discoveryTime : '';
  const profiles = s.profiles;
  const key = JSON.stringify(profiles);
  if (key === profileKey) return;
  profileKey = key;
  const count = profiles.length;
  $('#switcher .kick').textContent = '无限暖暖 · ' + (count === 3 && new Set(profiles.map(p => p.channel)).size === 3 ? '三个渠道' : count + ' 个档案');
  $('#profs').replaceChildren(...profiles.map(p => {
    const b = document.createElement('button');
    b.className = 'prof ' + (p.available ? '' : 'na');
    b.disabled = !p.available;
    describeProfile(b, p);
    b.setAttribute('role','radio');
    b.setAttribute('aria-checked',p.current);
    b.innerHTML = '<span><b></b><small><i></i></small></span><svg viewBox="0 0 16 16"><path d="M2.5 8.5l3.5 3.5 7.5-8"/></svg>';
    b.querySelector('b').textContent = p.name;
    b.querySelector('small').append(p.reason);
    b.querySelector('i').classList.toggle('off',!p.available);
    b.onclick = () => { bridge?.postMessage({action:'select-profile',profileId:p.id}); switchProfiles(false); };
    return b;
  }));
}
