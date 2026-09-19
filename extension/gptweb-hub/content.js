(() => {
  const HOST_ID = 'gptweb-hub-extension-preview';
  if (document.getElementById(HOST_ID)) return;

  const host = document.createElement('div');
  host.id = HOST_ID;
  host.style.all = 'initial';
  document.documentElement.appendChild(host);

  function getIcon(name) {
    const paths = {
      gear: 'M19.43 12.98c.04-.32.07-.65-.07-.98l2.11-1.65-2-3.46-2.49 1a7.16 7.16 0 0 0-1.69-.98L15 3h-4l-.36 2.93c-.6.25-1.17.58-1.69.98l-2.49-1-2 3.46 2.11 1.65c-.04.32-.08.65-.08.98s.03.66.08.98l-2.11 1.65 2 3.46 2.49-1c.52.4 1.09.73 1.69.98L11 21h4l.36-2.93c.6-.25 1.17-.58 1.69-.98l2.49 1 2-3.46-2.11-1.65ZM13 15.5A3.5 3.5 0 1 1 13 8a3.5 3.5 0 0 1 0 7.5Z',
      close: 'M18.3 5.71 12 12l6.3 6.29-1.41 1.42L10.59 13.4 4.3 19.71 2.89 18.3 9.17 12 2.89 5.7 4.3 4.29l6.29 6.3 6.3-6.3 1.41 1.42Z',
      idle: 'M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20Z',
      arrowRight: 'M13 5v4h-2V5H3v14h8v-4h2v4h8V5h-8Zm-1 7 5-5v3h4v4h-4v3l-5-5Z',
      arrowLeft: 'M11 5v4h2V5h8v14h-8v-4h-2v4H3V5h8Zm1 7-5-5v3H3v4h4v3l-5-5Z',
      check: 'm9 16.17-3.88-3.88-1.41 1.42L9 19 21 7l-1.41-1.41L9 16.17Z',
      hub: 'M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20Zm1 15h-2v-2h2v2Zm2.07-7.25-.9.92C13.45 11.4 13 12 13 13h-2v-.5c0-.8.45-1.55 1.17-2.28l1.24-1.26A1.97 1.97 0 0 0 14 7.5 2 2 0 0 0 10 7H8a4 4 0 0 1 8 0c0 1.1-.45 2.1-1.93 2.75Z'
    };
    return `<svg viewBox="0 0 24 24" aria-hidden="true"><path d="${paths[name]}"/></svg>`;
  }

  function getStyles() {
    return '';
  }
  const root = host.attachShadow({ mode: 'open' });
  root.innerHTML = `
    <style>
      ${getStyles()}
    </style>
    <section class="hub-panel" aria-label="GPTWeb-Hub">
      <header class="hub-header">
        <div class="title-row">
          <h1>GPTWeb-Hub</h1>
          <div class="header-actions">
            <button class="icon-button settings" title="Open Settings" aria-label="Open Settings">${getIcon("gear")}</button>
            <button class="icon-button close" title="Hide panel" aria-label="Hide panel">${getIcon("close")}</button>
          </div>
        </div>
        <p class="system-line"><span class="system-icon">i</span><span class="system-text">정상적으로 연결되어 있습니다.</span></p>
      </header>

      <div class="status-list" aria-label="Connection status">
        ${statusRow('web', 'GPT Web', '—')}
        ${statusRow('worker', 'Worker', '—')}
        ${statusRow('status', 'Status', 'Disconnected')}
      </div>

      <main class="request-section">
        <div class="section-label">CURRENT REQUEST</div>
        <article class="request-card" data-state="IDLE">
          <div class="request-main">
            <div class="request-icon">${getIcon("idle")}</div>
            <div class="request-copy">
              <h2>작업 없음</h2>
              <p>현재 처리할 요청이 없습니다.</p>
            </div>
            <div class="spinner" aria-hidden="true"></div>
          </div>
          <div class="request-meta"><span>Task</span><strong>—</strong><span>|</span><span>From</span><strong>—</strong></div>
          <div class="request-composer">
            <textarea class="request-input" rows="3" placeholder="작업 지시를 입력하세요..." disabled></textarea>
            <div class="request-drop-zone" aria-disabled="true">파일을 여기에 드래그하세요</div>
            <div class="request-composer-footer"><span class="request-file-names"></span><button class="request-send" type="button" disabled>전송</button></div>
          </div>
        </article>
      </main>

      <section class="result-section" aria-label="GPT Web result">
        <div class="section-label">RESULT MESSAGE</div>
        <article class="result-message"><h2 class="result-title">GPT Web 응답</h2><p class="result-body">응답을 기다리고 있습니다.</p></article>
      </section>

      <footer class="hub-footer">GPTWeb-Hub <span>v0.1 UI Preview</span></footer>
    </section>
    <section class="settings-modal hidden" aria-label="GPTWeb-Hub Settings">
      <div class="settings-dialog" role="dialog" aria-modal="true" aria-labelledby="settings-title">
        <div class="settings-title-row">
          <h2 id="settings-title">GPTWeb-Hub Settings</h2>
          <button class="settings-close" type="button" aria-label="Close Settings">${getIcon("close")}</button>
        </div>
        <div class="settings-divider"></div>
        <form class="settings-form">
          <div class="settings-heading">WORKER BRIDGE</div>
          <label>Host<input name="bridgeHost" type="text" autocomplete="off"></label>
          <label>Port<input name="bridgePort" type="number" min="1" max="65535" inputmode="numeric"></label>
          <label>BasePath<input name="bridgeBasePath" type="text" autocomplete="off"></label>
          <label>Worker Path <span>(optional)</span><input name="workerPath" type="text" autocomplete="off"></label>
          <div class="settings-test-row">
            <button class="secondary-button test-connection" type="button">Test Connection</button>
            <span class="test-status" role="status">Not tested</span>
          </div>
          <div class="settings-actions">
            <button class="secondary-button settings-cancel" type="button">Cancel</button>
            <button class="primary-button settings-save" type="submit">Save</button>
          </div>
        </form>
      </div>
    </section>
    <button class="reopen" title="Show GPTWeb-Hub" aria-label="Show GPTWeb-Hub">${getIcon("hub")}</button>
  `;

  const states = [
    {
      key: 'IDLE',
      system: '정상적으로 연결되어 있습니다.',
      icon: getIcon("idle"),
      title: '작업 없음',
      detail: '현재 처리할 요청이 없습니다.',
      task: '—',
      from: '—',
      tone: 'idle'
    },
    {
      key: 'WORKER_TO_WEB',
      system: '검토 결과를 Worker로 전달하고 있습니다.',
      icon: getIcon("arrowRight"),
      title: 'GPT Web → Worker',
      detail: '검토 결과 전달 중',
      task: 'PH-1042',
      from: 'WEB',
      tone: 'green',
      progress: true
    },
    {
      key: 'WEB_TO_WORKER',
      system: 'ChatGPT에서 응답을 생성하고 있습니다.',
      icon: getIcon("arrowLeft"),
      title: 'Worker → GPT Web',
      detail: 'ChatGPT 응답 생성 중',
      task: 'PH-1042',
      from: 'CODEX',
      tone: 'blue',
      progress: true
    },
    {
      key: 'FINISHED',
      system: '작업이 완료되었습니다. 확인이 필요할 수 있습니다.',
      icon: getIcon("check"),
      title: '작업 종료',
      detail: '사용자 확인 필요',
      task: 'PH-1042',
      from: 'CODEX',
      tone: 'amber'
    }
  ];

  let stateIndex = 0;
  const panel = root.querySelector('.hub-panel');
  const card = root.querySelector('.request-card');
  const systemText = root.querySelector('.system-text');
  const systemIcon = root.querySelector('.system-icon');
  const requestInput = root.querySelector('.request-input');
  const requestDropZone = root.querySelector('.request-drop-zone');
  const requestFileNames = root.querySelector('.request-file-names');
  const requestSend = root.querySelector('.request-send');
  const resultTitle = root.querySelector('.result-title');
  const resultBody = root.querySelector('.result-body');
  const requestIcon = root.querySelector('.request-icon');
  const title = root.querySelector('.request-copy h2');
  const detail = root.querySelector('.request-copy p');
  const task = root.querySelector('.request-meta strong:nth-of-type(1)');
  const from = root.querySelector('.request-meta strong:nth-of-type(2)');
  const spinner = root.querySelector('.spinner');

  const DEFAULT_SETTINGS = {
    bridgeHost: '127.0.0.1',
    bridgePort: 43821,
    bridgeBasePath: '/bridge',
    workerPath: ''
  };
  let settings = { ...DEFAULT_SETTINGS };
  const settingsModal = root.querySelector('.settings-modal');
  const settingsForm = root.querySelector('.settings-form');
  const settingsClose = root.querySelector('.settings-close');
  const settingsCancel = root.querySelector('.settings-cancel');
  const testConnection = root.querySelector('.test-connection');
  const testStatus = root.querySelector('.test-status');

  const webConnect = root.querySelector('.web-connect');
  let currentConversationId = null;
  let currentProjectId = '';
  let pendingFiles = [];
  let lastAssistantMessage = '';
  let requestInFlight = false;

  function getConversationId() {
    const match = window.location.pathname.match(/\/c\/([^/?#]+)/);
    return match ? decodeURIComponent(match[1]) : null;
  }

  function getConversationTitle(conversationId) {
    const title = document.title.replace(/\s*[-|]\s*ChatGPT\s*$/i, '').trim();
    if (title && !/^ChatGPT$/i.test(title)) {
      const separator = title.match(/^(.+?)\s+-\s+(.+)$/);
      return separator ? separator[1].trim() + '\n' + separator[2].trim() : title;
    }
    return conversationId ? 'ChatGPT 대화 ' + conversationId.slice(0, 8) : '';
  }


  function visibleElement(element) {
    return element && element.getClientRects().length > 0 && getComputedStyle(element).visibility !== 'hidden';
  }

  function findChatComposer() {
    const selectors = [
      '[contenteditable="true"][role="textbox"]',
      '[contenteditable="true"]',
      'textarea'
    ];
    for (const selector of selectors) {
      const element = [...document.querySelectorAll(selector)].find(visibleElement);
      if (element) return element;
    }
    return null;
  }

  function setComposerText(element, value) {
    element.focus();
    if (element instanceof HTMLTextAreaElement || element instanceof HTMLInputElement) {
      const setter = Object.getOwnPropertyDescriptor(element.constructor.prototype, 'value')?.set;
      setter?.call(element, value);
      element.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: value }));
      return;
    }
    const selection = window.getSelection();
    const range = document.createRange();
    range.selectNodeContents(element);
    selection?.removeAllRanges();
    selection?.addRange(range);
    document.execCommand('insertText', false, value);
    element.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: value }));
  }
  function createFileTransfer(files) {
    const transfer = new DataTransfer();
    files.forEach(file => transfer.items.add(file));
    return transfer;
  }

