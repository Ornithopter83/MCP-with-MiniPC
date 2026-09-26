# ProjectHub

ProjectHub는 개발 PC, Mini PC 중앙 서비스, 공통 도메인·인프라, AI Worker와 ChatGPT Web 브리지를 분리해 구성하는 프로젝트다.

## 구성

- `src/ProjectHub.Core`: 공통 도메인 모델과 계약
- `src/ProjectHub.Infrastructure`: Supabase·파일시스템·NAS 등 외부 인프라 구현
- `src/ProjectHub.Server`: Mini PC의 ASP.NET Core 중앙 HTTP 서비스
- `src/ProjectHub.Agent`: 개발 PC 상태 수집 및 Server 통신
- `src/ProjectHub.Worker`: AI 관제와 로컬 작업 실행용 Windows Worker
- `extension/gptweb-hub`: ChatGPT Web과 Worker 사이의 브라우저 확장 브리지
- `tests/`: 각 프로젝트의 자동 테스트

## 정책 문서

- `Master-Polish.md`: ProjectHub 전체 공통 영구 정책
- `Core-Polish.md`
- `Infrastructure-Polish.md`
- `Server-Polish.md`
- `Agent-Polish.md`
- `Worker-Polish.md`
- `Web-Polish.md`
- `CurrentWork.md`: 프로젝트별 현재 상태 표지판

## Server 실행

```powershell
dotnet run --project src/ProjectHub.Server
```

상태 확인: `GET /api/status`

## Worker와 Web 런타임

Worker는 HQ, WORK, RESOURCE, JUDGE와 기계 계측 흐름을 관리한다. 세부 실행 정책은 `Worker-Polish.md`와 Worker 전용 계약 문서에 둔다.

HQ와 RESOURCE는 서로 다른 persistent profile의 Chrome for Testing을 사용한다. 로그인 정보는 profile에 유지하지만 브라우저를 시작할 때 이전 탭 복원 정보는 제거한다.

각 슬롯은 일반 탭 브라우저가 아니라 ChatGPT URL 하나를 여는 `--app` window로 실행된다. `로그인/표시`와 `숨김 실행`은 기존 창을 복원하지 않고 기존 슬롯 프로세스를 비동기로 종료한 뒤 새 visible/hidden app window를 시작한다.

GPTWeb-Hub는 관리형 Chromium 전용 bridge다. Worker가 발급한 runtime token이 없는 일반 Chrome과 임의의 ChatGPT 페이지는 Worker bridge에 연결할 수 없다. 세부 정책은 `Web-Polish.md`에 둔다.

Worker의 메시지 및 작업 이력 입력은 파일 drag-and-drop과 화면 캡처 이미지 Ctrl+V 첨부를 지원한다. 첨부는 최초 작업과 작업 추가, coordinator-first, HQ Web, WORK worktree와 `계약문서 무시` 직통 AI 실행에서 공통 attachment 흐름으로 전달된다.
