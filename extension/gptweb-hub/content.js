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
            <button class="icon-button settings" title="Preview next state" aria-label="Preview next state">${getIcon("gear")}</button>
            <button class="icon-button close" title="Hide panel" aria-label="Hide panel">${getIcon("close")}</button>
          </div>
        </div>
        <p class="system-line"><span class="system-icon">i</span><span class="system-text">정상적으로 연결되어 있습니다.</span></p>
      </header>

      <div class="status-list" aria-label="Connection status">
        ${statusRow('project', 'Project', 'MCP-with-MiniPC')}
        ${statusRow('worker', 'Worker', 'ProjectHub Worker')}
        ${statusRow('web', 'Web', 'Connected')}
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
        </article>
      </main>

      <footer class="hub-footer">GPTWeb-Hub <span>v0.1 UI Preview</span></footer>
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
  const requestIcon = root.querySelector('.request-icon');
  const title = root.querySelector('.request-copy h2');
  const detail = root.querySelector('.request-copy p');
  const task = root.querySelector('.request-meta strong:nth-of-type(1)');
  const from = root.querySelector('.request-meta strong:nth-of-type(2)');
  const spinner = root.querySelector('.spinner');

  function renderState() {
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
  }

  root.querySelector('.settings').addEventListener('click', () => {
    stateIndex = (stateIndex + 1) % states.length;
    renderState();
  });

  root.querySelector('.close').addEventListener('click', () => {
    panel.classList.add('hidden');
    root.querySelector('.reopen').classList.add('visible');
  });

  root.querySelector('.reopen').addEventListener('click', () => {
    panel.classList.remove('hidden');
    root.querySelector('.reopen').classList.remove('visible');
  });

  function statusRow(kind, label, value) {
    return `<div class="status-row" data-kind="${kind}"><strong>${label}</strong><span>${value}</span><b><i></i>READY</b></div>`;
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
    .status-row { min-height: 41px; display: grid; grid-template-columns: 75px 1fr auto; align-items: center; column-gap: 7px; border-bottom: 1px solid #d8e0e7; font-size: 16px; }
    .status-row:last-child { border-bottom: 0; }
    .status-row strong { font-size: 16px; font-weight: 800; }
    .status-row > span { color: #18253a; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .status-row b { display: flex; align-items: center; gap: 8px; color: #087a3b; font-size: 15px; font-weight: 800; }
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
    .request-meta { display: flex; align-items: center; gap: 10px; margin-top: 15px; color: #56687c; font-size: 13px; }
    .request-meta strong { color: #536274; font-weight: 500; }
    .hub-footer { padding: 0 21px 10px; text-align: center; color: #91a0b0; font-size: 11px; }
    .hub-footer span { margin-left: 4px; }
    .reopen { display: none; position: fixed; top: 100px; right: 24px; z-index: 2147483646; width: 42px; height: 42px; border: 0; border-radius: 50%; color: #fff; background: #26323a; padding: 10px; cursor: pointer; box-shadow: 0 8px 20px rgba(15,23,42,.25); }
    .reopen.visible { display: block; }
    @keyframes spin { to { transform: rotate(360deg); } }
    @media (max-width: 680px) { .hub-panel { top: 72px; right: 12px; left: 12px; width: auto; } .reopen { right: 12px; } }
  `;
  root.querySelector('style').textContent = styles;
  renderState();
})();



