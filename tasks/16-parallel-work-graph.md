# 16 동적 병렬 WORK Graph

갱신일: 2026-09-25

정책 원본은 Master-Polish.md다.

## 1. 목표

ProjectHub의 단일 WORK 직렬 흐름을 동적 DAG 기반의 병렬 WORK 실행 구조로 확장한다.

사용자는 WORK 설정에서 최대 동시 실행 수만 지정한다. 예를 들어 `maxConcurrentWork=4`이면 Worker는 HQ가 승인한 READY WorkItem을 최대 네 개까지 동시에 실행한다.

병렬화 여부, 작업 분해, 의존성, 새 작업 추가·취소는 HQ가 의미적으로 판단한다. Worker는 의미 판단 없이 WorkGraph 상태, 실행 슬롯, 세션, 프로세스, Git branch/worktree, 기계적 완료 사실만 관리한다.

## 2. 복구 지점

구현 시작 전 원격 main의 다음 commit을 복구 기준으로 고정한다.

- 기준 commit: `000a478f6e21c25e8d89020137e93abed1cab5e2`
- 복구 branch: `recovery/pre-parallel-work-graph-20260925`
- 의미: 병렬 WORK Graph 정책·코드 변경이 시작되기 직전의 정상 기준선

복구가 필요하면 해당 branch 또는 commit을 기준으로 새 branch를 만들고 이후 병렬화 commit을 폐기한다. 강제 reset은 사용자가 명시적으로 지시하지 않는 한 수행하지 않는다.

## 3. 최종 구조

~~~text
                               ┌─ WORK A ───────────┐
                               ├─ WORK B ───────────┤
USER -> HQ -> WorkGraph ------>├─ WORK C ───────────┼─> Integration WORK -> HQ
          ^                    └─ WORK D ───────────┘
          |                           |
          |                      SPLIT_REQUEST
          |                           |
          +-------- GraphPatch <------+
~~~

고정 WORK-A/B/C/D 역할은 만들지 않는다. 실행 슬롯은 교체 가능한 기계 자원이다.

~~~text
Slot 1 -> WorkItem W17
Slot 2 -> WorkItem W23
Slot 3 -> WorkItem W31
Slot 4 -> 비어 있음
~~~

## 4. 핵심 불변식

- HQ만 WorkGraph의 의미를 설계한다.
- Worker는 의미적으로 새 WorkItem을 만들지 않는다.
- WORK는 자신에게 배정된 WorkItem만 수행한다.
- WORK는 추가 분리가 필요하면 SPLIT_REQUEST를 HQ에 보낸다.
- Worker는 이미 승인된 READY WorkItem을 빈 슬롯에 즉시 배정할 수 있다.
- 동시 쓰기 WorkItem은 서로 다른 Git worktree와 branch를 사용한다.
- Integration은 새 역할이 아니라 특수 목적의 WORK WorkItem이다.
- RESOURCE/JUDGE/OBSERVATION은 기존 책임을 유지하며 workItemId로 귀속한다.
- 모든 상태와 이벤트는 재시작 후 복구 가능하도록 작업공간 .projecthub 아래에 기계적으로 저장한다.

## 5. WorkItem 모델

초기 필드:

~~~text
id
kind                    NORMAL | INTEGRATION
goal
dependencies[]
state
createdOrder
baseRef
branch
worktreePath
sessionId
resultRef
resultSummary
failureCode
createdAtUtc
startedAtUtc
finishedAtUtc
~~~

`goal`과 dependency 의미는 HQ가 정한다. Worker는 문자열 의미를 해석하지 않는다.

### 상태

~~~text
PLANNED
  -> READY
  -> RUNNING
       -> COMPLETED
       -> FAILED
       -> BLOCKED
       -> CANCELED

BLOCKED
  -> READY      HQ GraphPatch 또는 dependency 변화로 해제
  -> CANCELED
~~~

READY 계산은 명시된 dependency 상태만 사용한다.

