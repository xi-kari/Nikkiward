function createCommandPanel(trigger, commands) {
  const dialog = document.createElement('dialog');
  dialog.id = 'palette';
  dialog.setAttribute('aria-label', '命令面板');
  dialog.innerHTML = '<div class="pin"><svg class="i"><use href="#i-search"/></svg><input role="combobox" aria-label="命令" aria-controls="plist" aria-expanded="true" autocomplete="off" placeholder="去哪里，或做什么"><span class="kbd">Esc</span></div><div id="plist" role="listbox" aria-label="命令"></div><footer>↑ ↓ 选择　 Enter 执行　 Esc 关闭</footer>';
  document.body.append(dialog);
  const input = dialog.querySelector('input'), list = dialog.querySelector('#plist');
  let selected = 0;
  function render() {
    const query = input.value.trim().toLowerCase();
    const items = commands().filter(([group,label]) => (group+label).toLowerCase().includes(query));
    list.replaceChildren();
    input.removeAttribute('aria-activedescendant');
    selected = Math.min(selected, Math.max(0, items.length-1));
    let previous;
    items.forEach(([group,label,run], i) => {
      if (group !== previous) {
        const heading = document.createElement('h4');
        heading.textContent = group;
        list.append(heading);
      }
      previous = group;
      const button = document.createElement('button');
      button.textContent = label;
      button.id = 'command-' + i;
      button.setAttribute('role','option');
      button.tabIndex = -1;
      button.setAttribute('aria-selected',String(i === selected));
      button.onmousedown = e => e.preventDefault();
      button.onclick = () => { dialog.close(); run(); };
      list.append(button);
      if (i === selected) {
        button.className = 'sel';
        input.setAttribute('aria-activedescendant',button.id);
      }
    });
    list.querySelector('.sel')?.scrollIntoView({block:'nearest'});
    if (!items.length) {
      const empty = document.createElement('h4');
      empty.textContent = '没有匹配的命令';
      list.append(empty);
    }
  }
  function open() {
    selected = 0;
    input.value = '';
    dialog.showModal();
    render();
    input.focus();
  }
  trigger.onclick = open;
  input.oninput = () => { selected = 0; render(); };
  dialog.onclick = e => {
    if (e.target !== dialog) return;
    const box = dialog.getBoundingClientRect();
    if (e.clientX < box.left || e.clientX > box.right || e.clientY < box.top || e.clientY > box.bottom) dialog.close();
  };
  document.addEventListener('keydown', e => {
    if (e.isComposing) return;
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
      e.preventDefault();
      if (!e.repeat) dialog.open ? dialog.close() : open();
    } else if (dialog.open && ['ArrowDown','ArrowUp','Enter'].includes(e.key)) {
      e.preventDefault();
      if (e.key === 'Enter') { if (!e.repeat) list.querySelector('.sel')?.click(); }
      else { selected = Math.max(0, selected + (e.key === 'ArrowDown' ? 1 : -1)); render(); }
    }
  });
  return dialog;
}
