'use strict';
const $ = selector => document.querySelector(selector);
const win = $('#win');
const bridge = window.chrome?.webview;
const cover = $('#coverImg');
const video = $('#coverVideo');
let backgroundKey, backgroundReady, currentState;
let presented = !bridge;
let failedVideo = false;
let frozenCover;

function freezeImage() {
  const bg = currentState.background;
  const freeze = currentState.still && bg.type === 'image' && /\.(gif|webp|png|avif)$/i.test(bg.url);
  if (!freeze) {
    frozenCover?.remove();
    frozenCover = null;
    cover.hidden = false;
  } else if (!frozenCover && cover.complete && cover.naturalWidth) {
    frozenCover = document.createElement('canvas');
    frozenCover.width = cover.naturalWidth;
    frozenCover.height = cover.naturalHeight;
    frozenCover.getContext('2d').drawImage(cover, 0, 0);
    frozenCover.style.objectPosition = cover.style.objectPosition;
    cover.after(frozenCover);
    cover.hidden = true;
  }
}

function playCover() {
  if (!currentState || currentState.background.type !== 'video' || failedVideo) return;
  if (!presented || document.hidden || currentState.still) {
    video.pause();
    video.hidden = true;
  } else {
    video.play().catch(error => { if (error.name !== 'AbortError') video.hidden = true; });
  }
}
video.onplaying = () => {
  video.style.objectPosition = video.videoWidth < video.videoHeight ? '50% 22%' : '50% 40%';
  video.hidden = currentState.still || document.hidden;
};
video.onerror = () => { failedVideo = true; video.hidden = true; };
document.addEventListener('visibilitychange', playCover);

function updateCover(state) {
  const bg = state.background;
  const key = JSON.stringify(bg);
  if (key !== backgroundKey) {
    backgroundKey = key;
    video.hidden = true;
    if (video.hasAttribute('src')) {
      video.removeAttribute('src');
      video.load();
    }
    failedVideo = false;
    frozenCover?.remove();
    frozenCover = null;
    cover.hidden = false;
    cover.src = bg.poster || bg.url;
    backgroundReady = cover.decode().then(() => {
      if (key !== backgroundKey) return;
      cover.style.objectPosition = cover.naturalWidth < cover.naturalHeight ? '50% 22%' : '50% 40%';
      freezeImage();
    });
    if (bg.type === 'video') video.src = bg.url;
  } else freezeImage();
  playCover();
  return backgroundReady;
}

function photoMarked(photo,index,count) {
  return 'session' in photo ? photo.session === (currentState.currentSessionPhotos != null ? 'current' : 'previous') : index < count;
}
function sessionLabel(s, fallback) {
  if (s.currentSessionPhotos != null) return '本次游玩';
  return s.lastSessionPhotos != null && 'currentSessionPhotos' in s ? '上次游玩' : fallback;
}
function elapsedLabel(s) {
  if (s.sessionMinutes == null) return '';
  const start = s.sessionStarted ? new Date(s.sessionStarted).toLocaleTimeString('zh-CN',{hour:'2-digit',minute:'2-digit',hour12:false})+' 开始 · ' : '';
  return start+'已运行 '+s.sessionMinutes+' 分钟';
}
const appreciation = (() => {
  const reduced = matchMedia('(prefers-reduced-motion: reduce)');
  let timer, delay, active = false, plate;
  function change(on) {
    if (active === on) return;
    active = on;
    if (currentState.skin === 'cinema') { setIdle(on); return; }
    if (on) {
      if (!plate) {
        plate = document.createElement('aside');
        plate.className = 'art-caption';
        win.append(plate);
        cover.parentElement.classList.add('appreciation-art');
      }
      plate.textContent = currentState.artName;
    }
    win.classList.toggle('appreciating',on);
  }
  function enter() { clearTimeout(timer); change(true); }
  function wake() {
    clearTimeout(timer);
    change(false);
    if (delay) timer = setTimeout(enter,delay * 1000);
  }
  function refresh() {
    const s = currentState;
    const allowed = bridge && presented && s?.enabled && s.visual === 'ready' && !s.still && !s.running &&
      !document.hidden && !reduced.matches && !win.dataset.ov && !win.querySelector('button[aria-expanded="true"]') &&
      !document.querySelector('dialog[open]');
    const next = allowed ? s.idleSeconds : 0;
    if (next !== delay || (!allowed && active)) { delay = next; wake(); }
    if (active && plate) plate.textContent = s.artName;
  }
  for (const event of ['pointermove','pointerdown','wheel']) document.addEventListener(event,wake,{passive:true});
  document.addEventListener('keydown',e => {
    if (active) { e.preventDefault(); e.stopImmediatePropagation(); }
    wake();
  },true);
  document.addEventListener('visibilitychange',refresh);
  reduced.addEventListener('change',refresh);
  new MutationObserver(refresh).observe(document.body,{subtree:true,attributes:true,attributeFilter:['open','aria-expanded','data-ov']});
  return {refresh,wake,enter};
})();
let minuteTimer;
function updateClock(s) {
  clearTimeout(minuteTimer);
  if (!s.running || !s.sessionStarted) return;
  const ms = Math.max(0,Date.now() - Date.parse(s.sessionStarted));
  s.sessionMinutes = Math.floor(ms/60000);
  minuteTimer = setTimeout(() => {
    updateClock(s);
    render({...s,photos:undefined});
  },60000-ms%60000);
}

