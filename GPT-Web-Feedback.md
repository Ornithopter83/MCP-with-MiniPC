# GPT Web 피드백 — 정책 문서 계층 분리

갱신일: 2026-09-26

제1조 (반영 완료)

① `Master-Polish.md`는 ProjectHub 전체 공통 영구 정책과 규범 문서 작성 형식만 남기도록 축소했다.
② 기존 Worker 세부 정책은 `Worker-Polish.md`로 분리했다.
③ `Core-Polish.md`, `Infrastructure-Polish.md`, `Server-Polish.md`, `Agent-Polish.md`, `Web-Polish.md`를 생성했다.
④ `AGENTS.md`는 특정 Worker 중심 규칙을 제거하고 저장소 공통 작업 계약으로 일반화했다.
⑤ `CurrentWork.md`는 CORE, INFRASTRUCTURE, SERVER, AGENT, WORKER, WEB 여섯 영역의 현재 상태만 보관하도록 재구성했다.
⑥ Worker 전용 JEV 계약, 활성 병렬 WORK 작업 문서와 구현 로드맵은 Worker 정책 문서를 세부 정책 원본으로 참조하도록 정리했다.
⑦ README는 여섯 프로젝트 구성과 정책 문서 체계를 설명하도록 갱신했다.

제2조 (문서 역할)

① Master는 하위 프로젝트의 구현 세부를 담지 않는다.
② 각 `*-Polish.md`는 해당 프로젝트의 장기 책임과 경계만 담는다.
③ 역할·API·전송 계약은 프로젝트 정책보다 세부적인 프로토콜 경계를 정의한다.
④ CurrentWork의 과거 상태는 누적하지 않고 Git 이력으로 보존한다.

제3조 (Web assistant 응답 수집 보강)

① ChatGPT Web이 새 assistant DOM을 추가하지 않고 기존 assistant DOM의 텍스트만 갱신하는 경우를 새 응답 후보로 감지하도록 확장했다.
② 텍스트 변화 fallback은 현재 Worker 메시지가 실제 사용자 메시지로 대화에 나타난 것이 확인된 경우에만 허용한다.
③ 응답 감지 근거가 텍스트 변화인 경우 `RESPONSE_TEXT_CHANGED` 진행 상태를 기록한다.
④ 확장 버전은 0.1.11, 빌드는 2026-09-26.2로 갱신했다.
⑤ Web 확장 파일은 Worker 실행파일의 EmbeddedResource이므로 Worker 관리 배포본을 갱신하려면 새 Worker 빌드가 필요하다.

제4조 (관리형 HQ RESOURCE Web 런타임)

① 구현 전 복구 기준을 `recovery/pre-managed-web-runtime-20260926` 브랜치로 고정했으며 기준 commit은 `b21f6f8682a0a1faaf7128535a72a15887561f4e`다.
② Worker에 HQ와 RESOURCE 두 개의 고정 Web 브라우저 슬롯을 추가했다.
③ 두 슬롯은 Git 작업공간 밖의 별도 persistent profile을 사용하며 평상시는 화면 밖에서 실행하고 Worker UI의 `로그인/표시`와 `숨김 실행`으로 전환한다.
④ Worker 시작 시 저장된 HQ/RESOURCE conversationId가 있으면 해당 대화를 열고, 없으면 ChatGPT 시작 화면을 연다.
⑤ 관리형 Web 확장은 슬롯 역할을 profile에 기억하고 사용자가 대화를 선택하면 HQ 또는 RESOURCE 역할을 자동 연결한다.
⑥ 관리형 모드에서는 확장 패널을 숨기며 수동 Chrome 호환 모드의 패널은 유지한다.
⑦ bridge 진행 단계는 BridgeTask에 마지막 stage, detail, attempt, 시각을 저장해 송신·응답·다운로드 단계의 기계적 관측값을 보존한다.
⑧ Worker 첨부 업로드와 RESOURCE 반환 파일에 SHA-256 교차 검증을 추가했다. RESOURCE 최종 저장 뒤에는 경로, 크기, SHA-256 receipt를 기록한다.
⑨ 확장 버전은 0.2.0, build는 2026-09-26.5다.
⑩ 현재 RESOURCE 파일은 기존처럼 base64 resultFiles[]를 통해 bridge로 반환한다. 브라우저 다운로드 이벤트를 Worker 로컬 파일로 직접 이전하는 후속 최적화는 이번 변경에 포함하지 않았다.
⑪ 일반 Google Chrome 137+의 unpacked extension 명령행 제한을 피하기 위해 설치된 Chrome 자동 fallback을 제거하고, 호환 런타임이 없으면 공식 Chrome for Testing Stable win64를 사용자 로컬 데이터 영역에 자동 준비하도록 변경했다.
⑫ 숨김 모드는 headless가 아니라 일반 브라우저 렌더링을 유지하고 화면 밖 시작 뒤 Windows 창을 숨기는 방식으로 구현했다. 로그인 시에는 Worker UI에서 visible 브라우저로 다시 시작한다.
⑬ Send 버튼 실행과 실제 전송 확인을 분리해 composer clear만으로 전송 성공을 확정하지 않는다.
⑭ Windows Worker 실제 빌드·실행 및 ChatGPT 로그인/E2E는 아직 수행하지 않았다.

