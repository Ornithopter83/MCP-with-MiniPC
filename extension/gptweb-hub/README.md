# ProjectHub Managed Web Bridge

버전: 0.4.2 / build 2026-09-30.4

ProjectHub Worker가 직접 실행하는 HQ/RESOURCE ChatGPT app window와 로컬 Worker를 연결한다.

제1조 (관리형 전용)

① 확장은 Worker가 실행한 관리형 Chromium에서만 bridge를 시작한다.
② 시작 URL의 `projecthub-managed-role`과 `projecthub-runtime-token`을 확인하고 profile 저장소에 현재 실행 정보를 보존한다.
③ 역할 또는 runtime token이 없으면 content script는 bridge 초기화를 시작하지 않는다.
④ 일반 Chrome이나 사용자가 임의로 연 ChatGPT 페이지는 Worker bridge에 연결하지 않는다.
⑤ 확장 UI 패널은 관리형 모드에서 표시하지 않는다.

제2조 (app window)

① Worker는 일반 탭 브라우저가 아니라 `--app=<ChatGPT URL>`로 Chromium을 실행한다.
② HQ와 RESOURCE는 각자 별도 persistent profile을 사용한다.
③ 새 실행 전에 profile의 browser session/tab restore 파일만 제거한다.
④ Cookies와 로그인 데이터는 제거하지 않는다.
⑤ 확장은 tabs 권한을 요구하거나 탭 생성·조회·삭제를 수행하지 않는다.

제3조 (표시와 숨김)

① `로그인/표시`는 기존 hidden window를 복원하지 않는다.
② 기존 슬롯 Chromium을 UI thread 밖에서 종료한 뒤 clean session 상태의 visible app window를 새로 시작한다.
③ `숨김 실행`도 같은 방식으로 새 app window를 시작하되 창을 최소화하거나 SW_HIDE하지 않고 화면 밖 위치에 유지한다.
④ 숨김 app window는 background timer, renderer, occluded-window throttling을 끈 상태로 실행해 heartbeat와 Web 자동화를 계속 수행한다.
⑤ 이 전환은 로그인 profile을 유지하면서 브라우저 프로세스와 화면 상태만 새로 만든다.

제4조 (Bridge 인증)

① Worker는 실행마다 새로운 managed runtime token을 생성한다.
② content script는 loopback bridge 요청에 `X-ProjectHub-Managed-Token` 헤더를 포함한다.
③ Worker는 token이 없거나 현재 실행 token과 다르면 bridge 요청을 거부한다.
④ 과거 버전 확장이나 일반 Chrome의 오래된 content script가 살아 있어도 현재 Worker bridge 작업을 claim하거나 결과를 제출할 수 없다.

제5조 (작업 처리)

① 작업 조회, claim, 진행 보고와 결과 반환은 taskId, conversationId와 leaseId를 사용한다.
② HQ Web 작업은 assistant 텍스트를 수집해 반환한다.
③ Send 클릭과 실제 메시지 전송 확인을 구분한다.
④ 숨김 app window에서는 polling만 신뢰하지 않고 MutationObserver가 user/assistant turn 변화를 관측하는 즉시 전송 증거를 latch해 이후 DOM virtualization이나 timer 지연이 있어도 잃지 않는다.
⑤ 전송 전 conversation turn의 role·message key·text fingerprint를 baseline으로 저장하고, 메시지 개수 증가가 없어도 baseline에 없던 turn을 새 전송/응답 증거로 인정한다.
⑥ user turn 수집은 실제 user role만 사용하며 일반 conversation article을 user 메시지로 혼합하지 않는다.
⑦ SEND_CONFIRM 제한시간 직전에는 현재 DOM을 다시 reconciliation해 이번 prompt user turn과 뒤따른 assistant turn이 있으면 실패 대신 WAIT_RESPONSE로 복구한다.
⑧ 기존 assistant DOM이 재사용될 때는 현재 Worker 메시지의 전송이 확인된 뒤 텍스트 변화도 새 응답 증거로 사용할 수 있다.
⑨ 진행 단계는 Worker에 보고하고 Worker는 마지막 기계 체크포인트를 저장한다.
⑩ HQ task에 correlation KEY가 있으면 SEND_CONFIRM과 WAIT_RESPONSE에서 현재 KEY를 독립적으로 감시하며 role·turn selector가 현재 DOM을 놓쳐도 KEY 응답을 전송·응답 증거로 사용할 수 있다.
⑪ KEY 감시는 MutationObserver에만 의존하지 않고 주기적 재확인을 함께 사용하며, 관측 상태는 `HQ_KEY_WATCH` 진행 이벤트로 보고한다.
⑫ 요청 prompt 안의 동일 KEY는 응답으로 오인하지 않고, prompt와 분리된 현재 응답 후보에서 KEY 뒤 실제 본문을 찾아 결과를 회수한다.
⑬ assistant 응답 root를 식별하지 못해도 현재 KEY 뒤에 HQ 계약의 ACTION이 이어지고 요청 prompt와 구분되는 경우에는 conversation body fallback으로 결과 회수를 계속한다.
⑭ 이미 claim된 HQ task에서 이전 turn baseline을 복구할 수 없더라도 현재 task의 KEY가 있으면 prompt를 재전송하지 않고 KEY 기준 WAIT_RESPONSE로 복구한다.

