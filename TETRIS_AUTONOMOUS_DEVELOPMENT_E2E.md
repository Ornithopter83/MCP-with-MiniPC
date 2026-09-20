# TETRIS Autonomous Development E2E Record

Updated: 2026-09-20

## 목적

이 문서는 ProjectHub Worker ↔ GPT Web ↔ Codex CLI 장기 왕복 구조를 사용해 TETRIS를 거의 자동으로 개발한 실제 E2E 사례를 기록하고, 다음 단계에서 파일 첨부 대신 Git/Server를 기준으로 소스 상태를 확인·피드백하기 위한 설계 기준을 남긴다.

## 실험 개요

대상 프로젝트:
- 경로: `C:\GameProject\TETRIS`
- 기술: .NET 9 / WPF
- 외부 NuGet 없음

역할 분리:
- GPT Web: Planner / Reviewer / Controller
- Worker: orchestration / protocol / transcript / usage aggregation
- Codex CLI: Executor / Tester / Reporter
- 사용자: 최초 목표 제시, 환경 문제 개입, 최종 체감 QA

첫 CLI 턴은 작업을 임의로 시작하지 않고 현재 상태를 조사해 `[REPORT 0]`으로 반환했다. GPT Web은 이 보고를 기준으로 전체 계획을 수립하고 이후 각 라운드마다 `[ACTION=CONTINUE|PAUSE|END]` 프로토콜로 다음 Codex 작업을 통제했다.

## 구현 완료 범위

기본 게임:
- 10x20 보드
- 7종 테트로미노
- 좌우 이동 / 회전 / Soft Drop / Hard Drop
- 충돌, 고정, 줄 삭제
- Game Over / Restart

UI:
- 보드 중심 dark UI
- 상태 카드
- 동적 Stage / Lines / Score
- Stage Clear / Game Over / Final Clear overlay
- 반응형 Viewbox

연출:
- 완성 라인을 즉시 삭제하지 않고 약 3회 점멸 후 삭제
- LineClearing 상태에서 입력/자동 낙하 잠금
- Stage Clear Curtain/Scan line
- BonusRows 계산 및 Stage Bonus 반영

Stage:
- Stage 1~5
- 낙하 간격: 500 / 400 / 320 / 250 / 190 ms
- Stage 5 완료 후 FinalClear
- Stage 6 생성 금지

점수:
- 1줄: 100 × Stage
- 2줄: 300 × Stage
- 3줄: 500 × Stage
- 4줄: 800 × Stage
- Stage Bonus: BonusRows × 100 × CurrentStage

사운드:
- 외부 음원 없이 코드 생성 PCM WAV
- Move / Rotate / SoftDrop / HardDrop / Line / StageClear / GameOver / Restart
- Stage 1~5별 코드 생성 BGM
- SFX는 SoundPlayer, BGM은 별도 BgmManager + WPF MediaPlayer
- Stage 전환 / Restart / GameOver / FinalClear에서 BGM 수명 관리

## 상태 머신

최종 핵심 상태:
- Playing
- LineClearing
- StageClear
- StageTransition
- FinalClear
- GameOver

중요 원칙:
- 비-Playing 상태에서 Move/Rotate/SoftDrop/HardDrop/Tick이 Board를 변경하지 않는다.
- Line Flash 중 새 Piece를 Spawn하지 않는다.
- Stage 목표 달성 즉시 다음 Stage로 넘어가지 않는다.
- Stage Clear Curtain/Bonus 종료 후 명시적으로 `CompleteStageTransition()`을 호출한다.
- Stage 5도 동일한 Curtain/Bonus를 거친 뒤 FinalClear가 된다.

## 실제 검증 결과

Core diagnostic:
- 152 / 152 PASS
- 1/2/3/4줄 삭제 실제 검증 PASS
- Stage 1 → 2 → 3 → 4 → 5 → FinalClear PASS
- TotalClearedLines 최종 25 PASS
- BonusRows edge case 0 / 1 / 12 / 19 / 20 PASS
- duplicate bonus / duplicate CompleteLineClear 방어 PASS
- Restart 전체 초기화 PASS
- 비-Playing 상태 안전성 PASS

Stress:
- 10,000 operations
- 202 Game Over
- 202 Restart
- exception 0
- invariant PASS

