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
- COMPLETED checkpoint를 Worker가 주 작업공간 현재 branch에 fast-forward로 landing
- landing 성공 사실과 target branch / before / after HEAD를 결과 보고에 추가
- 결과를 HQ에 반환

Worker는 Git 명령 실행 환경과 안전 경계를 제공하지만 충돌 해결 내용을 선택하지 않는다. 주 작업공간이 dirty, detached HEAD, non-fast-forward 상태이면 force/reset/push를 사용하지 않고 `INTEGRATION_LANDING_FAILED`로 BLOCKED 처리한다.

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


### 2026-09-26 병렬 runtime 연결 이후 보강

- 중단된 이전 실행 중 main은 이미 병렬 관제 runtime 연결, RESOURCE/JUDGE/OBSERVATION WorkItem 귀속, WorkGraph persistence, 최대 동시 WORK 설정 UI, pipeline 점유 표시까지 구현된 상태였다.
- `63e0bdab51b84eb35d79a3fc778ca0145c673d13`: `GitWorktreeManager.LandIntegrationAsync`를 추가했다. 주 작업공간이 clean branch이고 Integration result가 현재 HEAD의 후손일 때만 `git merge --ff-only`로 반영한다. dirty/detached/non-fast-forward에서는 변경하지 않는다.
- `524e212dd2b2127a6820f8070c15e2ee589c5ce5`: kind=INTEGRATION WorkItem이 COMPLETED checkpoint를 만든 뒤 주 작업공간 landing까지 성공해야 최종 COMPLETED가 되도록 연결했다. landing 실패는 checkpoint resultRef를 보존한 `INTEGRATION_LANDING_FAILED` BLOCKED로 HQ에 돌려준다.
- `adc4307e57907f4c7965d65d6258ec91540523c5`: 병렬 USER_FOLLOWUP 복구 시 snapshot 파일 경로만 전달하지 않고 현재 WorkGraph 기계 상태를 HQ 본문에 직접 포함한다. Web HQ도 복구된 BLOCKED/COMPLETED 상태를 읽고 GraphPatch를 판단할 수 있다.
- 현재 실행 환경에는 .NET SDK가 없어 신규 테스트와 전체 solution 빌드는 아직 실행하지 못했다. Windows 환경 실검증이 필요하다.


### 2026-09-26 Integration 이후 기준 ref 연속성

- `3e3e1a461092d72935e99fd53cb64e3542495e61`: 주 작업공간 landing의 clean 검사에서 ProjectHub 자체 런타임 상태 폴더 `.projecthub`를 제외했다. 내부 기억/event 파일 때문에 Integration이 항상 dirty로 오판되는 경로를 막았다.
- `21dc6ac0ec7a6a9cd2b64b6bb064ef7b2675ec6a`: 성공한 Integration resultRef를 이후 새 WorkItem의 기계적 기본 baseRef로 승격했다. dependency 자체만으로 Worker가 의미적 base를 추론하지는 않는다.
- `bec9c6b3d6330d895accb99f15fbf1d10956299f`: USER_FOLLOWUP 등 새 병렬 실행 구간 시작 시 저장된 과거 `_gitTarget` 대신 현재 작업공간 Git HEAD를 다시 읽어 기준 ref를 갱신한다.
- 특정 dependency 결과에서 직접 이어야 하는 WorkItem은 HQ가 `baseRef`를 명시한다. 생략 시 현재 주 작업공간 HEAD 또는 가장 최근 성공 Integration resultRef가 기본값이다.


### 2026-09-26 Windows 검증 실행 스크립트

- `bin/ProjectHub_Worker_Parallel_Test.ps1`를 추가해 병렬 WorkGraph 관련 Worker 테스트, 전체 solution 테스트, Debug/Release 빌드, `git diff --check`를 한 번에 실행할 수 있게 한다.
- 자동 검증 뒤 실제 Explorer에서 max=1 회귀, max=4 병렬, 동적 SPLIT_REQUEST, Integration landing, sidecar 귀속, 취소/복구, END gate를 확인하는 수동 E2E 체크 항목을 출력한다.
- 이 Web 실행 환경에는 .NET SDK가 없어 스크립트 자체의 실제 dotnet 실행 결과는 아직 없다.


### 2026-09-26 병렬 상태 UI