제5조 (본문 구조 마커 방어)

① 수정 전 로그에서 HQ가 ACTION/GOTO 뒤에 설계 설명을 먼저 출력하고 이후 WORK_GRAPH_PATCH를 정상 출력했지만 WorkGraphTransportContract가 본문의 첫 비어 있지 않은 행만 검사해 WORK_GRAPH_PATCH_MARKER_MISSING으로 종료된 사실을 확인했다.
② WORK_GRAPH_PATCH는 본문 전체 행에서 정확히 하나의 마커를 찾도록 변경했다.
③ WORK_GRAPH_PATCH 뒤에서는 첫 번째 완전한 JSON 객체 하나만 추출해 파싱하므로 JSON 뒤의 설명은 WORK_GRAPH_PATCH_JSON_INVALID 원인이 되지 않는다.
④ 같은 마커가 두 번 나오면 WORK_GRAPH_PATCH_MARKER_DUPLICATE로 거부한다.
⑤ WORK_ITEM_STATUS와 RESOURCE_TYPE도 본문 내 위치를 독립적으로 탐색하고 중복 마커를 각각 WORK_ITEM_STATUS_DUPLICATE, RESOURCE_TYPE_DUPLICATE로 거부한다.
⑥ RESOURCE_TYPE 앞의 설명은 RESOURCE 생성 프롬프트에 포함하지 않고 마커 뒤의 자연어 요청만 전달한다.
⑦ 레거시 NEXT 자체는 첫 제어행 규칙을 유지하되 NEXT 뒤 REPORT와 VALIDATION REQUEST 구조 마커도 본문 내 위치 독립·중복 거부 방식으로 맞췄다.
⑧ ACTION, GOTO, NEXT 등 라우팅 제어행의 기존 위치 규칙은 완화하지 않았다.
⑨ 수정 전 로그 형태, JSON 뒤 설명, inline WORK_GRAPH_PATCH JSON, 중복 WorkGraph/WorkItem/RESOURCE/레거시 마커에 대한 회귀 테스트를 추가했다.
⑩ 이 환경에서는 저장소 Windows .NET 빌드와 테스트를 실제 실행하지 못해 정적 검증까지만 완료했다.

제6조 (관리형 Chromium 확장 동기화)

