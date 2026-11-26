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

async function show(state) {
  currentState = state;
  const ready = updateCover(state);
  if (!bridge) state.still = true;
  render(state);
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
  if (data === 'presented') {
    presented = true;
    playCover();
  } else show(data).catch(error => bridge.postMessage('failed:首页渲染失败 · ' + error.message));
});
else if (location.protocol === 'file:' && new URLSearchParams(location.search).has('mock')) {
  const script = document.createElement('script');
  const id = location.pathname.split('/').at(-2);
  script.src = '../../../Web.dev/' + id + '/cases.js';
  document.body.classList.add('bare','noanim');
  win.classList.add('still');
  document.addEventListener('DOMContentLoaded', () => document.head.append(script), {once:true});
}

if (bridge) document.addEventListener('click', e => {
  const button = e.target.closest('button[data-action]');
  if (!button || button.disabled) return;
  const message = {action:button.dataset.action};
  if (message.action === 'photo') message.photoId = button.dataset.photoId;
  if (message.action === 'profile') {
    const {x,y,width,height} = button.getBoundingClientRect();
    message.rect = {x,y,width,height};
  }
  bridge.postMessage(message);
});
