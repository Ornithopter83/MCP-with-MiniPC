# GPTWeb-Hub 확장

버전: 0.2.0 / build 2026-09-26.4

ProjectHub Worker와 ChatGPT Web 대화를 루프백 브리지로 연결한다.

제1조 (관리형 브라우저 모드)

① Worker가 관리하는 HQ 브라우저와 RESOURCE 브라우저에서는 역할이 브라우저 슬롯에서 정해진다.
② 각 슬롯은 별도의 persistent profile을 사용하며 로그인 상태와 역할 정보를 서로 공유하지 않는다.
③ 사용자가 로그인 뒤 ChatGPT 대화를 선택하면 확장이 현재 conversationId를 해당 슬롯의 HQ 또는 RESOURCE 역할로 자동 연결한다.
④ 관리형 모드에서는 확장 패널을 화면에 표시하지 않고 bridge 기능만 실행한다.
⑤ 수동 브라우저 호환 모드에서는 기존 확장 패널과 수동 역할 연결을 사용할 수 있다.

제2조 (작업 처리)

① 작업 조회, claim, 진행 보고와 결과 반환은 taskId, conversationId와 leaseId를 사용한다.
② 일반 HQ Web 작업은 assistant 텍스트를 TEXT_RESULT로 반환한다.
③ 새 assistant 응답은 새 turn, DOM 교체, message key 변경 또는 조건부 assistant 텍스트 변화로 탐지한다.
④ 기존 assistant DOM의 텍스트 변화는 현재 Worker 메시지가 실제 사용자 메시지로 전송된 것이 확인된 경우에만 새 응답 증거로 사용한다.
⑤ 전송과 응답 진행 단계는 Worker에 보고하며 Worker는 마지막 진행 체크포인트를 bridge 상태에 저장한다.

제3조 (첨부 업로드 검증)

① Worker가 Web으로 전달하는 첨부에는 가능한 경우 SHA-256이 포함된다.
② 확장은 Worker attachment endpoint에서 받은 실제 bytes의 SHA-256을 다시 계산한다.
③ 전달된 SHA-256과 실제 bytes가 다르면 첨부를 ChatGPT에 올리지 않고 실패 처리한다.
④ 일치가 확인된 파일만 ChatGPT file input에 넣는다.

제4조 (RESOURCE 파일 처리)

① RESOURCE 작업은 자연어 요청을 ChatGPT Web에 보내고 최신 assistant 결과에서 생성 파일 후보를 탐지한다.
② 이미지, 다운로드 링크, 첨부, 오디오와 비디오 등 실제로 수집 가능한 결과를 공통 resultFiles[]로 반환한다.
③ 콘텐츠 스크립트에서 직접 fetch할 수 없으면 허용된 ChatGPT/OpenAI 파일 호스트에 대해 background service worker가 재시도할 수 있다.
④ 다운로드한 bytes의 SHA-256을 계산해 resultFiles[]에 함께 전달한다.
⑤ Worker는 받은 bytes를 다시 SHA-256으로 검증한 뒤 작업공간 아래 안전한 최종 경로에 저장한다.
⑥ 현재 파일 bytes는 base64 resultFiles[]로 bridge에 반환한다. 브라우저 다운로드 이벤트를 Worker 로컬 파일로 직접 이전하는 방식은 아직 사용하지 않는다.

제5조 (RESOURCE 완료)

① 생성 파일 없이 텍스트 응답만 끝나면 제한 시간 뒤 resource_not_generated로 실패 처리한다.
② 생성 파일 후보가 확인되면 실제 로드·다운로드 가능 상태와 안정화 시간을 확인한다.
③ Worker는 한 RESOURCE 응답의 파일을 임시 파일에 먼저 기록한 뒤 최종 이름으로 원자적으로 이동한다.
④ 저장 완료 뒤 Worker는 경로, 크기와 SHA-256 receipt를 bridge task에 기록한다.
⑤ RESOURCE는 파일 저장까지만 수행하며 생성 결과의 코드 연결 또는 품질 판정을 수행하지 않는다.

제6조 (안전 경계)

① 브리지 호스트는 127.0.0.1 또는 localhost만 허용한다.
② 생성 파일 다운로드는 허용된 ChatGPT/OpenAI 파일 호스트로 제한한다.
③ RESOURCE 최종 저장 경로는 Worker가 작업공간 하위인지 다시 검증한다.
④ 확장은 Web 응답이나 생성 리소스의 의미적 품질을 판단하지 않는다.
