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
② 완료하려 한 작업은 관리형 HQ/RESOURCE Chromium의 확장 버전이 서로 어긋나는 상태를 방어하기 위해 Worker 시작 시 ProjectHub 소유 잔존 Chromium을 정리하고, 역할별 실제 확장 version/build와 기대 version/build를 진단 표시하며, MainWindow.ManagedWeb.cs의 WPF Brush/Brushes 형식 모호성을 제거하는 것이다.
③ 중단 지점은 코드·정적 경계 테스트·정책 반영까지 완료했고 새 Worker 빌드·게시 후 기존 HQ/RESOURCE profile 로그인 유지, 두 역할의 extension build 동기화와 실제 송수신 E2E 확인이 남은 상태다.

제6조 (WEB)

① 상태는 중단이다.
② 완료하려 한 작업은 관리형 HQ/RESOURCE 브라우저에서 확장 UI를 숨기고 역할 자동 연결, SEND_TRIGGERED와 실제 전송 확인 분리, 응답 수집, 첨부·RESOURCE 파일 SHA-256 검증을 수행하도록 Web bridge를 확장하는 것이다.
③ 중단 지점은 JavaScript 정적 문법과 Worker 연결점 확인까지 진행했고 실제 ChatGPT 로그인·자동 바인딩·HQ 송수신·RESOURCE 생성/다운로드 E2E가 남은 상태다.
