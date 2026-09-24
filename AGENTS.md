# ProjectHub 작업 지침

- 변경 전 `ProjectHub_IMPLEMENTATION_PLAN.md`, `CurrentWork.md`, 활성 task 파일을 먼저 읽는다.
- AI 역할·라우팅 정책은 `Master-Polish.md`의 **가장 최신 최종 정책 절**을 원본으로 본다. `CurrentWork.md`, 구현계획, task의 과거 완료 기록이 최신 Master와 충돌하면 과거 구현 이력으로만 해석한다. Worker는 최신 계약이 허용한 문법·상태 전이·세션·transport를 강제하되 작업 본문·테스트 주장·완료 여부를 의미적으로 재판정하지 않는다.
- **Worker 비판단 원칙:** Worker는 흐름 제어 도구다. 작업 내용, AC, 테스트, evidence, JUDGE/JEV 결과, 완료 여부를 스스로 평가하지 않는다. 판단과 다음 작업 선택은 AI 역할(HQ/WORK/JUDGE/HIGH)이 수행한다. protocol/transport/session 같은 기계적 오류만 UNKNOWN으로 HQ에 전달한다.
- **Opaque body 원칙:** 신규 CLI에서 AI 출력 제어 계약은 ACTION/GOTO만 사용한다. INSTRUCTION/REPORT/VALIDATION REQUEST/JUDGMENT 같은 semantic body tag를 요구·검색·삽입해 routing 또는 History에 사용하지 않는다. 제어행 뒤 전체 문자열을 opaque body로 전달한다.
- **History 표시 원칙:** 신규 CLI 이력 카드는 Worker가 이미 알고 있는 role/state/응답 완료/usage/file telemetry로 만든다. AI 본문 tag나 source 문자열을 역해석하지 않는다. 카드 본문은 1줄 축약(기계적 truncate + …), 2줄 토큰, 3줄 파일 변경 정보로 표시하며, 생성·수정·삭제 정보가 없으면 추정하지 않는다.
- 한 번에 하나의 번호 작업과 하나의 A/B/C 작업만 수행한다.
- 완료일, 잔여 작업 식별자, 결과와 검증 명령을 문서에 갱신한다.
- 무관한 사용자 변경과 공개 계약을 보존한다.
- 변경 규모에 맞는 빌드·테스트를 실행하고 실제 결과만 기록한다.
- 향후 실검증은 빌드가 완료된 Explorer 실행파일을 우선 실행하고, 실제 화면에서 메시지를 작성·전송해 결과와 연동 상태를 확인한다. Explorer 화면 검증이 불가능할 때만 CLI·API·직접 프로세스 호출 등 다음 가능한 대체 방법을 사용하고, 대체 검증임을 결과에 명시한다.
- 비밀키, 자격증명, 토큰, 결제정보, 민감한 URL을 코드·문서·로그에 남기지 않는다.
- 명시적 승인 없이 commit, push, 배포, 외부 시스템 변경을 수행하지 않는다.
- 저장소 동기화가 필요한 작업에서는 먼저 `git fetch`와 `git pull --rebase`를 완료한 뒤, 동기화된 최신 `GPT-Web-Feedback.md`를 반드시 읽고 분석한다. 이 파일은 기존 정책과 활성 task를 보조하며 충돌 시 기존 정책과 task를 우선한다.
- 동기화 전 로컬에 있던 `GPT-Web-Feedback.md`를 최신 피드백으로 간주하지 않는다. 동기화 완료 후의 파일과 커밋 상태를 기준으로 판단한다.

## 프로젝트 기준

- ProjectHub v0.2는 개발 PC Agent → Mini PC Server → Supabase 흐름을 따르며, 사용자의 명시적 승인에 한해 Git commit/push/fetch/pull을 수행할 수 있다.
- v0.2 Git 동작은 detached HEAD, dirty pull 대상, 진행 중 merge/rebase, 충돌, push reject를 자동 해결하지 않고 중단한다. reset, checkout, 원격 shell 실행은 계속 금지한다.
- Supabase Service Role Key는 Server에만 환경 변수로 제공하고 Agent에는 배포하지 않는다.

