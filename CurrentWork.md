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
② 완료하려 한 작업은 메시지 및 작업 이력의 신규/추가 작업 입력에 파일 drag-and-drop과 화면 캡처 Ctrl+V 첨부를 추가하고, 첨부를 캐시·SHA-256 검증·workspace staging한 뒤 coordinator-first HQ/WORK와 `계약문서 무시` 직통 AI까지 동일하게 전달하는 것이다.
③ 중단 지점은 UI, 공통 attachment transport, direct AI, HQ CLI/Web, WORK worktree, legacy Web 연결과 회귀 테스트 코드를 반영했고 새 Worker Windows 빌드·게시 후 실제 파일 드롭, 캡처 붙여넣기, direct/일반 실행 E2E 검증이 남은 상태다.

제6조 (WEB)

① 상태는 중단이다.
② 완료하려 한 작업은 사용자 첨부 파일을 기존 BridgeAttachment 다운로드·SHA-256 검증·ChatGPT file input 경로로 HQ Web에 실제 전송하고 downstream WORK용 workspace staging 경로도 프롬프트 메타데이터로 전달하는 것이다.
③ 중단 지점은 기존 GPTWeb-Hub 0.3.0 / build 2026-09-26.9의 인증된 attachment 다운로드 파이프라인을 재사용하도록 Worker를 연결했고, 실제 ChatGPT Web에서 다중 파일·스크린샷 첨부 송신 E2E 확인이 남은 상태다.
