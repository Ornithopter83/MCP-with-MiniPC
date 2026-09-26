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
② 완료하려 한 작업은 숨김 HQ/RESOURCE app window가 시간이 지나면 heartbeat 대기 상태로 떨어지는 문제를 막기 위해 최소화·SW_HIDE 방식 대신 화면 밖 정상 렌더링 상태를 사용하고 Chromium background throttling을 비활성화하며 heartbeat 생존 허용 구간을 30초로 완화하는 것이다.
③ 중단 지점은 Worker 실행 인자, heartbeat 판정, 회귀 테스트와 정책 반영까지 완료했고 새 Worker 빌드·게시 후 장시간 숨김 상태에서 heartbeat 유지와 실제 HQ/RESOURCE 작업 송수신 E2E 확인이 남은 상태다.

제6조 (WEB)

① 상태는 중단이다.
② 완료하려 한 작업은 관리형 app window의 content script가 숨김 상태에서도 1.5초 refresh/heartbeat와 Web 자동화를 계속 수행하도록 브라우저 렌더러가 background throttling되지 않는 실행 환경을 보장하는 것이다.
③ 중단 지점은 확장 자체의 추가 변경 없이 Worker Chromium 실행 방식과 연결 생존 판정을 보강했고 실제 장시간 hidden app window에서 heartbeat·메시지 전송·응답 수집이 유지되는지 확인해야 한다.
