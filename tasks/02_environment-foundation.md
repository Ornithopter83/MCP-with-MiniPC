# 개발환경 기반구축

## 목표

ProjectHub의 계층형 .NET 솔루션과 테스트 프로젝트를 준비한다.

## 현재 기준

.NET SDK 9.0.312에서 솔루션과 프로젝트 생성이 완료됐다.

## 세부 작업

### A. 솔루션·프로젝트 생성 (완료: 2026-09-15)

- Core, Infrastructure, 서버, 에이전트 및 Core/서버 테스트 프로젝트를 생성했다.

### B. 프로젝트 참조 연결 (완료: 2026-09-15)

- Infrastructure와 서버가 Core를 참조하고 서버가 Infrastructure를 참조하도록 연결했다.

### C. 기본 빌드 기준 확인 (완료: 2026-09-15)

- 복원 및 빌드를 실행해 생성 구조를 확인한다.

## 진행

잔여 작업 0개

## 변경 금지

- 외부 패키지는 실제 구현 task에서 필요성을 확인한 뒤 추가한다.

## 완료 기준

- 모든 프로젝트가 솔루션에 등록되고 참조 방향이 계층 규칙을 따른다.

## 검증 방법

- `dotnet build ProjectHub.sln`

## 결과

- A/B/C 완료. `dotnet build ProjectHub.sln` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-restore` 성공(2개 통과).