- `1ac715f5b8e1d45b02eb980fce1c3379fb3b91af`: Pipeline의 작업 카드에 병렬 상태 요약을 추가했다.
- 표시 요약은 `RUN N/M · R <READY> · B <BLOCKED> · C <COMPLETED> · F <FAILED>` 형식이며 의미 요약 없이 WorkGraph의 기계 상태만 사용한다.
- 작업 카드 ToolTip에는 RUNNING WorkItem의 slot과 READY/BLOCKED/COMPLETED/FAILED WorkItem ID 목록을 표시한다.
- 병렬 실행 종료 시 상태 텍스트와 ToolTip을 제거하고 기존 모델 표시로 복구한다.
- `ParallelWorkUiFormatterTests`를 Windows 병렬 검증 스크립트 대상에 포함했다.


### 2026-09-26 max=1 동일 runtime 정리

- `d446365a17debaf3f9b2ba3102e7c2377389738c`: 새 Job은 `maxConcurrentWork=1`이어도 WorkGraph/Scheduler runtime을 사용하도록 전환했다.
- 따라서 max=1과 max=4의 차이는 실행 엔진이 아니라 슬롯 수뿐이며, max=1 회귀 검증도 동일 병렬 구조의 직렬 모드 검증이 된다.
- 병렬화 이전 continuation을 깨지 않기 위해 저장 WorkGraph가 없는 기존 continuation은 max=1일 때만 레거시 직렬 runtime으로 이어간다.
- 저장 WorkGraph가 있거나 maxConcurrentWork를 2 이상으로 올린 continuation은 병렬 runtime으로 복귀한다.
- `ParallelWorkActivationPolicyTests`를 추가했고 Windows 검증 스크립트의 핵심 테스트 필터에도 포함했다.


### 2026-09-26 UI 상세 목록과 Git 사전 차단

- `2469627236ab6f8a484cbc27588ff29ec1d0a20a`: 메시지/작업 이력 상단에 현재 WorkItem 상태 목록을 직접 표시하도록 추가했다. 기존 Pipeline 요약과 ToolTip도 유지한다.
- `993ed64c33f94fc92d633eae3159a3876bfb37d2`: 상태 목록에 CANCELED를 포함하고, Integration WorkItem은 `[I]`, BLOCKED/FAILED는 기계적 코드도 함께 표시한다.
- `2eab6a01eac0b6e82f021bb5755cfc12a6cbc59e`: 병렬 runtime이 HQ를 호출하기 전에 Git 저장소, HEAD commit, attached branch를 기계적으로 확인한다. 조건을 만족하지 않으면 WorkGraph 실행을 시작하지 않는다.
- `a705ae6f7a45db71693a85c7dcfaa275d4c56164`: 같은 Git 사전 검사를 실행 버튼과 작업 추가 사전 점검에도 연결해 사용자가 AI 호출 전에 문제를 확인할 수 있게 했다.
- 새 병렬 Job이 max=1에서도 WorkGraph runtime을 사용하므로 Git worktree 요구 조건도 동일하게 적용된다.
- 저장 WorkGraph가 없는 과거 continuation도 WorkGraph runtime으로 승격되므로 동일한 Git 사전 검사를 적용한다.
- Windows 핵심 검증 스크립트에 `ParallelWorkGitPreflightTests`를 추가했다.


## 22. 현재 구현 판정

소스 기준으로 계획한 핵심 구조는 모두 연결된 상태다.

구현 완료 범위:
- WorkGraph / GraphPatch / dependency / revision
- ParallelWorkScheduler와 설정 가능한 1~8 슬롯
- WorkItem별 Git worktree / branch / checkpoint
- 실제 Codex WORK executor와 WorkItem별 session
- HQ GraphPatch와 WORK SPLIT_REQUEST
- Integration WorkItem과 안전한 주 작업공간 landing
- RESOURCE / JUDGE / OBSERVATION의 workItemId 귀속
- WorkGraph persistence와 USER_FOLLOWUP 복구
- 새 Job의 max=1 동일 WorkGraph runtime
- 병렬 상태 Pipeline 요약과 WorkItem 상세 목록
- Git 저장소/HEAD/attached branch 사전 점검
- Windows 자동 검증 스크립트

