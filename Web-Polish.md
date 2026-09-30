# Web-Polish — ProjectHub Web 확장 정책

갱신일: 2026-09-30 (KST)

이 문서는 `extension/gptweb-hub`의 장기 정책을 정의한다. Worker의 슬롯·task·runtime 생명주기 책임은 `Worker-Polish.md`에 두고, 이 문서는 관리형 Web 확장의 페이지 연결·전송·관측·수집 경계를 원본으로 둔다. Worker와 확장 사이의 loopback HTTP wire는 `src/ProjectHub.Worker/Contracts/WEB-BRIDGE-CONTRACT.md`를 전용 원본으로 사용한다.

제1조 (책임)

① Web 확장은 Worker가 관리하는 ChatGPT app window와 로컬 Worker 사이의 브리지 역할을 수행한다.
② 확장은 HQ 또는 RESOURCE 슬롯에서만 동작하며 일반 Chrome이나 수동 브라우저 연결 모드를 제공하지 않는다.
③ 확장은 ProjectHub의 Core 도메인, Server 중앙 서비스 또는 Agent 상태 수집 책임을 대신하지 않는다.

제2조 (연결 경계)

① HQ와 RESOURCE는 서로 다른 persistent profile과 서로 다른 conversationId를 사용한다.
② Worker가 app window 실행마다 발급한 runtime token이 없는 페이지는 로컬 bridge를 사용하지 않는다.
③ bridge 요청은 loopback 주소에만 보내고 runtime token을 인증 경계로 사용한다.
④ heartbeat는 연결 생존과 확장 상태 확인에 사용하며 작업 목적지의 의미를 결정하지 않는다.
⑤ heartbeat 허용 시간과 polling 주기 같은 수치는 현재 구현과 테스트를 원본으로 사용한다.

---

제3조 (app window)

① 관리형 Chromium은 HQ 또는 RESOURCE 역할별 app window로 실행하며 일반 탭 브라우저 운영을 확장 책임으로 두지 않는다.
② 확장은 탭 조회·생성·삭제를 담당하지 않는다.
③ Worker는 로그인 상태를 보존하면서 세션 복원과 창 재시작을 관리한다.
④ 숨김 실행에서도 정상 렌더링과 bridge 진행이 유지되어야 하며 구체 Chromium 플래그와 창 위치는 런타임 구현을 원본으로 사용한다.

---

제4조 (작업 전달)

① 일반 HQ Web 작업은 Worker가 지정한 대화에서 텍스트 결과를 수집해 반환한다.
② RESOURCE 작업은 Worker가 전달한 자연어 요청을 ChatGPT Web에 보내고 생성된 파일 결과를 기계적으로 수집한다.
③ 메시지 전송 확인은 제8조의 숨김 전송 확인 규칙을 따른다.
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

① HQ Web 첨부는 Worker가 제공한 인증된 attachment bytes와 메타데이터를 사용한다.
② content script는 가능한 경우 SHA-256을 다시 계산해 Worker가 제공한 값과 일치할 때만 ChatGPT 입력에 전달한다.
③ 첨부 내용의 의미·적합성은 확장이 판단하지 않는다.
④ file input, composer staging과 준비 판정의 DOM 세부는 현재 확장 구현과 테스트를 원본으로 사용한다.

---

제8조 (숨김 전송 확인)

① Send 제어 실행과 실제 메시지 전송 확인을 구분한다.
② 숨김 app window의 전송 확인은 단일 polling 신호에만 의존하지 않고 현재 conversation의 기계적 증거를 사용한다.
③ 이전 turn과 현재 turn을 구분하기 위한 baseline·mutation 증거는 현재 task 범위 안에서만 사용한다.
④ 전송 확인의 DOM selector, heuristic, 제한시간과 진행 단계 세부는 확장 구현과 테스트를 원본으로 사용한다.
⑤ HQ task의 현재 correlation KEY가 응답 영역에서 확인되면 해당 task 전송의 직접 기계 증거로 사용할 수 있으며 user/assistant turn selector가 현재 DOM을 인식하지 못했다는 이유만으로 SEND_CONFIRM 상태에 머물지 않는다.
⑥ HQ correlation KEY 감시는 DOM mutation 이벤트나 role·turn selector 한 종류에만 의존하지 않고 SEND_CONFIRM과 WAIT_RESPONSE 동안 주기적으로 현재 conversation을 재확인한다.

