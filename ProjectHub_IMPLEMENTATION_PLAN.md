# ProjectHub 구현 로드맵

Updated: 2026-09-16

## 목표

Mini PC의 ASP.NET Core 서버가 개발 PC Agent들의 Git·활동 상태를 수집하고 Supabase에 영속화하는 ProjectHub v0.1을 구축한다.

흐름: `Agent → ProjectHub.Server → ProjectService/Infrastructure → Supabase`

## 작업지시서

| No. | 작업 | 목적 | 상태 |
| --- | --- | --- | --- |
| 01 | [목표와 개발 기준](tasks/01_goal-and-standards.md) | 요구사항·보안·수용 기준 확정 | 대기 |
| 02 | [개발환경 기반구축](tasks/02_environment-foundation.md) | 솔루션과 프로젝트 골격 구성 | 완료 |
| 03 | [Server 스켈레톤](tasks/03-server-skeleton.md) | `/api/status`와 설정 기반 마련 | 완료 |
| 04 | [Supabase 스키마와 저장소](tasks/04_supabase-schema.md) | 중앙 상태 저장 계층 구현 | 완료 |
| 05 | [Agent heartbeat와 상태수집](tasks/05-agent-state.md) | PC·Git 상태 수집 | 완료 |
| 06 | [Large Data/NAS](tasks/06-large-data-nas.md) | 대용량 데이터 계약·Gateway·업로드 | 진행 |
| 07 | [Project 상태 API](tasks/06_project-status-api.md) | 프로젝트 상태 조회 API | 대기 |
| 08 | [멀티 PC와 동시작업 판정](tasks/07-multi-pc-concurrency.md) | lease·충돌 상태 검증 | 대기 |
| 09 | [검증·운영·확장](tasks/08-validation-operations.md) | 배포 검증과 후속 경계 확정 | 대기 |

## 순서

01 → 02 → 03 → 04 → 05 → 06 → 07 → 08 → 09

## 운영 규칙

- 번호 작업 하나, 세부 A/B/C 하나씩 진행한다.
- 완료 항목에는 날짜와 검증 근거를 남긴다.
- 새로 발견된 범위는 별도 후보로 기록하고 임의로 흡수하지 않는다.

## 변경 금지 경계

- v0.1에서 자동 Git commit/push/reset/checkout/merge, 파일 삭제·수정, 원격 shell, 자동 빌드, MCP, 자체 PostgreSQL·Redis·Docker는 구현하지 않는다. Large Data/NAS는 명시적 assertion·scope·Gateway 계약 안에서만 구현한다.
- Service Role Key와 Agent API Key는 저장소에 기록하지 않는다.

## 현재 상태

현재 작업: 06 Large Data/NAS A. ProjectHub-native 계약과 구현 경계

잔여 작업 4개 (06, 07, 08, 09)