WPF:
- 격리된 APPDATA / DOTNET_CLI_HOME / NUGET_PACKAGES와 외부 intermediate/output 경로를 사용
- restore 성공
- build 성공
- warning 0 / error 0
- 최신 EXE 생성
- 실제 프로세스 실행 성공
- MainWindow 생성 성공
- responsive = true

최종 사용자 QA:
- 기능: 정상
- BGM: 적당
- SFX: 적당
- Curtain: 동작은 정상이나 단일 선보다 겹겹이 쌓이는 느낌의 연출을 선호

## 자동개발 관점에서 얻은 검증

이번 사례에서 사람은 세부 구현을 직접 지휘하지 않았다. 대부분의 흐름은 다음 순환으로 진행됐다.

1. Codex가 현재 상태 또는 작업 결과를 REPORT로 반환
2. GPT Web이 실제 보고 및 첨부 소스를 검토
3. 구조/버그/검증 결함을 찾아 다음 제한된 작업을 지시
4. Codex가 수정·검증
5. Worker가 다음 라운드로 전달
6. 완료 또는 사용자 판단이 필요한 경우만 PAUSE

특히 GPT Web이 Codex의 성공 보고를 그대로 신뢰하지 않고 진단 harness의 결함도 검출했다. 기존 `TestProgression()`이 각 줄 테스트마다 `Restart()`를 호출해 Stage progression 검증을 무효화하던 문제를 찾아 수정하게 했고, 이후 실제 `152/152 PASS` 및 10,000 operation 검증으로 이어졌다.

이 결과는 ProjectHub 장기 왕복 구조가 단순 질의응답이 아니라 다음을 수행할 수 있음을 보여준다.
- 계획
- 구현
- 코드 리뷰
- 재작업
- 테스트 설계
- 테스트 자체 검증
- 환경 문제 우회
- 최종 빌드
- 사용자 QA handoff

단, 완전 무인 개발로 간주하지 않는다. 화면/음향과 같은 체감 품질, 환경 권한 문제, 최종 수용 판단에는 사람 개입이 필요했다.

## 토큰 계측에서 발견된 문제

현재 Worker의 `이번 작업 누적` 토큰 값은 실제 Task 소비량으로 해석하면 안 된다.

Worker는 각 Codex CLI 결과의 `usage` 또는 `token_usage`를 파싱해 라운드마다 `_commandUsage.Add(result.Usage)`로 합산한다.

하지만 Codex CLI가 같은 resume session에서 반환하는 usage가 session cumulative snapshot이면 다음 문제가 생긴다.

예:
- round 1 session total = 300k
- round 2 session total = 600k
- round 3 session total = 900k

현재 Worker 표시:
- 300k + 600k + 900k = 1.8M

실제 최신 session total:
- 900k

따라서 수천만 토큰 표시는 실제 Task 신규 소비량보다 크게 부풀려질 수 있다. 기존 session resume라면 Task 시작 이전 사용량까지 포함될 수 있다.

향후 telemetry는 최소 다음을 구분해야 한다.
- task baseline session usage
- latest session usage
- task delta = latest - baseline
- cached input
- output/reasoning
- round count
- duration

## 다음 실험: 파일 첨부 대신 Git / Server 기준 검증

다음 장기 개발부터는 매 라운드마다 소스 파일을 Web에 첨부하지 않고, Git repository 또는 ProjectHub Server의 상태를 기준으로 Web이 최신 소스를 읽고 리뷰하는 방식을 우선 검토한다.

목표 흐름:

```
Codex CLI
  ↓ local edit / test
Worker
  ↓ checkpoint metadata
Git remote + ProjectHub Server
  ↓
GPT Web
  ↓ inspect exact commit/state
review / next ACTION
```

### 중요한 전제: Git 동기화는 자동으로 즉시 일어나지 않는다

구분해야 한다.

1. Codex가 로컬 파일을 수정
   - 아직 Git remote에는 반영되지 않음.

2. local commit 생성
   - 로컬 repository에만 존재.
   - remote Web 조회로는 아직 보이지 않음.

3. `git push` 완료
   - remote branch가 새 commit으로 이동.
   - 이후 Web은 해당 commit SHA를 기준으로 읽어야 한다.

4. Web 조회
   - branch 이름만 믿지 말고 Worker가 보고한 exact commit SHA를 조회한다.
   - branch HEAD 조회 지연/캐시 가능성보다 SHA 직접 조회가 안전하다.

