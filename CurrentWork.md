# 현재 작업

갱신일: 2026-09-28

제1조 (CORE)

① 상태는 완료다.
② 방금 완료한 작업은 Core의 장기 책임과 의존 경계를 `Core-Polish.md`로 분리해 현재 정책 원본을 명확히 한 것이다.
③ 다음 작업은 미지정이다.

제2조 (INFRASTRUCTURE)

① 상태는 완료다.
② 방금 완료한 작업은 Infrastructure의 외부 시스템 책임과 보안 경계를 `Infrastructure-Polish.md`로 분리해 현재 정책 원본을 명확히 한 것이다.
③ 다음 작업은 미지정이다.

제3조 (SERVER)

① 상태는 완료다.
② 방금 완료한 작업은 Server의 중앙 HTTP 서비스 책임과 프로젝트 경계를 `Server-Polish.md`로 분리해 현재 정책 원본을 명확히 한 것이다.
③ 다음 작업은 미지정이다.

제4조 (AGENT)

① 상태는 완료다.
② 방금 완료한 작업은 Agent의 개발 PC 상태 수집과 Server 통신 경계를 `Agent-Polish.md`로 분리해 현재 정책 원본을 명확히 한 것이다.
③ 다음 작업은 미지정이다.

제5조 (WORKER)

① 상태는 완료다.
② 방금 완료한 작업은 Coordinator-first의 화면 작업 폴더를 최종 target workspace로 고정하고, WorkItem 격리는 유지하면서 HQ END 종료 게이트에서 단일 미반영 CODE_CHANGE를 ff-only로 실제 작업 폴더에 반영하도록 보강한 것이다.
③ 미반영 CODE_CHANGE가 둘 이상이면 Worker가 자동 병합하지 않고 HQ에 Integration 필요 사실을 반환한다.

제6조 (WEB)

① 상태는 완료다.
② 방금 완료한 작업은 Worker 정책과 중복되던 관리형 Web 책임을 줄이고 확장의 페이지 연결·전송·관측·수집 경계를 `Web-Polish.md`에 명확히 한 것이다.
③ 다음 작업은 미지정이다.