아직 완료로 판정하지 않는 이유:
- 현재 Web 실행 환경에서는 .NET SDK를 사용할 수 없어 신규 C# 단위 테스트와 전체 solution build를 실제 실행하지 못했다.
- Explorer 실제 실행에서 max=1, max=4, SPLIT_REQUEST, Integration landing, sidecar 귀속, 취소/복구, END gate를 확인하지 못했다.

따라서 tasks/16은 코드 구현 단계는 종료하고 Windows 실검증 단계로 유지한다. 실검증 결과에 따라 회귀 수정이 생기면 이 작업 문서에 이어서 기록한다.


### 2026-09-26 최소 Git 준비 자동화

- `f7a4ef4820865901880d7e99d4f4b737400eb0e0`: `GitWorkspaceBootstrapper`를 추가했다. 작업 폴더가 Git 저장소가 아니면 `git init`을 기계적으로 수행하고, repository root / branch / HEAD / dirty 상태를 확인한다.
- `3ba2909987a243208254a28661d21625ac665e52`: 새 작업 실행과 병렬 USER_FOLLOWUP의 실제 시작 직전에 Git 준비 단계를 연결했다.
- 실행 버튼의 일반 사전 점검에서는 Git 미준비를 더 이상 클릭 불가 오류로 취급하지 않는다. 사용자가 실제 실행을 누른 뒤 Worker가 Git 준비를 먼저 처리한다.
- HEAD가 없거나 commit되지 않은 변경사항이 있으면 기준점 생성 경로(repository root)와 현재 branch를 사용자에게 표시하고 승인을 받는다.
- 승인 시에만 `git add --all` 후 `ProjectHub <projecthub@local>` 로컬 identity로 baseline commit을 생성한다. 전역 Git 사용자 설정은 변경하지 않는다.
- 사용자가 취소하면 HQ/WORK를 호출하지 않고 현재 입력 상태에 머문다.
- 초기 구현에서는 `.gitignore`와 프로젝트별 preset을 수정하지 않았으나, Windows worktree 실검증에서 추적된 검증 캐시의 긴 경로 문제가 확인되어 이후 Git hygiene 단계에서 보강한다.
- 기존 `origin` 자동 확인은 유지한다. remote가 없어도 로컬 Git + HEAD + attached branch 조건만 충족하면 병렬 WORK를 사용할 수 있고, 자동 push/pull/remote 생성은 하지 않는다.
- Windows 핵심 검증에 `GitWorkspaceBootstrapperTests`를 추가한다.


### 2026-09-26 Git 기준점 준비 중 중복 입력 차단

- `421d58781c238fc290cd0a496cc79390ae7bce2d`: Git 준비 시작부터 완료·취소까지 `_gitPreparationInProgress` 게이트를 추가했다.
- 기준점 확인창에서 사용자가 확인한 뒤 `git add --all` / baseline commit / HEAD 재확인이 끝날 때까지 실행 버튼과 작업 추가 버튼을 비활성화한다.
- 준비 중 실행 버튼과 작업 추가 버튼에는 `Git 준비 중...`을 표시해 클릭이 무시되는 상태임을 명시한다.
- 실행/작업 추가 event handler도 같은 게이트를 먼저 검사해 빠른 연속 클릭으로 Git 준비가 중복 시작되지 않게 한다.
- 기준점 생성 취소·실패·성공 어느 경로에서도 `finally`에서 게이트를 해제하고 버튼 상태를 다시 계산한다.


### 2026-09-26 명령 단위 transcript 복원

- `3ba67341de15d5ea7b8d0aa3671af9cdfc8c6cae`: 프로젝트 transcript를 Job 전체 통합 파일에서 사용자 명령 실행 구간별 파일로 되돌렸다.
- 최초 `실행`과 각 `작업 추가`는 transcript 시작 지점을 새로 잡고, 종료 시 그 구간에서 추가된 메시지만 별도 파일로 저장한다.
- 프로젝트 복구 시 과거 이벤트는 History 복구에만 사용하고 다음 명령 transcript에 자동 합산하지 않는다.
- 파일명은 `yyMMdd-HHmmss.txt`로 짧게 만들고 같은 초 충돌 시 `-02`, `-03` 접미사를 붙인다.
- `events/<jobId>.jsonl`은 세션 복구와 실시간 관측을 위한 Job 단위 원시 이벤트 스트림으로 그대로 유지한다.
- `6c8b7f649566e3b855756dbf62ea94e8f7314a5d`: transcript 파일명의 시각 문자열이 호출 시각의 offset을 그대로 사용하도록 고정해 환경 timezone에 따른 이름 변화를 없앴다.
- `CommandTranscriptPathUsesShortTimestampAndAvoidsOverwrite` 테스트를 추가했다.