① HQ와 RESOURCE를 서로 다른 persistent profile로 로그인한 상태에서 HQ만 확장 업데이트 필요, RESOURCE는 연결됨으로 표시되는 현상을 확인했다.
② Worker는 시작 시 내장 확장 파일을 최신 상태로 배포하지만 이전 Worker가 남긴 Chrome for Testing 프로세스가 살아 있으면 해당 profile이 예전 content script heartbeat를 계속 보낼 수 있다.
③ Worker 시작 직후 HQ/RESOURCE 슬롯을 열기 전에 ProjectHub 관리 BrowserRuntime 경로의 chrome.exe 프로세스만 찾아 종료하도록 보강했다.
④ 시스템 Chrome, 외부 PROJECTHUB_CHROMIUM_PATH 등 ProjectHub 소유 경로 밖의 브라우저는 자동 종료 대상에서 제외한다.
⑤ 역할별 Web 상태에 실제 ExtensionVersion/ExtensionBuild와 ExpectedExtensionVersion/ExpectedExtensionBuild를 포함하고 UI에서 불일치 값을 직접 표시한다.
⑥ 이전 로컬 재게시에서 확인된 MainWindow.ManagedWeb.cs의 System.Drawing 대 WPF Brush/Brushes 모호성은 System.Windows.Media 형식을 명시하는 방식으로 저장소에 정식 반영했다.
⑦ 당시 확장 version/build는 0.2.0 / 2026-09-26.5였고, 이후 관리형 단일 탭 기능에서 0.2.1 / 2026-09-26.6으로 갱신했다.
⑧ 새 Worker 빌드·게시 후 실제 HQ/RESOURCE 로그인 profile 유지와 두 역할의 heartbeat build 동기화 E2E가 필요하다.

제7조 (관리형 ChatGPT 단일 탭)

① Worker의 `로그인/표시` 또는 hidden 시작으로 생성된 `projecthub-managed-role` launch 탭만 해당 profile의 탭 정리 권한을 행사하도록 했다.
② background service worker에 `ensure-single-chatgpt-tab` 명령을 추가하고 관리형 탭 정리 요청을 직렬화했다.
③ 저장된 conversationId가 있으면 Worker launch 탭이 그 대화를 열고 있는 경우 해당 탭을 최우선으로 유지하며, 같은 profile의 다른 ChatGPT 탭을 닫는다.
④ conversationId가 없으면 Worker launch 탭 하나만 남겨 최초 로그인과 대화 선택을 계속할 수 있게 한다.
⑤ 정리 대상은 현재 profile의 `chatgpt.com` 및 `www.chatgpt.com` 탭으로 제한하고 다른 사이트 탭은 유지한다.
⑥ manifest에 `tabs` 권한을 추가했고 확장을 0.2.1 / build 2026-09-26.6으로 갱신했다.
⑦ Worker Bridge의 기대 version/build도 0.2.1 / 2026-09-26.6으로 맞췄다.
⑧ Worker EXE에 내장되는 manifest/background/content에 단일 탭 계약이 실제 포함되는지 확인하는 테스트를 추가했다.
⑨ 실제 Windows Worker 빌드·게시와 세션 복원 환경의 HQ/RESOURCE 단일 탭 E2E는 아직 수행하지 않았다.

제8조 (중복 탭과 표시 프리징 방어)

① 실제 관리형 Chromium에서 같은 ChatGPT 대화 제목의 탭이 여러 개 열리고 `로그인/표시` 버튼 클릭 시 Worker UI가 프리징되는 현상을 확인했다.
② 실제 ChatGPT 대화 URL이 `/g/<project-or-gpt>/c/<conversationId>` 형태였지만 background의 conversationId 파서는 `^/c/<id>`만 인식해 같은 대화를 식별하지 못했다.
③ conversationId 파서를 경로 어디의 `/c/<id>`도 인식하도록 변경해 일반 대화와 Project/GPT 대화 URL을 동일 conversation으로 취급한다.
④ 저장된 conversationId와 일치하는 기존 Project/GPT 대화 탭이 있으면 Worker가 임시로 연 canonical `/c/<id>` launch 탭보다 기존 대화 탭을 우선 유지한다.
⑤ 기존 대화 탭에 관리 역할 marker를 붙여 다시 로드한 뒤 canonical launch 탭을 지연 제거해 content script 교체 중 응답 채널이 끊기는 경쟁을 줄였다.
⑥ `로그인/표시`는 더 이상 기존 브라우저를 종료·재시작하지 않는다. 실행 중인 브라우저의 top-level window를 Win32 ShowWindow/SetWindowPos로 복원·활성화한다.
⑦ `숨김 실행`도 브라우저 재시작 대신 기존 window를 숨겨 profile, 로그인 상태와 대화 탭을 유지한다.
⑧ 기존 프로세스가 없을 때만 새 관리형 Chromium을 시작한다.
⑨ `로그인/표시` 요청은 BridgeServer의 역할별 managedTabCleanupGeneration을 증가시키고, content script가 이를 관측하면 background에 단일 탭 정리를 다시 요청한다.
⑩ 확장은 0.2.2 / build 2026-09-26.8이며 Worker Bridge의 기대 version/build도 동일하다.
⑪ JavaScript 문법, manifest tabs 권한, version/build 일치는 정적으로 확인했고 실제 Windows Worker 빌드·게시 및 중복 탭/프리징 E2E는 아직 수행하지 않았다.

