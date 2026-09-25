# 프로젝트 목표와 개발 기준 확정

## 목표

ProjectHub v0.2의 범위, 기술 기준, 보안 경계와 완료 조건을 확정한다.

## 현재 기준

설계 문서 기준으로 ASP.NET Core, Supabase, 에이전트 구조가 합의된 상태이며 실제 운영 환경값은 미정이다.

## 세부 작업

### A. 요구사항과 상태 판정 규칙 정리

- CLEAN, WORKING, OFFLINE, STALE, HEAD_MISMATCH, CONCURRENT_WORK의 판정 기준을 문서화한다.

### B. 인증·비밀값 경계 확정

- 서버만 Supabase 서비스 역할 키를 보유하고 에이전트는 에이전트 API Key로 서버에 접근한다.

### C. v0.2 수용 시나리오 확정

- 생존 신호, Git 상태, 프로젝트 조회, 60초 offline 판정, 서버 재시작 후 상태 조회 시나리오를 확정한다.

## 진행

잔여 작업 3개 (A, B, C)

## 변경 금지

- 에이전트 자동 Git 조작은 금지하되, 사용자가 명시적으로 실행한 v0.2 Commit_Push/Fetch_Pull CMD의 Git orchestration은 허용한다. MCP, 원격 shell, 자체 DB 도입은 범위에 넣지 않는다.

## 완료 기준

- 기술·보안·수용 기준이 구현 task에서 참조 가능한 문서로 확정된다.

## 검증 방법

- 로드맵과 task 간 범위가 일치하는지 문서 검토

## 결과

- 아직 수행하지 않음
