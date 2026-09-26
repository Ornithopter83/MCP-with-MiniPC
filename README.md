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

Worker는 HQ와 RESOURCE용 Web 브라우저 슬롯을 최대 두 개 관리할 수 있다. 두 슬롯은 서로 다른 persistent profile을 사용하고 평상시에는 화면 밖에서 실행하며, 사용자가 로그인하거나 대화를 선택해야 할 때 Worker UI에서 해당 브라우저를 표시할 수 있다.

관리형 브라우저에서 GPTWeb-Hub 확장은 시각 패널 없이 bridge 기능만 실행하고 현재 대화를 슬롯의 HQ 또는 RESOURCE 역할에 자동 연결한다. 수동 브라우저 연결 방식은 호환용으로 유지한다. 세부 정책은 `Web-Polish.md`에 둔다.

과거 실행 기록과 작업 계획은 정책 원본으로 사용하지 않는다.
