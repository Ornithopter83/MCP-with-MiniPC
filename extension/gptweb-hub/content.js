(() => {
  const HOST_ID = 'gptweb-hub-extension-preview';
  const EXTENSION_VERSION = '0.1.6';
  const EXTENSION_BUILD = '2026-09-24.4';
  if (document.getElementById(HOST_ID)) return;
  const host = document.createElement('div');
  host.id = HOST_ID;
  host.style.all = 'initial';
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
    '<main class="task-section"><div class="section-label">TASK</div><article class="task-card" data-state="IDLE"><div class="task-main"><div class="task-icon">'+icon(icons.idle)+'</div><div class="task-copy"><h2 class="task-title">작업 없음</h2><p class="task-detail">현재 처리할 요청이 없습니다.</p></div><div class="spinner"></div></div><div class="task-meta"><span>Task</span><strong class="task-id">—</strong><span>|</span><span>From</span><strong class="task-from">—</strong></div><div class="message-block worker-message-block"><h3>Worker Message</h3><div class="worker-message">대기 중</div></div><div class="message-block response-block hidden"><h3>Web Response</h3><div class="web-response">응답을 기다리고 있습니다.</div></div></article></main>'+
    '<footer class="hub-footer">GPTWeb-Hub <span>v0.1</span></footer></section>'+
    '<section class="settings-modal hidden"><div class="settings-dialog"><div class="settings-title-row"><h2>GPTWeb-Hub Settings</h2><button class="settings-close">'+icon(icons.close)+'</button></div><form class="settings-form"><label>Host<input name="bridgeHost"></label><label>Port<input name="bridgePort" type="number"></label><label>Base Path<input name="bridgeBasePath"></label><div class="settings-test-row"><button class="secondary-button test-connection" type="button">Test Connection</button><span class="test-status">Not tested</span></div><div class="settings-actions"><button class="secondary-button settings-cancel" type="button">Cancel</button><button class="primary-button" type="submit">Save</button></div></form></div></section>'+
        '<button class="reopen" aria-label="Open GPTWeb-Hub">'+icon(icons.info)+'</button>';
  function statusRow(kind,label,value,action) { return '<div class="status-row" data-kind="'+kind+'"><strong>'+label+'</strong><span class="status-value status-pending">'+value+'</span>'+(action?'<button class="status-action web-connect" type="button">연결</button>':'')+'</div>'; }
  const style = root.querySelector('style');
  style.textContent = ':host{all:initial;font-family:Inter,system-ui,sans-serif;color:#101828}*{box-sizing:border-box}.hub-panel{position:fixed;top:88px;right:24px;width:428px;max-height:calc(100vh - 106px);overflow:hidden;background:#fff;border:9px solid #26323a;border-radius:19px;box-shadow:0 16px 40px #0f172a38;z-index:2147483646}.hub-panel.hidden{display:none}.hub-header{padding:20px 21px 14px}.title-row{display:flex;justify-content:space-between;align-items:center}h1,h2,h3,p{margin:0}h1{font-size:26px;font-weight:800}.header-actions{display:flex;gap:10px;align-items:center}.header-update{border:1px solid #cbd7e3;border-radius:7px;padding:5px 9px;color:#24364d;background:#fff;font:inherit;font-size:12px;font-weight:700;cursor:pointer}.header-update:hover{background:#f3f7fb}.icon-button{border:0;background:transparent;color:#637183;width:23px;height:23px;padding:0;cursor:pointer}.icon-button svg{width:100%;height:100%;fill:currentColor}.system-line{display:flex;gap:9px;align-items:center;margin-top:14px;color:#617084;font-size:15px}.system-icon{display:grid;place-items:center;width:20px;height:20px;border-radius:50%;background:#3288e8;color:#fff;font-weight:800}.status-list{border-top:1px solid #d8e0e7;padding:5px 21px 7px}.status-row{min-height:48px;display:grid;grid-template-columns:75px 1fr auto;align-items:center;gap:7px;border-bottom:1px solid #d8e0e7;font-size:16px}.status-row:last-child{border:0}.status-row[data-kind=web]{min-height:60px}.status-row strong{font-weight:800}.role-bind{display:flex;gap:7px;align-items:center;padding:8px 21px 10px;border-top:1px solid #d8e0e7}.role-binding{margin-right:auto;color:#617084;font-size:12px;font-weight:700}.status-value{white-space:pre-line;line-height:1.2;overflow:hidden;text-overflow:ellipsis}.status-ok{color:#078443;font-weight:700}.status-offline{color:#c53b3b;font-weight:700}.status-pending{color:#111827;font-weight:700}.status-action{border:1px solid #d5a34b;border-radius:6px;padding:4px 8px;color:#9a5b00;background:#fff8e8;font:inherit;font-size:12px;font-weight:700}.task-section{border-top:1px solid #d8e0e7;padding:19px 21px 20px}.section-label{color:#617084;font-size:14px;font-weight:800;margin-bottom:14px}.task-card{min-height:390px;border-radius:12px;padding:20px 18px 15px;background:#edf1f5}.task-card[data-tone=green]{background:#e6f7ee}.task-card[data-tone=blue]{background:#e5f1fe}.task-card[data-tone=amber]{background:#fff6d9}.task-main{display:flex;align-items:center;gap:15px;min-height:74px}.task-icon{width:56px;height:56px;flex:0 0 56px;padding:13px;border-radius:50%;color:#fff;background:#8b99a7}.task-icon svg{width:100%;height:100%;fill:currentColor}.task-card[data-tone=green] .task-icon{background:#0b9b5c}.task-card[data-tone=blue] .task-icon{background:#247bd4}.task-card[data-tone=amber] .task-icon{background:#f0ad00}.task-title{font-size:21px;font-weight:800}.task-detail{margin-top:8px;color:#536274;font-size:15px}.spinner{display:none;margin-left:auto;width:23px;height:23px;border:3px dotted #29445c;border-radius:50%;animation:spin 1.3s linear infinite}.spinner.visible{display:block}.task-meta{display:flex;gap:10px;margin-top:15px;color:#56687c;font-size:13px}.task-meta strong{font-weight:500}.message-block{margin-top:18px}.message-block h3{color:#617084;font-size:13px;font-weight:800;margin-bottom:7px}.worker-message,.web-response{max-height:140px;overflow-y:auto;padding:10px 12px;border-radius:8px;background:#fff8;white-space:pre-wrap;overflow-wrap:anywhere;color:#24364d;font-size:14px;line-height:1.45}.web-response{max-height:220px}.response-block{border-top:1px solid #ffffffaa;padding-top:14px}.hidden{display:none!important}.hub-footer{text-align:center;padding:0 21px 10px;color:#91a0b0;font-size:11px}.settings-modal{position:fixed;inset:0;display:grid;place-items:center;background:#0f172a47;z-index:2147483647;padding:16px}.settings-modal.hidden{display:none}.settings-dialog{width:380px;max-width:100%;background:#fff;border:3px solid #26323a;border-radius:14px;padding:18px}.settings-title-row{display:flex;justify-content:space-between}.settings-close{border:0;background:transparent;width:24px;height:24px}.settings-form{display:grid;gap:11px;margin-top:16px}.settings-form label{display:grid;gap:5px;font-size:13px;font-weight:700}.settings-form input{min-height:34px;border:1px solid #bdcad7;border-radius:7px;padding:6px 9px;font:inherit}.settings-test-row,.settings-actions{display:flex;justify-content:flex-end;gap:10px;align-items:center}.secondary-button,.primary-button{min-height:34px;border-radius:7px;padding:6px 12px;font:inherit;font-size:13px;font-weight:700}.secondary-button{border:1px solid #cbd7e3;background:#fff}.primary-button{border:1px solid #1475de;background:#1475de;color:#fff}.test-status{font-size:12px}.test-status.success{color:#078443}.test-status.error{color:#c53b3b}.reopen{display:none;position:fixed;top:100px;right:24px;z-index:2147483646;width:42px;height:42px;border:0;border-radius:50%;color:#fff;background:#26323a;padding:10px}.reopen.visible{display:block}@keyframes spin{to{transform:rotate(360deg)}}';
  const panel=root.querySelector('.hub-panel'), systemText=root.querySelector('.system-text'), systemIcon=root.querySelector('.system-icon'), card=root.querySelector('.task-card'), taskIcon=root.querySelector('.task-icon'), taskTitle=root.querySelector('.task-title'), taskDetail=root.querySelector('.task-detail'), taskId=root.querySelector('.task-id'), taskFrom=root.querySelector('.task-from'), spinner=root.querySelector('.spinner'), workerMessage=root.querySelector('.worker-message'), responseBlock=root.querySelector('.response-block'), webResponse=root.querySelector('.web-response'), settingsModal=root.querySelector('.settings-modal'), settingsForm=root.querySelector('.settings-form'), testStatus=root.querySelector('.test-status');
  const DEFAULT={bridgeHost:'127.0.0.1',bridgePort:43821,bridgeBasePath:'/bridge'};
  let settings=Object.assign({},DEFAULT), currentConversationId=null, lastBoundConversationId=null, currentProjectId='', activeTaskId=null, sentTaskId=null, activeLeaseId=null, activeResource=null, phase='IDLE', baselineAssistant='', baselineAssistantElement=null, baselineAssistantKey='', baselineAssistantCount=-1, baselineUserMessages=[], pendingResult=null, lastProgressKey='', activeTaskOwner=null, responseStarted=false, stableSnapshot='', progressQueue=Promise.resolve(); let resetGeneration=0, navigationGeneration=0; const DELIVERY_TIMEOUT=180000, COMPOSER_TIMEOUT=120000, SEND_CONFIRM_TIMEOUT=45000, RESOURCE_IMAGE_WAIT_TIMEOUT=120000;
  function validate(c){if(c.bridgeHost!=='127.0.0.1'&&c.bridgeHost!=='localhost')throw new Error('Loopback host only');if(!Number.isInteger(Number(c.bridgePort))||Number(c.bridgePort)<1||Number(c.bridgePort)>65535)throw new Error('Invalid port');return {bridgeHost:String(c.bridgeHost),bridgePort:Number(c.bridgePort),bridgeBasePath:'/'+String(c.bridgeBasePath||'/bridge').replace(/^\/|\/$/g,'')};}
  function url(path,cfg){const c=cfg||settings;return 'http://'+c.bridgeHost+':'+c.bridgePort+c.bridgeBasePath+'/'+path;} function reportProgress(stage,detail='',attempt=0){if(!activeTaskId||!currentConversationId)return;const key=stage+'|'+detail+'|'+attempt;if(key===lastProgressKey)return;lastProgressKey=key;const body=JSON.stringify({taskId:activeTaskId,conversationId:currentConversationId,leaseId:activeLeaseId||'',stage,detail,attempt});progressQueue=progressQueue.then(()=>fetchWithTimeout(url('progress'),{method:'POST',headers:{'Content-Type':'application/json'},body},8000)).catch(()=>{});} function setPhase(next,detail='',attempt=0){phase=next;reportProgress(next,detail,attempt);}
  async function fetchWithTimeout(resource,options={},timeout=4000){const controller=new AbortController();const timer=setTimeout(()=>controller.abort(),timeout);try{return await fetch(resource,{...options,signal:controller.signal});}finally{clearTimeout(timer);}}
  function saveTaskMemory(){if(!currentConversationId||!activeTaskId)return;sessionStorage.setItem('gptweb-hub-task-'+currentConversationId+'-'+activeTaskId,JSON.stringify({sentTaskId,activeLeaseId,baselineAssistant,baselineAssistantKey,baselineAssistantCount,baselineUserMessages}));}
function restoreTaskMemory(taskId,prompt=''){if(!currentConversationId)return false;try{const value=JSON.parse(sessionStorage.getItem('gptweb-hub-task-'+currentConversationId+'-'+taskId)||'null');if(!value)return false;sentTaskId=value.sentTaskId||null;activeLeaseId=value.activeLeaseId||null;baselineAssistant=value.baselineAssistant||'';baselineAssistantKey=value.baselineAssistantKey||'';baselineAssistantCount=Number.isInteger(value.baselineAssistantCount)?value.baselineAssistantCount:-1;baselineAssistantElement=baselineAssistantCount===assistantMessages().length?latestAssistantElement():null;if(Array.isArray(value.baselineUserMessages))baselineUserMessages=value.baselineUserMessages;else{const messages=userMessages(),expected=normalizeText(prompt),probe=expected.slice(0,Math.min(120,expected.length));let index=-1;for(let i=messages.length-1;i>=0;i--){if(messages[i]===expected||probe&&messages[i].includes(probe)){index=i;break;}}baselineUserMessages=index>=0?messages.filter((_,i)=>i!==index):messages;}return true;}catch{return false;}}
function setStatus(kind,value,tone){const e=root.querySelector('.status-row[data-kind="'+kind+'"] .status-value');if(!e)return;e.textContent=value;e.className='status-value status-'+tone;}
  function conversation(){const m=location.pathname.match(/\/c\/([a-zA-Z0-9-]+)/);return m?m[1]:null;}
  function title(){const raw=document.title.replace(/\s*[|—-]\s*ChatGPT.*$/i,'').trim()||'현재 대화';const match=raw.match(/^(.+?)\s+-\s+(.+)$/);return match?match[1].trim()+'\n'+match[2].trim():raw;}
  function setTask(state,t){const tones={IDLE:'',CLAIMED:'blue',PENDING:'blue',COMPLETED:'amber',FAILED:'amber'};card.dataset.state=state;card.dataset.tone=tones[state]||'';const active=state==='PENDING'||state==='CLAIMED';taskTitle.textContent=state==='IDLE'?'작업 없음':state==='COMPLETED'?'작업 종료':state==='FAILED'?'작업 종료':active?'WORKER → GPT WEB':'GPT WEB → WORKER';taskDetail.textContent=state==='IDLE'?'현재 처리할 요청이 없습니다.':state==='PENDING'?'요청 준비 중':state==='CLAIMED'?'ChatGPT 응답 생성 중':state==='COMPLETED'?(t?.finishReason||'정상 완료'):(t?.finishReason||'오류 발생 · 사용자 확인 필요');taskId.textContent=t?.id||'—';taskFrom.textContent=t?.owner||'—';spinner.classList.toggle('visible',active);if(t?.prompt)workerMessage.textContent=t.prompt;if(t?.result){responseBlock.classList.remove('hidden');webResponse.textContent=t.result;}else if(state==='IDLE'){responseBlock.classList.add('hidden');webResponse.textContent='응답을 기다리고 있습니다.';workerMessage.textContent='대기 중';}}
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
  function userMessages(){const selectors=['[data-message-author-role="user"]','[data-testid*="conversation-turn-user"]','article[data-testid*="conversation-turn"]'];const nodes=[...new Set(selectors.flatMap(selector=>[...document.querySelectorAll(selector)]))].filter(visible);return nodes.map(node=>normalizeText(node.innerText||node.textContent||'' )).filter(Boolean);} function latestUserMessage(){const messages=userMessages();return messages.length?messages[messages.length-1]:'';} function hasNewUserMessage(prompt,beforeMessages){const messages=userMessages();if(messages.length<=beforeMessages.length)return false;const expected=normalizeText(prompt);const probe=expected.slice(0,Math.min(120,expected.length));return messages.slice(beforeMessages.length).some(message=>message===expected||message.includes(probe)||(expected.includes(message)&&message.length>=Math.floor(expected.length*0.6)));}
  function userMessageMatches(prompt){const actual=normalizeText(latestUserMessage());const expected=normalizeText(prompt);if(!actual||!expected)return false;const probe=expected.slice(0,Math.min(120,expected.length));return actual.includes(probe)||expected.includes(actual)&&actual.length>=Math.floor(expected.length*0.6);}
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
      const response=await fetchWithTimeout(attachment.downloadUrl,{},120000);
      if(!response.ok)throw new Error('ATTACH_DOWNLOAD: '+attachment.fileName+' (HTTP '+response.status+')');
      const blob=await response.blob();
      const file=new File([blob],attachment.fileName||'attachment',{type:attachment.mimeType||blob.type||'application/octet-stream'});
      transfer.items.add(file);
    }
    input.files=transfer.files;
    input.dispatchEvent(new Event('input',{bubbles:true,composed:true}));
    input.dispatchEvent(new Event('change',{bubbles:true,composed:true}));
  }  async function sendToChatGPT(prompt,attachments){const input=await waitFor(composer,COMPOSER_TIMEOUT);if(!input)throw new Error('ChatGPT composer not found');setPhase('TEXT_INSERT','composer 대상 확인 · '+(input.id||input.getAttribute('data-testid')||input.tagName));input.focus({preventScroll:true});setText(input,prompt);if(!composerHasPrompt(input,prompt))throw new Error('COMPOSER_TEXT_INSERT_FAILED: 실제 ChatGPT 입력창에서 전달 문구를 확인하지 못했습니다.');if(attachments?.length){setPhase('TEXT_INSERT','첨부 파일을 준비하는 중');await attachFiles(input,attachments);}setPhase('SEND_BUTTON_FIND','입력 완료 · 전송 버튼 감시 중');void monitorSendReady(prompt);} async function monitorSendReady(prompt){
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
          setPhase('SEND_CONFIRM','Send 버튼 클릭 · 실제 전송 확인 중',attempt);
          const confirmed=await waitFor(()=>phase!=='SEND_CONFIRM'||hasNewUserMessage(prompt,baselineUserMessages)||hasNewAssistantTurn(),SEND_CONFIRM_TIMEOUT);
          if(phase!=='SEND_CONFIRM'||!activeTaskId)return;
          if(confirmed){setPhase('WAIT_RESPONSE','전송 확인 · Web 응답 시작 감시 중',attempt);observeResponse();return;}
          setPhase('SEND_BUTTON_FIND','전송 확인 실패 · 버튼 재탐색 중',attempt);
        }else if(voiceButton()){
          if(hasNewUserMessage(prompt,baselineUserMessages)||hasNewAssistantTurn()){setPhase('WAIT_RESPONSE','대화에 새 사용자 메시지 또는 assistant 응답을 확인했습니다.',attempt);observeResponse();return;}
          if(!voiceOnlySince)voiceOnlySince=Date.now();
          setPhase('SEND_BUTTON_FIND','입력은 되었지만 Voice 버튼만 표시됨 · 메시지는 아직 전송되지 않았습니다.',attempt);
          if(Date.now()-voiceOnlySince>=2250){
            const reason='Send 버튼이 없어 Worker 메시지를 전송하지 못했습니다. Voice 버튼만 표시되었습니다.';
            reportProgress('FAILED',reason,attempt);
            await failTask(reason);
            return;
          }
        }else{
          voiceOnlySince=0;
          setPhase('SEND_BUTTON_FIND','전송 버튼을 찾는 중',attempt);
        }
      }else{
        voiceOnlySince=0;
        setPhase('TEXT_INSERT','composer 입력 상태를 확인하는 중',attempt);
      }
      await new Promise(resolve=>setTimeout(resolve,750));
    }
    if(activeTaskId&&['WAIT_SEND_READY','SEND_BUTTON_FIND','SEND_CONFIRM'].includes(phase)){
      const reason='Web 전송 확인 시간이 초과되었습니다. 단계: '+phase;
      reportProgress('FAILED',reason,attempt);
      await failTask(reason);
    }
  }
  function assistantMessages(){return [...document.querySelectorAll('[data-message-author-role="assistant"]')];}
