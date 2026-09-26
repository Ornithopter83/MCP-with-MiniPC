(async () => {
  const HOST_ID = 'gptweb-hub-extension-preview';
  const EXTENSION_VERSION = '0.3.1';
  const EXTENSION_BUILD = '2026-09-26.10';
  const launchUrl = new URL(location.href);
  const launchRoleRaw = String(launchUrl.searchParams.get('projecthub-managed-role')||'').trim().toUpperCase();
  const launchRuntimeToken = String(launchUrl.searchParams.get('projecthub-runtime-token')||'').trim();
  let persistedManagedRole='', persistedRuntimeToken='';
  if(globalThis.chrome?.storage?.local){
    const persisted=await chrome.storage.local.get({managedRole:'',managedRuntimeToken:''});
    persistedManagedRole=String(persisted.managedRole||'').trim().toUpperCase();
    persistedRuntimeToken=String(persisted.managedRuntimeToken||'').trim();
  }
  const preflightManagedRole=(launchRoleRaw==='HQ'||launchRoleRaw==='RESOURCE')?launchRoleRaw:((persistedManagedRole==='HQ'||persistedManagedRole==='RESOURCE')?persistedManagedRole:'');
  const preflightRuntimeToken=launchRuntimeToken||persistedRuntimeToken;
  if(!preflightManagedRole||!preflightRuntimeToken)return;
  if (document.getElementById(HOST_ID)) return;
  const host = document.createElement('div');
  host.id = HOST_ID;
  host.style.all = 'initial';
  host.style.display = 'none';
  document.documentElement.appendChild(host);
  const root = host.attachShadow({mode:'open'});
  const icon = (path) => '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="'+path+'"/></svg>';
  const icons = {
    gear:'M19.43 12.98c.04-.32.07-.65-.07-.98l2.11-1.65-2-3.46-2.49 1a7.16 7.16 0 0 0-1.69-.98L15 3h-4l-.36 2.93c-.6.25-1.17.58-1.69.98l-2.49-1-2 3.46 2.11 1.65c-.04.32-.08.65-.08.98s.03.66.08.98l-2.11 1.65 2 3.46 2.49-1c.52.4 1.09.73 1.69.98L11 21h4l.36-2.93c.6-.25 1.17-.58 1.69-.98l2.49 1 2-3.46-2.11-1.65ZM13 15.5A3.5 3.5 0 1 1 13 8a3.5 3.5 0 0 1 0 7.5Z',
    close:'M18.3 5.71 12 12l6.3 6.29-1.41 1.42L10.59 13.4 4.3 19.71 2.89 18.3 9.17 12 2.89 5.7 4.3 4.29l6.29 6.3 6.3-6.3 1.41 1.42Z',
    idle:'M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20Z',
    info:'M11 17h2v-6h-2v6Zm1-15a10 10 0 1 0 0 20 10 10 0 0 0 0-20Zm0 18a8 8 0 1 1 0-16 8 8 0 0 1 0 16Zm-1-11h2V7h-2v2Z'
  };
  root.innerHTML = '<style></style><section class="hub-panel" aria-label="GPTWeb-Hub">'+
    '<header class="hub-header"><div class="title-row"><h1>GPTWeb-Hub</h1><div class="header-actions"><button class="header-update reload-extension" type="button">업데이트</button><button class="icon-button settings" aria-label="Settings">'+icon(icons.gear)+'</button><button class="icon-button close" aria-label="Hide">'+icon(icons.close)+'</button></div></div><p class="system-line"><span class="system-icon">i</span><span class="system-text">연결 확인 중...</span></p></header>'+
    '<div class="status-list">'+statusRow('web','GPT Web','—','')+statusRow('worker','Worker','—','')+statusRow('status','Status','Disconnected','')+'</div>'+
    '<div class="role-bind"><span class="role-binding">역할 미연결</span><button class="status-action bind-hq" type="button">HQ 연결</button><button class="status-action bind-resource" type="button">RESOURCE 연결</button></div>'+
    '<main class="task-section"><div class="section-label">TASK</div><article class="task-card" data-state="IDLE"><div class="task-main"><div class="task-icon">'+icon(icons.idle)+'</div><div class="task-copy"><h2 class="task-title">작업 없음</h2><p class="task-detail">현재 처리할 요청이 없습니다.</p></div><div class="spinner"></div></div><div class="task-meta"><span>Task</span><strong class="task-id">—</strong><span>|</span><span>From</span><strong class="task-from">—</strong></div><div class="message-block worker-message-block"><h3>Worker Message</h3><div class="worker-message">대기 중</div></div><div class="message-block response-block hidden"><h3>Web Response</h3><div class="web-response">응답을 기다리고 있습니다.</div></div><button class="secondary-button resource-rescan hidden" type="button">현재 결과 다시 수집</button></article></main>'+
    '<footer class="hub-footer">GPTWeb-Hub <span>v0.3.1</span></footer></section>'+
    '<section class="settings-modal hidden"><div class="settings-dialog"><div class="settings-title-row"><h2>GPTWeb-Hub Settings</h2><button class="settings-close">'+icon(icons.close)+'</button></div><form class="settings-form"><label>Host<input name="bridgeHost"></label><label>Port<input name="bridgePort" type="number"></label><label>Base Path<input name="bridgeBasePath"></label><div class="settings-test-row"><button class="secondary-button test-connection" type="button">Test Connection</button><span class="test-status">Not tested</span></div><div class="settings-actions"><button class="secondary-button settings-cancel" type="button">Cancel</button><button class="primary-button" type="submit">Save</button></div></form></div></section>'+
        '<button class="reopen" aria-label="Open GPTWeb-Hub">'+icon(icons.info)+'</button>';
  function statusRow(kind,label,value,action) { return '<div class="status-row" data-kind="'+kind+'"><strong>'+label+'</strong><span class="status-value status-pending">'+value+'</span>'+(action?'<button class="status-action web-connect" type="button">연결</button>':'')+'</div>'; }
  const style = root.querySelector('style');
  style.textContent = ':host{all:initial;font-family:Inter,system-ui,sans-serif;color:#101828}*{box-sizing:border-box}.hub-panel{position:fixed;top:88px;right:24px;width:428px;max-height:calc(100vh - 106px);overflow:hidden;background:#fff;border:9px solid #26323a;border-radius:19px;box-shadow:0 16px 40px #0f172a38;z-index:2147483646}.hub-panel.hidden{display:none}.hub-header{padding:20px 21px 14px}.title-row{display:flex;justify-content:space-between;align-items:center}h1,h2,h3,p{margin:0}h1{font-size:26px;font-weight:800}.header-actions{display:flex;gap:10px;align-items:center}.header-update{border:1px solid #cbd7e3;border-radius:7px;padding:5px 9px;color:#24364d;background:#fff;font:inherit;font-size:12px;font-weight:700;cursor:pointer}.header-update:hover{background:#f3f7fb}.icon-button{border:0;background:transparent;color:#637183;width:23px;height:23px;padding:0;cursor:pointer}.icon-button svg{width:100%;height:100%;fill:currentColor}.system-line{display:flex;gap:9px;align-items:center;margin-top:14px;color:#617084;font-size:15px}.system-icon{display:grid;place-items:center;width:20px;height:20px;border-radius:50%;background:#3288e8;color:#fff;font-weight:800}.status-list{border-top:1px solid #d8e0e7;padding:5px 21px 7px}.status-row{min-height:48px;display:grid;grid-template-columns:75px 1fr auto;align-items:center;gap:7px;border-bottom:1px solid #d8e0e7;font-size:16px}.status-row:last-child{border:0}.status-row[data-kind=web]{min-height:60px}.status-row strong{font-weight:800}.role-bind{display:flex;gap:7px;align-items:center;padding:8px 21px 10px;border-top:1px solid #d8e0e7}.role-binding{margin-right:auto;color:#617084;font-size:12px;font-weight:700}.status-value{white-space:pre-line;line-height:1.2;overflow:hidden;text-overflow:ellipsis}.status-ok{color:#078443;font-weight:700}.status-offline{color:#c53b3b;font-weight:700}.status-pending{color:#111827;font-weight:700}.status-action{border:1px solid #d5a34b;border-radius:6px;padding:4px 8px;color:#9a5b00;background:#fff8e8;font:inherit;font-size:12px;font-weight:700}.task-section{border-top:1px solid #d8e0e7;padding:19px 21px 20px}.section-label{color:#617084;font-size:14px;font-weight:800;margin-bottom:14px}.task-card{min-height:390px;border-radius:12px;padding:20px 18px 15px;background:#edf1f5}.task-card[data-tone=green]{background:#e6f7ee}.task-card[data-tone=blue]{background:#e5f1fe}.task-card[data-tone=amber]{background:#fff6d9}.task-main{display:flex;align-items:center;gap:15px;min-height:74px}.task-icon{width:56px;height:56px;flex:0 0 56px;padding:13px;border-radius:50%;color:#fff;background:#8b99a7}.task-icon svg{width:100%;height:100%;fill:currentColor}.task-card[data-tone=green] .task-icon{background:#0b9b5c}.task-card[data-tone=blue] .task-icon{background:#247bd4}.task-card[data-tone=amber] .task-icon{background:#f0ad00}.task-title{font-size:21px;font-weight:800}.task-detail{margin-top:8px;color:#536274;font-size:15px}.spinner{display:none;margin-left:auto;width:23px;height:23px;border:3px dotted #29445c;border-radius:50%;animation:spin 1.3s linear infinite}.spinner.visible{display:block}.task-meta{display:flex;gap:10px;margin-top:15px;color:#56687c;font-size:13px}.task-meta strong{font-weight:500}.message-block{margin-top:18px}.message-block h3{color:#617084;font-size:13px;font-weight:800;margin-bottom:7px}.worker-message,.web-response{max-height:140px;overflow-y:auto;padding:10px 12px;border-radius:8px;background:#fff8;white-space:pre-wrap;overflow-wrap:anywhere;color:#24364d;font-size:14px;line-height:1.45}.web-response{max-height:220px}.response-block{border-top:1px solid #ffffffaa;padding-top:14px}.resource-rescan{width:100%;margin-top:12px;border-color:#8bb7df;background:#eef7ff;color:#1f5f99;cursor:pointer}.resource-rescan:hover{background:#e2f1ff}.hidden{display:none!important}.hub-footer{text-align:center;padding:0 21px 10px;color:#91a0b0;font-size:11px}.settings-modal{position:fixed;inset:0;display:grid;place-items:center;background:#0f172a47;z-index:2147483647;padding:16px}.settings-modal.hidden{display:none}.settings-dialog{width:380px;max-width:100%;background:#fff;border:3px solid #26323a;border-radius:14px;padding:18px}.settings-title-row{display:flex;justify-content:space-between}.settings-close{border:0;background:transparent;width:24px;height:24px}.settings-form{display:grid;gap:11px;margin-top:16px}.settings-form label{display:grid;gap:5px;font-size:13px;font-weight:700}.settings-form input{min-height:34px;border:1px solid #bdcad7;border-radius:7px;padding:6px 9px;font:inherit}.settings-test-row,.settings-actions{display:flex;justify-content:flex-end;gap:10px;align-items:center}.secondary-button,.primary-button{min-height:34px;border-radius:7px;padding:6px 12px;font:inherit;font-size:13px;font-weight:700}.secondary-button{border:1px solid #cbd7e3;background:#fff}.primary-button{border:1px solid #1475de;background:#1475de;color:#fff}.test-status{font-size:12px}.test-status.success{color:#078443}.test-status.error{color:#c53b3b}.reopen{display:none;position:fixed;top:100px;right:24px;z-index:2147483646;width:42px;height:42px;border:0;border-radius:50%;color:#fff;background:#26323a;padding:10px}.reopen.visible{display:block}@keyframes spin{to{transform:rotate(360deg)}}';
  const panel=root.querySelector('.hub-panel'), systemText=root.querySelector('.system-text'), systemIcon=root.querySelector('.system-icon'), card=root.querySelector('.task-card'), taskIcon=root.querySelector('.task-icon'), taskTitle=root.querySelector('.task-title'), taskDetail=root.querySelector('.task-detail'), taskId=root.querySelector('.task-id'), taskFrom=root.querySelector('.task-from'), spinner=root.querySelector('.spinner'), workerMessage=root.querySelector('.worker-message'), responseBlock=root.querySelector('.response-block'), webResponse=root.querySelector('.web-response'), resourceRescan=root.querySelector('.resource-rescan'), settingsModal=root.querySelector('.settings-modal'), settingsForm=root.querySelector('.settings-form'), testStatus=root.querySelector('.test-status');
  const DEFAULT={bridgeHost:'127.0.0.1',bridgePort:43821,bridgeBasePath:'/bridge'};
  let settings=Object.assign({},DEFAULT), managedRole=preflightManagedRole, managedRuntimeToken=preflightRuntimeToken, currentConversationId=null, lastBoundConversationId=null, currentProjectId='', activeTaskId=null, sentTaskId=null, activeLeaseId=null, activeResource=null, phase='IDLE', baselineAssistant='', baselineAssistantElement=null, baselineAssistantKey='', baselineAssistantCount=-1, baselineUserMessages=[], baselineTurnFingerprints=new Set(), latchedSendEvidence='', baselineImageSources=new Set(), baselineFileUrls=new Set(), pendingResult=null, lastProgressKey='', activeTaskOwner=null, sendTriggeredForActiveTask=false, responseStarted=false, responseStartedAt=0, responseDeadlineTimer=0, lastResourceProgressKey='', stableSnapshot='', progressQueue=Promise.resolve(); let resetGeneration=0, navigationGeneration=0; const LONG_OPERATION_TIMEOUT=300000, DELIVERY_TIMEOUT=LONG_OPERATION_TIMEOUT, COMPOSER_TIMEOUT=LONG_OPERATION_TIMEOUT, SEND_CONFIRM_TIMEOUT=LONG_OPERATION_TIMEOUT, RESOURCE_WAIT_TIMEOUT=LONG_OPERATION_TIMEOUT, RESOURCE_SETTLE_DELAY=5000;
  function validate(c){if(c.bridgeHost!=='127.0.0.1'&&c.bridgeHost!=='localhost')throw new Error('Loopback host only');if(!Number.isInteger(Number(c.bridgePort))||Number(c.bridgePort)<1||Number(c.bridgePort)>65535)throw new Error('Invalid port');return {bridgeHost:String(c.bridgeHost),bridgePort:Number(c.bridgePort),bridgeBasePath:'/'+String(c.bridgeBasePath||'/bridge').replace(/^\/|\/$/g,'')};}
  function normalizeManagedRole(value){const role=String(value||'').trim().toUpperCase();return role==='HQ'||role==='RESOURCE'?role:'';}
  function managedRoleFromUrl(){try{return normalizeManagedRole(new URL(location.href).searchParams.get('projecthub-managed-role'));}catch{return '';}}
  function managedRuntimeTokenFromUrl(){try{return String(new URL(location.href).searchParams.get('projecthub-runtime-token')||'').trim();}catch{return '';}}
  function stripManagedLaunchMarker(){try{const value=new URL(location.href);let changed=false;for(const key of ['projecthub-managed-role','projecthub-runtime-token']){if(value.searchParams.has(key)){value.searchParams.delete(key);changed=true;}}if(!changed)return;const next=value.pathname+(value.searchParams.toString()?'?'+value.searchParams.toString():'')+value.hash;history.replaceState(history.state,'',next);}catch{}}
  function url(path,cfg){const c=cfg||settings;return 'http://'+c.bridgeHost+':'+c.bridgePort+c.bridgeBasePath+'/'+path;} function reportProgress(stage,detail='',attempt=0){if(!activeTaskId||!currentConversationId)return;const key=stage+'|'+detail+'|'+attempt;if(key===lastProgressKey)return;lastProgressKey=key;const body=JSON.stringify({taskId:activeTaskId,conversationId:currentConversationId,leaseId:activeLeaseId||'',stage,detail,attempt});progressQueue=progressQueue.then(()=>fetchWithTimeout(url('progress'),{method:'POST',headers:{'Content-Type':'application/json'},body},8000)).catch(()=>{});} function setPhase(next,detail='',attempt=0){phase=next;reportProgress(next,detail,attempt);}
  async function fetchWithTimeout(resource,options={},timeout=4000){const controller=new AbortController();const timer=setTimeout(()=>controller.abort(),timeout);try{const headers=new Headers(options.headers||{});try{const target=new URL(String(resource));if((target.hostname==='127.0.0.1'||target.hostname==='localhost')&&managedRuntimeToken)headers.set('X-ProjectHub-Managed-Token',managedRuntimeToken);}catch{}return await fetch(resource,{...options,headers,signal:controller.signal});}finally{clearTimeout(timer);}}
  function saveTaskMemory(){if(!currentConversationId||!activeTaskId)return;sessionStorage.setItem('gptweb-hub-task-'+currentConversationId+'-'+activeTaskId,JSON.stringify({sentTaskId,activeLeaseId,baselineAssistant,baselineAssistantKey,baselineAssistantCount,baselineUserMessages,baselineTurnFingerprints:[...baselineTurnFingerprints],latchedSendEvidence,baselineImageSources:[...baselineImageSources],baselineFileUrls:[...baselineFileUrls]}));}
function restoreTaskMemory(taskId,prompt=''){if(!currentConversationId)return false;try{const value=JSON.parse(sessionStorage.getItem('gptweb-hub-task-'+currentConversationId+'-'+taskId)||'null');if(!value)return false;sentTaskId=value.sentTaskId||null;activeLeaseId=value.activeLeaseId||null;baselineAssistant=value.baselineAssistant||'';baselineAssistantKey=value.baselineAssistantKey||'';baselineAssistantCount=Number.isInteger(value.baselineAssistantCount)?value.baselineAssistantCount:-1;baselineAssistantElement=baselineAssistantCount===assistantMessages().length?latestAssistantElement():null;baselineTurnFingerprints=new Set(Array.isArray(value.baselineTurnFingerprints)?value.baselineTurnFingerprints:[]);latchedSendEvidence=String(value.latchedSendEvidence||'');baselineImageSources=new Set(Array.isArray(value.baselineImageSources)?value.baselineImageSources:[]);baselineFileUrls=new Set(Array.isArray(value.baselineFileUrls)?value.baselineFileUrls:[]);if(Array.isArray(value.baselineUserMessages))baselineUserMessages=value.baselineUserMessages;else{const messages=userMessages(),expected=normalizeText(prompt),probe=expected.slice(0,Math.min(120,expected.length));let index=-1;for(let i=messages.length-1;i>=0;i--){if(messages[i]===expected||probe&&messages[i].includes(probe)){index=i;break;}}baselineUserMessages=index>=0?messages.filter((_,i)=>i!==index):messages;}return true;}catch{return false;}}
function setStatus(kind,value,tone){const e=root.querySelector('.status-row[data-kind="'+kind+'"] .status-value');if(!e)return;e.textContent=value;e.className='status-value status-'+tone;}
  function conversation(){const m=location.pathname.match(/\/c\/([a-zA-Z0-9-]+)/);return m?m[1]:null;}
  function title(){const raw=document.title.replace(/\s*[|—-]\s*ChatGPT.*$/i,'').trim()||'현재 대화';const match=raw.match(/^(.+?)\s+-\s+(.+)$/);return match?match[1].trim()+'\n'+match[2].trim():raw;}
  function setTask(state,t){const tones={IDLE:'',CLAIMED:'blue',PENDING:'blue',COMPLETED:'amber',FAILED:'amber'};card.dataset.state=state;card.dataset.tone=tones[state]||'';const active=state==='PENDING'||state==='CLAIMED';taskTitle.textContent=state==='IDLE'?'작업 없음':state==='COMPLETED'?'작업 종료':state==='FAILED'?'작업 종료':active?'WORKER → GPT WEB':'GPT WEB → WORKER';taskDetail.textContent=state==='IDLE'?'현재 처리할 요청이 없습니다.':state==='PENDING'?'요청 준비 중':state==='CLAIMED'?'ChatGPT 응답 생성 중':state==='COMPLETED'?(t?.finishReason||'정상 완료'):(t?.finishReason||'오류 발생 · 사용자 확인 필요');taskId.textContent=t?.id||'—';taskFrom.textContent=t?.owner||'—';spinner.classList.toggle('visible',active);resourceRescan.classList.toggle('hidden',!(active&&(t?.resource||activeResource)));if(t?.prompt)workerMessage.textContent=t.prompt;if(t?.result){responseBlock.classList.remove('hidden');webResponse.textContent=t.result;}else if(state==='IDLE'){responseBlock.classList.add('hidden');webResponse.textContent='응답을 기다리고 있습니다.';workerMessage.textContent='대기 중';}}
  function visible(e){return !!e&&!!(e.offsetWidth||e.offsetHeight||e.getClientRects().length)&&getComputedStyle(e).visibility!=='hidden';}
  function composer(){const known=document.querySelector('#prompt-textarea,[data-testid="prompt-textarea"]');if(visible(known)&&!known.disabled)return known;const candidates=[...document.querySelectorAll('textarea,[contenteditable="true"],[role="textbox"]')].filter(e=>visible(e)&&!e.disabled&&!e.closest('[data-testid="writing-block-container"],[data-message-author-role="assistant"]'));const labeled=candidates.find(e=>{const label=[e.getAttribute('aria-label'),e.getAttribute('placeholder'),e.getAttribute('data-testid')].filter(Boolean).join(' ');return /message|메시지|prompt|질문|composer/i.test(label);});return labeled||candidates.find(e=>e.closest('form')&&e.getAttribute('role')==='textbox')||candidates.find(e=>e.closest('form'))||candidates.find(e=>e.tagName==='TEXTAREA'&&e.closest('main'))||null;}
  function isVoiceControl(button){const label=[button.getAttribute('aria-label'),button.getAttribute('data-testid'),button.getAttribute('title')].filter(Boolean).join(' ');return /voice|microphone|speech|음성|마이크|음성 모드/i.test(label);}
  function voiceButton(){return [...document.querySelectorAll('button')].find(button=>visible(button)&&!button.disabled&&button.getAttribute('aria-disabled')!=='true'&&isVoiceControl(button))||null;}
  function sendButton(){
    const selectors=['button[data-testid="send-button"]','button[data-testid*="send"]','button[aria-label="Send prompt"]','button[aria-label="Send message"]','button[aria-label*="Send" i]','button[aria-label="보내기"]','button[aria-label="전송"]','button[aria-label*="메시지 보내기"]'];
    const input=composer();
    const scopes=[];
    if(input){const form=input.closest('form');if(form)scopes.push(form);let parent=input.parentElement;for(let index=0;parent&&index<4;index++,parent=parent.parentElement)scopes.push(parent);}
    scopes.push(document);
    for(const scope of scopes){
      for(const selector of selectors){
        for(const button of scope.querySelectorAll(selector)){
          const disabled=button.disabled||button.getAttribute('aria-disabled')==='true';
          if(visible(button)&&!disabled&&button.getAttribute('aria-hidden')!=='true'&&!isVoiceControl(button))return button;
        }
      }
    }
    return null;
  }
   function activateSendButton(button){
     button.focus({preventScroll:true});
     const init={bubbles:true,cancelable:true,view:window};
     try{
       button.dispatchEvent(new PointerEvent('pointerdown',{...init,pointerId:1,button:0}));
       button.dispatchEvent(new MouseEvent('mousedown',{...init,button:0}));
       button.dispatchEvent(new PointerEvent('pointerup',{...init,pointerId:1,button:0}));
       button.dispatchEvent(new MouseEvent('mouseup',{...init,button:0}));
     }finally{button.click();}
   }async function waitFor(predicate,timeout=10000){const end=Date.now()+timeout;while(Date.now()<end){const value=predicate();if(value)return value;await new Promise(resolve=>setTimeout(resolve,150));}return null;}
  function setText(el,text){el.focus();if(el.isContentEditable){document.execCommand('selectAll',false,null);document.execCommand('insertText',false,text);el.dispatchEvent(new InputEvent('beforeinput',{bubbles:true,inputType:'insertText',data:text}));el.dispatchEvent(new InputEvent('input',{bubbles:true,inputType:'insertText',data:text}));}else{const proto=Object.getPrototypeOf(el);const setter=Object.getOwnPropertyDescriptor(proto,'value')?.set;if(setter)setter.call(el,text);else el.value=text;el.dispatchEvent(new InputEvent('input',{bubbles:true,inputType:'insertText',data:text}));}el.dispatchEvent(new Event('change',{bubbles:true}));}
  function composerText(el){return el?.isContentEditable?(el.innerText||el.textContent||'').trim():(el?.value||'').trim();} function normalizeText(value){return String(value||'').replace(/\s+/g,' ').trim();} function composerHasPrompt(el,prompt){const actual=normalizeText(composerText(el));const expected=normalizeText(prompt);if(!actual||!expected)return false;const probe=expected.slice(0,Math.min(80,expected.length));return actual.includes(probe)||actual.length>=Math.floor(expected.length*0.85);} function skyEnter(input){input.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',code:'Enter',which:13,keyCode:13,bubbles:true,cancelable:true}));input.dispatchEvent(new KeyboardEvent('keyup',{key:'Enter',code:'Enter',which:13,keyCode:13,bubbles:true}));}
  function turnRole(node){if(!node)return '';const direct=String(node.getAttribute?.('data-message-author-role')||'').toLowerCase();if(direct==='user'||direct==='assistant')return direct;const nested=node.querySelector?.('[data-message-author-role="user"],[data-message-author-role="assistant"]');const nestedRole=String(nested?.getAttribute?.('data-message-author-role')||'').toLowerCase();if(nestedRole==='user'||nestedRole==='assistant')return nestedRole;const testId=String(node.getAttribute?.('data-testid')||'').toLowerCase();if(/(?:^|[-_])user(?:[-_]|$)/.test(testId))return 'user';if(/(?:^|[-_])assistant(?:[-_]|$)/.test(testId))return 'assistant';return '';}
  function turnContainer(node){return node?.closest?.('[data-testid*="conversation-turn"],article')||node;}
  function conversationTurns(){const candidates=[...document.querySelectorAll('[data-message-author-role="user"],[data-message-author-role="assistant"],[data-testid*="conversation-turn"],article[data-testid*="conversation-turn"]')],seen=new Set(),result=[];for(const candidate of candidates){const container=turnContainer(candidate);if(!container||seen.has(container))continue;const role=turnRole(container)||turnRole(candidate);if(role!=='user'&&role!=='assistant')continue;const roleNode=container.querySelector?.('[data-message-author-role="'+role+'"]')||candidate;const text=normalizeText(roleNode?.innerText||roleNode?.textContent||container.innerText||container.textContent||'');if(!text)continue;seen.add(container);const key=roleNode?.getAttribute?.('data-message-id')||container.getAttribute?.('data-message-id')||roleNode?.id||container.id||container.getAttribute?.('data-testid')||'';result.push({role,element:roleNode||container,container,key:String(key||''),text});}return result;}
  function turnFingerprint(turn){if(!turn)return '';const text=normalizeText(turn.text||'');const head=text.slice(0,240),tail=text.length>240?text.slice(-120):'';return [turn.role||'',turn.key||'',text.length,head,tail].join('|');}
  function promptMatchesText(text,prompt){const actual=normalizeText(text),expected=normalizeText(prompt);if(!actual||!expected)return false;const probe=expected.slice(0,Math.min(120,expected.length));return actual===expected||actual.includes(probe)||(expected.includes(actual)&&actual.length>=Math.floor(expected.length*0.6));}
  function userTurnRecords(){return conversationTurns().filter(turn=>turn.role==='user');}
  function assistantTurnRecords(){return conversationTurns().filter(turn=>turn.role==='assistant');}
  function userMessages(){return userTurnRecords().map(turn=>turn.text);}
  function latestUserMessage(){const turns=userTurnRecords();return turns.length?turns[turns.length-1].text:'';}
  function hasNewUserMessage(prompt,beforeMessages=[]){const matching=userTurnRecords().filter(turn=>promptMatchesText(turn.text,prompt));if(matching.some(turn=>!baselineTurnFingerprints.has(turnFingerprint(turn))))return true;if(baselineTurnFingerprints.size)return false;const messages=matching.map(turn=>turn.text);return messages.length>beforeMessages.filter(message=>promptMatchesText(message,prompt)).length;}
  function userMessageMatches(prompt){return promptMatchesText(latestUserMessage(),prompt);}
  function fileInput(){return [...document.querySelectorAll('input[type="file"]')].find(element=>visible(element)&&!element.disabled)||[...document.querySelectorAll('input[type="file"]')].find(element=>!element.disabled)||null;}
  async function attachFiles(composerInput,attachments){
    if(!attachments?.length)return;
    let input=fileInput();
    if(!input){
      const attachButton=[...document.querySelectorAll('button,[role="button"]')].find(element=>visible(element)&&/attach|upload|파일|첨부/i.test([element.getAttribute('aria-label'),element.getAttribute('data-testid'),element.getAttribute('title')].filter(Boolean).join(' ')));
      if(attachButton)attachButton.click();
      input=await waitFor(fileInput,15000);
    }
    if(!input)throw new Error('ATTACH_INPUT: ChatGPT file input not found');
    const transfer=new DataTransfer();
    for(const attachment of attachments){
      if(!attachment.downloadUrl)throw new Error('ATTACH_URL: attachment download URL missing');
      const response=await fetchWithTimeout(attachment.downloadUrl,{},LONG_OPERATION_TIMEOUT);
      if(!response.ok)throw new Error('ATTACH_DOWNLOAD: '+attachment.fileName+' (HTTP '+response.status+')');
      const blob=await response.blob();
      const buffer=await blob.arrayBuffer();
      const actualSha256=await sha256Hex(buffer);
      if(attachment.sha256&&actualSha256.toLowerCase()!==String(attachment.sha256).trim().toLowerCase())throw new Error('ATTACH_HASH_MISMATCH: '+(attachment.fileName||'attachment'));
      reportProgress('ATTACHMENT_VERIFIED',(attachment.fileName||'attachment')+' · sha256='+actualSha256);
      const file=new File([buffer],attachment.fileName||'attachment',{type:attachment.mimeType||blob.type||'application/octet-stream'});
      transfer.items.add(file);
    }
    input.files=transfer.files;
    input.dispatchEvent(new Event('input',{bubbles:true,composed:true}));
    input.dispatchEvent(new Event('change',{bubbles:true,composed:true}));
  }  async function sendToChatGPT(prompt,attachments){const input=await waitFor(composer,COMPOSER_TIMEOUT);if(!input)throw new Error('ChatGPT composer not found');setPhase('TEXT_INSERT','composer 대상 확인 · '+(input.id||input.getAttribute('data-testid')||input.tagName));input.focus({preventScroll:true});setText(input,prompt);if(!composerHasPrompt(input,prompt))throw new Error('COMPOSER_TEXT_INSERT_FAILED: 실제 ChatGPT 입력창에서 전달 문구를 확인하지 못했습니다.');if(attachments?.length){setPhase('TEXT_INSERT','첨부 파일을 준비하는 중');await attachFiles(input,attachments);}setPhase('SEND_BUTTON_FIND','입력 완료 · 전송 버튼 감시 중');void monitorSendReady(prompt);}
  function sendConfirmationEvidence(prompt){
    if(latchedSendEvidence)return latchedSendEvidence;
    let evidence='';
    if(hasNewUserMessage(prompt,baselineUserMessages))evidence='USER_MESSAGE';
    if(!evidence){const assistantEvidence=assistantTurnEvidence();if(assistantEvidence)evidence=assistantEvidence==='TEXT'?'ASSISTANT_TEXT_CHANGED':'ASSISTANT_RESPONSE';}
    if(!evidence&&activeResource&&latestGeneratedResourceCandidates().length)evidence='RESOURCE_RESULT';
    if(!evidence)evidence=reconcileConversationAfterSend(prompt);
    if(evidence){
      latchedSendEvidence=evidence;
      saveTaskMemory();
      reportProgress('SEND_EVIDENCE_LATCHED','숨김/지연 DOM에서도 전송 증거를 고정했습니다. evidence='+evidence);
    }
    return evidence;
  }
  function enterWaitResponse(detail,attempt=0){
    setPhase('WAIT_RESPONSE',detail,attempt);
    if(activeResource)scheduleResponseDeadline();
    observeResponse();
  }
  async function monitorSendReady(prompt){
    const deadline=Date.now()+DELIVERY_TIMEOUT;
    let attempt=0,voiceOnlySince=0;
    while((phase==='WAIT_SEND_READY'||phase==='SEND_BUTTON_FIND'||phase==='SEND_CONFIRM')&&activeTaskId&&Date.now()<deadline){
      const input=composer();
      if(input&&composerHasPrompt(input,prompt)){
        const send=sendButton();
        if(send){
          voiceOnlySince=0;
          attempt++;
          activateSendButton(send);
          sendTriggeredForActiveTask=true;
          setPhase('SEND_CONFIRM','Send 버튼 클릭 완료 · 사용자 turn 또는 assistant 응답 확인 중',attempt);
          const confirmed=await waitFor(()=>phase!=='SEND_CONFIRM'?'PHASE_CHANGED':sendConfirmationEvidence(prompt),SEND_CONFIRM_TIMEOUT);
          if(phase!=='SEND_CONFIRM'||!activeTaskId)return;
          if(confirmed){
            enterWaitResponse('전송 확인 · '+confirmed+' · Web 응답 감시 중',attempt);
            return;
          }
          if(activeResource&&latestGeneratedResourceCandidates().length){
            enterWaitResponse('전송 확인 응답은 없지만 새 RESOURCE 결과를 확인해 수집을 시작합니다.',attempt);
            return;
          }
          setPhase('SEND_BUTTON_FIND','전송 확인 실패 · 실제 입력 상태 재확인 중',attempt);
        }else if(voiceButton()){
          const evidence=sendConfirmationEvidence(prompt);
          if(evidence){
            enterWaitResponse('대화 전송 증거 확인 · '+evidence,attempt);
            return;
          }
          if(!voiceOnlySince)voiceOnlySince=Date.now();
          setPhase('SEND_BUTTON_FIND','입력은 되었지만 Voice 버튼만 표시됨 · 메시지는 아직 전송되지 않았습니다.',attempt);
          if(Date.now()-voiceOnlySince>=2250){
            const reason='Send 버튼이 없어 Worker 메시지를 전송하지 못했습니다. Voice 버튼만 표시되었습니다.';
            reportProgress('FAILED',reason,attempt);
            await failTask(reason);
            return;
          }
        }else{
          const evidence=sendConfirmationEvidence(prompt);
          if(evidence){
            enterWaitResponse('전송 상태 확인 · '+evidence,attempt);
            return;
          }
          voiceOnlySince=0;
          setPhase('SEND_BUTTON_FIND','전송 버튼을 찾는 중',attempt);
        }
      }else{
        const evidence=sendConfirmationEvidence(prompt);
        if(evidence){
          enterWaitResponse('실제 전송 확인 · '+evidence+' · Web 응답 감시 중',attempt);
          return;
        }
        voiceOnlySince=0;
        if(sendTriggeredForActiveTask){
          setPhase('SEND_CONFIRM','composer는 비워졌지만 실제 사용자 turn 또는 assistant 응답 확인 대기',attempt);
        }else{
          setPhase('TEXT_INSERT','composer 입력 상태를 확인하는 중',attempt);
        }
      }
      await new Promise(resolve=>setTimeout(resolve,750));
    }
    if(activeTaskId&&['WAIT_SEND_READY','SEND_BUTTON_FIND','SEND_CONFIRM'].includes(phase)){
      const reconciled=sendConfirmationEvidence(prompt)||reconcileConversationAfterSend(prompt);
      if(reconciled){
        reportProgress('SEND_TIMEOUT_RECOVERED','timeout 직전 현재 DOM에서 전송/응답 증거를 복구했습니다. evidence='+reconciled,attempt);
        enterWaitResponse('timeout 직전 DOM reconciliation으로 전송 확인 · '+reconciled,attempt);
        return;
      }
      const reason='Web 전송 확인 시간이 초과되었습니다. 단계: '+phase;
      reportProgress('FAILED',reason,attempt);
      await failTask(reason);
    }
  }
  function assistantMessages(){return assistantTurnRecords().map(turn=>turn.element);}
function latestAssistantElement(){const turns=assistantTurnRecords();return turns.length?turns[turns.length-1].element:null;}
function assistantMessageKey(node){const container=turnContainer(node);return node?.getAttribute?.('data-message-id')||container?.getAttribute?.('data-message-id')||node?.id||container?.id||container?.getAttribute?.('data-testid')||'';}
function latestAssistant(){const turns=assistantTurnRecords();return turns.length?turns[turns.length-1].text:'';}
function mainSurface(){return document.querySelector('main')||document.body;}
function imageSrc(img){return img?.currentSrc||img?.src||'';}
function imageFingerprint(img){return [imageSrc(img),img.complete?'1':'0',img.naturalWidth||0,img.naturalHeight||0].join(':');}
function collectVisibleLargeImages(root=mainSurface()){const seen=new Set();return [...root.querySelectorAll('img')].filter(img=>{const src=imageSrc(img);if(!src||seen.has(src)||!visible(img))return false;seen.add(src);const width=img.naturalWidth||img.width||0,height=img.naturalHeight||img.height||0;return Math.max(width,height)>=128;});}
function captureImageBaseline(){baselineImageSources=new Set([...mainSurface().querySelectorAll('img')].map(imageSrc).filter(Boolean));}
function latestGeneratedImageCandidates(){const seen=new Set(),result=[];const append=images=>{for(const img of images){const src=imageSrc(img);if(!src||baselineImageSources.has(src)||seen.has(src))continue;seen.add(src);result.push(img);}};const assistant=latestAssistantElement();if(assistant)append(collectVisibleLargeImages(assistant));append(collectVisibleLargeImages(mainSurface()));return result;}
function latestGeneratedImages(){return latestGeneratedImageCandidates().filter(img=>img.complete&&(img.naturalWidth||0)>=128&&(img.naturalHeight||0)>=128);}
function resourceElementUrl(element){if(!element)return '';if(element.tagName==='A')return element.href||element.getAttribute('href')||'';if(element.tagName==='AUDIO'||element.tagName==='VIDEO')return element.currentSrc||element.src||'';if(element.tagName==='SOURCE')return element.src||'';return '';}
function resourceFileNameHint(element,url=''){const direct=element?.getAttribute?.('download');if(direct&&direct!==true)return direct;const labels=[element?.getAttribute?.('aria-label'),element?.getAttribute?.('title'),element?.textContent].filter(Boolean).join(' ').trim();const match=labels.match(/([^\\/:*?"<>|\s][^\\/:*?"<>|]*\.(?:png|jpe?g|webp|gif|svg|mp3|wav|ogg|flac|m4a|mp4|webm|pdf|zip|json|txt|md|csv|docx|xlsx|pptx))\b/i);if(match)return match[1].trim();try{const parsed=new URL(url,location.href);const name=decodeURIComponent(parsed.pathname.split('/').filter(Boolean).pop()||'');if(/\.[a-z0-9]{1,15}$/i.test(name))return name;}catch{}return '';}
function allowedResourceCandidateUrl(url){if(/^(?:blob:|data:)/i.test(url))return true;try{const parsed=new URL(url,location.href);if(parsed.protocol!=='https:')return false;const host=parsed.hostname.toLowerCase();return host==='chatgpt.com'||host==='www.chatgpt.com'||host.endsWith('.oaiusercontent.com')||host.endsWith('.openai.com');}catch{return false;}}
function looksLikeResourceFile(element,url){if(!url||/^javascript:/i.test(url)||!allowedResourceCandidateUrl(url))return false;if(/^(?:blob:|data:)/i.test(url))return true;if(element?.tagName==='AUDIO'||element?.tagName==='VIDEO'||element?.tagName==='SOURCE')return true;const label=[element?.getAttribute?.('download'),element?.getAttribute?.('aria-label'),element?.getAttribute?.('title'),element?.textContent].filter(Boolean).join(' ');if(element?.hasAttribute?.('download'))return true;if(/download|다운로드|attachment|첨부|파일 저장/i.test(label))return true;if(/\/(?:files?|downloads?|attachments?|backend-api\/files)(?:\/|\?|$)/i.test(url))return true;return /\.(?:png|jpe?g|webp|gif|svg|mp3|wav|ogg|flac|m4a|mp4|webm|pdf|zip|json|txt|md|csv|docx|xlsx|pptx)(?:[?#]|$)/i.test(url);}
function collectDownloadableFiles(root=mainSurface()){const result=[],seen=new Set();for(const element of root.querySelectorAll('a[href],audio[src],video[src],audio source[src],video source[src]')){const url=resourceElementUrl(element);if(!url||seen.has(url)||!looksLikeResourceFile(element,url))continue;seen.add(url);result.push({element,url,fileName:resourceFileNameHint(element,url)});}return result;}
function captureFileBaseline(){baselineFileUrls=new Set(collectDownloadableFiles(mainSurface()).map(item=>item.url));}
function latestGeneratedFileCandidates(){const result=[],seen=new Set();const append=items=>{for(const item of items){if(!item.url||baselineFileUrls.has(item.url)||seen.has(item.url))continue;seen.add(item.url);result.push(item);}};const assistant=latestAssistantElement();if(assistant)append(collectDownloadableFiles(assistant));append(collectDownloadableFiles(mainSurface()));return result;}
function latestGeneratedResourceCandidates(){const result=[],seen=new Set();for(const img of latestGeneratedImageCandidates()){const url=imageSrc(img);if(!url||seen.has(url))continue;seen.add(url);result.push({kind:'image',element:img,url,fileName:''});}for(const file of latestGeneratedFileCandidates()){if(!file.url||seen.has(file.url))continue;seen.add(file.url);result.push({kind:'file',element:file.element,url:file.url,fileName:file.fileName||''});}return result;}
function readyResourceCandidates(){return latestGeneratedResourceCandidates().filter(item=>item.kind!=='image'||(item.element.complete&&(item.element.naturalWidth||0)>=128&&(item.element.naturalHeight||0)>=128));}
function resourceFingerprint(item){if(item.kind==='image')return 'image:'+imageFingerprint(item.element);return 'file:'+item.url+':'+(item.fileName||'');}
function responseSnapshot(){const resources=activeResource?latestGeneratedResourceCandidates():[];return latestAssistant()+'|'+resources.map(resourceFingerprint).join('|');}
function bytesToBase64(buffer){const bytes=new Uint8Array(buffer);let binary='';const chunk=0x8000;for(let i=0;i<bytes.length;i+=chunk)binary+=String.fromCharCode(...bytes.subarray(i,i+chunk));return btoa(binary);}
async function sha256Hex(buffer){const digest=await crypto.subtle.digest('SHA-256',buffer);return [...new Uint8Array(digest)].map(value=>value.toString(16).padStart(2,'0')).join('').toUpperCase();}
function contentDispositionFileName(value){if(!value)return '';const utf=value.match(/filename\*\s*=\s*UTF-8''([^;]+)/i);if(utf){try{return decodeURIComponent(utf[1].trim().replace(/^"|"$/g,''));}catch{}}const plain=value.match(/filename\s*=\s*"?([^";]+)"?/i);return plain?plain[1].trim():'';}
function extensionForMime(mime=''){const type=String(mime).split(';')[0].trim().toLowerCase();return ({'image/png':'.png','image/jpeg':'.jpg','image/jpg':'.jpg','image/webp':'.webp','image/gif':'.gif','image/svg+xml':'.svg','audio/mpeg':'.mp3','audio/wav':'.wav','audio/x-wav':'.wav','audio/ogg':'.ogg','audio/flac':'.flac','audio/mp4':'.m4a','video/mp4':'.mp4','video/webm':'.webm','application/pdf':'.pdf','application/zip':'.zip','application/json':'.json','text/plain':'.txt','text/markdown':'.md','text/csv':'.csv','application/vnd.openxmlformats-officedocument.wordprocessingml.document':'.docx','application/vnd.openxmlformats-officedocument.spreadsheetml.sheet':'.xlsx','application/vnd.openxmlformats-officedocument.presentationml.presentation':'.pptx'})[type]||'.bin';}
async function fetchResourceBytes(src){try{const response=await fetchWithTimeout(src,{credentials:'include'},LONG_OPERATION_TIMEOUT);if(!response.ok)throw new Error('RESOURCE_DOWNLOAD_HTTP_'+response.status);const blob=await response.blob();if(!blob.size)throw new Error('RESOURCE_FILE_EMPTY');const buffer=await blob.arrayBuffer();return {base64:bytesToBase64(buffer),sha256:await sha256Hex(buffer),mimeType:blob.type||response.headers.get('content-type')||'application/octet-stream',contentDisposition:response.headers.get('content-disposition')||''};}catch(error){if(!/^https?:/i.test(src)||!globalThis.chrome?.runtime?.sendMessage)throw error;return await new Promise((resolve,reject)=>chrome.runtime.sendMessage({type:'fetch-resource-file',url:src},response=>{if(chrome.runtime.lastError){reject(new Error(chrome.runtime.lastError.message));return;}if(!response?.ok){reject(new Error(response?.error||String(error.message||error)));return;}resolve({base64:response.base64,sha256:response.sha256||'',mimeType:response.mimeType||'application/octet-stream',contentDisposition:response.contentDisposition||''});}));}}
async function fetchResourcePayload(item,index,total){reportProgress(index===0?'DOWNLOAD_START':'DOWNLOAD_PROGRESS',(index+1)+'/'+total+' 리소스 파일 다운로드 중');const payload=await fetchResourceBytes(item.url);let fileName=item.fileName||contentDispositionFileName(payload.contentDisposition);if(!fileName){const prefix=item.kind==='image'?'image':'resource';fileName=prefix+'-'+String(index+1).padStart(2,'0')+extensionForMime(payload.mimeType);}reportProgress('DOWNLOAD_VERIFIED',(index+1)+'/'+total+' 다운로드 검증 완료 · sha256='+payload.sha256);return {base64:payload.base64,mimeType:payload.mimeType,fileName,sha256:payload.sha256};}
async function resourcePayloads(){const candidates=readyResourceCandidates();if(!candidates.length)throw new Error('RESOURCE_NOT_FOUND');const files=[];for(let i=0;i<candidates.length;i++)files.push(await fetchResourcePayload(candidates[i],i,candidates.length));return files;}

function clearResponseTimers(){clearTimeout(stableTimer);stableTimer=0;stableSnapshot='';clearTimeout(responseDeadlineTimer);responseDeadlineTimer=0;}
function resetResponseTracking(){clearResponseTimers();sendTriggeredForActiveTask=false;latchedSendEvidence='';responseStarted=false;responseStartedAt=0;lastResourceProgressKey='';}
function captureAssistantBaseline(){resetResponseTracking();const turns=conversationTurns();baselineTurnFingerprints=new Set(turns.map(turnFingerprint).filter(Boolean));const assistants=turns.filter(turn=>turn.role==='assistant');const latest=assistants.length?assistants[assistants.length-1]:null;baselineAssistantElement=latest?.element||null;baselineAssistant=latest?.text||'';baselineAssistantKey=latest?.key||assistantMessageKey(baselineAssistantElement);baselineAssistantCount=assistants.length;captureImageBaseline();captureFileBaseline();}
function taskUserMessageConfirmed(){const prompt=normalizeText(workerMessage?.textContent||'');return !!prompt&&hasNewUserMessage(prompt,baselineUserMessages);}
function assistantTurnEvidence(){const turns=assistantTurnRecords();if(!turns.length)return '';const currentSendConfirmed=taskUserMessageConfirmed()||sendTriggeredForActiveTask;if(!currentSendConfirmed)return '';const latest=turns[turns.length-1],fingerprint=turnFingerprint(latest);if(fingerprint&&!baselineTurnFingerprints.has(fingerprint))return latest.key?'TURN':'TEXT';if(baselineAssistantCount>=0&&turns.length>baselineAssistantCount)return 'COUNT';const latestElement=latest.element,key=latest.key||assistantMessageKey(latestElement);if(!!baselineAssistantElement&&latestElement!==baselineAssistantElement)return 'ELEMENT';if(!!baselineAssistantKey&&!!key&&key!==baselineAssistantKey)return 'KEY';const latestText=normalizeText(latest.text||''),baselineText=normalizeText(baselineAssistant);if(latestElement===baselineAssistantElement&&latestText&&latestText!==baselineText)return 'TEXT';return '';}
function reconcileConversationAfterSend(prompt){const turns=conversationTurns();let userIndex=-1;for(let index=turns.length-1;index>=0;index--){const turn=turns[index];if(turn.role!=='user'||!promptMatchesText(turn.text,prompt))continue;if(!baselineTurnFingerprints.has(turnFingerprint(turn))||!baselineTurnFingerprints.size){userIndex=index;break;}}if(userIndex>=0){for(let index=userIndex+1;index<turns.length;index++){const turn=turns[index];if(turn.role==='assistant'&&normalizeText(turn.text))return 'ASSISTANT_RECONCILED';}return 'USER_MESSAGE_RECONCILED';}const assistantEvidence=assistantTurnEvidence();if(assistantEvidence)return assistantEvidence==='TEXT'?'ASSISTANT_TEXT_CHANGED':'ASSISTANT_RESPONSE';return '';}
function hasNewAssistantTurn(){return !!assistantTurnEvidence();}
async function failTask(reason,finishReason='send_failed'){clearResponseTimers();const body={success:false,taskId:activeTaskId,conversationId:currentConversationId,leaseId:activeLeaseId,responseText:reason,resultType:activeResource?'RESOURCE_ERROR':'TEXT_RESULT',finishReason};try{const r=await fetchWithTimeout(url('task/'+activeTaskId+'/result'),{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)},10000);const p=await r.json();if(p.ok){phase='FINISHED';setTask('FAILED',p.data);return;}}catch{}phase='WAIT_WORKER';systemText.textContent='Worker 결과 전달 실패: '+reason;}
function assistantStreaming(){return [...document.querySelectorAll('button,[role="button"],[data-is-streaming="true"],[data-streaming="true"],[aria-busy="true"]')].some(e=>{const state=e.getAttribute('data-is-streaming')||e.getAttribute('data-streaming')||e.getAttribute('aria-busy');if(state==='true')return true;if(!visible(e)||e.disabled||e.getAttribute('aria-disabled')==='true')return false;const label=[e.getAttribute('aria-label'),e.getAttribute('data-testid'),e.getAttribute('title')].filter(Boolean).join(' ');return /stop|중지|생성 중단/i.test(label);});}
let stableTimer=0;
function scheduleStableCheck(snapshot,delay,callback,allowStreaming=false){if(stableSnapshot===snapshot&&stableTimer)return;clearTimeout(stableTimer);stableSnapshot=snapshot;stableTimer=setTimeout(async()=>{stableTimer=0;const resourceCandidates=activeResource?latestGeneratedResourceCandidates():[];if(phase!=='WAIT_RESPONSE'||!activeTaskId||responseSnapshot()!==snapshot||(!hasNewAssistantTurn()&&!resourceCandidates.length)||(!allowStreaming&&assistantStreaming()))return;await callback();},delay);}
function scheduleResponseDeadline(){if(responseDeadlineTimer||!activeTaskId||!activeResource)return;if(!responseStartedAt)responseStartedAt=Date.now();const remaining=Math.max(1000,RESOURCE_WAIT_TIMEOUT-(Date.now()-responseStartedAt));responseDeadlineTimer=setTimeout(async()=>{responseDeadlineTimer=0;if(phase!=='WAIT_RESPONSE'||!activeTaskId)return;const ready=readyResourceCandidates();if(ready.length){reportProgress('RESOURCE_READY',ready.length+'개 생성 파일 확인 · deadline');await submitResult(hasNewAssistantTurn()?latestAssistant():('RESOURCE files generated: '+ready.length));return;}const reason='RESOURCE_NOT_GENERATED: response deadline exceeded without downloadable files.\n'+(hasNewAssistantTurn()?latestAssistant():'');reportProgress('RESOURCE_NO_FILE','시간 내 다운로드 가능한 생성 파일을 확인하지 못했습니다.');await failTask(reason,'resource_not_generated');},remaining);}
function observeResponse(){if(phase!=='WAIT_RESPONSE'||!activeTaskId)return;const isResource=!!activeResource,candidates=isResource?latestGeneratedResourceCandidates():[],assistantEvidence=assistantTurnEvidence(),newAssistant=!!assistantEvidence;if(!newAssistant&&!(isResource&&candidates.length))return;const text=newAssistant?latestAssistant():'',ready=isResource?readyResourceCandidates():[];if(!text&&!candidates.length)return;responseBlock.classList.remove('hidden');webResponse.textContent=text||(candidates.length?'생성 리소스 처리 중':'응답을 기다리고 있습니다.');if(!responseStarted){responseStarted=true;if(!responseStartedAt)responseStartedAt=Date.now();reportProgress('RESPONSE_START',isResource?'RESOURCE 생성 결과 응답을 확인했습니다.':'새 assistant 응답을 확인했습니다. evidence='+(assistantEvidence||'RESOURCE'));if(assistantEvidence==='TEXT')reportProgress('RESPONSE_TEXT_CHANGED','기존 assistant DOM의 텍스트 변화로 새 응답을 확인했습니다.');if(isResource)scheduleResponseDeadline();}if(isResource){const progressKey=candidates.length+'/'+ready.length;if(progressKey!==lastResourceProgressKey){lastResourceProgressKey=progressKey;reportProgress('RESOURCE_DETECTED','candidate='+candidates.length+', ready='+ready.length);}for(const candidate of candidates){if(candidate.kind==='image'&&!candidate.element.complete&&!candidate.element.dataset.projecthubLoadWatch){candidate.element.dataset.projecthubLoadWatch='1';candidate.element.addEventListener('load',()=>{candidate.element.dataset.projecthubLoadWatch='';observeResponse();},{once:true});candidate.element.addEventListener('error',()=>{candidate.element.dataset.projecthubLoadWatch='';observeResponse();},{once:true});}}if(ready.length){const snapshot=responseSnapshot(),delay=assistantStreaming()?15000:RESOURCE_SETTLE_DELAY;scheduleStableCheck(snapshot,delay,async()=>{const files=readyResourceCandidates();if(!files.length)return;reportProgress('RESOURCE_READY',files.length+'개 생성 파일 확인');await submitResult(hasNewAssistantTurn()?latestAssistant():('RESOURCE files generated: '+files.length));},true);}return;}if(assistantStreaming()){clearTimeout(stableTimer);stableTimer=0;stableSnapshot='';return;}const snapshot=responseSnapshot();scheduleStableCheck(snapshot,5000,async()=>{reportProgress('RESPONSE_STABLE','응답이 안정되어 결과를 전달합니다.');await submitResult(latestAssistant());});}
  function resumeClaimedResponse(task){activeResource=task.resource||null;activeTaskOwner=task.owner||null;resetResponseTracking();sentTaskId=task.id;activeLeaseId=task.leaseId||activeLeaseId||null;saveTaskMemory();setTask('PENDING',task);workerMessage.textContent=task.prompt;reportProgress('CLAIMED','전송된 대화의 응답 수신을 재개했습니다.');enterWaitResponse('기존 사용자 메시지 또는 생성 결과를 확인해 응답 수신을 재개합니다.');systemText.textContent='ChatGPT 응답을 Worker로 전달하는 중입니다.';}
  async function submitResult(text){
    if((phase!=='WAIT_RESPONSE'&&phase!=='WAIT_WORKER')||!activeTaskId)return;
    clearResponseTimers();
    setPhase('RESULT_POST','Worker에 Web 결과를 전달하는 중');
    pendingResult=text;
    setTask('CLAIMED',{id:activeTaskId,owner:activeTaskOwner||'WEB',prompt:workerMessage.textContent,result:text});
    const body={success:true,taskId:activeTaskId,conversationId:currentConversationId,leaseId:activeLeaseId,responseText:text,resultType:'TEXT_RESULT'};
    if(activeResource){
      try{body.resultFiles=await resourcePayloads();body.resultType='RESOURCE_FILES';}
      catch(error){const message=String(error.message||error);const finishReason=/DOWNLOAD_HTTP|RESOURCE_FILE_EMPTY|BACKGROUND_FETCH|URL_NOT_ALLOWED/i.test(message)?'resource_download_failed':'resource_capture_failed';await failTask('RESOURCE_CAPTURE: '+message,finishReason);return;}
    }
    let lastError=null;
    for(let attempt=0;attempt<3;attempt++){
      try{
        const r=await fetchWithTimeout(url('task/'+activeTaskId+'/result'),{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)},LONG_OPERATION_TIMEOUT);
        const p=await r.json();
        if(!p.ok)throw new Error(p.data?.error||'result rejected');
        pendingResult=null;activeResource=null;setPhase('FINISHED','Worker 결과 전달 완료');setTask(p.data?.status==='FAILED'?'FAILED':'COMPLETED',p.data);return;
      }catch(error){lastError=error;reportProgress('RESULT_POST_RETRY','결과 전달 재시도 중: '+error.message,attempt+1);await new Promise(resolve=>setTimeout(resolve,500*(attempt+1)));}
    }
    systemText.textContent='Worker 결과 전달 실패: '+lastError.message+' · 재시도 대기';phase='WAIT_WORKER';
  }
  async function deliverClaimedTask(claimed){activeResource=claimed.resource||null;activeTaskOwner=claimed.owner||null;resetResponseTracking();sentTaskId=claimed.id;activeLeaseId=claimed.leaseId||activeLeaseId||null;saveTaskMemory();setTask('PENDING',claimed);workerMessage.textContent=claimed.prompt;reportProgress('CLAIMED','Worker task를 확장이 수신했습니다.');setPhase('WAIT_SEND_READY','Worker Message를 전달하고 있습니다.');systemText.textContent='Worker Message를 전달하고 있습니다.';try{await sendToChatGPT(claimed.prompt,claimed.attachments||[]);}catch(error){if(String(error.message||error).startsWith('WEB_REQUIRES_FOREGROUND')){phase='WAIT_FOREGROUND';systemText.textContent='WEB_REQUIRES_FOREGROUND: ChatGPT 탭을 활성화하면 전송을 재개합니다.';setTask('CLAIMED',claimed);return;}systemText.textContent='Worker Message 전달 실패: '+error.message;phase='IDLE';sentTaskId=null;activeLeaseId=null;await fetchWithTimeout(url('task/'+claimed.id+'/result'),{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({success:false,taskId:claimed.id,conversationId:currentConversationId,leaseId:claimed.leaseId,responseText:error.message,resultType:'TEXT_RESULT',finishReason:'send_failed'})});}}async function bind(role){if(!currentConversationId)return;const response=await fetchWithTimeout(url('bind'),{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({conversationId:currentConversationId,projectId:currentProjectId,role})});const payload=await response.json();if(!payload.ok){systemText.textContent='역할 연결 실패: '+(payload.data?.error||'unknown');return;}refresh();}
  async function refreshNow(autoBind=false){const refreshGeneration=resetGeneration;const refreshNavigationGeneration=navigationGeneration;try{const previousConversationId=currentConversationId;const cid=conversation();currentConversationId=cid;const [sr,pr,tr,br]=await Promise.all([fetchWithTimeout(url('status')),fetchWithTimeout(url('projects')),fetchWithTimeout(url('task?conversationId='+(encodeURIComponent(cid||'__none__')))),cid?fetchWithTimeout(url('bindings/'+encodeURIComponent(cid))):Promise.resolve(null)]);if(!sr.ok||!pr.ok||!tr.ok)throw new Error('bridge unavailable');const status=await sr.json(),projects=await pr.json(),taskPayload=await tr.json(),binding=br?await br.json():null;if(refreshGeneration!==resetGeneration||refreshNavigationGeneration!==navigationGeneration||currentConversationId!==cid)return;const project=projects.data?.[0];let task=taskPayload.data?.task;if(task&&currentConversationId){const ignoredTaskId=sessionStorage.getItem('gptweb-hub-reset-task-'+currentConversationId);if(ignoredTaskId===task.id){task=null;}else if(ignoredTaskId){sessionStorage.removeItem('gptweb-hub-reset-task-'+currentConversationId);}}currentProjectId=project?.id||'';let bound=binding?.data?.bound?binding.data.projectId:'';let boundRole=binding?.data?.role||'';if(refreshNavigationGeneration!==navigationGeneration||currentConversationId!==cid)return;if(cid&&managedRole&&boundRole!==managedRole){const managedBindResponse=await fetchWithTimeout(url('bind'),{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({conversationId:cid,projectId:currentProjectId,role:managedRole})});const managedBindPayload=await managedBindResponse.json();if(!managedBindPayload.ok){systemText.textContent='관리형 역할 자동 연결 실패: '+(managedBindPayload.data?.error||'unknown');return;}bound=currentProjectId||'managed';boundRole=managedRole;}lastBoundConversationId=bound?cid:null;if(sr.ok) await fetchWithTimeout(url('heartbeat'),{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({client:'gptweb-hub',conversationId:cid,projectId:currentProjectId,conversationTitle:title(),extensionVersion:EXTENSION_VERSION,extensionBuild:EXTENSION_BUILD})});if(refreshNavigationGeneration!==navigationGeneration||currentConversationId!==cid)return;setStatus('web',title(),cid?'ok':'pending');setStatus('worker',status.data?.repository||'—',status.data?.repository?'ok':'pending');setStatus('status','Connected','ok');systemText.textContent='정상적으로 연결되어 있습니다.';const roleLabel=root.querySelector('.role-binding');roleLabel.textContent=boundRole?boundRole+' 연결됨':'역할 미연결';if(!cid){phase='IDLE';setTask('IDLE');systemText.textContent=managedRole?'ChatGPT 로그인 후 '+managedRole+' 전용 대화를 선택하세요.':'현재 ChatGPT 대화를 식별할 수 없습니다.';return;}if(!bound){phase='IDLE';setTask('IDLE');systemText.textContent=managedRole?'관리형 '+managedRole+' 대화를 자동 연결하는 중입니다.':'현재 ChatGPT 대화를 HQ 또는 RESOURCE 역할로 명시적으로 연결하세요.';return;}if(!task){if(phase!=='WAIT_RESPONSE'&&phase!=='WAIT_WORKER')phase='IDLE';if(phase==='IDLE')setTask('IDLE');systemText.textContent='정상적으로 연결되어 있습니다.';return;}const previousTaskId=activeTaskId;activeTaskId=task.id;if(previousTaskId&&previousTaskId!==task.id){phase='IDLE';sentTaskId=null;activeTaskOwner=null;resetResponseTracking();baselineAssistant='';baselineAssistantElement=null;baselineAssistantKey='';baselineAssistantCount=-1;baselineUserMessages=[];baselineTurnFingerprints=new Set();latchedSendEvidence='';baselineImageSources=new Set();baselineFileUrls=new Set();lastProgressKey='';}if(['COMPLETED','FAILED'].includes(task.status)){if(task.status==='FAILED'&&task.finishReason==='canceled'&&sentTaskId===task.id){const input=composer();if(input&&normalizeText(composerText(input))===normalizeText(task.prompt))clearComposer();}phase='FINISHED';setTask(task.status,task);return;}if(task.status==='PENDING'&&phase==='IDLE'&&!sentTaskId){captureAssistantBaseline();baselineUserMessages=userMessages();const claim=await fetchWithTimeout(url('task/'+task.id+'/claim'),{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({conversationId:currentConversationId})});const cp=await claim.json();if(cp.ok){await deliverClaimedTask(cp.data);}return;}if(task.status==='CLAIMED'){if(phase==='IDLE'){restoreTaskMemory(task.id,task.prompt);if(sentTaskId!==task.id||baselineAssistantCount<0){systemText.textContent='기존 task의 응답 기준점을 복구할 수 없습니다. 중복 전송을 막기 위해 대기합니다.';setTask('CLAIMED',task);return;}if(hasNewUserMessage(task.prompt,baselineUserMessages)||hasNewAssistantTurn()||(task.resource&&latestGeneratedResourceCandidates().length)){resumeClaimedResponse(task);return;}await deliverClaimedTask(task);}else if(phase==='WAIT_FOREGROUND'){await deliverClaimedTask(task);}setTask('CLAIMED',task);if(!sentTaskId)systemText.textContent='이미 전달된 작업입니다. 응답을 기다립니다.';}}catch(e){setStatus('status','Disconnected','offline');systemText.textContent='Worker bridge 오류: '+(e.message||e);}}
  let refreshInFlight=false, refreshQueued=false;
  async function refresh(autoBind=false){
    if(refreshInFlight){refreshQueued=refreshQueued||autoBind;return;}
    refreshInFlight=true;
    try{return await refreshNow(autoBind);}
    finally{refreshInFlight=false;if(refreshQueued){const queued=refreshQueued;refreshQueued=false;void refresh(queued);}}
  }
  async function loadSettings(){let saved=Object.assign({},DEFAULT,{managedRole:preflightManagedRole,managedRuntimeToken:preflightRuntimeToken});if(globalThis.chrome?.storage?.local)saved=await chrome.storage.local.get(Object.assign({},DEFAULT,{managedRole:preflightManagedRole,managedRuntimeToken:preflightRuntimeToken}));settings=validate(Object.assign({},DEFAULT,saved));const launchRole=managedRoleFromUrl();const launchToken=managedRuntimeTokenFromUrl();managedRole=launchRole||normalizeManagedRole(saved.managedRole)||preflightManagedRole;managedRuntimeToken=launchToken||String(saved.managedRuntimeToken||'').trim()||preflightRuntimeToken;if(!managedRole||!managedRuntimeToken)return;if(globalThis.chrome?.storage?.local&&(launchRole||launchToken))await chrome.storage.local.set({managedRole,managedRuntimeToken});host.style.display='none';stripManagedLaunchMarker();refresh();}
  function openSettings(){settingsForm.elements.bridgeHost.value=settings.bridgeHost;settingsForm.elements.bridgePort.value=settings.bridgePort;settingsForm.elements.bridgeBasePath.value=settings.bridgeBasePath;settingsModal.classList.remove('hidden');}
  root.querySelector('.settings').onclick=openSettings;root.querySelector('.settings-close').onclick=()=>settingsModal.classList.add('hidden');root.querySelector('.settings-cancel').onclick=()=>settingsModal.classList.add('hidden');root.querySelector('.bind-hq').onclick=()=>bind('HQ');root.querySelector('.bind-resource').onclick=()=>bind('RESOURCE');root.querySelector('.close').onclick=()=>{panel.classList.add('hidden');root.querySelector('.reopen').classList.add('visible')};root.querySelector('.reopen').onclick=()=>{panel.classList.remove('hidden');root.querySelector('.reopen').classList.remove('visible')};
  resourceRescan.onclick=()=>{if(!activeTaskId||!activeResource||phase==='RESULT_POST')return;reportProgress('RESOURCE_RESCAN','사용자가 현재 생성 결과 다시 수집을 요청했습니다.');enterWaitResponse('현재 RESOURCE 결과를 다시 수집합니다.');systemText.textContent='현재 생성 결과를 다시 확인하고 다운로드를 시도합니다.';};
  root.querySelector('.test-connection').onclick=async()=>{try{await fetchWithTimeout(url('status',validate(Object.fromEntries(new FormData(settingsForm)))));testStatus.textContent='Connected';testStatus.className='test-status success';}catch(e){testStatus.textContent=e.message;testStatus.className='test-status error';}};
  function clearComposer(){
    const input=composer();
    if(!input)return;
    input.focus({preventScroll:true});
    if(input.isContentEditable){
      input.textContent='';
      input.dispatchEvent(new InputEvent('input',{bubbles:true,inputType:'deleteContentBackward',data:null}));
    }else{
      const proto=Object.getPrototypeOf(input);
      const setter=Object.getOwnPropertyDescriptor(proto,'value')?.set;
      if(setter)setter.call(input,'');else input.value='';
      input.dispatchEvent(new InputEvent('input',{bubbles:true,inputType:'deleteContentBackward',data:null}));
    }
    input.dispatchEvent(new Event('change',{bubbles:true}));
  }
  function resetExtensionState(){
    try{
      for(let index=sessionStorage.length-1;index>=0;index--){
        const key=sessionStorage.key(index);
        if(key?.startsWith('gptweb-hub-task-'))sessionStorage.removeItem(key);
      }
    }catch{}
    clearResponseTimers();
    try{
      if(currentConversationId&&activeTaskId)sessionStorage.setItem('gptweb-hub-reset-task-'+currentConversationId,activeTaskId);
    }catch{}
    clearComposer();
    currentConversationId=null;
    lastBoundConversationId=null;
    activeResource=null;
    activeTaskOwner=null;
    currentProjectId='';
    activeTaskId=null;
    sentTaskId=null;
    activeLeaseId=null;
    phase='IDLE';
    sendTriggeredForActiveTask=false;
    responseStarted=false;
    responseStartedAt=0;
    lastResourceProgressKey='';
    stableSnapshot='';
    baselineAssistant='';
    baselineAssistantElement=null;
    baselineAssistantKey='';
    baselineAssistantCount=-1;
    baselineUserMessages=[];
    baselineImageSources=new Set();
    baselineFileUrls=new Set();
    pendingResult=null;
    lastProgressKey='';
    progressQueue=Promise.resolve();
    responseBlock.classList.add('hidden');
    webResponse.textContent='응답을 기다리고 있습니다.';
    workerMessage.textContent='대기 중';
    setTask('IDLE');
    systemText.textContent='확장 상태를 초기화했습니다.';
  }
  async function resetWorkerTask(){resetGeneration++;
    try{
      if(currentConversationId){
        await fetchWithTimeout(url('reset'),{
          method:'POST',
          headers:{'Content-Type':'application/json'},
          body:JSON.stringify({conversationId:currentConversationId,taskId:activeTaskId})
        },6000);
      }
    }catch{}
    resetExtensionState();
  }  settingsForm.onsubmit=async e=>{e.preventDefault();try{settings=validate(Object.fromEntries(new FormData(settingsForm)));if(globalThis.chrome?.storage?.local)await chrome.storage.local.set(settings);settingsModal.classList.add('hidden');refresh();}catch(e){testStatus.textContent=e.message;testStatus.className='test-status error';}}; root.querySelector('.reload-extension').onclick=async()=>{const button=root.querySelector('.reload-extension');button.disabled=true;systemText.textContent='업데이트 적용 중...';await resetWorkerTask();if(globalThis.chrome?.runtime?.sendMessage){chrome.runtime.sendMessage({type:'reload-extension'},()=>{if(chrome.runtime.lastError){systemText.textContent=chrome.runtime.lastError.message;button.disabled=false;return;}setTimeout(()=>location.reload(),500);});}else{location.reload();}};
  let last=location.href;const nav=()=>{if(location.href===last)return;const followBinding=!!currentConversationId&&lastBoundConversationId===currentConversationId;navigationGeneration++;last=location.href;activeTaskId=null;sentTaskId=null;activeLeaseId=null;activeResource=null;activeTaskOwner=null;resetResponseTracking();baselineAssistant='';baselineAssistantElement=null;baselineAssistantKey='';baselineAssistantCount=-1;baselineImageSources=new Set();baselineFileUrls=new Set();pendingResult=null;phase='IDLE';setTask('IDLE');refresh(followBinding)};const push=history.pushState;history.pushState=function(){const r=push.apply(this,arguments);nav();return r};const replace=history.replaceState;history.replaceState=function(){const r=replace.apply(this,arguments);nav();return r};addEventListener('popstate',nav);addEventListener('hashchange',nav);function observeConversationMutation(){if(activeTaskId&&['WAIT_SEND_READY','SEND_BUTTON_FIND','SEND_CONFIRM'].includes(phase)){const prompt=normalizeText(workerMessage?.textContent||'');const evidence=prompt?sendConfirmationEvidence(prompt):'';if(evidence&&['SEND_BUTTON_FIND','SEND_CONFIRM'].includes(phase)){reportProgress('SEND_MUTATION_CONFIRMED','DOM mutation에서 전송 증거를 즉시 확인했습니다. evidence='+evidence);enterWaitResponse('숨김 상태 DOM mutation에서 전송 확인 · '+evidence);return;}}if(phase==='WAIT_RESPONSE')observeResponse();}
new MutationObserver(observeConversationMutation).observe(document.body,{subtree:true,childList:true,characterData:true,attributes:true,attributeFilter:['data-message-author-role','data-message-id','data-testid']});loadSettings();setInterval(()=>refresh(),1500);setInterval(()=>{if(phase==='WAIT_RESPONSE')observeResponse();},1000);
})();