제6조 (파일 검증)

① Worker가 Web으로 전달하는 첨부와 RESOURCE가 반환하는 파일은 가능한 경우 SHA-256을 함께 전달한다.
② 수신 측은 실제 bytes의 SHA-256을 다시 계산해 불일치를 실패 처리한다.
③ CORS 때문에 content script가 직접 가져올 수 없는 허용된 ChatGPT/OpenAI 파일은 background service worker가 재시도할 수 있다.
④ RESOURCE 최종 저장 경로와 경로 안전성은 Worker가 검증한다.

제7조 (일반 Web 응답 파일)

① HQ 등 일반 Web 응답도 assistant 텍스트와 함께 다운로드 가능한 파일 링크를 수집한다.
② 일반 응답 파일은 RESOURCE 전용 이미지·리소스 처리와 분리하되 동일한 bytes 다운로드, MIME 확인과 SHA-256 검증 경로를 사용한다.
③ PDF, ZIP, JSON, TXT, Markdown, CSV, DOCX, XLSX, PPTX와 허용된 오디오·비디오 등 비이미지 파일을 포함하며 download 속성·첨부/다운로드 표기·파일 API URL도 후보로 탐지한다.
④ 일반 Web 응답의 텍스트가 먼저 안정돼도 새 다운로드 링크가 나타나면 response snapshot이 변경된 것으로 보고 안정화 대기를 다시 시작한다.
⑤ 검증된 일반 Web 파일은 Worker의 `Worker/web-results/<taskId>/`에 저장하고 path·size·SHA-256 receipt를 남긴다.
⑥ 파일 다운로드나 hash 검증이 실패하면 텍스트만 성공 처리하지 않고 해당 Web 작업을 파일 회수 실패로 처리한다.
⑦ HQ Web 역할 결과는 저장된 파일 경로를 AiRoleRunResult.Files에도 포함해 후속 실행에서 결과 파일을 잃지 않는다.

제8조 (첨부 준비 확인)

① Worker attachment bytes의 SHA-256 검증과 ChatGPT의 실제 첨부 처리 완료를 같은 단계로 취급하지 않는다.
② bytes 검증 뒤 ATTACHMENT_BYTES_VERIFIED를 기록하고 file input 설정 뒤 ATTACHMENT_INPUT_SET을 기록한다.
③ ChatGPT composer 영역에서 첨부 카드/파일 표시가 확인되면 ATTACHMENT_UI_DETECTED를 기록할 수 있다.
④ 업로드 또는 파일 처리 UI가 확인되면 ATTACHMENT_PROCESSING을 기록하고 Send 버튼이 활성화될 때까지 기다린다.
⑤ 활성 Send 버튼을 확인한 뒤에만 ATTACHMENT_READY를 기록하고 일반 Send 감시 단계로 진행한다.
⑥ 첨부가 있는 요청에서 Voice 버튼만 보이는 상태는 조기 실패 조건으로 사용하지 않고 전체 첨부 준비 제한시간 안에서 Send 활성화를 계속 기다린다.
⑦ 명시적인 첨부 오류 UI가 확인되면 ATTACHMENT_UI_ERROR로 실패 처리한다.
⑧ 첨부 준비 제한시간은 일반 장기 Web 작업 제한시간과 동일한 5분을 사용한다.



제9조 (Web UI 이상 관측)

① assistant 응답과 별개인 오류·한도·timeout·첨부 실패 UI를 현재 task의 진단 관측으로 기록할 수 있다.
② 관측된 이상은 WEB_UI_ANOMALY_OBSERVED 진행 이벤트로 Worker 통합로그에 전달한다.
③ 동일 task의 동일 관측은 중복 억제할 수 있다.
④ 관측만으로 task 실패, 대화방 이동, 재전송, role binding, lease 또는 correlation KEY를 변경하지 않는다.