function acceptsFile(input, file) {
    if (!input.accept) return true;
    return input.accept.split(',').some(token => {
      const accepted = token.trim().toLowerCase();
      return accepted === '*/*' || accepted === file.type.toLowerCase() || (accepted.endsWith('/*') && file.type.toLowerCase().startsWith(accepted.slice(0, -1)));
    });
  }
  function attachFilesToChat(composer, files) {
    const transfer = createFileTransfer(files);
    const fileInput = [...document.querySelectorAll('input[type="file"]')]
      .find(input => [...files].every(file => acceptsFile(input, file)));
    if (fileInput) {
      fileInput.files = transfer.files;
      fileInput.dispatchEvent(new Event('change', { bubbles: true }));
      return;
    }
    const pasteEvent = new ClipboardEvent('paste', {
      bubbles: true,
      cancelable: true,
      clipboardData: transfer
    });
    composer.dispatchEvent(pasteEvent);
    if (!pasteEvent.defaultPrevented) {
      composer.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: transfer }));
    }
  }

  function findChatSendButton() {
    const selectors = [
      'button[data-testid="send-button"]',
      'button[aria-label*="Send"]',
      'button[aria-label*="보내"]',
      'button[title*="Send"]'
    ];
    return selectors.flatMap(selector => [...document.querySelectorAll(selector)]).find(visibleElement);
  }

  function updateFileNames() {
    requestFileNames.textContent = pendingFiles.map(file => file.name).join(', ');
  }

  function updateRequestControls() {
    const enabled = stateIndex === 0 && Boolean(currentConversationId) && !requestInFlight;
    requestInput.disabled = !enabled;
    requestSend.disabled = !enabled;
    requestDropZone.classList.toggle('disabled', !enabled);
    requestDropZone.setAttribute('aria-disabled', String(!enabled));
  }

  function handleRequestDrop(event) {
    event.preventDefault();
    if (stateIndex !== 0 || !currentConversationId) return;
    pendingFiles = [...event.dataTransfer.files];
    updateFileNames();
    requestDropZone.classList.remove('dragging');
  }

  async function sendRequestToChat() {
    if (stateIndex !== 0 || !currentConversationId) return;
    const message = requestInput.value.trim();
    if (!message && pendingFiles.length === 0) {
      systemText.textContent = '전송할 문구나 파일을 입력하세요.';
      return;
    }
    const composer = findChatComposer();
    if (!composer) {
      systemText.textContent = 'ChatGPT 입력창을 찾을 수 없습니다.';
      return;
    }
    requestSend.disabled = true;
    requestInput.disabled = true;
    try {
      if (message) setComposerText(composer, message);
      if (pendingFiles.length) attachFilesToChat(composer, pendingFiles);
      await new Promise(resolve => window.setTimeout(resolve, 400));
      const sendButton = findChatSendButton();
      if (!sendButton || sendButton.disabled) throw new Error('ChatGPT 전송 버튼을 찾을 수 없습니다.');
      requestInFlight = true;
      sendButton.click();
      resultTitle.textContent = 'GPT Web 응답';
      resultBody.textContent = '응답을 기다리고 있습니다.';
      systemText.textContent = '현재 GPT Web 대화에 요청을 전송했습니다.';
      requestInput.value = '';
      pendingFiles = [];
      updateFileNames();
    } catch (error) {
      requestInFlight = false;
      systemText.textContent = error.message;
    } finally {
      updateRequestControls();
    }
  }

  function updateLatestAssistantMessage() {
    const messages = [...document.querySelectorAll('[data-message-author-role="assistant"]')]
      .filter(visibleElement);
    const latest = messages.at(-1);
    const text = latest?.innerText?.trim() || latest?.textContent?.trim() || '';
    if (!text || text === lastAssistantMessage) return;
    lastAssistantMessage = text;
    requestInFlight = false;
    resultTitle.textContent = 'GPT Web 응답';
    resultBody.textContent = text;
  }

  async function bindCurrentConversation() {
    if (!currentConversationId || !currentProjectId) {
      systemText.textContent = '현재 대화를 식별하거나 연결할 프로젝트가 없습니다.';
      return;
    }
    webConnect.disabled = true;
    webConnect.textContent = '연결 중...';
    try {
      const response = await fetch(bridgeUrl('bind'), {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          conversationId: currentConversationId,
          projectId: currentProjectId
        })
      });
      if (!response.ok) throw new Error('Conversation binding failed');
      const payload = await response.json();
      if (payload.ok === false) throw new Error(payload.data?.error || 'Conversation binding failed');
      await refreshBridge();
    } catch (error) {
      systemText.textContent = error.message;
      webConnect.disabled = false;
      webConnect.textContent = '연결';
    }
  }
  function bridgeUrl(path, config = settings) {
    const host = config.bridgeHost === '::1' ? '[::1]' : config.bridgeHost;
    const basePath = config.bridgeBasePath.replace(/\/+$/, '');
    return 'http://' + host + ':' + config.bridgePort + basePath + '/' + path;
  }

  function validateSettings(config) {
    const host = config.bridgeHost.trim().toLowerCase();
    if (!['127.0.0.1', 'localhost', '::1'].includes(host)) {
      throw new Error('Host must be loopback');
    }
    const port = Number(config.bridgePort);
    if (!Number.isInteger(port) || port < 1 || port > 65535) {
      throw new Error('Invalid port');
    }
    if (!config.bridgeBasePath.startsWith('/') || config.bridgeBasePath.includes('..')) {
      throw new Error('Invalid base path');
    }
    return {
      bridgeHost: host,
      bridgePort: port,
      bridgeBasePath: config.bridgeBasePath.replace(/\/+$/, '') || '/bridge',
      workerPath: config.workerPath.trim()
    };
  }

  function formSettings() {
    const data = new FormData(settingsForm);
    return {
      bridgeHost: String(data.get('bridgeHost') || ''),
      bridgePort: Number(data.get('bridgePort')),
      bridgeBasePath: String(data.get('bridgeBasePath') || ''),
      workerPath: String(data.get('workerPath') || '')
    };
  }

  function fillSettings(config) {
    for (const [name, value] of Object.entries(config)) {
      const input = settingsForm.elements.namedItem(name);
      if (input) input.value = value;
    }
  }

  async function loadSettings() {
    if (!globalThis.chrome?.storage?.local) return;
    const saved = await chrome.storage.local.get(DEFAULT_SETTINGS);
    try {
      settings = validateSettings({ ...DEFAULT_SETTINGS, ...saved });
    } catch {
      settings = { ...DEFAULT_SETTINGS };
    }
  }

  async function testBridge(config) {
    const response = await fetch(bridgeUrl('status', config));
    if (!response.ok) throw new Error('Connection refused');
    const payload = await response.json();
    const status = payload.data || payload;
    if (status.bridge !== 'ready' || status.loopback !== true) {
      throw new Error('Invalid bridge response');
    }
    return status;
  }

  function showSettings() {
    fillSettings(settings);
    testStatus.textContent = 'Not tested';
    testStatus.className = 'test-status';
    settingsModal.classList.remove('hidden');
  }

  function hideSettings() {
    settingsModal.classList.add('hidden');
  }

  async function saveSettings(event) {
    event.preventDefault();
    try {
      const next = validateSettings(formSettings());
      if (globalThis.chrome?.storage?.local) await chrome.storage.local.set(next);
      settings = next;
      hideSettings();
      await refreshBridge();
    } catch (error) {
      testStatus.textContent = error.message;
      testStatus.className = 'test-status error';
    }
  }

  async function runConnectionTest() {
    try {
      const candidate = validateSettings(formSettings());
      testStatus.textContent = 'Testing...';
      testStatus.className = 'test-status pending';
      await testBridge(candidate);
      testStatus.textContent = 'Connected';
      testStatus.className = 'test-status success';
    } catch (error) {
      testStatus.textContent = error.message;
      testStatus.className = 'test-status error';
    }
  }
  function setStatusValue(kind, value, tone) {
    const element = root.querySelector('.status-row[data-kind="' + kind + '"] .status-value');
    if (!element) return;
    element.textContent = value;
    element.classList.remove('status-ok', 'status-offline', 'status-pending');
    element.classList.add('status-' + tone);
  }
  async function refreshBridge() {
    try {
      const conversationId = getConversationId();
      currentConversationId = conversationId;
      const bindingResponse = conversationId
        ? fetch(bridgeUrl('bindings/' + encodeURIComponent(conversationId)))
        : Promise.resolve(null);
      const taskUrl = bridgeUrl('task') + (conversationId ? '?conversationId=' + encodeURIComponent(conversationId) : '?conversationId=__none__');
      const [statusResponse, projectsResponse, taskResponse, bindingResult] = await Promise.all([
        fetch(bridgeUrl('status')),
        fetch(bridgeUrl('projects')),
        fetch(taskUrl),
        bindingResponse
      ]);
      if (!statusResponse.ok || !projectsResponse.ok || !taskResponse.ok || (bindingResult && !bindingResult.ok)) {
        throw new Error('bridge unavailable');
      }
      const status = await statusResponse.json();
      const projects = await projectsResponse.json();
      const pending = await taskResponse.json();
      const binding = bindingResult ? await bindingResult.json() : null;
      const bindingData = binding?.data;
      const availableProject = projects.data?.[0];
      const boundProjectId = bindingData?.bound ? bindingData.projectId : '';
      const bridgeTask = pending.data?.task;
      const conversationTitle = getConversationTitle(conversationId);
      const workerName = typeof status.data?.repository === 'string' ? status.data.repository.trim() : (typeof availableProject?.name === 'string' ? availableProject.name.trim() : '');
      currentProjectId = availableProject?.id || '';
      setStatusValue('web', conversationTitle || '—', conversationTitle ? 'ok' : 'pending');
      setStatusValue('worker', workerName || '—', workerName ? 'ok' : 'pending');
      setStatusValue('status', 'Connected', 'ok');
      if (webConnect) {
        webConnect.hidden = !conversationId || Boolean(boundProjectId);
        webConnect.disabled = false;
        webConnect.textContent = boundProjectId ? '연결됨' : '연결';
      }
      if (bridgeTask) {
        stateIndex = ['COMPLETED', 'FAILED'].includes(bridgeTask.status) ? 3 : bridgeTask.status === 'CLAIMED' ? 2 : 1;
        renderState();
        task.textContent = bridgeTask.id;
        from.textContent = bridgeTask.status;
      } else {
        stateIndex = 0;
        renderState();
      }
      if (!conversationId) {
        systemText.textContent = '현재 ChatGPT 대화를 식별할 수 없습니다.';
      } else if (!boundProjectId) {
        systemText.textContent = '현재 GPT Web 대화를 Worker 프로젝트와 연결하세요.';
      } else if (!bridgeTask) {
        systemText.textContent = '정상적으로 연결되어 있습니다.';
      }
    } catch {
      setStatusValue('status', 'Disconnected', 'offline');
      systemText.textContent = 'Worker bridge에 연결할 수 없습니다.';
      if (webConnect) {
        webConnect.hidden = !getConversationId();
        webConnect.disabled = false;
        webConnect.textContent = '연결';
      }
    }
  }  function renderState() {
    const state = states[stateIndex];
    card.dataset.state = state.key;
    card.dataset.tone = state.tone;
    systemText.textContent = state.system;
    systemIcon.textContent = state.key === 'FINISHED' ? '!' : 'i';
    systemIcon.classList.toggle('warning', state.key === 'FINISHED');
    requestIcon.innerHTML = state.icon;
    title.textContent = state.title;
    detail.textContent = state.detail;
    task.textContent = state.task;
    from.textContent = state.from;
    spinner.classList.toggle('visible', Boolean(state.progress));
     updateRequestControls();
  }

  let lastLocation = window.location.href;
  function refreshAfterNavigation() {
    const nextLocation = window.location.href;
    if (nextLocation === lastLocation) return;
    lastLocation = nextLocation;
    stateIndex = 0;
    requestInFlight = false;
    pendingFiles = [];
    updateFileNames();
    renderState();
    refreshBridge();
  }

  const originalPushState = history.pushState;
  history.pushState = function (...args) {
    const result = originalPushState.apply(this, args);
    refreshAfterNavigation();
    return result;
  };
  const originalReplaceState = history.replaceState;
  history.replaceState = function (...args) {
    const result = originalReplaceState.apply(this, args);
    refreshAfterNavigation();
    return result;
  };
  window.addEventListener('popstate', refreshAfterNavigation);
  window.addEventListener('hashchange', refreshAfterNavigation);
  root.querySelector('.settings').addEventListener('click', showSettings);
  settingsClose.addEventListener('click', hideSettings);
  settingsCancel.addEventListener('click', hideSettings);
  testConnection.addEventListener('click', runConnectionTest);
  settingsForm.addEventListener('submit', saveSettings);
  webConnect?.addEventListener('click', bindCurrentConversation);
  requestSend?.addEventListener('click', sendRequestToChat);
  requestDropZone?.addEventListener('dragover', event => {
    if (stateIndex !== 0 || !currentConversationId) return;
    event.preventDefault();
    requestDropZone.classList.add('dragging');
  });
  requestDropZone?.addEventListener('dragleave', () => requestDropZone.classList.remove('dragging'));
  requestDropZone?.addEventListener('drop', handleRequestDrop);

  root.querySelector('.close').addEventListener('click', () => {
    panel.classList.add('hidden');
    root.querySelector('.reopen').classList.add('visible');
  });

  root.querySelector('.reopen').addEventListener('click', () => {
    panel.classList.remove('hidden');
    root.querySelector('.reopen').classList.remove('visible');
  });