제9조 (clean ChatGPT app window 전환)

① 관리형 Chromium은 오로지 ProjectHub의 숨김 HQ/RESOURCE ChatGPT 실행환경으로 사용한다는 전제로 구조를 단순화했다.
② 각 역할 브라우저는 일반 탭 URL 인자가 아니라 `--app=<ChatGPT URL>`로 시작해 일반 탭 UI와 세션 복원 탭 정리 로직에 의존하지 않는다.
③ 새 브라우저를 시작하기 전에 profile의 `Default/Sessions`, `Current Session`, `Current Tabs`, `Last Session`, `Last Tabs`만 제거하며 Cookies와 로그인 데이터는 유지한다.
④ `로그인/표시`와 `숨김 실행`은 기존 hidden/visible window를 상호 복원하지 않는다. 기존 슬롯 프로세스를 UI thread 밖에서 종료한 뒤 clean session 상태의 새 visible/hidden app window를 실행한다.
⑤ 기존 Win32 SetWindowPos/SetForegroundWindow 기반 창 복원 경로는 제거했다. hidden 시작 시에만 생성 직후 창을 숨긴다.
⑥ GPTWeb-Hub는 0.3.0 / build 2026-09-26.9부터 관리형 Chromium 전용이다. 시작 URL에서 HQ/RESOURCE 역할과 runtime token을 확인하지 못하면 bridge 초기화를 시작하지 않는다.
⑦ Worker는 실행마다 임의 managed runtime token을 생성하고 app URL로 전달한다. content script는 loopback 요청에 `X-ProjectHub-Managed-Token`을 포함하며 Worker는 token이 없거나 다르면 HTTP 401과 `managed_runtime_required`로 거부한다.
⑧ 이 token 경계 때문에 일반 Chrome에 과거 unpacked extension이 남아 있거나 오래된 content script가 살아 있어도 현재 Worker bridge를 사용할 수 없다.
⑨ background의 chrome.tabs 기반 단일 탭 정리 로직과 manifest tabs 권한을 제거했다.
⑩ 잔존 관리형 Chromium 프로세스 정리도 UI thread 밖에서 실행하도록 변경했다.
⑪ Worker의 app mode/session cleanup 테스트와 내장 확장의 managed-only/no-tabs 계약 테스트를 갱신했다.
⑫ JavaScript 문법과 extension/Bridge version-build 일치는 정적으로 확인했으며 Windows Worker 실제 빌드·게시와 ChatGPT 로그인/E2E는 아직 수행하지 않았다.

제10조 (숨김 상태 heartbeat 유지)

① clean app window 전환 뒤 숨김 상태에서 일정 시간이 지나면 Worker UI가 Web heartbeat 대기 상태로 떨어지는 현상을 확인했다.
② 당시 숨김 실행은 `--start-minimized`와 화면 밖 위치를 함께 사용한 뒤 Win32 `SW_HIDE`까지 적용했고, Worker는 heartbeat가 10초만 끊겨도 연결 끊김으로 판정했다.
③ 숨김 Chromium은 더 이상 `--start-minimized`를 사용하지 않고 Win32 `SW_HIDE`도 적용하지 않는다.
④ 숨김 창은 `--window-position=-32000,-32000`로 화면 밖에 두되 정상 렌더링 window 상태를 유지한다.
⑤ Chrome for Testing 실행 인자에 `--disable-background-timer-throttling`, `--disable-renderer-backgrounding`, `--disable-backgrounding-occluded-windows`를 추가했다.
⑥ Worker의 전역 및 역할별 Web heartbeat 생존 판정을 10초에서 30초로 완화했다.
⑦ content script에는 별도의 document.hidden 또는 visibilityState 기반 전송 차단이 없음을 확인했다.
⑧ 확장 version/build는 0.3.0 / 2026-09-26.9를 유지한다. 이번 수정은 Worker Chromium 실행 및 연결 판정 변경이다.
⑨ 숨김 실행 인자 테스트를 갱신해 최소화 플래그 부재와 세 가지 background throttling 비활성화 플래그를 검증한다.
⑩ Windows 실제 빌드·게시와 장시간 숨김 heartbeat E2E는 아직 수행하지 않았다.

