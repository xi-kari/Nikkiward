(() => {
  let view;
  function node(tag, value, cls) {
    const el = document.createElement(tag);
    if (value != null) el.textContent = value;
    if (cls) el.className = cls;
    return el;
  }
  function table(labels, rightFrom) {
    const el = node('table',null,'t'), header = el.createTHead().insertRow();
    labels.forEach((label,i) => header.append(node('th',label,i >= rightFrom ? 'r' : '')));
    el.createTBody();
    return el;
  }
  function detail(pool) {
    const box = node('div',null,'records'), header = node('header');
    const records = pool.records;
    header.append(node('b','逐条记录'));
    const select = node('select');
    select.setAttribute('aria-label','按月份定位');
    select.append(new Option('按月份定位',''));
    header.append(select);
    const list = table(['时间','名称','星级','抽数','距上次五星'],3);
    let previous;
    records.forEach(r => {
      const row = list.tBodies[0].insertRow();
      if (r.month !== previous) {
        previous = r.month;
        row.id = 'wish-month-' + r.month;
        select.append(new Option(r.month,row.id));
      }
      [r.time,r.name,r.rarity,r.pull,r.distance].forEach((value,i) =>
        row.append(node('td',value ?? '—',i >= 3 ? 'r m' : i === 0 ? 'm' : '')));
    });
    select.onchange = () => document.getElementById(select.value)?.scrollIntoView({block:'start'});
    box.append(header,list);
    return box;
  }
  function mount({root,send,commands}) {
    const section = node('section');
    section.id = 'wish';
    section.innerHTML = '<header class="ph"><h1>心愿共鸣记录</h1><div class="meta"></div><div class="sp"></div><button class="btn" data-action="export">导出</button><button class="btn solid"><svg class="i"><use href="#i-export"/></svg>导入 JSON</button></header><div class="cols"><section class="mod"><h2>合计</h2><div class="stats"></div><p class="hint import-hint"></p></section><section class="mod pools"><h2>按卡池</h2><div class="history"></div><div class="wardrobe"></div></section></div>';
    root.append(section);
    section.querySelector('[data-action="export"]').onclick = () => send('export');
    section.querySelector('.solid').onclick = () => send('import');
    commands.push(...[['导入 JSON','import'],['导出全部记录为 JSON','export'],['去官方手账同步','sync']]
      .map(([label,action]) => ['心愿共鸣',label,() => send(action)]));
    let expanded = -1, state;
    function history() {
      const area = section.querySelector('.history');
      area.replaceChildren();
      if (!state.pools.length) {
        const empty = node('div',null,'empty');
        empty.append(node('p','尚无逐条共鸣记录'));
        const sync = node('button','去官方手账同步','btn');
        sync.onclick = () => send('sync');
        empty.append(sync);
        area.append(empty);
        return;
      }
      const list = table(['卡池','时间','次数','五星','距上次五星'],2);
      state.pools.forEach((pool,i) => {
        const row = list.tBodies[0].insertRow();
        row.className = 'pool';
        const b = node('button',pool.name);
        b.setAttribute('aria-expanded',String(expanded === i));
        row.insertCell().append(b);
        [pool.period,pool.count,pool.five,pool.distance].forEach((v,j) => row.append(node('td',v ?? '—',j ? 'r m' : 'm')));
        row.onclick = () => {
          expanded = expanded === i ? -1 : i;
          history();
          area.querySelectorAll('.pool button')[i].focus({preventScroll:true});
        };
        if (expanded === i) {
          const cell = list.tBodies[0].insertRow().insertCell();
          cell.colSpan = 5;
          cell.append(detail(pool));
        }
      });
      area.append(list,node('p','卡池名称以导入文件里的为准；文件没写名称时按顺序编号。','hint'));
    }
    view = section;
    return next => {
      state = next;
      section.querySelector('.import-hint').textContent = next.importHint ?? '可导入 Nikkiward 导出的 JSON 文件。记录在本机按卡池合并，同一条记录只保留一次。';
      if (next.expanded != null) expanded = next.expanded;
      const meta = section.querySelector('.meta');
      meta.replaceChildren('离线导入 · '+(next.date ? (next.dateLabel ?? '最近更新')+' ' : '尚无记录'),
        node('span',next.date,'m'),' · 不碰账号');
      const stats = section.querySelector('.stats');
      stats.replaceChildren();
      for (const [label,value,unit] of [['总共鸣次数',next.total,'次'],['五星部件',next.five,'件'],['平均出五星',next.average,'次'],
        ...(next.guarantee == null ? [] : [['距保底',next.guarantee,'次']])]) {
        const row = node('div',null,'kv'), number = node('span');
        number.append(node('span',value ?? '—','m big'),node('small',unit));
        row.append(node('span',label),number);
        stats.append(row);
      }
      history();
      const wardrobe = section.querySelector('.wardrobe');
      wardrobe.replaceChildren();
      for (const banner of next.banners ?? []) {
        const item = node('details');
        item.append(node('summary',banner.name || '共鸣衣橱'));
        if (banner.cover) {
          const cover = node('img',null,'cover');
          cover.loading = 'lazy';
          cover.alt = '';
          cover.src = banner.cover;
          item.append(cover);
        }
        item.append(node('p',[banner.patch,banner.outfit,banner.rarity >= 1 && banner.rarity <= 5 && '★'.repeat(banner.rarity),banner.completion,banner.remaining].filter(Boolean).join(' · ')));
        item.append(node('p','总计共鸣 '+(banner.total || '—')+' · 平均共鸣 '+(banner.average || '—')));
        const slots = table(['服装部件','已拥有'],1);
        banner.slots.forEach(slot => {
          const row = slots.tBodies[0].insertRow();
          const label = node('td',slot.name);
          if (slot.image) {
            const icon = node('img',null,'slot');
            icon.loading = 'lazy';
            icon.alt = '';
            icon.src = slot.image;
            label.prepend(icon);
          }
          row.append(label,node('td',slot.count,'r m'));
        });
        item.append(slots);
        wardrobe.append(item);
      }
    };
  }
  registerPage('wish',{mount, unmount() { view?.remove(); view = null; }});
})();

