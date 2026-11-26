(() => {
  const renderers = new Map();
  window.registerSettingsSection = (name, render) => renderers.set(name, render);
  const loads = new Map();
  function element(tag, text, cls) {
    const el = document.createElement(tag);
    if (text != null) el.textContent = text;
    if (cls) el.className = cls;
    return el;
  }
  function icon(path) {
    const svg = document.createElementNS('http://www.w3.org/2000/svg','svg');
    svg.setAttribute('viewBox','0 0 24 24');
    svg.setAttribute('class','i');
    svg.setAttribute('aria-hidden','true');
    const line = document.createElementNS(svg.namespaceURI,'path');
    line.setAttribute('d',path);
    svg.append(line);
    return svg;
  }
  function mount({root,send,commands}) {
    const view = element('section');
    view.id = 'settings';
    view.innerHTML = '<header class="ph"><h1>设置</h1><div class="meta">Nikkiward <span class="m"></span> · 非官方 · 社区项目 · MIT</div></header><div class="cols"><nav class="slist" aria-label="设置分类"></nav><section class="mod content"></section><aside class="mod"></aside></div>';
    root.append(view);
    let state, active, revision = 0, awaitingReply = false;
    const nav = view.querySelector('nav'), content = view.querySelector('.content'), aside = view.querySelector('aside');
    function action(section,op,data = {}) { awaitingReply = true; send('section',{section,op,...data}); }
    function button(label,click) {
      const btn = element('button',label,'btn');
      btn.onclick = click;
      return btn;
    }
    function check() {
      const info = state.sections.about;
      const btn = button(info.checking ? '正在检查…' : '检查更新',() => action('about','check'));
      btn.dataset.focus = 'check-update';
      btn.prepend(icon('M19.5 12a7.5 7.5 0 1 1-2.2-5.3 M19.5 4v4h-4'));
      btn.disabled = info.checking || !info.available;
      return btn;
    }
    function go(id) {
      if (!(id in state.sections)) { send('native',{section:id}); return; }
      if (id === active) return;
      content.replaceChildren();
      active = id;
      draw();
      if (id === 'FileManagement') action(id,'refresh');
    }
    function draw() {
      const ticket = ++revision;
      const appearance = state.sections.appearance;
      if (appearance.theme === 'WarmDark' || appearance.theme === 'WarmLight')
        document.documentElement.dataset.tone = appearance.theme === 'WarmDark' ? 'dark' : 'light';
      view.dataset.density = appearance.density;
      view.dataset.motion = appearance.motion;
      if (appearance.serif != null) view.dataset.serif = String(appearance.serif);
      if (appearance.accentMode === 'Fixed') view.style.setProperty('--accent',appearance.customAccent);
      else view.style.removeProperty('--accent');
      const focus = view.contains(document.activeElement) ? document.activeElement.dataset.focus : null;
      const navItems = state.navigation.map(n => [n.id,n.label]);
      nav.replaceChildren(...navItems.map(([id,label]) => {
        const b = element('button',label);
        b.dataset.focus = id;
        if (id === active) b.setAttribute('aria-current','page');
        b.onclick = () => go(id);
        return b;
      }));
      view.querySelector('.meta .m').textContent = state.sections.about.version;
      aside.replaceChildren(element('h2','键盘'));
      for (const [name,key] of [['启动游戏','Enter'],['命令面板','Ctrl K'],['唤出主窗口',state.mainKey],['游戏截图',state.screenshotKey]]) {
        const row = element('div',null,'kv');
        row.append(element('span',name),element('span',key.replaceAll('+',' '),'kbd'));
        aside.append(row);
      }
      aside.append(element('h2','关于','about-heading'),element('p','Nikkiward 是《无限暖暖》的非官方社区启动器，与叠纸游戏没有关联。它不下载游戏、不登录账号、不改游戏文件。','hint'),check());
      if (active !== 'about' && state.sections.about.status) aside.append(element('p',state.sections.about.status,'hint'));
      function render() {
        if (!view.isConnected || ticket !== revision) return;
        content.replaceChildren();
        if (active !== 'appearance') content.append(element('h2',navItems.find(([id]) => id === active)?.[1]));
        let parent = content;
        function row(label,control,description) {
          const r = element('div',null,'row'), caption = element('div');
          caption.append(element('b',label));
          if (description) caption.append(element('p',description));
          if (control.matches('input,button.tg')) control.setAttribute('aria-label',label);
          control.dataset.focus = label;
          r.append(caption,control);
          parent.append(r);
          return control;
        }
        function save(field,value,section = active) { action(section,'change',{field,value:String(value)}); }
        const ui = {
          element, row, button, action, check, save, go,
          command(label,caption,op,description,disabled = false) {
            const control = button(caption,() => action(active,op));
            control.disabled = disabled;
            return row(label,control,description);
          },
          facts(values) {
            for (const [name,value] of Object.entries(values)) {
              if (value) row(name,element('span',value,'value'));
            }
          },
          add: el => parent.append(el),
          group(label) {
            parent = element('section',null,'settings-group');
            parent.append(element('h2',label));
            content.append(parent);
          },
          toggle(label,field,value,desc) {
            const b = button('',() => save(field,!value));
            b.className = 'tg';
            b.setAttribute('role','switch');
            b.setAttribute('aria-checked',String(value));
            return row(label,b,desc);
          },
          select(label,field,value,options,desc,onChange) {
            const box = element('label',null,'btn select');
            const select = element('select');
            for (const [v,text] of options) select.add(new Option(text,v));
            select.value = String(value);
            if (field === 'delay' && Number(value)) box.append(element('span',value,'m'),' 秒后');
            else box.append(element('span',select.selectedOptions[0]?.textContent));
            box.append(icon('M6 9l6 6 6-6'),select);
            select.setAttribute('aria-label',label);
            select.dataset.focus = field;
            select.onchange = () => (onChange ?? (v => save(field,v)))(select.value);
            row(label,box,desc);
            return select;
          },
          input(label,field,type,value,attrs = {}) {
            const input = element('input',null,'field');
            Object.assign(input,{type,value},attrs);
            input.onchange = () => { if (input.reportValidity()) save(field,input.value); };
            return row(label,input);
          }
        };
        renderers.get(active)(ui,state.sections[active],state);
        if (focus) view.querySelector('[data-focus="'+CSS.escape(focus)+'"]')?.focus({preventScroll:true});
      }
      if (renderers.has(active)) { render(); return; }
      if (!loads.has(active)) {
        const dependencies = ['Journal','Files','Components','Diagnostics'].includes(active);
        if (dependencies && !loads.has('developer')) loads.set('developer',asset('script','settings/developer.js'));
        const path = 'settings/sections/'+active+'.js';
        loads.set(active,dependencies ? loads.get('developer').then(() => asset('script',path)) : asset('script',path));
      }
      loads.get(active).then(render).catch(() => {
        if (view.isConnected && ticket === revision) send('module-error');
      });
    }
    commands.push(['设置','检查更新',() => {
      if (state.sections.about.available && !state.sections.about.checking) action('about','check');
    }]);
    function visibleState(snapshot) {
      const {sections,...shell} = snapshot;
      return JSON.stringify([shell,sections.appearance,sections.general,sections.about,sections[active]]);
    }
    return next => {
      const previous = state;
      state = next;
      active ??= next.initial ?? 'appearance';
      if (!(active in state.sections)) active = 'Advanced';
      if (!awaitingReply && previous && visibleState(previous) === visibleState(next)) return;
      awaitingReply = false;
      draw();
    };
  }
  registerPage('settings',{mount,unmount() {}});
})();