제11조 (사용자 파일과 화면 캡처 첨부)

① 메시지 및 작업 이력의 신규 작업과 작업 추가 입력에 파일 drag-and-drop과 화면 캡처 Ctrl+V 첨부 UI를 추가했다.
② 첨부는 본문 텍스트에 원본 bytes를 삽입하지 않고 UserAttachmentInput으로 분리해 Worker attachment 캐시에 복사하고 파일명, MIME, byte 크기, SHA-256과 입력 출처를 관리한다.
③ 한 메시지 최대 20개, 파일당 50MB이며 폴더와 exe/com/scr/msi/msp/cpl/lnk 실행 계열은 거부한다. 소스·스크립트 파일은 개발 입력으로 허용한다.
④ 클립보드 이미지 Ctrl+V는 PNG로 인코딩해 일반 첨부와 같은 캐시·hash 경로를 사용하며 Clipboard 잠금 예외도 UI 오류로 방어한다.
⑤ CLI 대상 파일은 작업공간 `.projecthub/attachments/<batch>/`에 staging하고 SHA-256을 재검증한다. Git info exclude에도 해당 런타임 경로를 추가하도록 시도한다.
⑥ AiRoleRunRequest에 InputAttachments를 추가했고 OpenAI Codex 역할 runner는 USER_ATTACHMENTS 블록으로 실제 local path, MIME, 크기, SHA-256을 전달한다. 이미지 파일은 해당 local path의 이미지를 확인한 뒤 판단하도록 지시한다.
⑦ coordinator-first 첫 HQ 호출은 사용자 첨부를 받는다. HQ가 Web transport이면 기존 BridgeAttachment로 실제 ChatGPT file input에 파일 bytes가 첨부되며, HQ가 CLI이면 staged local path를 받는다.
⑧ WORK WorkItem은 각 독립 worktree에 동일 사용자 첨부를 별도로 staging해 구현 AI가 읽을 수 있다.
⑨ legacy 흐름은 최초 Codex 입력에 staged attachment context를 넣고 첫 HQ Web 전달에 실제 파일을 함께 첨부한다.
⑩ `계약문서 무시` 직통 모드는 IgnoreProjectInstructions=true를 유지하면서도 같은 attachment staging과 InputAttachments 전달을 사용한다. 따라서 계약/AGENTS 자동 주입만 우회하고 사용자가 첨부한 자료는 선택한 AI에 전달된다.
⑪ Web content script는 기존처럼 인증된 downloadUrl을 fetchWithTimeout으로 받고 실제 bytes SHA-256을 계산해 ATTACH_HASH_MISMATCH를 거부한 뒤 ChatGPT file input에 File 객체를 넣는다. 확장 자체의 추가 버전 증가는 필요하지 않았다.
⑫ UserAttachmentTransport 회귀 테스트를 추가해 cache/hash, 실행 바이너리 차단과 source script 허용, workspace staging, AI prompt metadata, Bridge attachment ID/hash를 검증하도록 했다.
⑬ 이 환경에서는 실제 Windows Worker 빌드와 UI/Web E2E를 아직 실행하지 못했다.

제12조 (숨김 상태 Send false negative 방어)

