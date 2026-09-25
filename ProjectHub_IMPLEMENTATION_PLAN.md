# ProjectHub 구현 로드맵

갱신일: 2026-09-25

정책 원본: Master-Polish.md

## 현재 구조

~~~text
                         ┌─ WORK Item A ─┐
                         ├─ WORK Item B ─┤
USER -> HQ -> WorkGraph ─┼─ WORK Item C ─┼─> Integration WORK -> HQ
                         └─ WORK Item D ─┘
                               │
                               ├─ RESOURCE_QUEUE (FIFO sidecar)
                               ├─ JUDGE
                               └─ OBSERVATION sidecar

Worker: 승인된 READY WorkItem의 슬롯/세션/worktree/전송/계측만 기계적으로 관리
HQ ACTION=END -> 열린 WorkItem이 없을 때 의미 종료 -> 기계적 outstanding 대기 -> DONE / DONE_WITH_ERROR
PAUSED / CANCELED / DONE / DONE_WITH_ERROR + 사용자 작업 추가 -> USER_FOLLOWUP -> HQ
~~~

미확인은 기계적 오류 상태이며 HQ에 한글 요약을 Job당 한 번 전달한다.

## 활성 작업 — 16 동적 병렬 WORK Graph

목표:
- 단일 WORK 직렬 실행을 동적 DAG 기반 병렬 WorkGraph로 확장
- HQ가 작업 분해·의존성·추가·취소를 의미적으로 결정
- Worker가 maxConcurrentWork 안에서 승인된 READY WorkItem을 기계적으로 병렬 실행
- WorkItem별 Codex session과 Git branch/worktree를 격리
- SPLIT_REQUEST와 GraphPatch로 실행 중 동적 작업 추가
- Integration WorkItem으로 병렬 결과를 통합
- RESOURCE/JUDGE/OBSERVATION과 프로젝트 기억을 workItemId 기준으로 확장

구현 단위:
1. WorkItem / WorkGraph / GraphPatch
2. dependency/cycle/revision 검증과 READY 계산
3. ParallelWorkScheduler와 maxConcurrentWork
4. GitWorktreeManager
5. WorkItem별 Codex session/progress/result 귀속
6. HQ GraphPatch transport와 WORK SPLIT_REQUEST
7. Integration WorkItem
8. RESOURCE/JUDGE/OBSERVATION workItemId 귀속
9. WorkGraph persistence/recovery
10. 병렬 상태 UI와 E2E

상세 계획과 복구 기준은 tasks/16-parallel-work-graph.md를 따른다.

## 보류 항목

- RESOURCE Web 동시 병렬 실행
- 자동 리소스 품질 판정
- 자동 코드/CSS/HTML 연결
- Claude/Muse 실제 CLI 연결
- 실행 중 프로세스 강제 종료 시점의 세부 checkpoint 복구 고도화
- 비용 기반 자동 정책

## 검증 기준

코드 변경 후 Windows 환경에서 solution test/빌드와 Explorer 실제 Web 왕복 검증을 완료하기 전까지 runtime 완료로 간주하지 않는다.



## 계약 유지 규칙

역할 계약에는 장기 역할 책임, ACTION/GOTO 문법, 전송 형식, 기계적 경계만 둔다. 특정 테스트·도메인·횟수·파일·장애 사례는 계약에 넣지 않고 tests/fixtures/작업 history에 둔다.


## 현재 종료와 후속 작업 정책

- HQ의 ACTION=END는 현재 실행 구간의 의미 작업 종료를 확정한다.
- Worker는 END 이후 현재 실행 구간에서 HQ/WORK/JUDGE 의미 흐름을 자동으로 다시 열지 않는다.
- 남은 RESOURCE 대기열을 포함한 기계적 대기 작업이 있으면 Worker가 대기 상태에서 완료만 기다린다.
- 대기 작업이 모두 끝나면 Worker가 DONE 또는 DONE_WITH_ERROR로 전환한다.
- RESOURCE 완료 이벤트는 HQ를 깨우지 않는다.
- PAUSE, CANCELED, DONE / DONE_WITH_ERROR 이후에도 HQ/WORK 세션과 작업공간은 유지한다.
- 실행 중 사용자 취소는 현재 프로세스를 중단하되 `thread.started`에서 확보한 CLI session ID를 보존한다.
- 사용자가 `작업 추가`를 실행할 때만 USER_FOLLOWUP으로 기존 HQ 세션에서 새 실행 구간을 시작한다.
- `새 작업`을 선택하면 이전 연속 세션과 이력을 명시적으로 초기화한다.
- 재개 가능한 상태는 작업공간 `.projecthub/session-state.json`에 저장하고 Worker 재시작 시 복구한다.
- 로컬 Codex 세션이 사라졌으면 저장된 session ID를 사용하지 않고 프로젝트 기억 파일과 이벤트 로그 경로를 새 HQ 세션에 전달한다.
- Worker 관측 메시지는 `.projecthub/events/<jobId>.jsonl`에 실시간 append하고 transcript는 `.projecthub/transcripts/<jobId>.txt`에 저장한다.

- RESOURCE 성공/실패 completion은 HQ END 전 다음 WORK 입력의 `RESOURCE_RESULT`로 전달하고, RESOURCE 실패를 UNKNOWN으로 승격하지 않는다.
