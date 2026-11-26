const planner = (() => {
  const dialog = document.createElement('dialog');
  dialog.className = 'planner';
  dialog.setAttribute('aria-labelledby','planner-title');
  dialog.innerHTML = '<header><h2 id="planner-title">清单与提醒</h2><button type="button" aria-label="关闭清单与提醒">关闭</button></header><p role="alert"></p><main></main>';
  win.append(dialog);
  const main = dialog.querySelector('main'), alert = dialog.querySelector('[role="alert"]');
  const groups = {check:'今日清单', reminder:'提醒', link:'固定链接'};
  let previousKey, editing, opener, pendingFocus;
  const node = (tag, text) => Object.assign(document.createElement(tag), {textContent:text});
  function button(text, click, key, label) {
    const b = node('button', text);
    b.type = 'button';
    b.onclick = click;
    b.dataset.key = key;
    if (label) b.setAttribute('aria-label',label);
    return b;
  }
  function send(action, fields, after) {
    if (pendingFocus) return;
    pendingFocus = after;
    alert.textContent = '';
    bridge?.postMessage({action:'planner',planner:{action,...fields}});
  }
  function rows() {
    const s = currentState;
    return {check:s.checklist?.items || [], reminder:s.reminders || [], link:s.links || []};
  }
  function schedule(r) {
    const day = r.repeat === 'daily' ? '每天' : r.repeat === 'weekly' ? '每周'+'日一二三四五六'[r.weekday] : r.date;
    return day+' '+r.time.slice(0,5);
  }
  function focus(key) {
    ([...dialog.querySelectorAll('[data-key]')].find(b => b.dataset.key === key && !b.disabled) || dialog.querySelector('header button')).focus();
  }
  function refresh(force = false) {
    if (!dialog.open || editing) return;
    const data = rows(), key = JSON.stringify(data);
    if (!force && previousKey === key) return;
    const active = document.activeElement.dataset.key;
    previousKey = key;
    main.replaceChildren();
    for (const [group,name] of Object.entries(groups)) {
      const section = node('section',''), header = node('header','');
      header.append(node('h3',name),button('添加',() => edit(group),group+'-add','添加'+name));
      section.append(header);
      data[group].forEach((item,i,list) => {
        const row = node('div','');
        row.className = 'planner-row';
        const label = node('div','');
        if (group === 'check') {
          const check = node('label','');
          const input = document.createElement('input');
          input.type = 'checkbox';
          input.checked = item.done;
          input.dataset.key = item.id+'-toggle';
          input.onchange = () => send('check-toggle',{id:item.id});
          check.append(input, node('span',item.text));
          label.append(check);
        } else if (group === 'link') {
          const link = button(item.text,() => send('link-open',{id:item.id}),item.id+'-open');
          link.title = item.url;
          label.append(link);
        } else {
          const when = node('small',schedule(item)+(item.due === null ? ' · 已结束' : ''));
          if (item.due) when.title = '下次提醒 '+new Date(item.due).toLocaleString('zh-CN');
          label.append(node('strong',item.text),when);
        }
        row.append(label);
        if (group !== 'reminder') {
          for (const [step,arrow,desc] of [[-1,'↑','上移'],[1,'↓','下移']]) {
            const move = button(arrow,() => send(group+'-move',{id:item.id,step}),item.id+desc,desc+item.text);
            move.disabled = i+step < 0 || i+step >= list.length;
            row.append(move);
          }
        }
        row.append(button('编辑',() => edit(group,item),item.id+'-edit','编辑'+item.text),
          button('删除',() => remove(group,item),item.id+'-delete','删除'+item.text));
        section.append(row);
      });
      main.append(section);
    }
    if (active) focus(active);
  }
  function finish(key) { editing = false; refresh(true); focus(key); }
  function remove(group,item) {
    editing = true;
    const cancel = button('取消',() => finish(item.id+'-delete'),'cancel');
    main.replaceChildren(node('h3','永久删除「'+item.text+'」？'),cancel,
      button('确认删除',() => { send(group+'-delete',{id:item.id},group+'-add'); },'delete'));
    cancel.focus();
  }
  function edit(group,item) {
    editing = true;
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
    form.append(save,button('取消',() => finish(key),'cancel'));
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
    main.replaceChildren(node('h3',(item ? '编辑' : '添加')+groups[group]),form);
    text.focus();
  }
  function open(group = 'check') {
    if (dialog.open) return;
    opener = document.activeElement;
    editing = false;
    alert.textContent = '';
    dialog.showModal();
    refresh(true);
    focus(group+'-add');
  }
  dialog.querySelector('header button').onclick = () => dialog.close();
  dialog.onclose = () => { pendingFocus = null; if (opener === document.body) win.querySelector('button:not(:disabled)')?.focus(); else if (opener?.isConnected) opener.focus(); };
  dialog.addEventListener('keydown',e => e.stopPropagation());
  document.querySelectorAll('[data-planner]').forEach(b => b.onclick = () => open(b.dataset.planner));
  function result(message) {
    const key = pendingFocus;
    pendingFocus = null;
    if (message) alert.textContent = message;
    else if (key && dialog.open) finish(key);
  }
  return {open, refresh, result, dialog};
})();
