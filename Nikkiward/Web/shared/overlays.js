'use strict';
const homeOverlays = (() => {
  const dialog = document.createElement('dialog');
  dialog.className = 'ho-dialog';
  win.append(dialog);
  const cinema = document.documentElement.dataset.skin === 'cinema';
  let suspended = [];
  const paths = {
    info:'M12 9v.2M12 12v4M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0', close:'M6 6l12 12M18 6L6 18', folder:'M3.5 7.5v10a1.5 1.5 0 0 0 1.5 1.5h14a1.5 1.5 0 0 0 1.5-1.5v-8A1.5 1.5 0 0 0 19 8h-7L10 5.5H5a1.5 1.5 0 0 0-1.5 1.5z',
    link:'M10 5H5.5v13.5H19V14M13.5 4.5h6v6M19.5 4.5L11 13', refresh:'M19.5 12a7.5 7.5 0 1 1-2.2-5.3M19.5 4v4h-4',
    export:'M12 15V4M8 8l4-4 4 4M4.5 14v5.5h15V14', check:'M5 12.5l4.5 4.5L19 7.5',
    shield:'M12 3l7.5 2.8v5.7c0 4.5-3 7.8-7.5 9.5-4.5-1.7-7.5-5-7.5-9.5V5.8zM12 8.5v4.5M12 16v.2'
  };
  const icon = k => '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="'+paths[k]+'"/></svg>';
  const escape = s => String(s ?? '').replace(/[&<>"']/g,c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const numbers = s => String(s ?? '').split(/([0-9]+(?:[:.][0-9]+)*)/).map((part,index) =>
    index % 2 ? '<span class="ho-num">'+part+'</span>' : escape(part)).join('');
  const kv = (k,v) => '<div class="ho-kv"><span>'+escape(k)+'</span><span>'+numbers(v)+'</span></div>';
  const btn = (action,label,image,enabled=true) => '<button data-do="'+action+'" '+(enabled?'':'disabled')+' aria-label="'+label+'">'+(image?icon(image):'')+(action==='open-root' || action==='dismiss'?'':label)+'</button>';
  const sections = {base:'基本信息',args:'启动参数',bg:'自定义背景',pkg:'游戏包体'};
  let mode, section, opener, previous, editKey;
  const find = selector => dialog.querySelector(selector);

  function technical(o) {
    if (!o) return '';
    const details = [['渠道',o.channelText],['启动方式',o.selectedChannelLaunchRoute],['官方启动器签名',o.signature],['定位结果',o.manualDiscoveryStatusText],
      ['启动检查',o.launchReason],['应用执行门',o.executionGate],['检查结果',o.failureCode],['检查说明',o.failureDetail],['启动能力',o.launchCapabilityText],['能力详情',o.capabilityDetailText],['静态身份',o.staticIdentityText],
      ['就绪检查',o.staticReadinessText],['执行门',o.providerExecutionEnabledText],['最近刷新',o.lastUpdatedText],
      ['当前活动',o.activityText],['最近错误',o.lastErrorText],['首页状态',o.homeFallback],['安装候选',o.installationCandidateText],['冻结契约',o.providerContractId],['手动执行授权',o.manualAttemptAuthorizationText],
      ['可执行文件',o.providerExecutablePath],['工作目录',o.providerWorkingDirectory],['参数向量',o.providerArgumentVector],['证据状态',o.providerEvidenceState],
      ['观测',o.providerObservationText],['外部证据',o.externalEvidenceText],['最近受控回执',o.latestControlledReceiptText],['应用执行',o.applicationExecutionText],
      ['生命周期',o.lifecycleText],['预检门',o.providerPreflightText],['观测边界',o.providerObservationPolicyText]];
    return '<details data-section="'+section+'"><summary>技术详情</summary>'+details.filter(([,v])=>v).map(([k,v])=>kv(k,v)).join('')+'</details>';
  }

  function settings(s,o) {
    const heading = title => '<h3>'+title+'</h3>';
    const values = rows => rows.map(([label,value]) => kv(label,value)).join('');
    if (section==='base') return heading('安装路径')+
      '<div class="ho-path"><div class="ho-num">'+escape(o.gameRootPath)+'</div>'+btn('locate','定位游戏','folder',o.canDiscover)+btn('open-root','在资源管理器中打开','link',o.canOpenRoot)+'</div>'+
      '<p class="ho-hint">自动发现在启动时运行；装在别处就手动指定目录。</p><div class="ho-grid">'+values([
        ['渠道 / 平台',o.channelText],['启动方式',o.selectedChannelLaunchRoute],['官方启动器',o.launcherVersion+' · '+o.signature],['最后检查',s.checkTime]
      ])+'</div><div class="ho-note"><b>只读核对</b>这里只展示安装和官方组件的状态。Nikkiward 不改游戏文件，也不下载或更新游戏。</div>'+
      (o.conclusion ? '<p class="ho-hint">'+escape(o.conclusion)+'</p>' : '')+technical(o.technical);
    if (section==='args') return heading('启动参数')+'<p class="ho-hint">'+escape(o.argumentsIntro)+'</p>'+values([
      ['启动程序',o.executable],['工作目录',o.workingDirectory],['参数',o.arguments]])+technical(o.technical);
    if (section==='pkg') return heading('游戏包体')+kv('游戏根目录',o.gameRootPath)+(s.components?.length ?
      s.components.map(c=>'<div class="ho-component"><div><b>'+escape(c.displayName+' · '+c.fileVersionText)+'</b><span>'+
        escape(c.statusText)+'</span></div><p class="ho-hint">'+escape(c.filePath)+'</p></div>').join('') : '<p class="ho-hint">暂无组件检查结果。</p>');
    return heading('自定义背景')+'<div class="ho-art"><img class="ho-preview" alt="当前背景" src="'+escape(s.background.poster || s.background.url)+'"><b>'+escape(s.artName)+'</b></div>'+
      '<div class="ho-actions">'+btn('import-background','导入背景…')+btn('reset','恢复默认')+'</div>'+
      '<label>背景标题 <input data-field="title" maxlength="120" placeholder="使用原文件名" '+(o.canNameArtwork?'':'disabled')+'></label><div class="ho-actions">'+btn('art-title','保存标题',null,o.canNameArtwork)+btn('clear-title','重置标题',null,o.canNameArtwork)+'</div>';
  }
  function diagnostics(s,o) {
    const checks = o.checks;
    const rows = [['安装目录',checks.directory],['渠道身份',checks.channel],['官方启动器签名',checks.signature],
      ['安装完整性',checks.integrity],['残留进程',checks.processes],['启动条件',checks.gate]];
    return '<h2>启动检查</h2><p class="ho-meta">'+escape(s.profile || '未选择档案')+' · 最后检查 '+numbers(s.checkTime)+'</p><div class="ho-scroll">'+
      (s.officialFlow!=null ? '<div class="ho-note"><b>'+escape(s.launchLabel)+'</b>'+escape(o.conclusion)+'</div>' : '')+
      rows.map(([label,check]) => {
        const mark = check.passed===true ? icon('check') : check.passed===false ? icon('shield') : icon('info');
        return '<div class="ho-row" data-result="'+(check.passed===false?'failed':check.passed===true?'passed':'info')+'">'+
          mark+'<div><b>'+label+'</b><p>'+escape(check.text)+'</p></div></div>';
      }).join('')+
      technical(o.technical)+
      (s.officialFlow?btn('official-flow','启动游戏'):'')+'</div><footer>'+btn('recheck','重新检查','refresh',o.canRefresh)+btn('export-diagnostics','导出脱敏诊断','export',o.canExport)+btn('close-dialog','关闭')+'</footer><p class="ho-footnote">导出的诊断会抹掉用户名、路径里的个人部分和设备标识。</p>';
  }

  function refresh() {
    if (!dialog.open) return;
    const s = currentState, o = s.overlay;
    if (!o) return;
    const key = JSON.stringify([section,s.profile,s.state,s.officialFlow,s.detail,s.launchLabel,s.checkTime,s.background,s.artName,s.components,o]);
    if (key===previous) return;
    previous = key;
    const active = document.activeElement;
    const focus = active?.dataset;
    const onSummary = active?.tagName==='SUMMARY';
    const scroll = find('.ho-scroll')?.scrollTop ?? 0;
    const detailsOpen = find('details')?.dataset.section===section && find('details')?.open;
    const input = find('[data-field=title]');
    const draft = input && editKey===o.homeArtKey && input.value!==input.defaultValue ? input.value : null;
    const selection = active===input && input ? [input.selectionStart,input.selectionEnd] : null;
    dialog.innerHTML = mode==='status' ? '<section class="ho-diag">'+diagnostics(s,o)+'</section>' :
      '<section class="ho-config"><header><h2>游戏设置</h2><p>'+escape(s.profile || '未选择档案')+'</p>'+btn('dismiss','关闭','close')+'</header><nav aria-label="游戏设置分区">'+
      Object.entries(sections).map(([k,v])=>'<button data-section="'+k+'" aria-pressed="'+(section===k)+'">'+v+'</button>').join('')+
      '</nav><div class="ho-scroll">'+settings(s,o)+'</div></section>';
    editKey = o.homeArtKey;
    if (find('input')) {
      find('input').defaultValue=o.artworkTitle ?? '';
      find('input').value=draft ?? find('input').defaultValue;
    }
    if (detailsOpen && find('details')) find('details').open=true;
    find('.ho-scroll').scrollTop=scroll;
    if (focus) for (const attr of ['do','section','field']) if (focus[attr]) find('[data-'+attr+'="'+focus[attr]+'"]')?.focus({preventScroll:true});
    if (onSummary) find('summary')?.focus({preventScroll:true});
    if (selection && document.activeElement===find('input')) find('input').setSelectionRange(...selection);
  }

  function open(name, initialSection='base') {
    if (!currentState?.overlay) return;
    opener=document.activeElement;
    mode=name; section=name==='background'?'bg':initialSection; previous=null;
    dialog.setAttribute('aria-label',name==='status'?'启动检查':'游戏设置');
    dialog.replaceChildren();
    if (!dialog.open) {
      if (cinema) {
        dialog.setAttribute('aria-modal','true');
        dialog.show();
        suspended = Array.from(win.children).filter(el => el !== dialog && !el.inert);
        suspended.forEach(el => { el.inert = true; });
      } else dialog.showModal();
    }
    refresh();
    dialog.focus();
  }
  dialog.tabIndex=-1;
  dialog.onclose=()=>{
    suspended.forEach(el => { el.inert = false; });
    suspended = [];
    dialog.replaceChildren();
    (opener?.isConnected && opener!==document.body ? opener : win.querySelector('button:not(:disabled)'))?.focus();
  };
  dialog.onkeydown=e=>{
    e.stopPropagation();
    if (!cinema || e.isComposing) return;
    if (e.key==='Escape') { e.preventDefault(); dialog.close(); }
    if (e.key!=='Tab') return;
    const controls = Array.from(dialog.querySelectorAll('button:enabled,input:enabled,select:enabled,summary'))
      .filter(el => el.getClientRects().length);
    const first = controls[0], last = controls.at(-1);
    if (e.shiftKey && [first,dialog].includes(document.activeElement)) { e.preventDefault(); last?.focus(); }
    else if (!e.shiftKey && document.activeElement===last) { e.preventDefault(); first?.focus(); }
  };
  dialog.onclick=e=>{
    if (e.target===dialog) return dialog.close();
    const b=e.target.closest('button');
    if (!b || b.disabled) return;
    if (b.dataset.section) { section=b.dataset.section; refresh(); find('.ho-scroll').scrollTop=0; return; }
    let action=b.dataset.do;
    if (action==='dismiss'||action==='close-dialog') return dialog.close();
    if (action==='art-title'||action==='clear-title') {
      const input=find('input');
      if (action==='clear-title') input.value='';
      bridge?.postMessage({action:'art-title',artKey:editKey,title:input.value});
    } else bridge?.postMessage({action});
  };
  return {open,refresh,dialog};
})();
