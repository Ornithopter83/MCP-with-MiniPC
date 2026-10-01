# WEB Bridge 전송 계약

갱신일: 2026-10-01 (KST)

상위 공통 정책은 `Master-Polish.md`, Worker 책임은 `Worker-Polish.md`, 관리형 Web 확장 책임은 `Web-Polish.md`를 따른다.
이 문서는 ProjectHub Worker와 관리형 HQ/RESOURCE Web 확장 사이의 loopback HTTP wire만 정의한다.

제1조 (전송 경계)

① Bridge는 Worker가 소유한 loopback HTTP endpoint로만 제공한다.
② 관리형 Web 확장은 Worker가 발급한 현재 runtime token이 있는 경우에만 Bridge를 사용한다.
③ Bridge는 ProjectHub Server, Supabase 또는 NAS의 API를 대신하지 않는다.
④ Web 응답의 의미 판단, 작업 목적 결정과 리소스 품질 판단은 이 전송 계약의 책임이 아니다.

제2조 (인증)

① OPTIONS를 제외한 Bridge 요청은 `X-ProjectHub-Managed-Token` 헤더에 현재 runtime token을 포함한다.
② Worker는 token이 없거나 현재 token과 일치하지 않으면 요청을 거부한다.
③ 인증 실패의 HTTP 상태는 401이며 오류 식별자는 `managed_runtime_required`다.
④ runtime token은 Git, AI 프롬프트 또는 일반 운영 로그에 기록하지 않는다.

제3조 (역할과 conversation binding)

① 관리형 Web 역할은 `HQ` 또는 `RESOURCE`다.
② HQ와 RESOURCE는 서로 다른 conversationId와 역할 binding을 사용한다.
③ binding은 conversationId, 선택적 projectId와 role을 기계적으로 연결하며 일반 본문 의미로 role을 추론하지 않는다.
④ 현재 binding이 없는 임의 ChatGPT 페이지를 Worker 작업 대상으로 간주하지 않는다.
⑤ Worker가 HQ task에 새 conversation 시작을 기계적으로 지정한 경우 확장은 해당 task를 먼저 claim한 뒤 새 ChatGPT conversation으로 이동해 같은 task의 prompt를 첫 요청으로 전송하고 새 conversation을 HQ 역할에 다시 binding한다.

제4조 (Bridge endpoint)

① 현재 Bridge base path는 `/bridge`이며 다음 endpoint를 사용한다.
1. `GET /bridge/status`: Worker/확장 연결 상태를 조회한다.
2. `GET /bridge/projects`: Bridge가 노출하는 프로젝트 목록을 조회한다.
3. `GET /bridge/bindings/{conversationId}`: conversation binding을 조회한다.
4. `POST /bridge/bind`: conversation binding을 등록 또는 갱신한다.
5. `GET /bridge/task?conversationId=<id>`: 해당 conversation의 대기 task를 조회한다.
6. `POST /bridge/task`: Bridge task를 생성한다.
7. `POST /bridge/task/{taskId}/claim`: 대기 task를 현재 conversation이 claim한다.
8. `POST /bridge/task/{taskId}/result`: claim된 task의 결과를 제출한다.
9. `POST /bridge/heartbeat`: 관리형 확장의 생존과 버전 정보를 보고한다.
10. `POST /bridge/progress`: 현재 task의 기계적 진행 단계를 보고한다.
11. `POST /bridge/reset`: 지정 conversation 또는 task의 Bridge 상태를 정리한다.
12. `GET /bridge/attachment/{id}`: Worker가 준비한 첨부 bytes를 전달한다.
② 지원하지 않는 endpoint는 404와 `not_found` 오류를 반환한다.
③ 잘못된 JSON 요청은 400과 `invalid_json` 오류를 반환한다.

제5조 (task 식별과 lease)

① Bridge task는 taskId와 conversationId로 식별한다.
② claim은 task의 conversationId와 요청 conversationId가 일치하는 경우에만 허용한다.
③ Worker는 성공한 claim에 leaseId를 발급한다.
④ result와 progress는 현재 task의 taskId, conversationId와 leaseId를 사용해 현재 실행 구간을 상관한다.
⑤ HQ conversation 교대 중에도 이미 claim한 task는 claim 시점의 taskId, conversationId와 leaseId를 유지하며, 새 conversationId는 후속 HQ 역할 binding에 사용한다.
⑥ 완료 또는 실패한 task의 결과를 새 task로 해석하지 않는다.
⑦ 동일 conversation에서 서로 충돌하는 활성 task는 동시에 claim하지 않는다.

제6조 (HQ correlation KEY와 완료 표식)

① HQ task에 correlation KEY가 발급된 경우 확장은 result 제출 시 동일한 KEY를 함께 반환한다.
② Worker는 제출된 correlation KEY와 task의 KEY가 일치하지 않으면 결과를 수락하지 않는다.
③ 성공 HQ 응답은 현재 KEY 뒤에서 별도 줄의 `[RESPONSE=OK]`가 확인되어야 하며, 확장과 Worker는 그 줄까지를 현재 응답 범위로 취급한다.
④ Worker는 `[RESPONSE=OK]` 줄 이전의 의미 본문만 역할 결과로 전달하고, 완료 줄 뒤의 페이지 UI나 다른 텍스트는 현재 응답 결과에 포함하지 않는다.
⑤ KEY가 있어도 완료 줄이 아직 보이지 않으면 성공 결과로 제출하지 않는다.
⑥ HQ 응답 timeout 시 확장은 직전 답변 완료 여부를 한 번만 다시 요청할 수 있으며, 두 번째 timeout은 transport 오류로 종료한다.
⑦ KEY 생성 길이와 응답 범위 절단 규칙은 현재 `WebCorrelationContract` 구현과 테스트를 따른다.
⑧ KEY/완료 표식 감시 진행 이벤트는 기계 관측으로만 사용하며 응답 의미를 판단하지 않는다.

제7조 (첨부와 결과 파일)

① Worker가 Web으로 전달하는 첨부는 파일명, MIME, 크기, download URL과 가능한 경우 SHA-256을 포함할 수 있다.
② 확장은 Worker가 제공한 attachment endpoint에서 인증된 bytes를 받아 ChatGPT 입력에 준비한다.
③ Web 결과 파일은 text result와 분리된 파일 payload로 반환할 수 있다.
④ Worker는 RESOURCE 및 일반 Web 결과 파일의 bytes, SHA-256과 저장 경로 안전성을 검증한 뒤 로컬 결과 경로에 저장한다.
⑤ 파일 탐지, ChatGPT/OpenAI URL fetch fallback과 DOM 판정 세부는 `Web-Polish.md` 및 현재 확장 구현·테스트를 따른다.

제8조 (진행과 heartbeat)

① heartbeat는 conversationId, projectId, conversation title과 확장 version/build 같은 기계 정보를 전달할 수 있다.
② progress는 taskId, conversationId, leaseId, stage, 선택적 detail과 attempt를 전달한다.
③ heartbeat와 progress는 생존·진행 관측이며 task의 의미, 성공 여부 또는 다음 역할을 스스로 결정하지 않는다.

제9조 (구현 세부)

① CORS header, polling 주기, DOM selector, timeout, extension version/build 상수와 같은 변경 가능한 구현값은 장기 wire 의미로 고정하지 않고 현재 구현과 테스트를 원본으로 사용한다.
② payload record의 선택 필드가 추가되더라도 이 계약의 역할·인증·task 상관·파일 안전 경계를 약화하지 않는다.