---

제9조 (assistant 결과 회수)

① Send 확인 뒤 assistant turn 감지와 결과 추출을 별도 기계 단계로 관리한다.
② HQ Web task에 correlation KEY가 있으면 현재 KEY가 확인된 응답 영역만 현재 task 결과로 제출한다.
③ KEY 이전 텍스트와 KEY가 없는 과거 turn은 현재 HQ 결과 의미 범위에서 제외한다.
④ 일반 응답 파일은 현재 응답 root 안에서만 수집해 사용자 첨부나 과거 turn 파일과 섞이지 않게 한다.
⑤ correlation KEY 한 줄만 확인된 상태는 완성된 HQ 응답 본문으로 취급하지 않고 KEY 뒤의 실제 본문이 생길 때까지 대기한다.
⑥ streaming 종료 판정이 새 DOM mutation에만 의존하지 않도록 응답 대기 중 기계적 재확인을 수행하며, 일반 HQ 응답도 제한시간 안에 안정화되지 않으면 무기한 대기하지 않고 기술 실패로 종료한다.
⑦ 파일 fetch fallback, response 안정화 주기와 DOM 탐지 세부는 현재 확장 구현과 테스트를 원본으로 사용한다.
⑧ HQ correlation KEY 탐지는 role·turn selector 결과에만 의존하지 않고 현재 conversation 본문에서 동일 KEY를 직접 찾는 기계 fallback을 사용하며, 요청 prompt 자체에 포함된 KEY는 응답 증거에서 제외한다.
⑨ 응답 root를 식별하지 못한 경우에도 현재 KEY 뒤에 HQ 계약의 ACTION이 이어지는 현재 conversation 본문을 보조 결과로 사용할 수 있다. 이 fallback은 같은 KEY가 포함된 요청 prompt와 구분되는 경우에만 사용한다.
⑩ 이미 claim된 HQ task에서 이전 turn baseline을 복구할 수 없더라도 현재 task의 correlation KEY가 있으면 같은 KEY만을 기준으로 응답 수신을 재개할 수 있다. 이 복구는 기존 prompt를 다시 전송하지 않는다.

---

제10조 (첨부 준비 상태)

① Worker에서 attachment bytes/hash 검증이 성공한 사실과 ChatGPT UI에서 첨부 업로드·처리가 준비된 사실을 구분한다.
② 확장은 첨부 UI와 실제 Send 가능 상태를 기계적으로 관찰해 준비 여부를 보고한다.
③ 명시적 첨부 오류는 실패 사실로 보고할 수 있으나 구체 조기 실패 시간과 DOM 판정 규칙은 구현·테스트를 원본으로 사용한다.

---

제11조 (Web UI 이상 관측)

① 관리형 HQ/RESOURCE 대화에서 assistant 응답과 별개인 오류·한도·timeout·첨부 실패 UI를 기계적으로 관측할 수 있다.
② 관측된 이상은 WEB_UI_ANOMALY_OBSERVED 진행 이벤트로 Worker 통합로그에 기록한다.
③ 동일 task의 동일 이상은 기계적으로 중복 억제할 수 있다.
④ UI 이상 관측만으로 task 실패, 대화방 이동, 재전송, role binding 변경, lease/KEY 변경 또는 자동 복구를 수행하지 않는다.
⑤ 실제 반복 증거와 재현 로그가 확보된 뒤 별도 정책 변경으로 개입 여부를 결정한다.
