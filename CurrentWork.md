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
② 완료하려 한 작업은 CODE_CHANGE checkpoint에 Commit Manifest를 생성해 HQ와 후속 WORK에 전달하고, WORK/INTEGRATION AI 실행 중 Git metadata를 작업공간 밖으로 격리하며 Git 원격 프로토콜을 차단하는 것이다.
③ 중단 지점은 Commit Manifest, Integration dependency snapshot, Git metadata 격리·복원, Git 원격 프로토콜 차단과 회귀 테스트 소스 반영까지 완료했으며 실제 Windows Worker 빌드·테스트·게시 후 실행 검증이 남은 상태다.

제6조 (WEB)

① 상태는 완료다.
② 방금 완료한 작업은 GPTWeb-Hub 0.4.1 / build 2026-09-27.10에서 Web UI 이상을 통합로그 관측으로만 기록하고 task·conversation·KEY·전송 흐름에는 자동 개입하지 않도록 정리한 것이다.
③ 다음 작업은 미지정이다.
