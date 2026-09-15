# 프로젝트 목표와 개발 기준 확정

## 목표

ProjectHub v0.1의 범위, 기술 기준, 보안 경계와 완료 조건을 확정한다.

## 현재 기준

설계 문서 기준으로 ASP.NET Core, Supabase, Agent 구조가 합의된 상태이며 실제 운영 환경값은 미정이다.

## 세부 작업

### A. 요구사항과 상태 판정 규칙 정리

- CLEAN, WORKING, OFFLINE, STALE, HEAD_MISMATCH, CONCURRENT_WORK의 판정 기준을 문서화한다.

### B. 인증·비밀값 경계 확정

- Server만 Supabase Service Role Key를 보유하고 Agent는 Agent API Key로 Server에 접근한다.

### C. v0.1 수용 시나리오 확정

- heartbeat, Git 상태, 프로젝트 조회, 60초 offline 판정, 서버 재시작 후 상태 조회 시나리오를 확정한다.

## 진행

잔여 작업 3개 (A, B, C)

## 변경 금지

- 자동 Git 조작, MCP, NAS, 원격 shell, 자체 DB 도입을 v0.1 범위에 넣지 않는다.

## 완료 기준

- 기술·보안·수용 기준이 구현 task에서 참조 가능한 문서로 확정된다.

## 검증 방법

- 로드맵과 task 간 범위가 일치하는지 문서 검토

## 결과

- 아직 수행하지 않음
