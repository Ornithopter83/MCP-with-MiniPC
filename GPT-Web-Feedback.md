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

