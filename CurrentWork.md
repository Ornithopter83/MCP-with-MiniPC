# 현재 작업

갱신일: 2026-09-26

제1조 (CORE)

① 상태는 완료다.
② 방금 완료한 작업은 Core의 장기 책임과 의존 경계를 `Core-Polish.md`로 분리한 것이다.
③ 다음 작업은 미지정이다.

제2조 (INFRASTRUCTURE)

① 상태는 완료다.
② 방금 완료한 작업은 Infrastructure의 외부 시스템 책임과 보안 경계를 `Infrastructure-Polish.md`로 분리한 것이다.
③ 다음 작업은 미지정이다.

제3조 (SERVER)

① 상태는 완료다.
② 방금 완료한 작업은 Server의 중앙 HTTP 서비스 책임과 프로젝트 경계를 `Server-Polish.md`로 분리한 것이다.
③ 다음 작업은 미지정이다.

제4조 (AGENT)

① 상태는 완료다.
② 방금 완료한 작업은 Agent의 개발 PC 상태 수집과 Server 통신 경계를 `Agent-Polish.md`로 분리한 것이다.
③ 다음 작업은 미지정이다.

제5조 (WORKER)

① 상태는 중단이다.
② 완료하려 한 작업은 관리형 HQ/RESOURCE Chromium에서 로그인·표시/숨김 버튼이 브라우저 프로세스를 재시작해 UI 프리징과 새 launch 탭을 만드는 문제를 제거하고, 실행 중인 기존 브라우저 창만 Show/Hide하도록 전환하는 것이다.
③ 중단 지점은 C# 창 전환, 역할별 탭 정리 generation 신호, 정책 반영까지 완료했고 새 Worker 빌드·게시 후 버튼 클릭 시 UI 비프리징, 기존 profile 유지와 단일 ChatGPT 탭 E2E 확인이 남은 상태다.

제6조 (WEB)

① 상태는 중단이다.
② 완료하려 한 작업은 ChatGPT Project/GPT URL의 `/g/.../c/<conversationId>`를 일반 `/c/<conversationId>`와 같은 대화로 인식하고, 실제 기존 Project/GPT 대화 탭을 Worker의 canonical launch 탭보다 우선 유지하면서 중복 ChatGPT 탭을 정리하는 것이다.
③ 중단 지점은 확장 0.2.2 / build 2026-09-26.8, 버튼 재정리 generation, background 탭 선택 규칙과 정적 문법 검증까지 완료했고 실제 세션 복원 상태에서 3개 이상 중복 탭이 하나로 수렴하는지 확인해야 한다.