초기 규칙:
- dependency가 없으면 실행 가능한 PLANNED는 READY
- 모든 dependency가 COMPLETED면 READY
- dependency가 RUNNING/PLANNED/READY면 BLOCKED
- dependency가 FAILED/CANCELED이면 자동 성공 추론을 하지 않고 BLOCKED 유지
- HQ가 dependency 또는 상태를 변경하면 다시 기계적으로 계산

## 6. WorkGraph와 GraphPatch

WorkGraph는 현재 Job의 전체 WorkItem과 revision을 가진다.

~~~text
jobId
revision
maxConcurrentWork
items[]
~~~

HQ는 전체 Graph를 매번 다시 보내지 않고 revision 기반 GraphPatch를 반환할 수 있게 한다.

초기 Patch 연산 후보:
- ADD: WorkItem 추가
- CANCEL: 명시 WorkItem 취소
- SET_DEPENDENCIES: dependency 목록 교체
- SET_GOAL: 아직 RUNNING이 아닌 WorkItem 목표 변경
- SET_BASE_REF: 아직 RUNNING이 아닌 WorkItem 기준 ref 변경
- REQUEST_INTEGRATION: Integration WorkItem 추가와 동등한 명시 연산

Worker가 확인하는 것은 ID 중복, dependency 존재, self dependency, cycle, revision 일치 같은 기계적 Graph 유효성뿐이다.

작업 적합성, dependency가 의미적으로 맞는지, 분해 품질은 검사하지 않는다.

## 7. Scheduler

`ParallelWorkScheduler`를 MainWindow에서 분리된 실행 엔진으로 둔다.

책임:
- maxConcurrentWork 슬롯 유지
- WorkGraph의 READY 계산
- 안정적 순서로 슬롯 배정
- WorkItem별 CancellationToken 관리
- Codex 실행 시작/완료 사실 수집
- WorktreeManager와 CodexCliRunner 연결
- WorkItem 상태 이벤트 발행
- 모든 running task의 기계적 종료 대기

하지 않는 일:
- 어떤 작업이 더 중요한지 판단
- 새로운 작업 생성
- 실패를 의미적으로 수정
- 결과 품질 평가
- merge 충돌 자동 의미 해결

## 8. Git worktree

`GitWorktreeManager`를 신규 구성요소로 둔다.

worktree는 주 작업공간 내부에 중첩하지 않는다. Git worktree 간 경로 중첩을 피하기 위해 저장소의 형제 경로를 사용한다.

예상 경로:

~~~text
<workspace-parent>/.projecthub-worktrees/<repository>/<jobId>/<workItemId>/
~~~

branch 예:

~~~text
projecthub/<jobId>/<workItemId>
~~~

규칙:
- WorkItem 실행 전에 baseRef 존재 확인
- branch/worktree를 기계적으로 생성
- 동일 WorkItem 재개 시 기존 안전한 worktree 재사용 가능
- 다른 active WorkItem의 worktree를 공유하지 않음
- dirty/conflict/detached/rebase 같은 Git 위험 상태를 임의 해결하지 않음
- 완료 branch/result ref는 Integration까지 유지
- 정리는 Integration과 Job 종료 정책에 따라 명시적으로 수행

## 9. WORK 실행 계약

WORK 프롬프트에는 최소 다음 실행 컨텍스트를 기계적으로 제공한다.

~~~text
workItemId
workItemKind
goal
dependencies와 완료 결과 참조
baseRef
branch
worktreePath
RESOURCE/JUDGE/OBSERVATION 귀속 정보
~~~

WORK는 프로젝트 전체의 유일한 실행자가 아니라 WorkGraph의 한 노드를 수행한다.

완료 보고는 Integration이 소비할 수 있도록 최소 다음 사실을 포함하게 한다.
- 수행 범위
- 결과 ref/branch
- 빌드·테스트 등 실제 실행 사실
- 남은 blocker
- SPLIT_REQUEST 필요 여부
- Integration 주의사항

## 10. SPLIT_REQUEST

WORK가 실행 중 새 독립 작업을 발견해도 직접 새 Codex WORK를 시작하지 않는다.

~~~text
WORK W17
  -> SPLIT_REQUEST
  -> HQ
  -> GraphPatch ADD W29
  -> Worker
  -> W29 READY이면 빈 slot에 실행
