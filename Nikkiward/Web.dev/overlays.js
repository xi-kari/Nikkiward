'use strict';
function prepareOverlayCase(cases) {
  const name = new URLSearchParams(location.search).get('mock');
  if (!Object.hasOwn(cases,name)) return;
  async function showCase(state) {
    const real = name.endsWith('-real');
    const blocked = state.visual==='blocked';
    const signature = blocked?'签名与官方发布的不一致，已拒绝启动':'签名已核对';
    const path = real ? 'F:\\游戏安装目录\\长期保留的历史版本与多渠道安装档案\\InfinityNikkiGlobal\\InfinityNikki\\InfinityNikki Game' : 'F:\\InfinityNikki';
    state.overlay = {
      gameRootPath:path,channelText:'国服 · 官方 / Windows',selectedChannelLaunchRoute:'经官方启动器',launcherVersion:'1.6.2',signature,
      canOpenRoot:true,canDiscover:true,canRefresh:true,canExport:true,
      checks:{
        directory:{text:path,passed:true}, channel:{text:'国服 · 官方，与档案一致',passed:true},
        signature:{text:blocked?signature:'1.6.2 · 签名已核对',passed:!blocked},
        integrity:{text:'必要文件齐全',passed:true},processes:{text:'没有上次留下的游戏进程',passed:true},
        gate:{text:'管理员确认会在每次启动时弹出',passed:true}
      }
    };
    if (real) {
      state.overlay.checks.gate = {text:'执行门关闭',passed:false};
      state.officialFlow = false;
      Object.assign(state.overlay,{channelText:'国服 · 官方 / Windows',selectedChannelLaunchRoute:'官方国服 xstarter',
        providerContractId:'CN · 官方国服',staticIdentityText:state.detail,launchCapabilityText:state.launchLabel,
        providerExecutionEnabledText:'执行门关闭',providerEvidenceState:'签名未通过',lastErrorText:state.detail,
        activityText:'只读检查完成',providerExecutablePath:path+'\\launcher\\xstarter.exe'});
      state.components=[{componentId:'official-launcher',displayName:'官方启动器',statusText:'签名未通过',fileVersionText:'1.6.2',filePath:path+'\\launcher.exe'}];
    }
    await show(state);
    homeOverlays.open(name.startsWith('diag')?'status':'settings');
  }
  return {homeCase:cases[name],show:showCase};
}
