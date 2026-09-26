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
② 완료하려 한 작업은 HQ/WORK 본문에서 구조 마커가 설명문 뒤에 나타나도 안전하게 처리하도록 WORK_GRAPH_PATCH, WORK_ITEM_STATUS, RESOURCE_TYPE와 레거시 REPORT/VALIDATION REQUEST 파서를 위치 독립 방식으로 보강하는 것이다.
③ 중단 지점은 수정 전 실패 로그와 동일한 설명문 선행 형식, 중복 마커, JSON 뒤 설명을 방어하는 코드·회귀 테스트·계약 반영까지 완료했고 실제 .NET Worker 빌드·테스트 실행이 남은 상태다.

제6조 (WEB)

① 상태는 중단이다.
② 완료하려 한 작업은 관리형 HQ/RESOURCE 브라우저에서 확장 UI를 숨기고 역할 자동 연결, SEND_TRIGGERED와 실제 전송 확인 분리, 응답 수집, 첨부·RESOURCE 파일 SHA-256 검증을 수행하도록 Web bridge를 확장하는 것이다.
③ 중단 지점은 JavaScript 정적 문법과 Worker 연결점 확인까지 진행했고 실제 ChatGPT 로그인·자동 바인딩·HQ 송수신·RESOURCE 생성/다운로드 E2E가 남은 상태다.
