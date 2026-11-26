const planner = (() => {
  const dialog = document.createElement('dialog');
  dialog.className = 'pl-dialog';
  dialog.setAttribute('aria-labelledby','pl-title');
  dialog.innerHTML = `<div class="planner">
    <div class="pl-head">
      <h2 class="pl-title" id="pl-title">清单与提醒</h2><p class="pl-sub">每天 <b></b> 重置</p>
      <button class="pl-tool" type="button" aria-pressed="false" data-key="organize">整理</button>
      <button class="pl-close" type="button" aria-label="关闭"><svg viewBox="0 0 12 12" fill="none" stroke="currentColor" stroke-width="1.4"><path d="M1 1l10 10M11 1L1 11"/></svg></button>
    </div>
    <div class="pl-error" role="alert"></div>
    <div class="pl-sections">
      <section class="pl-sec">
        <div class="pl-label"><h3>今日清单</h3><span class="pl-count"></span><span class="pl-bar"><span></span></span></div>
        <ul class="pl-list"><li class="pl-new"><span class="pl-check"></span><span><input aria-label="添加清单项" data-key="check-add" placeholder="添加一项，回车保存"></span></li></ul>
      </section>
      <section class="pl-sec">
        <div class="pl-label"><h3>提醒</h3><span class="pl-spacer"></span><button type="button" class="pl-add" data-key="reminder-add">新提醒</button></div>
        <div class="pl-reminders"></div>
      </section>
      <section class="pl-sec">
        <div class="pl-label"><h3>固定链接</h3></div><div class="pl-links"></div>
      </section>
    </div>
    <div class="pl-sec pl-form" hidden></div>
    <p class="pl-foot">只保存在这台电脑上。链接会用系统浏览器打开。</p>
  </div>`;
  document.body.append(dialog);
  const find = selector => dialog.querySelector(selector);
  const panel = find('.planner'), sections = find('.pl-sections'), editor = find('.pl-form');
  const organize = find('[data-key=organize]'), quickAdd = find('[data-key=check-add]'), alert = find('[role=alert]');
  const groups = {check:'今日清单', reminder:'提醒', link:'固定链接'};
  const paths = {check:'M2 6.4l2.6 2.6L10 3.2',move:'M6 10V2M2.5 5.5L6 2l3.5 3.5',edit:'M2 10l1-3 5.5-5.5 2 2L5 9z',delete:'M2 3h8M4.5 3V1.8h3V3M3 3l.6 7.2h4.8L9 3',link:'M4 2h6v6M10 2L2.5 9.5'};
  let previousKey, opener, pending;
  const data = () => window.panelSample || currentState;
  const node = (tag,text,cls = '') => Object.assign(document.createElement(tag),{textContent:text,className:cls});
  function button(text,click,key,label,cls = 'pl-tool') {
    const b = node('button',text,cls);
    b.type = 'button';
    b.onclick = click;
    b.dataset.key = key;
    if (label) b.setAttribute('aria-label',label);
    return b;
  }
  function icon(kind) {
    const svg = document.createElementNS('http://www.w3.org/2000/svg','svg');
    svg.setAttribute('viewBox','0 0 12 12');
    svg.setAttribute('fill','none');
    svg.setAttribute('stroke','currentColor');
    svg.setAttribute('stroke-width',kind === 'check' ? '1.8' : '1.4');
    svg.setAttribute('aria-hidden','true');
    const path = document.createElementNS(svg.namespaceURI,'path');
    path.setAttribute('d',paths[kind]);
    svg.append(path);
    return svg;
  }
  function focus(key) {
    ([...dialog.querySelectorAll('[data-key]')].find(b => b.dataset.key === key && !b.disabled && b.getClientRects().length) || organize).focus();
  }
  function send(action,fields,after) {
    if (pending || !bridge) return;
    if (action !== 'link-open') pending = {action,after,text:fields.text};
    alert.textContent = '';
    bridge.postMessage({action:'planner',planner:{action,...fields}});
  }
  quickAdd.onkeydown = e => {
    if (e.key === 'Enter' && !e.isComposing && quickAdd.value.trim()) {
      e.preventDefault();
      send('check-add',{text:quickAdd.value.trim()},'check-add');
    }
  };
  function ops(group,item,index) {
    const span = node('span','','pl-ops');
    for (const [action,label,run] of [
      ['move','上移',() => send(group+'-move',{id:item.id,step:-1})],
      ['edit','改名',() => edit(group,item)],['delete','删除',() => remove(group,item)]
    ]) {
      const b = button('',run,item.id+'-'+action,label+'：'+item.text,'pl-op');
      b.title = label;
      b.disabled = action === 'move' && index === 0;
      b.append(icon(action));
      span.append(b);
    }
    return span;
  }
  function refresh(force = false) {
    if (!dialog.open || !editor.hidden) return;
    const s = data(), items = s.checklist?.items || [], reminders = s.reminders || [], links = s.links || [];
    const key = JSON.stringify([items,reminders,links,s.checklistReset,s.nextReminder?.id]);
    if (!force && key === previousKey) return;
    previousKey = key;
    const active = document.activeElement.dataset.key;
    find('.pl-sub b').textContent = (s.checklistReset || '04:00').slice(0,5);
    const completed = items.filter(i => i.done).length;
    find('.pl-count').replaceChildren(String(completed),node('i','/'),String(items.length));
    find('.pl-bar span').style.width = (items.length ? 100*completed/items.length : 0)+'%';
    const list = find('.pl-list'), add = find('.pl-new');
    list.querySelectorAll('.pl-item').forEach(el => el.remove());
    items.forEach((item,i) => {
      const row = node('li','',item.done ? 'pl-item done' : 'pl-item');
      const b = button('',() => send('check-toggle',{id:item.id}),item.id+'-toggle',(item.done ? '取消完成：' : '完成：')+item.text,'pl-check');
      b.setAttribute('aria-pressed',item.done);
      b.append(icon('check'));
      b.id = 'pl-check-'+item.id;
      const label = node('label',item.text,'pl-text');
      label.htmlFor = b.id;
      row.append(b,label,ops('check',item,i));
      list.insertBefore(row,add);
    });
    const rem = find('.pl-reminders');
    rem.replaceChildren();
    reminders.forEach((item,i) => {
      const day = item.repeat === 'daily' ? '每天' : item.repeat === 'weekly' ? '每周'+'日一二三四五六'[item.weekday] : item.date;
      const when = node('span',day+' '+item.time.slice(0,5)+(item.due === null ? ' · 已结束' : ''),'pl-when');
      if (item.due) when.title = new Date(item.due).toLocaleString('zh-CN');
      const row = node('div','','pl-rem');
      row.append(when,node('span',item.text,'pl-text'));
      if (item.id === s.nextReminder?.id) row.append(node('span','下一条','pl-next'));
      row.append(ops('reminder',item,i));
      rem.append(row);
    });
    if (!reminders.length) rem.append(node('p','暂无提醒','pl-empty'));
    const chips = find('.pl-links');
    chips.replaceChildren();
    for (const item of links) {
      const sorting = panel.classList.contains('edit');
      const b = button(item.text,() => sorting ? edit('link',item) : send('link-open',{id:item.id}),item.id+'-edit',sorting ? '编辑链接：'+item.text : item.text,'pl-link');
      b.title = sorting ? '编辑链接' : item.url;
      b.append(icon('link')); chips.append(b);
    }
    chips.append(button('添加链接',() => edit('link'),'link-add',null,'pl-link add'));
    if (active && active !== quickAdd.dataset.key) focus(active);
  }
  function showEditor() {
    sections.hidden = organize.hidden = true;
    editor.hidden = false;
    alert.textContent = '';
  }
  function finish(key) {
    editor.hidden = true;
    sections.hidden = organize.hidden = false;
    editor.replaceChildren();
    refresh(true);
    focus(key);
  }
  function remove(group,item) {
    showEditor();
    const key = item.id+(group === 'link' ? '-edit' : '-delete');
    const cancel = button('取消',() => finish(key),'cancel');
    const actions = node('div','','pl-actions');
    actions.append(cancel,button('确认删除',() => send(group+'-delete',{id:item.id},group+'-add'),'delete'));
    editor.replaceChildren(node('h3','永久删除「'+item.text+'」？'),actions);
    cancel.focus();
  }
  function edit(group,item) {
    showEditor();
    alert.textContent = '';
    const form = document.createElement('form');
    function field(name,title,type,value) {
      const label = node('label',title);
      const input = document.createElement(type === 'select' ? 'select' : 'input');
      if (type !== 'select') input.type = type;
      input.name = name;
      input.required = true;
      input.value = value || '';
      label.append(input); form.append(label);
      return input;
    }
    function options(select, opts, value) {
      opts.forEach(([v,t]) => select.add(new Option(t,v)));
      select.value = value;
    }
    const text = field('text',group === 'check' ? '清单项' : '名称','text',item?.text);
    if (group === 'link') {
      const url = field('url','HTTPS 地址','url',item?.url);
      url.pattern = 'https://.+';
      url.placeholder = 'https://';
    }
    if (group === 'reminder') {
      const repeat = field('repeat','重复','select');
      options(repeat,[['once','仅一次'],['daily','每天'],['weekly','每周']],item?.repeat || 'once');
      const date = field('date','日期','date',item?.date);
      const weekday = field('weekday','星期','select');
      options(weekday,[...'日一二三四五六'].map((day,i) => [i,'星期'+day]),item?.weekday ?? new Date().getDay());
      field('time','本地时间','time',item?.time.slice(0,5));
      repeat.onchange = () => {
        date.parentNode.hidden = date.disabled = repeat.value !== 'once';
        weekday.parentNode.hidden = weekday.disabled = repeat.value !== 'weekly';
      };
      repeat.onchange();
    }
    const key = item ? item.id+'-edit' : group+'-add';
    const save = node('button','保存');
    const actions = node('div','','pl-actions');
    save.className = 'pl-tool';
    actions.append(save,button('取消',() => finish(key),'cancel'));
    if (group === 'link' && item) {
      const links = data().links;
      const index = links.findIndex(link => link.id === item.id);
      for (const [step,label] of [[-1,'上移'],[1,'下移']]) {
        const move = button(label,() => send('link-move',{id:item.id,step},key),label);
        move.disabled = index+step < 0 || index+step >= links.length;
        actions.append(move);
      }
      actions.append(button('删除',() => remove(group,item),'delete'));
    }
    form.append(actions);
    form.onsubmit = e => {
      e.preventDefault();
      const fields = Object.fromEntries(new FormData(form));
      fields.text = fields.text.trim();
      if (!fields.text) { text.focus(); return; }
      if (fields.url) {
        try {
          const url = new URL(fields.url);
          if (url.username || url.password) throw new Error();
        } catch { alert.textContent = '请输入不含用户名或密码的 HTTPS 地址。'; return; }
      }
      if (fields.weekday != null) fields.weekday = Number(fields.weekday);
      if (fields.time) {
        if (fields.repeat === 'once' && new Date(fields.date+'T'+fields.time) <= new Date()) {
          alert.textContent = '请选择未来的提醒时间。'; return;
        }
        fields.time += ':00';
      }
      if (item) fields.id = item.id;
      send(group+(item ? '-edit' : '-add'),fields,key);
    };
    editor.replaceChildren(node('h3',(item ? '编辑' : '添加')+groups[group]),form);
    text.focus();
  }
  function arrange(on) {
    panel.classList.toggle('edit',on);
    organize.setAttribute('aria-pressed',on);
  }
  organize.onclick = () => { arrange(!panel.classList.contains('edit')); refresh(true); };
  find('[data-key=reminder-add]').onclick = () => edit('reminder');
  find('.pl-close').onclick = () => dialog.close();
  function open(group = 'check', sorting = false) {
    if (dialog.open) return;
    opener = document.activeElement;
    arrange(sorting);
    alert.textContent = quickAdd.value = '';
    editor.hidden = true;
    sections.hidden = organize.hidden = false;
    dialog.showModal();
    refresh(true);
    if (window.panelSample) dialog.focus();
    else focus(group+'-add');
  }
  dialog.onclose = () => { if (opener === document.body) win.querySelector('button:not(:disabled)')?.focus(); else if (opener?.isConnected) opener.focus(); };
  dialog.addEventListener('keydown',e => e.stopPropagation());
  document.querySelectorAll('[data-planner]').forEach(b => b.onclick = () => open(b.dataset.planner));
  function result(message) {
    const saved = pending;
    pending = null;
    if (message) alert.textContent = message;
    else if (saved?.after && dialog.open) {
      if (saved.action === 'check-add' && quickAdd.value.trim() === saved.text) quickAdd.value = '';
      finish(saved.after);
    }
  }
  return {open,refresh,result,dialog};
})();
