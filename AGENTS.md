# ProjectHub 작업 지침

- 변경 전 `ProjectHub_IMPLEMENTATION_PLAN.md`, `CurrentWork.md`, 활성 task 파일을 먼저 읽는다.
- 한 번에 하나의 번호 작업과 하나의 A/B/C 작업만 수행한다.
- 완료일, 잔여 작업 식별자, 결과와 검증 명령을 문서에 갱신한다.
- 무관한 사용자 변경과 공개 계약을 보존한다.
- 변경 규모에 맞는 빌드·테스트를 실행하고 실제 결과만 기록한다.
- 비밀키, 자격증명, 토큰, 결제정보, 민감한 URL을 코드·문서·로그에 남기지 않는다.
- 명시적 승인 없이 commit, push, 배포, 외부 시스템 변경을 수행하지 않는다.
- 저장소 동기화가 필요한 작업에서는 먼저 `git fetch`와 `git pull --rebase`를 완료한 뒤, 동기화된 최신 `GPT-Web-Feedback.md`를 반드시 읽고 분석한다. 이 파일은 기존 정책과 활성 task를 보조하며 충돌 시 기존 정책과 task를 우선한다.
- 동기화 전 로컬에 있던 `GPT-Web-Feedback.md`를 최신 피드백으로 간주하지 않는다. 동기화 완료 후의 파일과 커밋 상태를 기준으로 판단한다.

## 프로젝트 기준

- ProjectHub v0.1은 개발 PC Agent → Mini PC Server → Supabase 흐름을 따른다.
- v0.1 서버는 읽기/상태 수집 중심이며 자동 commit, push, reset, merge, 삭제, 원격 shell 실행을 하지 않는다.
- Supabase Service Role Key는 Server에만 환경 변수로 제공하고 Agent에는 배포하지 않는다.
