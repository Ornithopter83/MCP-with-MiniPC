# Server 스켈레톤

## 목표

ASP.NET Core Minimal API가 `/api/status`에 ProjectHub 상태를 응답한다.

## 현재 기준

Server는 `/api/status` 초기 스켈레톤이며 Supabase 연결은 미구성이다.

## 세부 작업

### A. `/api/status` 응답 (완료: 2026-09-15)

- 서버명, ok 상태, Supabase 표기, 현재 시각을 반환한다.

### B. ProjectHub 서비스 등록

- Core 서비스와 Infrastructure 저장소의 DI 등록 경계를 마련한다.

### C. Server 테스트 추가

- 상태 응답과 기본 설정을 자동 검증한다.

## 진행

잔여 작업 2개 (B, C)

## 변경 금지

- 이 작업에서는 Supabase 쓰기나 Agent 인증을 구현하지 않는다.

## 완료 기준

- 서버가 실행되고 `/api/status`가 안정된 JSON을 반환한다.

## 검증 방법

- `dotnet build ProjectHub.slnx`
- `dotnet test ProjectHub.slnx`

## 결과

- A 완료: `/api/status` Minimal API 스켈레톤을 생성했다. B/C는 후속이다.
