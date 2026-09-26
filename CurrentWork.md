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
② 완료하려 한 작업은 숨김 HQ Web에서 실제 assistant 응답이 생성됐는데 Worker가 회수하지 못하는 문제를 응답 회수 단계 기준으로 보강하고, 일반 HQ 응답의 비이미지 다운로드 파일도 로컬에 저장·검증·결과 Files로 노출하는 것이다.
③ 중단 지점은 Worker 기대 확장 0.3.2 / 2026-09-27.1, 일반 Web 결과 저장 경로 `Worker/web-results/<taskId>/`, SHA-256 저장 검증, AiRoleRunResult.Files 연결과 회귀 테스트를 반영했고 새 Worker 빌드·게시 후 숨김 HQ 응답/파일 회수 E2E 확인이 남은 상태다.

제6조 (WEB)

① 상태는 중단이다.
② 완료하려 한 작업은 role selector가 실제 ChatGPT assistant turn을 놓쳐도 현재 prompt 다음 conversation turn으로 응답을 회수하고, HQ 일반 응답에 PDF·ZIP·문서 등 다운로드 파일이 있으면 assistant turn 범위에서 탐지해 bytes와 SHA-256을 Worker에 함께 제출하는 것이다.
③ 중단 지점은 GPTWeb-Hub 0.3.2 / build 2026-09-27.1, ASSISTANT_TURN_DETECTED·ASSISTANT_TEXT_EXTRACTED·WEB_FILE_*·RESULT_POSTING 계측, prompt-container fallback, 일반 파일 TEXT_WITH_FILES 제출과 정적 JavaScript 검증까지 완료했고 실제 숨김 상태에서 응답 텍스트와 비이미지 파일을 함께 회수하는지 확인해야 한다.