① 실제 테스트는 관리형 Chromium이 숨김 상태인 동안 수행됐다.
② Worker 로그에서는 Send 버튼 클릭이 완료됐지만 5분 동안 실제 user turn 또는 assistant 응답 확인에 실패해 SEND_BUTTON_FIND timeout과 PARALLEL_HQ_EXECUTION_FAILED로 종료됐다.
③ 같은 실행의 ChatGPT 화면에는 HQ의 WORK_GRAPH_PATCH 응답이 완성된 상태로 존재했으므로 실제 Web Send와 assistant 생성은 성공했고 확장 감지가 false negative였다고 판단했다.
④ 기존 userMessages는 일반 conversation article까지 user 후보로 포함하고 새 메시지 개수 증가를 필수 조건으로 사용해 DOM virtualization이나 turn 교체에 취약했다.
⑤ user/assistant를 role-aware conversationTurns로 분리하고 role, message key, 정규화 text를 합친 fingerprint baseline을 전송 전에 저장하도록 변경했다.
⑥ 새 user turn은 총 메시지 개수가 늘지 않아도 baseline에 없던 동일 prompt turn이면 전송 증거로 인정한다.
⑦ assistant 증거는 이번 작업의 Send trigger 또는 현재 user turn 확인 이후에만 인정해 기존 streaming 응답 변화가 새 작업 증거로 오인되지 않게 했다.
⑧ SEND_BUTTON_FIND/SEND_CONFIRM 중 MutationObserver가 발견한 전송 증거는 latchedSendEvidence에 즉시 고정하고 sessionStorage task memory에도 저장한다.
⑨ polling이 늦거나 DOM이 이후 virtualization돼도 latch된 증거는 SEND_EVIDENCE_LATCHED / SEND_MUTATION_CONFIRMED 단계로 유지한다.
⑩ 5분 제한시간 직전에는 현재 DOM을 다시 reconciliation해 이번 prompt user turn과 뒤따른 assistant turn을 찾고 발견하면 SEND_TIMEOUT_RECOVERED로 WAIT_RESPONSE에 복구한다.
⑪ 확장 version/build는 0.3.1 / 2026-09-26.10으로 올리고 Worker Bridge 기대 version/build도 동일하게 동기화했다.
⑫ 내장 확장 계약 테스트에 fingerprint baseline, evidence latch, mutation 확인, timeout reconciliation과 현재 Send 이전 assistant 오탐 방지 조건을 추가했다.
⑬ JavaScript 문법과 version/build 정합성은 정적으로 확인하고 실제 Windows Worker 빌드·게시 및 숨김 상태 E2E는 후속 확인 대상으로 남긴다.

제13조 (assistant 응답 및 일반 파일 회수)

