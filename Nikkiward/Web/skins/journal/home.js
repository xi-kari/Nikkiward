'use strict';

const stamps = {
  ready:['出发','需管理员确认'], launching:['检票','等待确认'], running:['旅途中','已检票'],
  blocked:['未放行','不能启动'], notfound:['待定位','不能启动'], checking:['核对中','请稍候']
};

function renderPhotos(items) {
  const recent = $('.recent');
  const previous = new Map([...recent.querySelectorAll('.pola')].map(b => [b.dataset.photoId, b]));
  items.slice(0, 3).forEach((p, i) => {
    const button = previous.get(p.id) || document.createElement('button');
    if (!button.firstChild) {
      button.className = 'pola';
      button.dataset.action = 'photo';
      button.dataset.photoId = p.id;
      button.innerHTML = '<span class="tape"></span><i></i><span></span>';
    }
    button.firstChild.hidden = i !== 0;
    button.children[1].style.backgroundImage = p.thumbnail ? 'url(' + JSON.stringify(p.thumbnail) + ')' : '';
    button.children[2].textContent = p.time.replace(/^\d{4}-(?=\d{2}-\d{2} )/, '');
    button.setAttribute('aria-label', '打开照片：' + p.fileName + ' · ' + p.time);
    button.title = p.fileName;
    if (recent.children[i+1] !== button) recent.insertBefore(button, recent.children[i+1] || null);
    previous.delete(p.id);
  });
  previous.forEach(b => b.remove());
  recent.hidden = items.length === 0;
}

let profileKey;
function renderProfiles(s) {
  $('.foot span').textContent = s.discoveryTime ? '上次发现安装：' + s.discoveryTime : '';
  const key = JSON.stringify(s.profiles);
  if (profileKey === key) return;
  profileKey = key;
  const count = s.profiles.length;
  $('.switcher .lead').textContent = count === 3 ? '同一个游戏，三条线路。每个档案各自检查安装，互不影响。' :
    count ? '每个档案各自检查安装，互不影响。' : '尚未发现安装';
  const focused = document.activeElement.dataset.profileId;
  $('#profs').replaceChildren(...s.profiles.map(p => {
    const b = document.createElement('button');
    b.className = p.available ? 'prof can' : 'prof';
    b.disabled = !p.available;
    b.dataset.profileId = p.id;
    b.setAttribute('role','radio');
    b.setAttribute('aria-checked',p.current);
    b.innerHTML = '<div><b></b><span></span></div><div class="st"><i></i></div>';
    b.querySelector('b').textContent = p.name;
    b.querySelector('span').textContent = p.reason;
    b.querySelector('i').textContent = p.current ? '当前' : p.available ? '可选择' : '不可用';
    b.onclick = () => {
      bridge?.postMessage({action:'select-profile', profileId:p.id});
      switchProfiles(false);
    };
    return b;
  }));
  if (focused) {
    const buttons = [...$('#switcher').querySelectorAll('button:not(:disabled)')];
    (buttons.find(b => b.dataset.profileId === focused) || buttons[0]).focus();
  }
}

const switcher = $('#switcher');
function fitSwitcher() {
  switcher.classList.toggle('scroll', switcher.scrollHeight > switcher.clientHeight);
}
const switcherSize = new ResizeObserver(fitSwitcher);
switcherSize.observe($('#profs'));
switcherSize.observe(switcher);

