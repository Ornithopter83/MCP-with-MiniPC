# 현재 작업

갱신일: 2026-09-27

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
② 완료하려 한 작업은 사용자 첨부 파일을 HQ Web에 전달할 때 로컬 bytes/SHA 검증 직후 Send를 시도해 ChatGPT 첨부 처리 중 Voice-only 상태를 실패로 오인하는 문제를 막고, Worker의 기대 확장 version/build를 새 첨부 준비 감지 버전과 동기화하는 것이다.
③ 중단 지점은 Worker 기대 확장을 0.3.3 / 2026-09-27.2로 맞추고 Web 첨부 준비 stage 정책과 회귀 테스트를 반영했으며 새 Worker 빌드·게시 후 로그 파일·스크린샷 첨부 상태에서 HQ Web Send E2E 확인이 남은 상태다.

제6조 (WEB)

① 상태는 중단이다.
② 완료하려 한 작업은 ATTACHMENT_BYTES_VERIFIED와 실제 ChatGPT ATTACHMENT_READY를 분리하고, file input 설정 뒤 첨부 UI·처리 상태와 활성 Send 버튼을 기다리며 첨부 요청의 Voice-only 2.25초 조기 실패를 제거하는 것이다.
③ 중단 지점은 GPTWeb-Hub 0.3.3 / build 2026-09-27.2, ATTACHMENT_BYTES_VERIFIED·ATTACHMENT_INPUT_SET·ATTACHMENT_UI_DETECTED·ATTACHMENT_PROCESSING·ATTACHMENT_READY·ATTACHMENT_READY_TIMEOUT 계측과 explicit attachment error 조기 실패를 반영했고 실제 숨김 Chromium에서 첨부 완료 뒤 Send→응답 회수까지 확인해야 한다.
