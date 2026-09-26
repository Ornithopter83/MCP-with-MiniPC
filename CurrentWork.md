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
② 완료하려 한 작업은 HQ/RESOURCE용 Chromium을 일반 탭 브라우저가 아닌 clean ChatGPT app window로 매번 새로 시작하고, persistent profile의 로그인 정보는 유지하면서 이전 browser session/tab restore 정보만 제거하며, 로그인·표시/숨김 전환의 프로세스 종료 대기를 UI thread 밖으로 이동하는 것이다.
③ 중단 지점은 app mode 실행 인자, session restore 정리, fresh visible/hidden 재시작, managed runtime token 전달, 정책·테스트 반영까지 완료했고 새 Worker 빌드·게시 후 로그인 유지·단일 app window·비프리징 E2E 확인이 남은 상태다.

제6조 (WEB)

① 상태는 중단이다.
② 완료하려 한 작업은 GPTWeb-Hub를 Worker 관리 Chromium 전용 bridge로 제한하고 일반 Chrome이나 runtime token이 없는 ChatGPT 페이지가 Worker와 연결되지 않게 하며 tabs 권한과 탭 정리 로직을 제거하는 것이다.
③ 중단 지점은 확장 0.3.0 / build 2026-09-26.9, managed-only preflight, X-ProjectHub-Managed-Token 검증, tabs 권한 제거와 JavaScript 정적 검증까지 완료했고 실제 일반 Chrome 비연결·HQ/RESOURCE 송수신 E2E 확인이 남은 상태다.
