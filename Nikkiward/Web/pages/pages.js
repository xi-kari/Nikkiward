const bridge = window.chrome?.webview;
const modules = new Map(), assets = new Map();
const root = document.querySelector('#page'), error = document.querySelector('#error');
const destinations = {home:'启动', gallery:'相册', favorites:'收藏', journal:'手账', wish:'共鸣', settings:'设置'};
let active, pending, generation = 0;
function registerPage(name, module) { modules.set(name, module); }
function asset(tag, url) {
  return new Promise((resolve, reject) => {
    const node = document.createElement(tag);
    node.onload = resolve;
    node.onerror = reject;
    if (tag === 'link') { node.rel = 'stylesheet'; node.href = url; }
    else node.src = url;
    document.head.append(node);
  });
}
function navigate(to) { bridge?.postMessage({action:'navigate', to}); }
function send(action, args = {}) {
  if (active) bridge?.postMessage({...args, page:active.page, action});
}
for (const [to, label] of Object.entries(destinations)) {
  const b = document.createElement('button');
  b.textContent = label;
  b.dataset.route = to;
  b.onclick = () => navigate(to);
  document.querySelector('.nav').append(b);
}
// Where the window's title bar lets clicks through: the navigation buttons and the command button.
// Everything else in the top strip moves the window.
window.mastheadBounds = () => {
  const edges = list => {
    const rects = list.map(el => el.getBoundingClientRect()).filter(r => r.width > 0);
    return rects.length ? [Math.min(...rects.map(r => r.left)), Math.max(...rects.map(r => r.right))] : null;
  };
  const nav = edges([...document.querySelectorAll('.nav button')]);
  const tool = edges([document.querySelector('#cmdBtn')].filter(Boolean));
  return nav ? (tool ? [...nav, ...tool] : nav) : null;
};
const palette = createCommandPanel(document.querySelector('#cmdBtn'), () => [
  ...Object.entries(destinations).map(([to,label]) => ['前往',label,() => navigate(to)]),
  ...(active?.commands ?? [])
]);
function leave() {
  generation++;
  pending = null;
  if (palette.open) palette.close();
  active?.module.unmount();
  active = null;
  root.replaceChildren();
  error.hidden = true;
}
async function receivePage(message) {
  if (message.type === 'leave') { leave(); return; }
  if (message.type === 'error') {
    if (active?.page === message.page) { error.textContent = message.message; error.hidden = false; }
    return;
  }
  const {page,module:name,skin,tone,state} = message;
  document.documentElement.dataset.skin = skin;
  document.documentElement.dataset.tone = tone;
  if (message.type === 'appearance') return;
  if (active?.page === page) {
    error.hidden = true;
    try { active.update(state); }
    catch (failure) {
      if (!bridge) throw failure;
      bridge.postMessage({page,action:'module-error',reason:failure.message});
    }
    return;
  }
  if (pending?.page === page) { pending.state = state; return; }
  leave();
  pending = {page,state};
  const ticket = generation;
  try {
    if (!assets.has(name)) assets.set(name, Promise.all([asset('link',name+'/page.css'), asset('script',name+'/page.js')]));
    await assets.get(name);
    if (ticket !== generation) return;
    const module = modules.get(name);
    active = {page,module,commands:[]};
    active.update = module.mount({root, page, send:(action,fields) => { if (ticket === generation) send(action,fields); }, commands:active.commands});
    active.update(pending.state);
    pending = null;
    document.querySelectorAll('.nav button').forEach(b => {
      if (b.dataset.route === page) b.setAttribute('aria-current','page');
      else b.removeAttribute('aria-current');
    });
  } catch (failure) {
    if (ticket !== generation) return;
    bridge?.postMessage({page,action:'module-error',reason:failure.message});
    if (!bridge) throw failure;
  }
}
bridge?.addEventListener('message',e => receivePage(e.data));
if (!bridge && location.protocol === 'file:') {
  const name = new URLSearchParams(location.search).get('mock');
  if (name && /^[a-z]+(?:-[a-z]+)*$/.test(name)) asset('script','../../Web.dev/pages/'+name.split('-')[0]+'/cases.js');
}

