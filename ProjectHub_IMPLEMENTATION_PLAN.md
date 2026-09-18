# ProjectHub 구현 로드맵

Updated: 2026-09-17

## 목표

Mini PC의 ASP.NET Core 서버가 개발 PC Agent들의 Git·활동 상태를 수집하고 Supabase에 영속화하는 ProjectHub v0.2를 구축한다.

흐름: `Agent → ProjectHub.Server → ProjectService/Infrastructure → Supabase`

## 작업지시서

| No. | 작업 | 목적 | 상태 |
| --- | --- | --- | --- |
| 01 | [목표와 개발 기준](tasks/01_goal-and-standards.md) | 요구사항·보안·수용 기준 확정 | 대기 |
| 02 | [개발환경 기반구축](tasks/02_environment-foundation.md) | 솔루션과 프로젝트 골격 구성 | 완료 |
| 03 | [Server 스켈레톤](tasks/03-server-skeleton.md) | `/api/status`와 설정 기반 마련 | 완료 |
| 04 | [Supabase 스키마와 저장소](tasks/04_supabase-schema.md) | 중앙 상태 저장 계층 구현 | 완료 |
| 05 | [Agent heartbeat와 상태수집](tasks/05-agent-state.md) | PC·Git 상태 수집 | 완료 |
| 06 | [Large Data/NAS](tasks/06-large-data-nas.md) | 대용량 데이터 계약·Gateway·업로드 | 완료 |
| 07 | [프로젝트 배포 패키지](tasks/07-project-deployment-package.md) | Setup·Sync·Restore | 검증 중 |
| 08 | [Server 설치·이전](tasks/08-server-installation-migration.md) | Windows 11+ 재설치·연결 가이드 | 대기 |

## 순서

01 → 02 → 03 → 04 → 05 → 06 → 07 → 08 → 09

## 운영 규칙

- 번호 작업 하나, 세부 A/B/C 하나씩 진행한다.
- 완료 항목에는 날짜와 검증 근거를 남긴다.
- 새로 발견된 범위는 별도 후보로 기록하고 임의로 흡수하지 않는다.

## 변경 금지 경계

- v0.2의 사용자 명시적 `ProjectHub_Commit_Push.cmd`는 add/commit/fetch/pull --rebase/push 후 기존 Sync/checkpoint를 실행하고, `ProjectHub_Fetch_Pull.cmd`는 dirty·detached·충돌 상태를 자동 해결하지 않고 중단한다. reset/checkout/원격 shell/자동 빌드/MCP/자체 PostgreSQL·Redis·Docker는 구현하지 않는다. Large Data/NAS는 명시적 assertion·scope·Gateway 계약 안에서만 구현한다.
- Service Role Key와 Agent API Key는 저장소에 기록하지 않는다.

## 현재 상태

현재 작업: 07 프로젝트 배포 패키지 — 삭제 diff/tombstone 구현 완료, 운영 Server 배포 후 hw 탐색기 E2E 대기

잔여 작업: 새 Server 바이너리 배포 후 hw Sync 삭제 승인·tombstone 및 Restore 삭제 GUI E2E, 이후 08 Server 설치·이전

대용량 data plane은 `Agent → NAS Gateway → NAS1DUAL`, control plane은 `Agent → ProjectHub.Server → Supabase`로 분리하며 Server는 대용량 binary를 relay하지 않는다.

06 완료 기준: 정상 Agent는 대용량 파일을 자동 hash/upload/reconcile하지 않고 heartbeat·Git 상태만 관찰한다. 사용자가 `ProjectHub_Sync.ps1`을 명시적으로 실행하면 시작 시점의 고정 manifest와 control-plane 상태를 먼저 반영하고 별도 uploader가 동일 object의 기존 resumable session을 우선 재사용한 뒤 hash, assertion 갱신, chunk/status/resume/finalize, NAS identity 확인, Supabase STAGED 및 commit SHA 기반 CHECKPOINTED 기록을 수행한다. 업로드 중 파일이 변경되면 `CHANGED_DURING_UPLOAD`으로 제외하며 Git 변경은 자동 수행하지 않는다.
