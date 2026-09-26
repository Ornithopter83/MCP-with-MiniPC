# Web-Polish — ProjectHub Web 확장 정책

갱신일: 2026-09-26 (KST)

이 문서는 `extension/gptweb-hub`의 장기 정책을 정의한다.

제1조 (책임)

① Web 확장은 Worker가 관리하는 ChatGPT app window와 로컬 Worker 사이의 브리지 역할을 수행한다.
② 확장은 HQ 또는 RESOURCE 슬롯에서만 동작하며 일반 Chrome이나 수동 브라우저 연결 모드를 제공하지 않는다.
③ 확장은 ProjectHub의 Core 도메인, Server 중앙 서비스 또는 Agent 상태 수집 책임을 대신하지 않는다.

제2조 (연결 경계)

① HQ와 RESOURCE는 서로 다른 persistent profile과 서로 다른 conversationId를 사용한다.
② Worker가 app window 실행마다 발급한 runtime token이 없는 페이지는 로컬 bridge를 사용하지 않는다.
③ bridge 요청은 127.0.0.1 또는 localhost에만 보내며 runtime token을 전용 요청 헤더로 전달한다.
④ Worker는 올바른 runtime token이 없는 bridge HTTP 요청을 거부한다.
⑤ heartbeat는 연결 생존과 확장 상태 확인에 사용하며 작업 목적지의 의미를 결정하지 않는다.
⑥ Worker는 관리형 Web heartbeat에 30초의 생존 허용 구간을 사용한다.

제3조 (app window)

① 관리형 Chromium은 `--app=<ChatGPT URL>`로 실행하며 일반 탭 UI를 운영하지 않는다.
② 확장은 탭 조회·생성·삭제를 담당하지 않는다.
③ 이전 브라우저 세션의 탭 복원 정보는 Worker가 새 app window 시작 전에 제거한다.
④ persistent profile의 로그인 쿠키와 계정 상태는 유지한다.
⑤ 로그인·표시 또는 숨김 전환은 기존 window 복원이 아니라 새 app window 실행으로 처리한다.
⑥ 숨김 app window는 최소화 또는 실제 window hide 상태를 사용하지 않고 화면 밖에서 정상 렌더링 상태를 유지하며, Chromium background timer·renderer·occluded-window throttling을 비활성화한다.

제4조 (작업 전달)

① 일반 HQ Web 작업은 Worker가 지정한 대화에서 텍스트 결과를 수집해 반환한다.
② RESOURCE 작업은 Worker가 전달한 자연어 요청을 ChatGPT Web에 보내고 생성된 파일 결과를 기계적으로 수집한다.
③ Send 제어 실행과 실제 메시지 전송 확인을 구분한다. composer가 비워졌다는 사실만으로 전송 완료를 확정하지 않는다.
④ 기존 assistant DOM이 재사용되는 경우 현재 Worker 메시지 전송이 확인된 뒤 assistant 텍스트 변화도 새 응답의 기계적 증거로 사용할 수 있다.
⑤ 확장은 Web 응답이나 생성 리소스의 의미적 품질을 판단하지 않는다.

제5조 (생성 파일 수집)

① 생성 파일 탐지는 이미지, 첨부, 다운로드 링크, 오디오·비디오 등 실제로 수집 가능한 결과를 대상으로 한다.
② CORS 또는 브라우저 권한 문제로 콘텐츠 스크립트가 직접 수집할 수 없는 허용된 ChatGPT/OpenAI 파일은 background service worker가 기계적으로 재시도할 수 있다.
③ Worker에서 Web으로 보내는 첨부 파일과 RESOURCE가 Worker로 반환하는 파일은 가능한 경우 SHA-256을 함께 전달하고 양쪽에서 다시 계산해 불일치를 실패로 처리한다.
④ 생성 파일의 최종 저장 경로와 경로 안전성 검사는 Worker 책임으로 둔다.

제6조 (확장 표면)

① 관리형 확장은 시각 패널을 사용자에게 표시하지 않고 bridge 기능만 실행한다.
② runtime token과 HQ 또는 RESOURCE 역할을 확인하지 못하면 content script는 bridge 초기화를 시작하지 않는다.
③ manifest는 필요한 저장소와 ChatGPT/OpenAI 파일 접근 권한만 유지하며 browser tabs 권한을 요구하지 않는다.
④ 일반 Chrome에 같은 unpacked extension 경로가 남아 있더라도 runtime token이 없으면 Worker와 연결되지 않는다.

제7조 (사용자 입력 첨부)

① HQ Web 작업에 사용자 첨부가 있으면 Worker는 BridgeTask의 attachments에 파일명, MIME, 크기, downloadUrl과 SHA-256을 포함한다.
② content script는 Worker의 인증된 loopback attachment URL에서 bytes를 가져와 SHA-256을 다시 계산하고 일치할 때만 ChatGPT file input에 File 객체로 추가한다.
③ 첨부 다운로드는 일반 bridge 요청과 같은 managed runtime token 헤더를 사용한다.
④ Web으로 전달하는 프롬프트에는 실제 첨부 파일과 함께 downstream WORK가 참조할 workspace staging 경로와 SHA-256 메타데이터를 제공할 수 있다.
⑤ 확장은 첨부 파일의 내용 의미나 적합성을 판정하지 않고 실제 bytes 전달과 hash 검증만 수행한다.

