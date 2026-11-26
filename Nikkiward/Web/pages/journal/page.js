(() => {
  const link = '<svg class="i" aria-hidden="true" viewBox="0 0 24 24"><path d="M10 5H5.5v13.5H19V14M13.5 4.5h6v6M19.5 4.5L11 13"/></svg>';
  const plus = '<svg class="i" aria-hidden="true" viewBox="0 0 24 24"><path d="M12 5v14M5 12h14"/></svg>';
  function node(tag, text, cls) {
    const el = document.createElement(tag);
    if (text != null) el.textContent = text;
    if (cls) el.className = cls;
    return el;
  }
  function metric(label, value, unit) {
    const row = node('div', null, 'kv'), number = node('span');
    const raw = String(value ?? '').trim();
    const numeric = /^[\d.,]+$/.test(raw);
    const hours = unit === '小时' && raw.match(/^([\d.,]+)\s*(?:h|hours?|小时)$/i);
    number.append(node('span', hours ? hours[1] : raw || '—', 'm big'));
    if (numeric || hours) number.append(node('small', unit));
    row.append(node('span', label), number);
    return row;
  }
  function hours(value) { return Number(value.toFixed(1)).toLocaleString('zh-CN'); }
  function mount({root, send, commands}) {
    const page = node('section');
    page.id = 'journal';
    page.setAttribute('aria-label','奇想手账');
    page.innerHTML = `<header class="ph"><h1>奇想手账</h1><div class="meta"></div><div class="sp"></div><button class="btn solid">${link}打开官方手账</button></header>
      <div class="cols"><section class="mod"><h2>快照摘要</h2><div class="metrics"></div>
      <p class="hint snapshot-hint"></p><button class="webph">${link}<span>官方网页在这里用内置浏览器打开<span class="browser-note"><br><small></small></span></span></button><div class="modules"></div></section>
      <section class="mod"><h2>本机游玩时长 <span class="since"></span></h2><div class="play"></div></section>
      <section class="mod"><h2>今日清单 <span>手动添加 · 不联网</span></h2><div class="items"></div>
      <button class="btn ghost add-item">${plus}添加一项</button>
      <h2 class="tools-title">固定的工具 <span>用系统浏览器打开</span></h2><div class="links"></div>
      <button class="btn ghost add-link">${plus}固定一个链接</button></section></div>`;
    root.append(page);
    const find = s => page.querySelector(s);
    const official = () => send('official');
    find('.solid').onclick = find('.webph').onclick = official;
    commands.push(['手账','打开官方手账',official]);
    function editor(selector, isLink) {
      const opener = find(selector), form = node('form'), title = node('input');
      let submitted;
      function field(input, label) {
        input.className = 'field';
        input.required = true;
        input.placeholder = label;
        input.setAttribute('aria-label',label);
        form.append(input);
      }
      field(title, isLink ? '工具名称' : '清单内容');
      const url = node('input');
      if (isLink) { url.type = 'url'; field(url,'HTTPS 链接'); }
      const save = node('button', isLink ? '固定' : '添加', 'btn solid');
      const cancel = node('button','取消','btn');
      cancel.type = 'button';
      form.append(save,cancel);
      opener.after(form);
      form.hidden = true;
      function close() { form.hidden = true; opener.hidden = false; opener.focus(); }
      opener.onclick = () => { opener.hidden = true; form.hidden = false; title.focus(); };
      cancel.onclick = () => { submitted = null; close(); };
      form.onkeydown = e => { if (e.key === 'Escape') cancel.click(); };
      form.onsubmit = e => {
        e.preventDefault();
        if (!title.value.trim()) { title.focus(); return; }
        submitted = {text:title.value.trim(), ids:new Set([...find(isLink ? '.links' : '.items').querySelectorAll('[data-id]')].map(el => el.dataset.id))};
        send(isLink ? 'link-add' : 'check-add', {text:title.value.trim(), ...(isLink ? {url:url.value.trim()} : {})});
      };
      return entries => {
        if (!submitted || !entries.some(e => !submitted.ids.has(e.id) && e.text === submitted.text)) return;
        submitted = null;
        form.reset(); close();
      };
    }
    const confirmItem = editor('.add-item',false);
    const confirmLink = editor('.add-link',true);

    function snapshot(s) {
      const meta = find('.meta');
      meta.replaceChildren();
      if (s.date) meta.append('本地快照 ',node('span',s.date,'m'),' · 打开官方手账后更新');
      else meta.textContent = '尚未同步';
      find('.metrics').replaceChildren(...(s.date ? s.metrics.map(m => metric(m.label,m.value,m.unit)) : []));
      find('.snapshot-hint').textContent = s.date ? '数字来自上次打开官方手账时保存的快照，Nikkiward 不登录账号，也不会自己联网刷新。' : '打开官方手账并同步后，这里会显示快照。';
      find('.browser-note').hidden = !s.browserNote;
      find('.webph small').textContent = s.browserNote ?? '';
      find('.modules').replaceChildren(...(s.modules ?? []).map(m => {
        const details = node('details');
        details.append(node('summary',m.title || '手账模块'));
        if (m.text) details.append(node('p',m.text,'hint'));
        for (const r of m.rows) {
          const row = node('div',null,'kv');
          row.append(node('span',r.label),node('span',r.value || (r.image ? '' : '—')));
          if (r.image) {
            const preview = node('button','查看图片','btn');
            preview.setAttribute('aria-label','查看图片：'+r.label);
            preview.onclick = () => send('preview-image',{id:r.image});
            row.append(preview);
          }
          details.append(row);
        }
        return details;
      }));
    }
    function play(s) {
      const box = find('.play');
      box.replaceChildren();
      const since = find('.since');
      since.replaceChildren();
      if (!s.enabled) {
        const settings = node('button','去设置里开启','btn');
        settings.onclick = () => send('time-settings');
        box.append(node('p','本机游玩时长记录默认关闭。开启后，只在这台电脑上记录。','hint'),settings);
        return;
      }
      if (s.since) since.append('自 ',node('span',s.since,'m'),' 开启 · ');
      since.append('只记在这台电脑');
      if (s.error) box.append(node('p',s.error,'hint'));
      if (!s.play) { if (!s.error) box.append(node('p','正在读取本机记录…','hint')); return; }
      const p = s.play, week = metric('最近 7 天',hours(p.week),'小时');
      week.classList.add('week');
      box.append(metric('累计',hours(p.total),'小时'),week);
      const bars = node('div',null,'bars'), labels = node('div',null,'bl');
      const max = Math.max(1,...p.days.map(d => Math.ceil(d.hours)));
      for (const day of p.days) {
        const column = node('div'), bar = node('b');
        bar.style.height = (day.hours / max * 78) + '%';
        column.append(node('span',day.hours ? day.hours.toFixed(1) : '–','m'),bar);
        column.setAttribute('aria-label','星期'+day.label+'，'+hours(day.hours)+' 小时');
        bars.append(column);
        labels.append(node('span',day.label));
      }
      box.append(bars,labels,node('h2','最近几次','recent-title'));
      for (const r of p.recent) {
        const row = node('div',null,'kv');
        row.append(node('span',r.time,'m'),node('span',r.channel,'d'),node('span',r.duration,'m'));
        box.append(row);
      }
      if (!p.recent.length) box.append(node('p','暂无游玩记录','hint'));
    }
    function planner(items, links) {
      find('.items').replaceChildren(...items.map(i => {
        const label = node('label',null,'todo'+(i.done ? ' done' : '')), check = node('input');
        check.type = 'checkbox'; check.checked = i.done;
        check.dataset.id = i.id;
        check.onchange = () => { check.checked = i.done; send('check-toggle',{id:i.id}); };
        label.append(check,node('span',i.text));
        if (i.note) label.append(node('span',null,'sp'),node('span',i.note,'note m'));
        return label;
      }));
      find('.links').replaceChildren(...links.map(l => {
        const button = node('button',null,'kv');
        button.dataset.id = l.id;
        button.append(node('span',l.text));
        button.insertAdjacentHTML('beforeend',link);
        button.onclick = () => send('link-open',{id:l.id});
        return button;
      }));
      if (!items.length) find('.items').append(node('p','清单为空','hint'));
      if (!links.length) find('.links').append(node('p','还没有固定的工具','hint'));
    }
    let lastSnapshot, lastPlay, lastPlanner;
    return s => {
      const snapKey = JSON.stringify([s.date,s.metrics,s.modules,s.browserNote]);
      const playKey = JSON.stringify([s.enabled,s.error,s.play,s.since]);
      const plannerKey = JSON.stringify([s.items,s.links]);
      if (snapKey !== lastSnapshot) { snapshot(s); lastSnapshot = snapKey; }
      if (playKey !== lastPlay) { play(s); lastPlay = playKey; }
      if (plannerKey !== lastPlanner) {
        const focused = document.activeElement?.dataset.id;
        planner(s.items,s.links);
        confirmItem(s.items); confirmLink(s.links);
        if (focused) page.querySelector('[data-id="'+CSS.escape(focused)+'"]')?.focus();
        lastPlanner = plannerKey;
      }
    };
  }
  registerPage('journal',{mount, unmount() {}});
})();