### 2026-09-26 원격 최신 소스 Release 검증

- 원격 `main` `4b43568715791bd720c979aa9078dced75aa0ed6`를 기준으로 동기화했다.
- Git 준비 화면에서 `System.Windows.MessageBox`를 명시해 참조 모호성을 해결했다.
- Release 컴파일에서 확인된 파일 시스템 using 누락과 END 거부 보고 formatter 누락을 보완했다.
- Release solution build는 경고 0, 오류 0으로 성공했고 win-x64 publish와 배포 폴더 복사를 완료했다.
- 자동 테스트와 Explorer E2E는 이번에 실행하지 않았으며 계속 실검증 대기다.


### 2026-09-26 terminal WorkItem 재시도 패치 보강

- FAILED/COMPLETED/CANCELED WorkItem은 종료 기록으로 유지한다.
- 종료 항목에 대한 CANCEL은 멱등 no-op으로 수용해 동일 GraphPatch 안의 retry ADD와 dependency 교체가 불필요하게 원자 거부되지 않게 했다.
- HQ는 실패 작업 재시도 시 기존 종료 항목을 수정·취소하지 않고 새 ID WorkItem을 ADD하고 필요한 비종료 후속 dependency를 새 ID로 연결한다.
- `TerminalCancelIsIdempotentAndDoesNotBlockRetryPatch` 회귀 테스트를 추가했다.
- 현재 Web 실행 환경에는 .NET SDK가 없어 실제 dotnet test/build는 미실행이며 Windows 실검증이 필요하다.


### 2026-09-26 WorkGraph 단일 실행 경로와 JUDGE 반환 단순화

- maxConcurrentWork=1~8을 모두 동일 WorkGraph/Scheduler 실행으로 통합했다.
- 저장 WorkGraph가 없는 과거 continuation도 새 빈 WorkGraph에서 HQ 후속 GraphPatch로 이어지며 레거시 직렬 runtime을 사용하지 않는다.
- JUDGE 역할 출력 계약과 JUDGE -> WORK GOTO를 제거했다. Worker가 JEV raw 결과를 요청한 같은 WorkItem 세션에 직접 반환한다.
- HQ/WORK의 PARALLEL 조건부 계약과 WORK의 JUDGE 활성/비활성 조건부 계약을 제거했다.
- JUDGE 비활성 요청은 JUDGE_UNAVAILABLE BLOCKED 상태로 HQ에 노출하고 Worker가 의미적 대안을 선택하지 않는다.
- 현재 Web 환경에는 .NET SDK가 없어 자동 테스트/빌드는 미실행이며 Windows 검증 대상이다.


### 2026-09-26 repository 단위 worktree 준비 직렬화

- WorkGraph 슬롯은 계속 병렬 실행하지만 동일 repository의 `GitWorktreeManager.PrepareAsync` 준비 구간은 repository root 기준으로 직렬화한다.
- 공유 `.git/worktrees` 및 branch metadata를 변경하는 `git worktree add`가 동시에 실행되지 않게 한다.
- `WORKTREE_CREATE_FAILED` 등 Git 준비 오류에는 실제 Git exit code와 stderr/stdout 상세를 보존해 WorkItem report와 HQ 기계 상태에서 원인을 확인할 수 있게 한다.
- 동시 Prepare 직렬화와 오류 상세 전파 회귀 테스트를 추가한다.


### 2026-09-26 WORK 시작 전 준비 실패 복구