function switchProfiles(open, moveFocus = true) {
  document.body.dataset.over = open ? 'switcher' : '';
  $('#veil').hidden = !open;
  $('.home').inert = $('.tabs').inert = open;
  switcher.inert = !open;
  $('#profBtn').setAttribute('aria-expanded', open);

  if (moveFocus) (open ? switcher.querySelector('button:not(:disabled)') : $('#profBtn')).focus({preventScroll:true});
}
$('#profBtn').onclick = () => switchProfiles(true);
$('#veil').onclick = () => switchProfiles(false);
document.addEventListener('keydown', e => {
  if ($('#switcher').inert) return;
  if (e.key === 'Escape') switchProfiles(false);
  if (e.key === 'Tab') {
    const buttons = [...$('#switcher').querySelectorAll('button:not(:disabled)')];
    if (document.activeElement === buttons[e.shiftKey ? 0 : buttons.length - 1]) {
      e.preventDefault();
      buttons[e.shiftKey ? buttons.length - 1 : 0].focus();
    }
  }
});
$('#go').onclick = () => {
  if (win.classList.contains('still')) return;
  const ticket = $('#ticket');
  ticket.classList.remove('stamping');
  void ticket.offsetWidth;
  ticket.classList.add('stamping');
};

function render(s) {
  const body = document.body;
  const custom = s.artSource !== '内置原画';
  body.dataset.tone = s.theme;
  win.style.setProperty('--accent', custom ? s.accent : '');
  win.style.setProperty('--on-accent', custom ? s.onAccent : '');
  win.style.setProperty('--accent-2', custom ? s.palette[0] : '');
  win.style.setProperty('--stamp', custom ? s.palette[0] : '');
  win.classList.toggle('still',s.still);
  body.dataset.state = s.visual;
  const [stamp,note] = stamps[s.visual];
  $('#stampTxt').textContent = stamp;
  $('#stampTxt').setAttribute('font-size',stamp.length > 2 ? 20 : 27);
  $('#stampTxt').setAttribute('y',stamp.length > 2 ? 57 : 59.5);
  $('#stubNote').textContent = note;
  $('#goTtl').textContent = s.launchLabel;
  $('#goSub').textContent = s.visual === 'ready' && s.readySummary ? s.readySummary : s.detail;
  const go = $('#go');
  go.disabled = !s.enabled;
  go.title = s.detail;
  go.setAttribute('aria-label', s.launchLabel);
  go.setAttribute('aria-description', s.detail);
  go.setAttribute('aria-busy', s.busy);
  let primary = true;
  for (const b of $('#acts').children) {
    const action = b.dataset.action;
    b.hidden = action === 'close' ? !s.closeVisible : !s.nextActions.includes(action);
    if (action === 'close') {
      b.disabled = !s.closeEnabled;
      b.textContent = s.closeLabel;
    }
    b.classList.toggle('pri', primary && !b.hidden);
    if (!b.hidden) primary = false;
  }
  $('#profName').textContent = s.profile || '未选择';
  $('#profBtn').title = s.profile;
  const time = s.checkTime.replace(/^今天 /, '');
  const check = s.check === '检查通过' ? '通过' : s.check;
  $('#chkTxt').textContent = check === s.launchLabel && time ? time : [check,time].filter(Boolean).join(' · ');
  $('.field[data-action="status"]').title = [s.check,s.checkTime].filter(Boolean).join(' · ');
  $('.field:last-child b').textContent = s.officialDuration?.replace(/^([\d.]+)h$/, '$1 小时') || '未同步';
  const artName = $('#artName');
  const caption = custom ? s.artName.replace(/^\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}_(?=\S)/, '') : s.artName;
  artName.textContent = caption;
  artName.title = s.artName;
  $('.datestamp').hidden = caption !== s.artName;
  $('.datestamp').textContent = s.date.replaceAll('-','.');
  const task = $('.note');
  task.hidden = !s.taskText;
  const percent = s.progress == null ? '' : ' ' + Math.round(s.progress) + '%';
  const fullTask = s.taskText || '';
  const taskCaption = percent ? fullTask.replace(/ · \d+\/\d+$/, '') : fullTask;
  task.firstChild.textContent = taskCaption + percent;
  task.title = fullTask + percent;
  task.setAttribute('aria-label', task.title + '，打开诊断');
  $('.note .bar').hidden = s.progress == null;
  $('.note u').style.width = s.progress + '%';
  if (s.photos) renderPhotos(s.photos);
  renderProfiles(s);
}