const responseObserver = new MutationObserver(() => updateLatestAssistantMessage());
  responseObserver.observe(document.body, { subtree: true, childList: true, characterData: true });
  updateLatestAssistantMessage();
  function statusRow(kind, label, value) {
    const action = kind === 'web' ? '<button class="status-action web-connect" type="button">연결</button>' : '';
    return '<div class="status-row" data-kind="' + kind + '"><strong>' + label + '</strong><span class="status-value">' + value + '</span>' + action + '</div>';
  }
  function svg(path, extra = '') {
    return `<svg viewBox="0 0 24 24" aria-hidden="true" ${extra}><path d="${path}"/></svg>`;
  }

  const gearIcon = svg('M19.43 12.98c.04-.32.07-.65.07-.98s-.02-.66-.07-.98l2.11-1.65-2-3.46-2.49 1a7.16 7.16 0 0 0-1.69-.98L15 3h-4l-.36 2.93c-.6.25-1.17.58-1.69.98l-2.49-1-2 3.46 2.11 1.65c-.04.32-.08.65-.08.98s.03.66.08.98l-2.11 1.65 2 3.46 2.49-1c.52.4 1.09.73 1.69.98L11 21h4l.36-2.93c.6-.25 1.17-.58 1.69-.98l2.49 1 2-3.46-2.11-1.65ZM13 15.5A3.5 3.5 0 1 1 13 8a3.5 3.5 0 0 1 0 7.5Z');
  const closeIcon = svg('M18.3 5.71 12 12l6.3 6.29-1.41 1.42L10.59 13.4 4.3 19.71 2.89 18.3 9.17 12 2.89 5.7 4.3 4.29l6.29 6.3 6.3-6.3 1.41 1.42Z');
  const idleIcon = svg('M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20Z', 'class="idle-svg"');
  const arrowRightIcon = svg('M13 5v4h-2V5H3v14h8v-4h2v4h8V5h-8Zm-1 7 5-5v3h4v4h-4v3l-5-5Z');
  const arrowLeftIcon = svg('M11 5v4h2V5h8v14h-8v-4h-2v4H3V5h8Zm1 7-5-5v3H3v4h4v3l5-5Z');
  const checkIcon = svg('m9 16.17-3.88-3.88-1.41 1.42L9 19 21 7l-1.41-1.41L9 16.17Z');
  const hubIcon = svg('M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20Zm1 15h-2v-2h2v2Zm2.07-7.25-.9.92C13.45 11.4 13 12 13 13h-2v-.5c0-.8.45-1.55 1.17-2.28l1.24-1.26A1.97 1.97 0 0 0 14 7.5 2 2 0 0 0 10 7H8a4 4 0 0 1 8 0c0 1.1-.45 2.1-1.93 2.75Z');

  const styles = `
    :host { all: initial; font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; color: #101828; }
    * { box-sizing: border-box; }
    .hub-panel { position: fixed; top: 88px; right: 24px; width: 428px; overflow: hidden; background: #fff; border: 9px solid #26323a; border-radius: 19px; box-shadow: 0 16px 40px rgba(15, 23, 42, .22), 0 3px 8px rgba(15, 23, 42, .12); z-index: 2147483646; transition: opacity .2s, transform .2s; }
    .hub-panel.hidden { opacity: 0; transform: translateX(24px); pointer-events: none; }
    .hub-header { padding: 20px 21px 14px; background: #fff; }
    .title-row { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
    h1, h2, p { margin: 0; }
    h1 { font-size: 26px; line-height: 1; letter-spacing: -.8px; font-weight: 800; }
    .header-actions { display: flex; gap: 13px; align-items: center; }
    .icon-button { appearance: none; border: 0; background: transparent; color: #637183; padding: 0; width: 23px; height: 23px; cursor: pointer; display: grid; place-items: center; }
    .icon-button:hover { color: #132238; }
    svg { width: 100%; height: 100%; fill: currentColor; display: block; }
    .system-line { display: flex; align-items: center; gap: 9px; margin-top: 14px; color: #617084; font-size: 15px; font-weight: 500; }
    .system-icon { display: grid; place-items: center; width: 20px; height: 20px; border-radius: 50%; background: #3288e8; color: #fff; font-size: 13px; font-weight: 800; font-family: Georgia, serif; }
    .system-icon.warning { background: #f1aa00; }
    .status-list { border-top: 1px solid #d8e0e7; padding: 5px 21px 7px; }
    .status-row { min-height: 48px; display: grid; grid-template-columns: 75px 1fr auto; align-items: center; column-gap: 7px; border-bottom: 1px solid #d8e0e7; font-size: 16px; }
    .status-row:last-child { border-bottom: 0; }
    .status-row strong { font-size: 16px; font-weight: 800; }
    .status-row > span { color: #18253a; white-space: pre-line; line-height: 1.2; overflow: hidden; text-overflow: ellipsis; } .status-row .status-value.status-ok { color: #078443; font-weight: 700; } .status-row .status-value.status-offline { color: #c53b3b; font-weight: 700; } .status-row .status-value.status-pending { color: #b56b00; font-weight: 700; }
    .status-action { border: 1px solid #d5a34b; border-radius: 6px; padding: 4px 8px; color: #9a5b00; background: #fff8e8; font: inherit; font-size: 12px; font-weight: 700; cursor: pointer; }
    .status-action:hover { background: #ffefc7; }
    .status-action:disabled { opacity: .65; cursor: wait; }    .status-row b { display: flex; align-items: center; gap: 8px; color: #087a3b; font-size: 15px; font-weight: 800; }
    .status-row b i { width: 22px; height: 22px; border-radius: 50%; background: #22cf51; box-shadow: inset 0 0 0 1px rgba(0,0,0,.02); }
    .request-section { border-top: 1px solid #d8e0e7; padding: 19px 21px 20px; }
    .section-label { color: #617084; font-size: 14px; letter-spacing: .15px; font-weight: 800; margin-bottom: 14px; }
    .request-card { min-height: 300px; border-radius: 12px; padding: 20px 18px 15px; background: #edf1f5; transition: background .25s; }
    .request-card[data-tone="green"] { background: #e6f7ee; }
    .request-card[data-tone="blue"] { background: #e5f1fe; }
    .request-card[data-tone="amber"] { background: #fff6d9; }
    .request-main { min-height: 74px; display: flex; align-items: center; gap: 15px; }
    .request-icon { flex: 0 0 47px; width: 47px; height: 47px; padding: 10px; border-radius: 50%; color: #fff; background: #8b99a7; }
    [data-tone="green"] .request-icon { background: #0b9b5c; padding: 11px; }
    [data-tone="blue"] .request-icon { background: #247bd4; padding: 11px; }
    [data-tone="amber"] .request-icon { background: #f0ad00; padding: 11px; }
    .request-copy { min-width: 0; }
    .request-copy h2 { font-size: 21px; line-height: 1.15; letter-spacing: -.3px; font-weight: 800; }
    .request-copy p { margin-top: 8px; color: #536274; font-size: 14px; line-height: 1.2; }
    .spinner { display: none; margin-left: auto; width: 23px; height: 23px; border: 3px dotted #29445c; border-radius: 50%; animation: spin 1.3s linear infinite; }
    .spinner.visible { display: block; }
        .request-composer { margin-top: 13px; }
     .request-input { width: 100%; min-height: 72px; resize: vertical; border: 1px solid #bdcad7; border-radius: 8px; padding: 9px 10px; color: #18253a; background: #fff; font: inherit; font-size: 14px; line-height: 1.35; }
     .request-input:focus { outline: 2px solid rgba(50,136,232,.25); border-color: #3288e8; }
     .request-input:disabled { background: #eef2f6; color: #8290a0; cursor: not-allowed; }
     .request-drop-zone { margin-top: 8px; border: 1px dashed #9fb4c9; border-radius: 7px; padding: 8px 10px; color: #63758a; background: rgba(255,255,255,.55); font-size: 12px; text-align: center; transition: background .15s, border-color .15s; }
     .request-drop-zone.dragging { border-color: #1477e8; background: #e5f1fe; color: #1477e8; }
     .request-drop-zone.disabled { opacity: .55; cursor: not-allowed; }
     .request-composer-footer { display: flex; align-items: center; gap: 8px; margin-top: 8px; }
     .request-file-names { flex: 1; min-width: 0; color: #536274; font-size: 12px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
     .request-send { border: 1px solid #1475de; border-radius: 7px; padding: 6px 16px; color: #fff; background: #1475de; font: inherit; font-size: 13px; font-weight: 700; cursor: pointer; }
     .request-send:hover { background: #0d66c5; }
     .request-send:disabled { opacity: .55; cursor: not-allowed; }
     .result-section { border-top: 1px solid #d8e0e7; padding: 16px 21px 18px; }
     .result-message { min-height: 110px; border-radius: 10px; padding: 13px 14px; background: #f1f6fb; }
     .result-title { color: #18253a; font-size: 16px; font-weight: 800; }
     .result-body { margin-top: 8px; color: #536274; font-size: 14px; line-height: 1.45; white-space: pre-wrap; overflow-wrap: anywhere; }
 .request-meta { display: flex; align-items: center; gap: 10px; margin-top: 15px; color: #56687c; font-size: 13px; }
    .request-meta strong { color: #536274; font-weight: 500; }
    .hub-footer { padding: 0 21px 10px; text-align: center; color: #91a0b0; font-size: 11px; }
    .hub-footer span { margin-left: 4px; }
    .settings-modal { position: fixed; inset: 0; display: grid; place-items: center; background: rgba(15, 23, 42, .28); z-index: 2147483647; padding: 16px; }
    .settings-modal.hidden { display: none; }
    .settings-dialog { width: min(380px, 100%); max-height: calc(100vh - 32px); overflow: auto; background: #fff; border: 3px solid #26323a; border-radius: 14px; box-shadow: 0 16px 40px rgba(15,23,42,.28); padding: 18px; }
    .settings-title-row { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
    .settings-title-row h2 { font-size: 20px; line-height: 1.15; font-weight: 800; }
    .settings-close { width: 24px; height: 24px; border: 0; padding: 3px; color: #637183; background: transparent; cursor: pointer; }
    .settings-divider { height: 1px; background: #d8e0e7; margin: 14px -18px 15px; }
    .settings-form { display: grid; gap: 11px; }
    .settings-heading { color: #617084; font-size: 12px; letter-spacing: .8px; font-weight: 800; }
    .settings-form label { display: grid; gap: 5px; color: #334155; font-size: 13px; font-weight: 700; }
    .settings-form label span { color: #8a97a5; font-weight: 500; }
    .settings-form input { width: 100%; min-height: 34px; border: 1px solid #bdcad7; border-radius: 7px; padding: 6px 9px; color: #18253a; background: #fff; font: inherit; font-weight: 500; }
    .settings-form input:focus { outline: 2px solid rgba(50,136,232,.25); border-color: #3288e8; }
    .settings-test-row { display: flex; align-items: center; justify-content: space-between; gap: 10px; margin-top: 3px; }
    .secondary-button, .primary-button { min-height: 34px; border-radius: 7px; padding: 6px 12px; font: inherit; font-size: 13px; font-weight: 700; cursor: pointer; }
    .secondary-button { border: 1px solid #cbd7e3; color: #334155; background: #fff; }
    .primary-button { border: 1px solid #1475de; color: #fff; background: #1475de; }
    .secondary-button:hover { background: #f4f7fa; }
    .primary-button:hover { background: #0d66c5; }
    .test-status { min-width: 0; color: #718096; font-size: 12px; text-align: right; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .test-status.success { color: #078443; font-weight: 700; }
    .test-status.error { color: #c53b3b; font-weight: 700; }
    .test-status.pending { color: #b56b00; font-weight: 700; }
    .settings-actions { display: flex; justify-content: flex-end; gap: 10px; margin-top: 8px; }
    .reopen { display: none; position: fixed; top: 100px; right: 24px; z-index: 2147483646; width: 42px; height: 42px; border: 0; border-radius: 50%; color: #fff; background: #26323a; padding: 10px; cursor: pointer; box-shadow: 0 8px 20px rgba(15,23,42,.25); }
    .reopen.visible { display: block; }
    @keyframes spin { to { transform: rotate(360deg); } }
    @media (max-width: 680px) { .hub-panel { top: 72px; right: 12px; left: 12px; width: auto; } .reopen { right: 12px; } }
  `;
  root.querySelector('style').textContent = styles;
  setStatusValue('web', '—', 'pending');
  setStatusValue('worker', '—', 'pending');
  setStatusValue('status', 'Disconnected', 'offline');
  renderState();
  loadSettings().then(() => refreshBridge());
  window.setInterval(refreshBridge, 1500);
})();