- `WORKTREE_*`는 Codex WORK 세션 시작 전 실패이므로 새 실행부터 `FAILED`가 아니라 `BLOCKED`로 저장한다.
- USER_FOLLOWUP의 현재 Git preflight가 성공하면 preparation BLOCKED를 같은 WorkItem으로 재활성화한다.
- 과거 snapshot 호환을 위해 sessionId/resultRef가 없는 `WORKTREE_*` FAILED 중 현재 열린 dependency가 직접 참조하는 항목만 재활성화하고, 참조되지 않는 이전 실패는 기록으로 보존한다.
- 실패한 `git worktree add -b`가 branch만 남긴 경우 동일 base commit이며 다른 worktree에 연결되지 않은 ProjectHub branch만 안전하게 재사용한다.
- 이미 READY인 graph를 변경 없이 진행할 수 있도록 `operations: []`을 revision 불변 no-op GraphPatch로 허용한다.
- USER_FOLLOWUP에는 이전 HQ raw ACTION/GOTO/GraphPatch와 과거 실행 상태 문자열을 다시 주입하지 않고 현재 WorkGraph 기계 상태와 사용자 요청을 사용한다.
- 회귀 테스트는 legacy preparation failure 선택 복구, current preparation block 복구, no-op patch, residue branch 재사용, worktree 실패 BLOCKED 귀속을 포함한다.


### 2026-09-26 범용 Git ignore와 longpaths 보강

- `.verification-appdata/.../shader_cache/... `가 추적된 상태에서 Windows `Filename too long`으로 worktree checkout이 실패한 실사용 사례를 기준으로 Git 준비 계층을 보강했다.
- `PrepareAsync`는 repository local `core.longpaths=true`를 적용하고, ProjectHub 관리 ignore 갱신 필요 여부와 관리 경로의 현재 추적 여부만 검사한다.
- 실제 `.gitignore` 갱신과 `git rm --cached`는 사용자가 baseline 생성을 승인한 뒤 `CreateBaselineAsync`에서 수행해 취소 시 index를 변경하지 않는다.
- 기본 관리 ignore는 `.projecthub/`, `.verification-appdata/`, `.projecthub-worktrees/`와 명백한 OS/편집기 임시 파일이다.
- 신규 또는 아직 HEAD가 없는 저장소에는 Godot/Unity/.NET/Node 안전 preset을 파일 존재 기준으로 추가하고, 기존 저장소에는 프로젝트 preset을 새로 주입하지 않는다.
- 기존 사용자 `.gitignore` 내용은 보존하고 `# >>> ProjectHub managed` / `# <<< ProjectHub managed` 블록만 멱등 갱신한다.
- WorkItem worktree 경로 segment를 축소해 긴 경로 위험을 추가로 줄였다.


### 2026-09-26 HQ 설계 책임·PAUSE drain·History 작업 번호

- HQ 계약에는 `사용자의 요청에서 설계 기획에 관련된 부분은 반드시 HQ가 작업 수행한 뒤 구체화하여 WORK에 전달한다` 한 문장만 추가했다.
- PAUSE 수신 시 Scheduler는 새 READY 실행을 동결하고 현재 RUNNING WorkItem만 완료시킨다. RUNNING 결과가 graph에 반영된 뒤 Supervisor가 PAUSED snapshot을 반환한다.
- PAUSE 중 선행 WORK 완료로 새 WorkItem이 READY가 되어도 해당 실행 구간에서는 새로 시작하지 않는 회귀 테스트를 추가했다.
- 메시지 및 작업 이력 상단의 별도 병렬 WORK 상세 패널과 Pipeline 병렬 집계/ToolTip 노출을 제거했다.
- WORK 진행·응답 History 이벤트에 createdOrder+1 기반 WorkNumber를 전달해 왼쪽 역할명을 `작업 (#N)`으로 표시한다.
- 내부 WorkItem ID는 화면의 주 식별자로 반복 노출하지 않고 event log, ReferenceId, Full Message에서 추적한다.
- WorkGraph의 동시 실행 기능과 maxConcurrentWork는 변경하지 않았다.


### 2026-09-26 활성 WorkItem 게이지 크기

- `ImplementerWorkGaugeText` 네모 게이지의 `FontSize`를 24로 조정했다.
- Release 빌드 성공: 경고 0, 오류 0.


### 2026-09-26 단일 파일 빌드·복사

- 원격 `main` `c756233b9f1cbc5b7be66ee6f15625fb7167378a` Release 빌드 성공: 경고 0, 오류 0.
- self-contained win-x64 단일 파일 하나를 게시해 Worker 배포 경로로 복사했고 SHA-256 일치.
- 자동 테스트와 Explorer E2E는 실행하지 않았다.