function latestAssistantElement(){const nodes=assistantMessages();return nodes.length?nodes[nodes.length-1]:null;}
function assistantMessageKey(node){return node?.getAttribute('data-message-id')||node?.id||node?.getAttribute('data-testid')||'';}
function latestAssistant(){return latestAssistantElement()?.innerText.trim()||'';}
function latestGeneratedImageCandidates(){const assistant=latestAssistantElement();if(!assistant)return [];const seen=new Set();return [...assistant.querySelectorAll('img')].filter(img=>{const src=img.currentSrc||img.src||'';if(!src||seen.has(src))return false;if(img.complete&&img.naturalWidth>0&&img.naturalHeight>0&&(img.naturalWidth<128||img.naturalHeight<128))return false;seen.add(src);return true;});}
function latestGeneratedImages(){return latestGeneratedImageCandidates().filter(img=>visible(img)&&img.complete&&img.naturalWidth>=128&&img.naturalHeight>=128);}
function responseSnapshot(){const images=activeResource?.type==='IMAGE'?latestGeneratedImageCandidates():[];return latestAssistant()+'|'+images.map(img=>[(img.currentSrc||img.src||''),img.complete?'1':'0',img.naturalWidth,img.naturalHeight].join(':')).join('|');}
async function fetchImageBytes(src){try{const response=await fetchWithTimeout(src,{credentials:'include'},120000);if(!response.ok)throw new Error('RESOURCE_IMAGE_DOWNLOAD_HTTP_'+response.status);const blob=await response.blob();if(!blob.size)throw new Error('RESOURCE_IMAGE_EMPTY');const buffer=new Uint8Array(await blob.arrayBuffer());let binary='';const chunk=0x8000;for(let i=0;i<buffer.length;i+=chunk)binary+=String.fromCharCode(...buffer.subarray(i,i+chunk));return {base64:btoa(binary),mimeType:blob.type||'image/png'};}catch(error){if(!/^https?:/i.test(src)||!globalThis.chrome?.runtime?.sendMessage)throw error;return await new Promise((resolve,reject)=>chrome.runtime.sendMessage({type:'fetch-resource-image',url:src},response=>{if(chrome.runtime.lastError){reject(new Error(chrome.runtime.lastError.message));return;}if(!response?.ok){reject(new Error(response?.error||String(error.message||error)));return;}resolve({base64:response.base64,mimeType:response.mimeType||'image/png'});}));}}
async function fetchImagePayload(image,index,total){const src=image.currentSrc||image.src;if(!src)throw new Error('RESOURCE_IMAGE_SRC_MISSING');reportProgress(index===0?'DOWNLOAD_START':'DOWNLOAD_PROGRESS',(index+1)+'/'+total+' 이미지 다운로드 중');const payload=await fetchImageBytes(src);reportProgress('DOWNLOAD_PROGRESS',(index+1)+'/'+total+' 이미지 다운로드 완료');return {base64:payload.base64,mimeType:payload.mimeType,fileName:'image-'+String(index+1).padStart(2,'0')+'.png'};}
async function imagePayloads(){const images=latestGeneratedImages();if(!images.length)throw new Error('RESOURCE_IMAGE_NOT_FOUND');const files=[];for(let i=0;i<images.length;i++)files.push(await fetchImagePayload(images[i],i,images.length));return files;}