~~~

동적 분할된 WorkItem의 baseRef는 재현 가능한 ref가 필요하다. 필요한 경우 원 WorkItem이 checkpoint commit/ref를 만든 뒤 HQ에 제안한다.

Worker는 SPLIT_REQUEST가 타당한지 판단하지 않는다.

## 11. Integration WorkItem

여러 병렬 branch를 최종 작업공간으로 합치는 의미 판단은 Integration WORK가 맡는다.

Integration WorkItem:
- kind=INTEGRATION
- 통합 대상 WorkItem을 dependencies로 명시
- 각 dependency의 resultRef/branch를 입력으로 받음
- 별도 integration worktree에서 실행
- merge/cherry-pick/rebase 중 어떤 의미적 통합 방식을 택할지는 WORK가 현재 코드와 목표를 보고 판단
- 충돌 해결, 전체 build/test, 통합 결과 commit 생성
- 결과를 HQ에 반환

Worker는 Git 명령 실행 환경과 안전 경계를 제공하지만 충돌 해결 내용을 선택하지 않는다.

## 12. RESOURCE / JUDGE / OBSERVATION

병렬화 후에도 역할 의미는 바꾸지 않는다.

변경:
- 요청/완료에 `workItemId` 추가
- 결과는 동일 WorkItem 세션으로만 반환
- RESOURCE는 기존 전역 FIFO 1건 실행을 유지
- JUDGE 결과는 요청한 WorkItem으로 반환
- OBSERVATION 요청 저장 위치 또는 메타데이터에 workItemId를 포함

WORK_RESULT_REQUIRED는 해당 WorkItem만 WAIT한다. 다른 슬롯의 WorkItem은 계속 실행한다.

## 13. 프로젝트 기억과 이벤트

`.projecthub/session-state.json`을 단일 WorkSessionId 중심 구조에서 WorkGraph snapshot 중심으로 확장한다.

보존 대상:
- WorkGraph revision
- maxConcurrentWork
- 각 WorkItem state
- sessionId
- baseRef
- branch
- worktreePath
- resultRef
- running/canceled/completed 기계 상태
- HQ session
- 마지막 HQ message

이벤트에는 가능한 경우 다음 필드를 추가한다.
- workItemId
- graphRevision
- slot
- branch
- worktreePath

재시작 시 Worker는 저장 상태를 기계적으로 복원한다. RUNNING이었다는 기록만으로 자동 AI 재호출하지 않고, 실제 프로세스/세션/작업공간 상태를 확인한 뒤 사용자 또는 HQ의 명시적 재개 흐름을 따른다.

## 14. UI

Pipeline의 WORK 카드는 하나를 유지한다.

예:
~~~text
작업
GPT-6 Luna
3 / 4 실행 중
~~~

세부 상태에는 최소 다음 집계를 표시한다.
- RUNNING
- READY
- BLOCKED
- COMPLETED
- FAILED

History는 WorkItem 이벤트를 기존 카드 체계에 추가하며 workItemId를 표시한다.

첫 구현에서는 복잡한 그래프 시각화를 만들지 않는다. 텍스트/목록 기반 상태부터 검증한다.

## 15. 설정

WORK 설정에 정수 `maxConcurrentWork`를 추가한다.

초기 권장:
- 기본값: 1
- 최소: 1
- 최대: 8

1이면 현재와 유사한 직렬 실행이 가능해 회귀와 비교 기준으로 사용한다.

모델/추론은 기존 WORK 설정을 모든 WorkItem에 공통 적용한다. WorkItem별 모델 선택은 이번 작업 범위에 넣지 않는다.

## 16. 구현 단계

### 단계 0 — 기준선과 문서
- 복구 branch 생성
- Master-Polish.md 목표 정책 반영
- 본 작업 계획 문서 활성화
- CurrentWork/구현 로드맵 동기화

### 단계 1 — WorkGraph 도메인
- WorkItem 상태/종류 모델
- WorkGraph snapshot
- GraphPatch 기본 연산
- dependency/cycle/revision 기계 검증
- READY 계산
- 단위 테스트

