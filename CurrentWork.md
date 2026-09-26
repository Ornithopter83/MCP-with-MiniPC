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

① 상태는 완료다.
② 방금 완료한 작업은 기존 Master의 Worker 세부 정책을 `Worker-Polish.md`로 분리하고 Master를 공통 영구 정책으로 축소한 것이다.
③ 다음 작업은 미지정이다.

제6조 (WEB)

① 상태는 완료다.
② 방금 완료한 작업은 ChatGPT Web이 기존 assistant DOM을 재사용해 텍스트만 갱신하는 경우에도 Worker 메시지 전송이 확인된 뒤 새 응답을 수집할 수 있도록 감지 조건을 보강한 것이다.
③ 다음 작업은 미지정이다.