function captureAssistantBaseline(){const nodes=assistantMessages();baselineAssistantElement=nodes.length?nodes[nodes.length-1]:null;baselineAssistant=baselineAssistantElement?.innerText.trim()||'';baselineAssistantKey=assistantMessageKey(baselineAssistantElement);baselineAssistantCount=nodes.length;}
function hasNewAssistantTurn(){const nodes=assistantMessages();if(!nodes.length)return false;if(baselineAssistantCount>=0&&nodes.length>baselineAssistantCount)return true;const latest=nodes[nodes.length-1],key=assistantMessageKey(latest);return (!!baselineAssistantElement&&latest!==baselineAssistantElement)||(!!baselineAssistantKey&&!!key&&key!==baselineAssistantKey);}
  async function failTask(reason,finishReason='send_failed'){const body={success:false,taskId:activeTaskId,conversationId:currentConversationId,leaseId:activeLeaseId,responseText:reason,resultType:activeResource?'RESOURCE_ERROR':'TEXT_RESULT',finishReason};try{const r=await fetchWithTimeout(url('task/'+activeTaskId+'/result'),{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)},10000);const p=await r.json();if(p.ok){phase='FINISHED';setTask('FAILED',p.data);return;}}catch{}phase='WAIT_WORKER';systemText.textContent='Worker 결과 전달 실패: '+reason;} function assistantStreaming(){return [...document.querySelectorAll('button,[role="button"],[data-is-streaming="true"],[data-streaming="true"],[aria-busy="true"]')].some(e=>{const state=e.getAttribute('data-is-streaming')||e.getAttribute('data-streaming')||e.getAttribute('aria-busy');if(state==='true')return true;if(!visible(e)||e.disabled||e.getAttribute('aria-disabled')==='true')return false;const label=[e.getAttribute('aria-label'),e.getAttribute('data-testid'),e.getAttribute('title')].filter(Boolean).join(' ');return /stop|중지|생성 중단/i.test(label);});}
  let stableTimer=0;
  function scheduleStableCheck(snapshot,delay,callback,allowStreaming=false){if(stableSnapshot===snapshot&&stableTimer)return;clearTimeout(stableTimer);stableSnapshot=snapshot;stableTimer=setTimeout(async()=>{stableTimer=0;if(phase!=='WAIT_RESPONSE'||!activeTaskId||responseSnapshot()!==snapshot||!hasNewAssistantTurn()||(!allowStreaming&&assistantStreaming()))return;await callback();},delay);}
  function observeResponse(){if(phase!=='WAIT_RESPONSE'||!activeTaskId||!hasNewAssistantTurn())return;const isImage=activeResource?.type==='IMAGE',text=latestAssistant(),candidates=isImage?latestGeneratedImageCandidates():[],images=isImage?latestGeneratedImages():[];if(!text&&!candidates.length)return;responseBlock.classList.remove('hidden');webResponse.textContent=text||(candidates.length?'생성 이미지 처리 중':'응답을 기다리고 있습니다.');if(!responseStarted){responseStarted=true;reportProgress('RESPONSE_START',isImage?'RESOURCE assistant 응답을 확인했습니다.':'새 assistant 응답을 확인했습니다.');}if(isImage){for(const candidate of candidates){if(!candidate.complete&&!candidate.dataset.projecthubLoadWatch){candidate.dataset.projecthubLoadWatch='1';candidate.addEventListener('load',()=>{candidate.dataset.projecthubLoadWatch='';observeResponse();},{once:true});candidate.addEventListener('error',()=>{candidate.dataset.projecthubLoadWatch='';observeResponse();},{once:true});}}const snapshot=responseSnapshot();if(images.length&&images.length===candidates.length){const delay=assistantStreaming()?30000:7000;scheduleStableCheck(snapshot,delay,async()=>{reportProgress('IMAGE_READY',images.length+'개 생성 이미지 확인');await submitResult(latestAssistant()||('RESOURCE images generated: '+images.length));},assistantStreaming());return;}scheduleStableCheck(snapshot,RESOURCE_IMAGE_WAIT_TIMEOUT,async()=>{const loaded=latestGeneratedImages(),all=latestGeneratedImageCandidates();if(loaded.length&&loaded.length===all.length){reportProgress('IMAGE_READY',loaded.length+'개 생성 이미지 확인');await submitResult(latestAssistant()||('RESOURCE images generated: '+loaded.length));return;}const reason='RESOURCE_IMAGE_NOT_GENERATED: assistant 응답은 완료되었지만 생성 이미지를 확인하지 못했습니다.\n'+latestAssistant();reportProgress('RESOURCE_NO_IMAGE','assistant 응답에서 생성 이미지를 확인하지 못했습니다.');await failTask(reason,'resource_image_not_generated');},true);return;}if(assistantStreaming()){clearTimeout(stableTimer);stableTimer=0;stableSnapshot='';return;}const snapshot=responseSnapshot();scheduleStableCheck(snapshot,5000,async()=>{reportProgress('RESPONSE_STABLE','응답이 안정되어 결과를 전달합니다.');await submitResult(latestAssistant());});}
  function resumeClaimedResponse(task){activeResource=task.resource||null;activeTaskOwner=task.owner||null;responseStarted=false;stableSnapshot='';clearTimeout(stableTimer);sentTaskId=task.id;activeLeaseId=task.leaseId||activeLeaseId||null;saveTaskMemory();setTask('PENDING',task);workerMessage.textContent=task.prompt;reportProgress('CLAIMED','전송된 대화의 응답 수신을 재개했습니다.');setPhase('WAIT_RESPONSE','기존 사용자 메시지를 확인해 응답 수신을 재개합니다.');systemText.textContent='ChatGPT 응답을 Worker로 전달하는 중입니다.';observeResponse();}
  async function submitResult(text){
    if((phase!=='WAIT_RESPONSE'&&phase!=='WAIT_WORKER')||!activeTaskId)return;
    setPhase('RESULT_POST','Worker에 Web 결과를 전달하는 중');
    pendingResult=text;
    setTask('CLAIMED',{id:activeTaskId,owner:activeTaskOwner||'WEB',prompt:workerMessage.textContent,result:text});
    const body={success:true,taskId:activeTaskId,conversationId:currentConversationId,leaseId:activeLeaseId,responseText:text,resultType:'TEXT_RESULT'};
    if(activeResource?.type==='IMAGE'){
      try{body.resultFiles=await imagePayloads();body.resultType='RESOURCE_IMAGES';}
      catch(error){const message=String(error.message||error);const finishReason=/DOWNLOAD_HTTP|RESOURCE_IMAGE_EMPTY|BACKGROUND_FETCH|URL_NOT_ALLOWED/i.test(message)?'resource_image_download_failed':'resource_image_capture_failed';await failTask('RESOURCE_IMAGE_CAPTURE: '+message,finishReason);return;}
    }
    let lastError=null;
    for(let attempt=0;attempt<3;attempt++){
      try{
        const r=await fetchWithTimeout(url('task/'+activeTaskId+'/result'),{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)},120000);
        const p=await r.json();
        if(!p.ok)throw new Error(p.data?.error||'result rejected');
        pendingResult=null;activeResource=null;setPhase('FINISHED','Worker 결과 전달 완료');setTask(p.data?.status==='FAILED'?'FAILED':'COMPLETED',p.data);return;
      }catch(error){lastError=error;reportProgress('RESULT_POST_RETRY','결과 전달 재시도 중: '+error.message,attempt+1);await new Promise(resolve=>setTimeout(resolve,500*(attempt+1)));}
    }
    systemText.textContent='Worker 결과 전달 실패: '+lastError.message+' · 재시도 대기';phase='WAIT_WORKER';
  }
  async function deliverClaimedTask(claimed){activeResource=claimed.resource||null;activeTaskOwner=claimed.owner||null;responseStarted=false;stableSnapshot='';clearTimeout(stableTimer);sentTaskId=claimed.id;activeLeaseId=claimed.leaseId||activeLeaseId||null;saveTaskMemory();setTask('PENDING',claimed);workerMessage.textContent=claimed.prompt;reportProgress('CLAIMED','Worker task를 확장이 수신했습니다.');setPhase('WAIT_SEND_READY','Worker Message를 전달하고 있습니다.');systemText.textContent='Worker Message를 전달하고 있습니다.';try{await sendToChatGPT(claimed.prompt,claimed.attachments||[]);}catch(error){if(String(error.message||error).startsWith('WEB_REQUIRES_FOREGROUND')){phase='WAIT_FOREGROUND';systemText.textContent='WEB_REQUIRES_FOREGROUND: ChatGPT 탭을 활성화하면 전송을 재개합니다.';setTask('CLAIMED',claimed);return;}systemText.textContent='Worker Message 전달 실패: '+error.message;phase='IDLE';sentTaskId=null;activeLeaseId=null;await fetchWithTimeout(url('task/'+claimed.id+'/result'),{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({success:false,taskId:claimed.id,conversationId:currentConversationId,leaseId:claimed.leaseId,responseText:error.message,resultType:'TEXT_RESULT',finishReason:'send_failed'})});}}async function bind(role){if(!currentConversationId)return;const response=await fetchWithTimeout(url('bind'),{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({conversationId:currentConversationId,projectId:currentProjectId,role})});const payload=await response.json();if(!payload.ok){systemText.textContent='역할 연결 실패: '+(payload.data?.error||'unknown');return;}refresh();}
  async function refreshNow(autoBind=false){const refreshGeneration=resetGeneration;const refreshNavigationGeneration=navigationGeneration;try{const previousConversationId=currentConversationId;const cid=conversation();currentConversationId=cid;const [sr,pr,tr,br]=await Promise.all([fetchWithTimeout(url('status')),fetchWithTimeout(url('projects')),fetchWithTimeout(url('task?conversationId='+(encodeURIComponent(cid||'__none__')))),cid?fetchWithTimeout(url('bindings/'+encodeURIComponent(cid))):Promise.resolve(null)]);if(!sr.ok||!pr.ok||!tr.ok)throw new Error('bridge unavailable');const status=await sr.json(),projects=await pr.json(),taskPayload=await tr.json(),binding=br?await br.json():null;if(refreshGeneration!==resetGeneration||refreshNavigationGeneration!==navigationGeneration||currentConversationId!==cid)return;const project=projects.data?.[0];let task=taskPayload.data?.task;if(task&&currentConversationId){const ignoredTaskId=sessionStorage.getItem('gptweb-hub-reset-task-'+currentConversationId);if(ignoredTaskId===task.id){task=null;}else if(ignoredTaskId){sessionStorage.removeItem('gptweb-hub-reset-task-'+currentConversationId);}}currentProjectId=project?.id||'';let bound=binding?.data?.bound?binding.data.projectId:'';const boundRole=binding?.data?.role||'';if(refreshNavigationGeneration!==navigationGeneration||currentConversationId!==cid)return;lastBoundConversationId=bound?cid:null;if(sr.ok) await fetchWithTimeout(url('heartbeat'),{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({client:'gptweb-hub',conversationId:cid,projectId:currentProjectId,conversationTitle:title(),extensionVersion:EXTENSION_VERSION,extensionBuild:EXTENSION_BUILD})});if(refreshNavigationGeneration!==navigationGeneration||currentConversationId!==cid)return;setStatus('web',title(),cid?'ok':'pending');setStatus('worker',status.data?.repository||'—',status.data?.repository?'ok':'pending');setStatus('status','Connected','ok');systemText.textContent='정상적으로 연결되어 있습니다.';const roleLabel=root.querySelector('.role-binding');roleLabel.textContent=boundRole?boundRole+' 연결됨':'역할 미연결';if(!cid){phase='IDLE';setTask('IDLE');systemText.textContent='현재 ChatGPT 대화를 식별할 수 없습니다.';return;}if(!bound){phase='IDLE';setTask('IDLE');systemText.textContent='현재 ChatGPT 대화를 HQ 또는 RESOURCE 역할로 명시적으로 연결하세요.';return;}if(!task){if(phase!=='WAIT_RESPONSE'&&phase!=='WAIT_WORKER')phase='IDLE';if(phase==='IDLE')setTask('IDLE');systemText.textContent='정상적으로 연결되어 있습니다.';return;}const previousTaskId=activeTaskId;activeTaskId=task.id;if(previousTaskId&&previousTaskId!==task.id){phase='IDLE';sentTaskId=null;activeTaskOwner=null;responseStarted=false;stableSnapshot='';clearTimeout(stableTimer);baselineAssistant='';baselineAssistantElement=null;baselineAssistantKey='';baselineAssistantCount=-1;baselineUserMessages=[];lastProgressKey='';}if(['COMPLETED','FAILED'].includes(task.status)){if(task.status==='FAILED'&&task.finishReason==='canceled'&&sentTaskId===task.id){const input=composer();if(input&&normalizeText(composerText(input))===normalizeText(task.prompt))clearComposer();}phase='FINISHED';setTask(task.status,task);return;}if(task.status==='PENDING'&&phase==='IDLE'&&!sentTaskId){captureAssistantBaseline();baselineUserMessages=userMessages();const claim=await fetchWithTimeout(url('task/'+task.id+'/claim'),{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({conversationId:currentConversationId})});const cp=await claim.json();if(cp.ok){await deliverClaimedTask(cp.data);}return;}if(task.status==='CLAIMED'){if(phase==='IDLE'){restoreTaskMemory(task.id,task.prompt);if(sentTaskId!==task.id||baselineAssistantCount<0){systemText.textContent='기존 task의 응답 기준점을 복구할 수 없습니다. 중복 전송을 막기 위해 대기합니다.';setTask('CLAIMED',task);return;}if(hasNewUserMessage(task.prompt,baselineUserMessages)){resumeClaimedResponse(task);return;}await deliverClaimedTask(task);}else if(phase==='WAIT_FOREGROUND'){await deliverClaimedTask(task);}setTask('CLAIMED',task);if(!sentTaskId)systemText.textContent='이미 전달된 작업입니다. 응답을 기다립니다.';}}catch(e){setStatus('status','Disconnected','offline');systemText.textContent='Worker bridge 오류: '+(e.message||e);}}
  let refreshInFlight=false, refreshQueued=false;
  async function refresh(autoBind=false){
    if(refreshInFlight){refreshQueued=refreshQueued||autoBind;return;}
    refreshInFlight=true;
    try{return await refreshNow(autoBind);}
    finally{refreshInFlight=false;if(refreshQueued){const queued=refreshQueued;refreshQueued=false;void refresh(queued);}}
  }
  async function loadSettings(){if(globalThis.chrome?.storage?.local){const saved=await chrome.storage.local.get(DEFAULT);settings=validate(Object.assign({},DEFAULT,saved));}refresh();}
  function openSettings(){settingsForm.elements.bridgeHost.value=settings.bridgeHost;settingsForm.elements.bridgePort.value=settings.bridgePort;settingsForm.elements.bridgeBasePath.value=settings.bridgeBasePath;settingsModal.classList.remove('hidden');}
  root.querySelector('.settings').onclick=openSettings;root.querySelector('.settings-close').onclick=()=>settingsModal.classList.add('hidden');root.querySelector('.settings-cancel').onclick=()=>settingsModal.classList.add('hidden');root.querySelector('.bind-hq').onclick=()=>bind('HQ');root.querySelector('.bind-resource').onclick=()=>bind('RESOURCE');root.querySelector('.close').onclick=()=>{panel.classList.add('hidden');root.querySelector('.reopen').classList.add('visible')};root.querySelector('.reopen').onclick=()=>{panel.classList.remove('hidden');root.querySelector('.reopen').classList.remove('visible')};
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
    clearTimeout(stableTimer);
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
    responseStarted=false;
    stableSnapshot='';
    baselineAssistant='';
    baselineAssistantElement=null;
    baselineAssistantKey='';
    baselineAssistantCount=-1;
    baselineUserMessages=[];
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
  let last=location.href;const nav=()=>{if(location.href===last)return;const followBinding=!!currentConversationId&&lastBoundConversationId===currentConversationId;navigationGeneration++;last=location.href;activeTaskId=null;sentTaskId=null;activeLeaseId=null;activeResource=null;activeTaskOwner=null;responseStarted=false;stableSnapshot='';clearTimeout(stableTimer);baselineAssistant='';baselineAssistantElement=null;baselineAssistantKey='';baselineAssistantCount=-1;pendingResult=null;phase='IDLE';setTask('IDLE');refresh(followBinding)};const push=history.pushState;history.pushState=function(){const r=push.apply(this,arguments);nav();return r};const replace=history.replaceState;history.replaceState=function(){const r=replace.apply(this,arguments);nav();return r};addEventListener('popstate',nav);addEventListener('hashchange',nav);new MutationObserver(observeResponse).observe(document.body,{subtree:true,childList:true,characterData:true});loadSettings();setInterval(()=>refresh(),1500);setInterval(()=>{if(phase==='WAIT_RESPONSE')observeResponse();},1000);
})();
