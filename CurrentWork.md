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
② 완료하려 한 작업은 숨김 HQ/RESOURCE Web에서 실제 ChatGPT 응답이 생성됐는데 확장이 SEND_CONFIRM을 놓쳐 Worker가 PARALLEL_HQ_EXECUTION_FAILED로 종료하는 false negative를 방어하고 Worker의 기대 확장 version/build를 새 Web 감지 버전과 동기화하는 것이다.
③ 중단 지점은 Worker 기대 확장을 0.3.1 / 2026-09-26.10으로 맞췄고 새 Worker 빌드·게시 후 숨김 HQ 전송·응답 회수 E2E 확인이 남은 상태다.

제6조 (WEB)

① 상태는 중단이다.
② 완료하려 한 작업은 숨김 app window에서 polling과 DOM virtualization이 엇갈려 성공한 Send/assistant 응답을 놓치는 문제를 막기 위해 role-aware turn fingerprint baseline, MutationObserver send-evidence latch와 timeout 직전 DOM reconciliation을 추가하는 것이다.
③ 중단 지점은 GPTWeb-Hub 0.3.1 / build 2026-09-26.10, user/assistant role 분리, 현재 Send 이후 assistant 증거 제한, SEND_EVIDENCE_LATCHED·SEND_MUTATION_CONFIRMED·SEND_TIMEOUT_RECOVERED 계측과 회귀 테스트 반영까지 완료했고 실제 숨김 상태에서 같은 유형의 HQ 응답을 Worker가 정상 수집하는지 확인해야 한다.