검증 게이트:
- 독립 WorkItem 여러 개가 동시에 READY
- dependency가 있는 WorkItem은 선행 완료 전 BLOCKED
- cycle/unknown dependency/self dependency 거부
- GraphPatch revision mismatch 거부

### 단계 2 — 병렬 Scheduler
- maxConcurrentWork
- READY queue
- slot 배정
- cancellation
- 완료/실패 이벤트
- fake executor 기반 단위 테스트

검증 게이트:
- max=4에서 네 작업 동시 실행
- 다섯 번째는 슬롯 해제 후 시작
- 한 작업 실패가 독립 READY 작업을 막지 않음
- 의미 판단 코드가 scheduler에 없음

### 단계 3 — Git worktree
- GitWorktreeManager
- WorkItem별 branch/worktree
- baseRef/checkpoint
- 정리 정책
- Git 위험 상태 fail-safe

검증 게이트:
- 동일 base에서 네 worktree 생성
- 서로 다른 파일 수정 독립 유지
- branch/ref 기록
- 충돌을 Worker가 자동 해결하지 않음

### 단계 4 — 실제 Codex WORK 병렬 실행
- CodexCliRunner 연결
- WorkItem별 sessionId
- progress/event 귀속
- WORK 프롬프트에 WorkItem context
- 실제 Luna N개 동시 실행

검증 게이트:
- maxConcurrentWork=4 실제 동시 Codex 프로세스
- WorkItem별 독립 session/worktree
- 종료 코드/usage/result 귀속

### 단계 5 — HQ GraphPatch와 동적 분할
- HQ 계약 확장
- GraphPatch transport
- SPLIT_REQUEST
- HQ 재호출 조건
- 실행 중 Graph revision 갱신

검증 게이트:
- WORK 실행 중 새 WorkItem 제안
- HQ가 승인하면 빈 슬롯에서 실행
- HQ가 승인하지 않으면 Worker가 생성하지 않음

### 단계 6 — Integration
- Integration WorkItem 생성
- dependency result 전달
- integration worktree
- 통합 결과 ref
- 전체 build/test

검증 게이트:
- 독립 branch 여러 개 통합
- 충돌 시 Integration WORK가 해결
- Worker가 의미적 conflict resolution을 하지 않음

### 단계 7 — 사이드카와 영속화
- RESOURCE workItemId
- JUDGE workItemId
- OBSERVATION workItemId
- WorkGraph persistence
- restart recovery
- USER_FOLLOWUP 회귀

### 단계 8 — UI와 E2E
- maxConcurrentWork 설정
- WORK 카드 N/M
- WorkItem 목록
- History 귀속
- 실제 프로젝트 병렬 E2E
- max=1 회귀 비교

## 17. 예상 영향 파일

신규 예상:
- `WorkGraph.cs`
- `WorkGraphPatch.cs`
- `ParallelWorkScheduler.cs`
- `GitWorktreeManager.cs`
- 관련 테스트 파일

큰 수정 예상:
- `MainWindow.xaml.cs`
- `MainWindow.xaml`
- `CodexCliRunner.cs`
- `ProjectWorkspacePersistence.cs`
- `WorkerTargetConfiguration.cs`
- `Contracts/RoleContractLoader.cs`
- `Contracts/HQ-ROUTING-CONTRACT.md`
- `Contracts/WORK-ROUTING-CONTRACT.md`
- `ResourceSidecarQueue.cs`
- `ObservationSidecarQueue.cs`

중간/작은 수정:
- JUDGE transport 연결부
- History/event 모델
- 테스트와 정책 문서

전체 예상은 약 15~25개 파일, 신규·수정 코드 약 2,500~4,000줄 범위다. 실제 리팩터링 결과에 따라 달라질 수 있다.

## 18. 커밋 전략

큰 일괄 commit을 피하고 다음 단위로 원자화한다.
- 정책/계획
- WorkGraph 모델
- GraphPatch 검증
- Scheduler
- worktree
- Codex 연결
- HQ 계약
- SPLIT_REQUEST
- Integration
- 사이드카 귀속
- persistence
- UI
- E2E 보정

