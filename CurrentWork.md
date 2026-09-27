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

① 상태는 중단이다.
② 완료하려 한 작업은 Worker 작업 표시줄 아이콘을 적용하면서도 MainWindow 시작 경로가 XAML 이미지 디코딩 실패로 중단되지 않게 하는 것이다.
③ 중단 지점은 XAML의 직접 ICO 로딩을 제거하고 실행 파일 아이콘을 `InitializeComponent()` 이후 안전하게 적용하도록 수정했으며 Windows CI에서 Restore와 Build는 통과했다. 전체 Test 완료와 실제 배포본 publish 후 Worker 시작 확인은 아직 끝나지 않았다.

제6조 (WEB)

① 상태는 완료다.
② 방금 완료한 작업은 관리형 HQ/RESOURCE Web 런타임의 현재 정책과 확장 전송 경계를 `Web-Polish.md` 및 전용 구현에 정리한 것이다.
③ 다음 작업은 미지정이다.