async function show(state) {
  const root = document.documentElement;
  if (state.skin) root.dataset.skin = state.skin;
  root.dataset.tone = state.theme;
  updateClock(state);
  currentState = state;
  const ready = updateCover(state);
  if (!bridge) state.still = true;
  render(state);
  appreciation.refresh();
  planner.refresh();
  homeOverlays.refresh();
  if (win.classList.contains('pending')) {
    void win.offsetWidth;
    try { await Promise.all([document.fonts.ready, ready]); }
    catch (error) {
      if (ready === backgroundReady) bridge?.postMessage('failed:封面加载失败 · ' + error.message);
      return;
    }
    if (state !== currentState) return;
    win.classList.remove('pending');
    if (!bridge) document.getAnimations().forEach(animation => { animation.pause(); animation.currentTime = 0; });
    bridge?.postMessage('ready');
    if (window.panelSample) {
      planner.open('check',window.panelEdit);
      await document.fonts.ready;
    }
    if (!state.still) {
      win.classList.add('enter');
      setTimeout(() => win.classList.remove('enter'), 3200);
    }
  } else {
    try { await ready; }
    catch (error) { if (ready === backgroundReady) bridge?.postMessage('failed:封面加载失败 · ' + error.message); }
  }
}

if (bridge) bridge.addEventListener('message', ({data}) => {
  if (['open-status','open-background','open-settings'].includes(data)) return homeOverlays.open(data.slice(5));
  if (data === 'open-planner') return planner.open();
  if (typeof data === 'string' && data.startsWith('planner-error:')) return planner.result(data.slice(14));
  if (data === 'planner-saved') return planner.result();
  if (data === 'presented') {
    presented = true;
    appreciation.refresh();
    playCover();
  } else show(data).catch(error => bridge.postMessage('failed:首页渲染失败 · ' + error.message));
});
else if (location.protocol === 'file:' && new URLSearchParams(location.search).has('mock')) {
  const script = document.createElement('script');
  const id = location.pathname.split('/').at(-2);
  script.src = '../../../Web.dev/' + id + '/cases.js';
  document.body.classList.add('bare','noanim');
  win.classList.add('still');
  document.addEventListener('DOMContentLoaded', () => {
    const mock = new URLSearchParams(location.search).get('mock');
    if (['config','diag','config-blossom','config-real','diag-real','diag-real-blocked','config-real-pkg'].includes(mock)) {
      const fixture = document.createElement('script');
      fixture.src = '../../../Web.dev/overlays.js';
      fixture.onload = () => document.head.append(script);
      if (mock.includes('-real')) {
        const samples = document.createElement('script');
        samples.src = '../../../Web.dev/overlay-samples.js';
        samples.onload = () => document.head.append(fixture);
        document.head.append(samples);
      } else document.head.append(fixture);
      return;
    }
    if (!(mock === 'panel' || mock === 'panel-light' || id === 'atelier' && mock === 'panel-edit')) return document.head.append(script);
    window.panelHomeCase = mock === 'panel-light' ? 'home-blossom' : 'home-tide';
    window.panelEdit = mock === 'panel-edit';
    const fixture = document.createElement('script');
    fixture.src = '../../../Web.dev/panel.js';
    fixture.onload = () => document.head.append(script);
    document.head.append(fixture);
  }, {once:true});
}

function sendHomeAction(message) {
  if (['settings','status','background'].includes(message.action)) homeOverlays.open(message.action);
  else bridge?.postMessage(message);
}

document.addEventListener('click', e => {
  const button = e.target.closest('button[data-action]');
  if (!button || button.disabled) return;
  const message = {action:button.dataset.action};
  if (message.action === 'photo') message.photoId = button.dataset.photoId;
  sendHomeAction(message);
});

function describeProfile(button, profile) {
  if (profile.detail) {
    if (button.disabled) {
      button.disabled = false;
      button.setAttribute('aria-disabled', 'true');
      button.addEventListener('click', event => event.stopImmediatePropagation(), true);
    }
    button.title = profile.detail;
    button.setAttribute('aria-description', profile.detail);
  }
}