각 단계에서 이전 commit으로 되돌릴 수 있게 한다.

## 19. 검증 전략

가능한 환경에서는 각 단계마다:
- `dotnet test ProjectHub.sln`
- `dotnet build ProjectHub.sln -c Debug`
- `dotnet build ProjectHub.sln -c Release`
- `git diff --check`

을 실행한다.

현재 Web 실행 환경에 .NET SDK가 없으면 빌드/테스트 미실행 사실을 기록하고, Windows ProjectHub 환경에서 다음 작업 전에 우선 검증한다.

최종 E2E:
1. maxConcurrentWork=1 회귀
2. maxConcurrentWork=4 독립 WorkItem
3. dependency 해제 후 동적 시작
4. SPLIT_REQUEST -> HQ -> GraphPatch
5. Integration
6. RESOURCE/JUDGE/OBSERVATION 귀속
7. 취소/재시작/USER_FOLLOWUP
8. HQ END와 outstanding 종료

## 20. 완료 기준

다음이 모두 만족되어야 작업 16을 완료로 본다.
- HQ가 동적 WorkGraph를 생성·변경할 수 있음
- Worker가 의미 판단 없이 READY WorkItem을 최대 N개 병렬 실행
- 동시 WorkItem이 독립 worktree/session을 사용
- 실행 중 SPLIT_REQUEST로 새 WorkItem을 HQ가 추가 가능
- Integration WorkItem으로 실제 병렬 branch 통합 가능
- RESOURCE/JUDGE/OBSERVATION 결과가 올바른 WorkItem으로 복귀
- WorkGraph가 재시작 후 복구 가능
- UI에서 현재 병렬 상태를 식별 가능
- max=1 회귀와 max=4 실제 병렬 E2E가 모두 통과


## 21. 진행 기록

### 2026-09-25 착수

- 구현 전 복구 branch `recovery/pre-parallel-work-graph-20260925`를 commit `000a478f6e21c25e8d89020137e93abed1cab5e2`에 생성했다.
- 정책/종합 계획 commit: `0c87a0021643efc00e147fead82ed45cb1bf4c91`
- 단계 1 첫 구현 commit: `49a51cbd0e259dd43f7bd3fffe2484f2117dd0d8`
- `WorkGraph.cs`에 WorkItem 종류/상태, snapshot, revision, maxConcurrentWork, dependency 기반 READY 계산, 실행 상태 전이를 추가했다.
- `WorkGraphPatch.cs`에 ADD/CANCEL/SET_DEPENDENCIES/SET_GOAL/SET_BASE_REF/SET_MAX_CONCURRENCY와 revision 검증 결과 모델을 추가했다.
- patch는 임시 graph에 원자적으로 적용한 뒤 unknown dependency, self dependency, cycle을 기계적으로 검증하고 성공 시에만 commit한다.
- Integration은 새 역할이 아니라 `WorkItemKind.Integration`으로 같은 WORK 역할 안에 표현한다.
- 단위 테스트는 독립 READY 순서, dependency 해제, revision mismatch, self/unknown/cycle 거부, 실패 dependency 차단, RUNNING 정의 불변, cancel dependency 차단, concurrency 범위, Integration dependency를 포함한다.
- 현재 실행 환경에는 .NET SDK가 없어 `dotnet test`와 빌드는 실행하지 못했다. 다음 Windows 검증에서 단계 1 테스트를 우선 실행한다.


### 2026-09-25 단계 2~3 기반

