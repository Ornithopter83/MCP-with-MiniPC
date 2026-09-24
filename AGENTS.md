# ProjectHub 작업 지침

- 변경 전 ProjectHub_IMPLEMENTATION_PLAN.md, CurrentWork.md, 활성 task 파일을 먼저 읽는다.
- AI 역할·라우팅 정책은 Master-Polish.md의 가장 최신 최종 정책을 원본으로 본다. 다른 문서의 과거 완료 기록이 충돌하면 이력으로만 해석한다.
- Worker 비판단 원칙: Worker는 흐름 제어 도구다. 작업 내용, 요구사항 충족, 테스트 충분성, JUDGE 결과, 리소스 품질을 의미적으로 판단하지 않는다. protocol/transport/session/schema/path-safety 같은 기계적 오류만 UNKNOWN으로 처리한다.
- 현재 신규 역할은 HQ / WORK / RESOURCE / JUDGE / UNKNOWN이다. HIGH와 one-shot permit은 사용하지 않는다.
- 신규 AI 출력 제어 계약은 ACTION/GOTO만 사용한다. 일반 body는 opaque다. JUDGE는 전용 schema를 기계적으로 검사하고, RESOURCE는 자연어 body가 비어 있지 않은지만 검사한 뒤 FIFO sidecar queue에 넣는다.
- HQ는 설계·관제 역할이며 ChatGPT Web 또는 CLI Provider로 실행할 수 있다. WORK는 CLI Provider 실행을 사용한다. RESOURCE는 별도 ChatGPT Web 대화에 고정하고, Worker 내부 single-reader FIFO queue가 한 번에 1건씩 실행한다. JUDGE는 JEV transport다.
- HQ Web과 RESOURCE Web은 서로 다른 conversationId에 명시적으로 binding한다. heartbeat는 생존 확인용이며 task 목적지 선택에 사용하지 않는다.
- RESOURCE는 최종 생성 이미지 제작·복수 이미지 다운로드·지정 경로 저장까지만 담당한다. 자동 코드/CSS/HTML 연결, 의미 기반 컴포넌트 선택, 자동 품질 판정은 하지 않는다. 현재 실제 RESOURCE transport는 IMAGE만 지원한다.
- History는 Worker가 이미 가진 role/state/usage/file telemetry로 만든다. AI 본문 tag나 source 문자열을 routing 판단에 사용하지 않는다.
- 한 번에 하나의 활성 구조 작업을 기준으로 수행하고, 완료/잔여/실제 검증 결과를 문서에 갱신한다.
- 변경 규모에 맞는 빌드·테스트를 실행하고 실제 결과만 기록한다. 실행 환경에 도구가 없으면 미실행 사실과 대체 정적 검증을 명시한다.
- 향후 실검증은 빌드된 Explorer 실행파일에서 HQ(Web/CLI), WORK, RESOURCE, JUDGE 흐름과 실제 이미지 저장을 우선 확인한다.
- 비밀키, 자격증명, 토큰, 결제정보, 민감한 URL을 코드·문서·로그에 남기지 않는다.
- 명시적 승인 없이 commit/push/배포/외부 시스템 변경을 수행하지 않는다.
- 저장소 동기화 작업에서는 최신 main과 GPT-Web-Feedback.md를 확인한다. 피드백이 정책 원본과 충돌하면 Master-Polish.md와 활성 task를 우선한다.

## 프로젝트 기준

- ProjectHub v0.2는 개발 PC Agent → Mini PC Server → Supabase 흐름을 보존한다.
- Git 동작은 충돌/detached HEAD/dirty pull/rebase 진행 상태를 자동 해결하지 않는다.
- Supabase Service Role Key는 Server 환경 변수에만 둔다.

- 역할 prompt의 대괄호는 실제 ACTION/GOTO 제어 토큰에만 사용하고, role/inbound/availability 같은 metadata는 평문으로 쓴다.

- 역할 contract는 장기 불변식만 담는다. 특정 사용자 요청, 테스트 시나리오, 도메인 예시, 파일명, 횟수/목록, 일회성 장애 대응 문구를 contract에 추가하지 않는다.
- contract 변경 전 “무관한 다른 Job에도 그대로 적용되는가?”를 확인한다. 아니라면 tests/fixtures/task history/validation note에만 둔다.
- 장애를 고칠 때 관측된 실패 문장을 그대로 contract에 넣지 말고, 필요한 경우 일반 protocol invariant로 최소화한다.