즉 "Codex가 파일 저장 완료"와 "Web이 remote에서 그 파일을 볼 수 있음"은 같은 시점이 아니다.

### 권장 checkpoint 계약

각 Web review 라운드 직전에 Worker가 최소 다음 값을 확정해야 한다.

- repository URL
- branch
- working tree status
- local HEAD SHA
- pushed SHA
- remote HEAD SHA
- last push result
- last push time
- server observed SHA
- server observed time

Web에 넘길 기준은 branch 이름보다 `review_commit_sha` 하나를 명시하는 것이 좋다.

예:

```json
{
  "repository": "https://github.com/owner/repo",
  "branch": "main",
  "local_head": "abc123",
  "pushed_head": "abc123",
  "remote_head": "abc123",
  "server_head": "abc123",
  "review_commit_sha": "abc123",
  "sync_state": "CONFIRMED"
}
```

Web은 반드시 `review_commit_sha`의 파일을 읽고 피드백한다.

### 권장 sync state

- LOCAL_DIRTY
- LOCAL_COMMITTED
- PUSHING
- PUSHED_UNCONFIRMED
- REMOTE_CONFIRMED
- SERVER_CONFIRMED
- SYNC_MISMATCH
- CONFLICT
- PUSH_REJECTED

Web review 가능 기준은 기본적으로 `REMOTE_CONFIRMED` 이상으로 둔다.

Server를 authoritative observation layer로 사용할 경우 `SERVER_CONFIRMED`를 추가 수용할 수 있다.

### Git 작업 권한

기존 ProjectHub 정책대로 commit/push/fetch/pull은 사용자 명시적 승인 범위 안에서만 수행한다.

장기 자동개발에서는 Task 시작 시 사용자가 명시적으로:
- 이 Task 동안 자동 commit 허용
- 이 Task 동안 지정 branch push 허용
- fetch/pull 가능 범위

를 승인하는 task-scoped permission 방식이 적합하다.

충돌, dirty pull 대상, detached HEAD, push reject는 자동 해결하지 않고 PAUSE해야 한다.

## Worker UI에 추가해야 할 식별 정보

현재 Worker는 장기 작업을 실행할 수 있지만 사용자가 "어느 repository / 어느 server를 기준으로 동작 중인지" 즉시 확인하기 어렵다.

최소 표시 권장:

### Project / Git
- Project path
- Repository URL
- Branch
- Local HEAD short SHA
- Remote HEAD short SHA
- Git status: CLEAN / DIRTY
- Sync state
- Last push/fetch timestamp

### Server
- ProjectHub Server base URL
- Connection status
- Last successful heartbeat/state update
- Server가 기록한 latest commit SHA

### GPT Web review binding
- Review source: ATTACHMENT / GIT / SERVER
- Review commit SHA
- Review state: WAITING / CONFIRMED / MISMATCH

비밀키/API Key 등 자격증명은 UI에 표시하지 않는다.

## 다음 구현 권장 순서

1. Worker usage telemetry 수정
   - cumulative snapshot 단순 합산 제거
   - baseline/latest/delta 구조 도입

2. Worker UI에 repository/server identity 표시
   - repo URL
   - branch
   - local/remote SHA
   - Server URL/status

3. Worker에 Git review checkpoint 추가
   - push 완료 후 remote exact SHA 확인
   - 확인 전 GPT Web review 요청 생성 금지

4. Server project-state에 Git sync observation 확장
   - latest commit
   - observed_at
   - branch
   - workstation

5. Web prompt에 review source metadata 전달
   - exact repository
   - exact SHA
   - server observation state

6. 다음 자동개발 프로젝트에서 Git-based E2E 수행
   - 파일 첨부 없이 Web이 exact commit을 직접 읽어 리뷰
   - Git 지연/불일치 시 대기 또는 PAUSE
   - commit SHA가 일치한 경우에만 CONTINUE

## TETRIS E2E의 기준 가치

이 TETRIS 사례는 ProjectHub의 첫 장기 Autonomous Development E2E 기준 사례로 활용할 수 있다.

다음 프로젝트에서 비교할 지표:
- 총 duration
- Worker ↔ Web ↔ Codex round 수
- 사람 개입 횟수
- PAUSE 횟수
- build attempts
- test pass/fail
- 실제 Task token delta
- cached tokens
- changed file count
- Git checkpoints
- sync mismatch 횟수
- final human acceptance
