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
② 방금 완료한 작업은 Coordinator-first의 화면 작업 폴더를 최종 target workspace이자 독립 Git root로 고정하고, 상위 Git 저장소를 잘못 승계해 .gitignore가 상위 배포 폴더에 생성되는 경로를 차단한 것이다.
③ WorkItem 격리와 END 종료 게이트의 안전한 ff-only landing 규칙은 유지한다.

제6조 (WEB)

① 상태는 완료다.
② 방금 완료한 작업은 HQ 응답에서 correlation KEY 한 줄만 먼저 보인 상태를 완성 응답으로 오인하지 않게 하고, streaming 종료를 주기적으로 재확인해 Worker result 제출이 무기한 멈추는 경로를 보강한 것이다.
③ 확장 식별자는 0.4.2 / 2026-09-28.1이다.