- ParallelWorkScheduler commit: `f084cbf7c20f7a1150042c14d60c0f74290f6704`
- GitWorktreeManager commit: `b2ade9b4bc4f52c4cdfb6e8009dc107477fdf376`
- Scheduler는 READY WorkItem을 안정적인 생성 순서로 빈 슬롯에 배정하고 maxConcurrentWork를 넘지 않는다.
- 실행 중 maxConcurrentWork 증가를 GraphPatch로 반영하면 새 슬롯을 즉시 채운다.
- 특정 RUNNING WorkItem CANCEL은 해당 실행 token만 취소하고 독립 WorkItem은 계속 진행한다.
- executor 실패는 해당 WorkItem FAILED로 귀속하며 실패 dependency의 후속 WorkItem은 BLOCKED를 유지한다.
- Job lifetime 취소 또는 scheduler 전체 취소 시 RUNNING/READY/BLOCKED WorkItem을 CANCELED로 기계적으로 전환한다.
- GitWorktreeManager는 baseRef를 commit으로 먼저 해석하고 WorkItem별 고유 branch/worktree를 만든다.
- worktree는 주 저장소 내부가 아니라 저장소 형제 `.projecthub-worktrees` 루트에 두어 중첩 checkout을 피한다.
- 기존 branch/path를 임의 재사용하지 않으며, 등록된 동일 WorkItem worktree만 기계적으로 재사용한다.
- dirty worktree 제거와 force remove를 금지한다.
- 현재 환경에는 .NET SDK가 없어 추가 단위 테스트는 아직 실행하지 못했다.


### 2026-09-25 병렬 전송 계약 기반

- 병렬 HQ 프롬프트에는 WorkGraph revision, 사용자 maxConcurrentWork, 기준 ref를 제공한다.
- HQ의 병렬 CONTINUE 본문은 `WORK_GRAPH_PATCH:` 뒤 JSON 한 건으로 제한하고 Worker가 전용 transport parser로 기계 검증한다.
- HQ GraphPatch는 ADD/CANCEL/SET_DEPENDENCIES/SET_GOAL/SET_BASE_REF/RELEASE를 사용할 수 있다.
- maxConcurrentWork는 사용자 설정이므로 HQ transport에서 SET_MAX_CONCURRENCY를 거부한다.
- 병렬 WORK 프롬프트에는 workItemId, kind, goal, dependencies, baseRef, branch, worktree와 이전 보고를 제공할 수 있다.
- 병렬 WORK가 HQ로 보고할 때 `WORK_ITEM_STATUS: COMPLETED|BLOCKED|SPLIT_REQUEST|FAILED` 상태 행을 사용하도록 전용 계약을 추가했다.
- 레거시 단일 HQ/WORK 프롬프트에는 병렬 계약을 노출하지 않아 현재 직렬 흐름을 깨지 않는다.


### 2026-09-25 실제 WORK 실행 어댑터 기반

- 사용자 병렬도 설정 commit: `2af057b40fe905d4f3e4c9d98c9f4834d21d88a9`
- WorkItem checkpoint commit: `2ce5a6880fe116de1c53f891fadfaf9374389faf`
- Codex WorkItem executor commit: `d1e4aee004b894efcaa6d92d9dd61e7111208c12`
- WorkerTargetSettings에 maxConcurrentWork를 추가하고 기본값 1, 유효 범위 1~8을 사용한다. 저장된 잘못된 값은 덮어쓰지 않고 runtime에서 1로 해석한다.
- WorkItem 전용 worktree의 변경은 HQ 보고 전에 로컬 checkpoint commit으로 기계적으로 고정할 수 있다. push와 force는 수행하지 않는다.
- CodexWorkItemExecutor는 WorkItem별 worktree를 준비하고 기존 WORK 역할 계약으로 Codex를 실행한다.
- 병렬 WORK의 HQ 보고는 WORK_ITEM_STATUS를 파싱해 COMPLETED/FAILED/BLOCKED/SPLIT_REQUEST를 scheduler outcome으로 변환한다.
- JUDGE/RESOURCE 요청은 해당 WorkItem만 JUDGE_REQUEST/RESOURCE_REQUEST 상태로 BLOCKED하여 이후 전용 라우팅이 이어받을 수 있게 한다.
- dependency의 resultRef/resultSummary를 후속 WorkItem 프롬프트에 기계적으로 전달한다.
- 실행 결과에는 branch/worktree/sessionId를 함께 반환해 WorkGraph 실행 문맥에 보존한다.
- 아직 MainWindow 관제 루프와 실제 병렬 scheduler를 연결하지 않았으므로 현재 사용자 실행 경로는 기존 직렬 WORK 흐름을 유지한다.
