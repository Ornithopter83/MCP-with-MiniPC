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
② 완료하려 한 작업은 HQ/RESOURCE 관리형 browser profile에서 Worker launch 탭을 기준으로 ChatGPT 탭을 하나만 유지·활성화하고 같은 profile의 다른 ChatGPT 탭을 정리하도록 Web 런타임을 보완하는 것이다.
③ 중단 지점은 확장 코드, tabs 권한, Worker 내장 확장 계약 테스트와 정책 반영까지 완료했고 새 Worker 빌드·게시 후 HQ/RESOURCE profile별 단일 대화 탭 유지와 로그인 상태 보존 E2E 확인이 남은 상태다.

제6조 (WEB)

① 상태는 중단이다.
② 완료하려 한 작업은 관리형 launch 탭만 단일 탭 정리를 요청하고 background service worker가 같은 profile의 chatgpt.com 계열 탭을 직렬화해 정리하며 저장된 conversationId 대화 하나를 활성화하도록 GPTWeb-Hub를 확장하는 것이다.
③ 중단 지점은 확장 버전 0.2.1 / build 2026-09-26.6 반영과 정적 검증까지 완료했고 실제 ChatGPT 세션 복원 상황에서 중복 탭 제거·목표 대화 활성화·HQ/RESOURCE heartbeat 정상화를 확인해야 한다.
