# ProjectHub

Mini PC를 중앙 프로젝트 상태 서버로 사용하는 ProjectHub v0.1 초기 골격이다.

## 구성

- `src/ProjectHub.Core`: 도메인 모델과 핵심 로직
- `src/ProjectHub.Infrastructure`: Supabase·Git·파일시스템 연동
- `src/ProjectHub.Server`: ASP.NET Core Minimal API
- `src/ProjectHub.Agent`: 개발 PC 상태 수집 Agent
- `tests/`: Core와 Server 테스트

## 실행

```powershell
dotnet run --project src/ProjectHub.Server
```

상태 확인: `GET /api/status`

구현 로드맵과 작업 순서는 [ProjectHub_IMPLEMENTATION_PLAN.md](ProjectHub_IMPLEMENTATION_PLAN.md)를 참고한다.