① 실패 재현에서는 Worker→HQ Web 전송과 ChatGPT assistant 응답 생성까지는 성공했지만 GPTWeb-Hub가 응답을 Worker result로 회수하지 못했다.
② 기존 role-aware selector가 실제 ChatGPT turn 구조와 어긋날 수 있으므로 현재 Worker prompt를 포함하는 새 conversation-turn container를 찾고 그 다음 turn을 assistant 결과로 회수하는 순서 기반 fallback을 추가했다.
③ fallback prompt 매칭은 현재 prompt와 거의 동일한 turn만 허용하고 assistant role turn은 prompt 후보에서 제외해 응답이 입력을 인용하는 경우의 오탐을 줄였다.
④ currentResponseText는 현재 작업의 assistant 증거 또는 prompt-next-turn fallback이 없으면 과거 latestAssistant 텍스트를 사용하지 않는다.
⑤ 응답 회수 단계에 ASSISTANT_TURN_DETECTED, ASSISTANT_TEXT_EXTRACTED, RESPONSE_START, RESPONSE_STABLE, RESULT_POSTING을 분리했다.
⑥ HQ 일반 Web 응답도 RESOURCE 여부와 무관하게 다운로드 가능한 파일을 수집한다.
⑦ 일반 결과 파일 탐지는 assistant 응답 turn 내부로 제한해 user 입력 첨부를 생성 결과로 오인하지 않는다.
⑧ PDF, ZIP, JSON, TXT, Markdown, CSV, DOCX, XLSX, PPTX 및 허용된 오디오·비디오 등 비이미지 다운로드를 기존 fetch-resource-file fallback과 SHA-256 검증 경로로 회수한다.
⑨ 텍스트가 먼저 안정돼도 새 파일 링크가 나타나면 response snapshot이 바뀌므로 안정화 대기를 다시 시작하고 텍스트와 파일을 한 result로 제출한다.
⑩ 일반 Web 결과 파일은 TEXT_WITH_FILES로 제출하고 Worker는 `Worker/web-results/<taskId>/` 아래에 저장해 path·size·SHA-256 receipt를 남긴다.
⑪ 일반 Web 파일의 base64, 개별 25MB, 전체 128MB, SHA-256 검증을 RESOURCE 저장과 공통 decoder로 통합했다.
⑫ HQ Web 역할 결과는 저장된 파일을 AiRoleRunResult.Files로 노출한다.
⑬ CLAIMED 진행 로그에 실제 extension version/build를 기록하도록 바꿔 이후 실패 로그에서 사용 빌드를 바로 식별할 수 있다.
⑭ 확장은 0.3.2 / build 2026-09-27.1이며 Worker Bridge 기대 버전도 동일하다.
⑮ 내장 확장 회귀 테스트와 WebResultFileStorageTests를 추가해 assistant fallback, 일반 파일 탐지, non-image 파일 저장과 SHA mismatch 거부를 고정했다.
⑯ JavaScript 문법과 주요 연결은 정적으로 확인했으며 실제 Windows Worker 빌드·게시 및 숨김 HQ 응답/파일 E2E는 후속 확인 대상이다.

제14조 (첨부 처리 중 Voice-only 조기 실패 방어)

① 실제 실패 로그에서 사용자 첨부 파일의 bytes 다운로드와 SHA-256 검증은 성공했지만 ChatGPT file input 설정 직후 Send 버튼 대신 Voice 버튼만 표시됐고 약 2.25초 뒤 Web 작업이 실패했다.
② 기존 ATTACHMENT_VERIFIED는 Worker attachment bytes가 정확하다는 뜻일 뿐 ChatGPT가 해당 첨부를 업로드·처리 완료했다는 뜻이 아니었다.
③ 첨부 단계명을 ATTACHMENT_BYTES_VERIFIED로 분리하고 file input에 전체 파일 수가 설정된 뒤 ATTACHMENT_INPUT_SET을 기록하도록 변경했다.
④ composer 영역에서 파일명 또는 attachment/file UI가 확인되면 ATTACHMENT_UI_DETECTED를 기록하고 aria-busy, progressbar, upload 표시가 있으면 ATTACHMENT_PROCESSING을 기록한다.
⑤ 첨부 관련 alert/error UI를 탐지해 명시적인 업로드/파일 오류는 ATTACHMENT_UI_ERROR로 조기 실패할 수 있다.
⑥ 첨부 처리 완료의 최종 기계 증거는 활성 Send 버튼이다. 이를 확인하면 ATTACHMENT_READY를 기록한 뒤에만 일반 Send 감시로 넘어간다.
⑦ 첨부가 있는 요청에서 Voice 버튼만 표시될 때는 기존 2.25초 조기 실패 규칙을 적용하지 않고 5분 ATTACHMENT_READY_TIMEOUT 안에서 Send 활성화를 계속 기다린다.
⑧ 첨부 준비 뒤 monitorSendReady에도 attachmentCount를 전달해 Send 버튼이 일시적으로 다시 사라져도 Voice-only를 즉시 실패시키지 않는다.
⑨ 확장 version/build는 0.3.3 / 2026-09-27.2로 올리고 Worker Bridge 기대 버전도 동일하게 동기화했다.
⑩ ManagedWebExtensionContractTests에 첨부 bytes/input/UI/processing/ready 단계와 attachmentCount 기반 Voice-only 대기 계약을 추가했다.
⑪ JavaScript 문법은 정적으로 확인했고 실제 Windows Worker 빌드·게시 및 숨김 Chromium의 첨부 HQ Web E2E는 후속 확인 대상으로 남겼다.

