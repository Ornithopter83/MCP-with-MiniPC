# GPT Web Feedback

Updated: 2026-09-18

## 우선순위

1. `AGENTS.md`
2. `ProjectHub_IMPLEMENTATION_PLAN.md`
3. `CurrentWork.md`
4. 현재 활성 `tasks/*.md`
5. `GPT-Web-Feedback.md`

충돌 시 앞선 관리 문서와 활성 task를 우선한다.

---

# 1. 최신 확인 상태

최신 확인 커밋:

```text
5ffd68841206d91cc46f1268e6633e750cf786da
Support string enum values for large-data operations
```

최근 완료 상태:

```text
- NAS object deletion + daily full logging 구현
- delete assertion 운영 E2E 성공
- 동일 object 재호출 already_deleted=true 확인
- LargeDataOperation 문자열 enum 호환 수정
- build/test 통과
```

현재 07은 마무리 검증 단계다.

---

# 2. 사용자 실행 CMD 공통 pause

더블클릭 실행 시 예외/오류 메시지가 바로 닫히지 않도록
모든 사용자 실행용 `.cmd` 진입점은 마지막에 `pause`를 둔다.

대상 최소:

```text
ProjectHub_Setup.cmd
ProjectHub_Sync.cmd
ProjectHub_Restore.cmd
ProjectHub_GC.cmd
ProjectHub_update.cmd
ProjectHub_Agent_Test.cmd
```

권장 형식:

```bat
@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "..."
set EXITCODE=%ERRORLEVEL%

echo.
echo Exit code: %EXITCODE%
pause
exit /b %EXITCODE%
```

정상/실패 모두 결과를 확인할 수 있어야 한다.

PowerShell 내부에 중복 `Read-Host`를 넣기보다
사용자 더블클릭 진입점인 CMD에서 일관되게 처리한다.

---

# 3. ProjectHub_update.cmd 창 가로 크기

현재 `ProjectHub_update.cmd`는:

```bat
@echo off
title ProjectHub Update & Run
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\AI-Server\ProjectHub\ProjectHub_Update_Run.ps1"
...
```

형태이며 창 크기 제어가 없다.

사용자 요구:

> ProjectHub_update.cmd 실행 창의 가로 크기를 약 1800px 정도로 넓게 보이게 한다.

Windows CMD/Console의 `mode con`은 픽셀이 아니라 문자 열(cols) 기준이므로
정확히 1800px을 지정할 수는 없다.

현재 단계에서는 단순하게 약 1800px 체감을 목표로
`cols=220` 전후를 기본값으로 사용한다.

권장:

```bat
@echo off
title ProjectHub Update ^& Run
mode con: cols=220 lines=50
```

폰트/DPI에 따라 실제 픽셀 폭은 달라질 수 있다.

중요한 것은 정확한 픽셀 고정보다
Server operation log가 한 줄에서 잘리지 않고 충분히 보이는 것이다.

필요하면 실제 Server PC에서 220 columns를 먼저 확인하고
너무 넓거나 좁으면 200~240 범위에서 한 번만 조정한다.

---

# 4. ProjectHub_update.cmd 권장 최종 형태

```bat
@echo off
title ProjectHub Update ^& Run
mode con: cols=220 lines=50

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\AI-Server\ProjectHub\ProjectHub_Update_Run.ps1"
set EXITCODE=%ERRORLEVEL%

echo.
echo ProjectHub exited with code %EXITCODE%.
pause
exit /b %EXITCODE%
```

현재처럼 실패일 때만 pause하지 말고
정상 종료에서도 pause해서 마지막 로그를 확인할 수 있게 한다.

단 Server가 정상적으로 계속 실행 중일 때는
PowerShell 프로세스가 끝나지 않으므로 pause까지 내려오지 않는다.
Server 종료/실패 후에만 마지막 상태를 확인하게 된다.

---

# 5. 기존 NAS 삭제 / Full-log 방향 유지

이미 구현된 다음 방향은 유지한다.

```text
Sync 삭제 승인
→ REMOVED/tombstone
→ NAS named alias 삭제
→ active ref 확인
→ ref=0이면 canonical object 삭제
→ 결과 log 기록
```

별도 Purge UI는 만들지 않는다.

Full-log:

```text
ProjectHub\src\ProjectHub.Server\log\yyyyMMdd.log
```

정책도 유지한다.

```text
- 같은 날짜 재시작 시 append
- 정상 종료 시 SERVER_STOPPING + 빈 줄
- heartbeat 포함
- Console은 주요 operation만 표시
- log file writer는 하루 동안 유지
- secret/token 원문 금지
```

---

# 6. 다음 UX 방향: Git + ProjectHub 동작 통합

다음 단계 설계 방향으로만 유지한다.
아직 기존 Sync/Restore 엔진을 제거하지 않는다.

사용자에게 보이는 이름 후보:

```text
ProjectHub-Commit-Push
ProjectHub-Fetch-Pull
```

의미:

```text
Commit-Push
= git add
→ commit
→ fetch
→ 필요 시 rebase/pull
→ push
→ ProjectHub large-data Sync/checkpoint

Fetch-Pull
= local dirty 확인
→ fetch
→ pull
→ ProjectHub large-data Restore
```

내부 구현은 기존 `ProjectHub_Sync.ps1`,
`ProjectHub_Restore.ps1`를 재사용하는 wrapper 형태를 우선한다.

---

# 7. 07 남은 확인

```text
[x] Setup 실제 프로젝트 E2E
[x] Sync 500MiB 실제 업로드 E2E
[x] upload transport 안정화
[x] 반복 삭제 확인창 수정
[x] 최종 Console formatter
[x] NAS delete 구현
[x] NAS delete 운영 E2E
[x] daily Full-log 구현
[ ] 문자열 delete operation 수정본 운영 재확인
[ ] 모든 사용자 CMD pause 통일
[ ] ProjectHub_update.cmd 약 1800px 체감 폭 적용
[ ] Restore 실제 프로젝트 E2E
[ ] LOCAL_ONLY 보호 확인
[ ] stale workstation tombstone 보호 확인
[ ] Full-log 운영 재확인
[ ] build/test/PowerShell parser 최종 확인
```

07 완료 후 08 Server 설치/이전 가이드로 이동한다.

추가 실시간 관리, dashboard, lease, 별도 purge UI,
자동 background upload/reconcile은 v0.1 종료 조건에 넣지 않는다.
