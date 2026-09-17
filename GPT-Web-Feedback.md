# GPT Web Feedback

Updated: 2026-09-17

## 우선순위

1. `AGENTS.md`
2. `ProjectHub_IMPLEMENTATION_PLAN.md`
3. `CurrentWork.md`
4. 현재 활성 `tasks/*.md`
5. `GPT-Web-Feedback.md`

충돌 시 앞선 관리 문서와 활성 task를 우선한다. 아래 내용은 07 프로젝트 배포 패키지를 실제 사용 가능한 수준으로 다듬기 위한 최신 지시다.

---

# 1. 현재 상태

최신 확인 커밋:

```text
78103b24c1f75f90702f756bf4b5f6f131ad94fb
Document large data deletion tracking limits
```

06 Large Data/NAS는 **검증 완료 상태로 유지**한다.

06을 다시 열어 기능을 확장하지 않는다. 다만 06에서 확인된 현재 한계인 "로컬 대용량 파일 삭제 추적 부재"는 07의 실제 사용자 흐름 보완 항목으로 해결한다.

현재 활성 작업은 07 프로젝트 배포 패키지다.

핵심 방향:

```text
Setup을 쉽게
Sync를 명확하게
삭제를 안전하게
Restore를 탐색기 기준으로 완결
```

실시간 관리, lease, dashboard, background reconcile 확대는 추가하지 않는다.

---

# 2. ProjectHub의 사용자 관점 정의

ProjectHub는 Git revision 탐색 자체가 주 목적이 아니다.

탐색기 기준으로 다음 의미를 사용한다.

```text
Sync
= 현재 로컬 탐색기 상태를 ProjectHub 기준 상태로 올린다.

Restore
= ProjectHub 기준 상태를 현재 로컬 탐색기에 적용한다.
```

통합 checkpoint 정의는 유지한다.

```text
Checkpoint = Git commit SHA + Large Data manifest
```

다만 사용자 UX는 revision 조작보다 현재 프로젝트 폴더 상태의 일치에 초점을 둔다.

---

# 3. Sync에서 삭제 후보 판정

Sync는 이전 ProjectHub 관리 상태와 현재 로컬 manifest를 비교한다.

예:

```text
이전 ProjectHub 상태
A.bin
B.bin
C.bin

현재 로컬
B.bin
C.bin

결과
A.bin = REMOVED 후보
```

삭제 후보를 발견했다고 즉시 확정하지 않는다.

실제 Server/NAS metadata 변경 전에 전체 diff를 먼저 계산한다.

최소 분류:

```text
ADDED
CHANGED
UNCHANGED
REMOVED
FAILED
```

---

# 4. Sync 삭제 확정 GUI

`REMOVED`가 하나 이상 존재하면 실제 삭제 확정 전에 Windows GUI 확인창을 표시한다.

권장 예:

```text
ProjectHub - 삭제 확인

다음 파일이 로컬 프로젝트에서 삭제되었습니다.

A.bin
500 MB

ProjectHub에서도 삭제 상태로 확정하시겠습니까?

[모두(A)] [예(Y)] [아니오(N)] [취소(C)]
```

버튼 의미:

```text
모두(A)
- 현재 파일을 포함해 남은 삭제 후보 전체 승인

예(Y)
- 현재 파일만 승인하고 다음 후보 진행

아니오(N)
- 현재 파일은 삭제 확정하지 않고 다음 후보 진행

취소(C)
- Sync 전체 취소
```

중요:

삭제 후보를 먼저 전부 계산하고 승인 과정을 끝낸 뒤 실제 변경을 수행한다.

`취소(C)` 선택 시 일부 삭제만 이미 Server/NAS metadata에 반영된 상태가 되지 않도록 한다.

GUI 구현은 PowerShell/WinForms 또는 동등한 Windows native dialog를 사용할 수 있다. 기본 MessageBox로 `모두(A)`를 지원하기 어려우면 작은 custom dialog를 사용한다.

---

# 5. 삭제 확정 권한과 뒤처진 workstation 보호

최신 commit/push를 수행한 workstation이 삭제를 확정하는 흐름을 우선한다.

삭제 확정은 최소 다음 상태를 검증한 뒤 허용한다.

```text
local HEAD == remote 최신 HEAD
local base checkpoint == ProjectHub 최신 checkpoint
```

조건이 맞지 않으면 삭제 후보는 감지하되 확정하지 않는다.

예:

```text
Deletion cannot be confirmed.

Local checkpoint : #20
Latest checkpoint: #23

Update or restore before confirming removals.
```

뒤처진 workstation에 과거 파일이 남아 있어도 일반 Sync에서 삭제된 파일을 신규 파일처럼 되살리면 안 된다.

예:

```text
LOCAL_EXISTS
SERVER_REMOVED_AT_CHECKPOINT_23

-> 일반 Sync에서 재등록 금지
-> 재업로드 금지
-> tombstone 자동 해제 금지
```

명시적인 re-add 또는 동등한 사용자 승인 동작에서만 다시 관리 대상으로 등록할 수 있다.

---

# 6. 삭제 상태 기록

삭제 확정은 NAS canonical object의 즉시 삭제를 의미하지 않는다.

삭제된 경로는 tombstone 또는 동등한 영속 상태로 기록한다.

예:

```text
path: data\A.bin
state: REMOVED
removed_at_checkpoint: 23
git_head: A91F...
previous_hash: ABC123...
```

목적:

```text
- 뒤처진 workstation의 일반 Sync가 파일을 부활시키지 못하게 함
- Restore가 어떤 관리 파일을 삭제 대상으로 볼지 판단 가능
- object 삭제와 project path 삭제를 분리
```

---

# 7. Restore의 기본 동작

Restore는 기본적으로 별도 과거 revision 디렉터리를 만드는 기능보다 **현재 로컬 프로젝트 폴더를 ProjectHub 기준 상태에 맞추는 기능**으로 정리한다.

예:

```text
ProjectHub 최신
B.bin
C.bin

로컬 탐색기
A.bin
B.bin
C.bin

Restore 결과 후보
A.bin = 로컬 삭제 후보
B.bin = 유지 또는 갱신
C.bin = 유지 또는 갱신
```

Git 파일과 Large Data 모두 최신 ProjectHub 기준 상태에 맞추는 방향으로 동작한다.

실제 덮어쓰기/삭제 전에는 변경 목록을 먼저 계산한다.

---

# 8. Restore 삭제 GUI

Restore 중 로컬 관리 파일 삭제가 발생하면 실제 삭제 전에 Windows GUI 확인창을 표시한다.

예:

```text
ProjectHub - 로컬 파일 삭제 확인

ProjectHub 최신 상태에는 없는 관리 파일입니다.

A.bin
500 MB

로컬에서도 삭제하시겠습니까?

[모두(A)] [예(Y)] [아니오(N)] [취소(C)]
```

버튼 의미는 Sync와 동일하게 유지한다.

```text
모두(A) = 남은 삭제 대상 모두 승인
예(Y)   = 현재 파일만 삭제
아니오(N)= 현재 파일 유지
취소(C) = Restore 전체 취소
```

실제 파일 삭제 전에 승인 단계를 완료하는 방향을 우선한다.

---

# 9. LOCAL_ONLY 파일 보호

Restore는 ProjectHub가 관리한 적 없는 로컬 파일을 삭제해서는 안 된다.

분류 원칙:

```text
MANAGED + SERVER_REMOVED
-> Restore 삭제 후보
-> GUI 승인 대상

LOCAL_ONLY
-> 항상 보존
-> 자동 삭제 금지
```

예를 들어 다음 파일이 ProjectHub manifest/history에 한 번도 포함되지 않았다면 Restore가 건드리지 않는다.

```text
memo.txt
temporary.zip
개인자료.jpg
```

Restore 삭제 판단은 단순히 "최신 manifest에 없다"만으로 해서는 안 된다.

과거 ProjectHub 관리 이력이 있는 path인지 반드시 확인한다.

---

# 10. NAS object 삭제 정책

`REMOVED` 확정과 NAS canonical object 삭제는 분리한다.

정상 순서:

```text
1. 프로젝트 path 제거 확정
2. project-file 연결 해제 또는 removed 상태 기록
3. tombstone 기록
4. object 참조 여부 확인
5. 다른 checkpoint/project가 참조 중이면 object 유지
6. 미참조 object만 purge 후보로 분류
```

중요:

```text
REMOVED != immediate object deletion
Staging GC != Object purge
```

기존 `ProjectHub_GC.ps1`의 staging cleanup을 canonical object 삭제 기능으로 확장하지 않는다.

Object purge가 필요하면 별도 명시적 작업/승인으로 유지한다.

---

# 11. Sync / Restore 결과 요약

사용자는 실행 결과만 보고 무엇이 변경됐는지 알 수 있어야 한다.

Sync 예:

```text
SYNC COMPLETE

Added      : 1
Changed    : 2
Removed    : 3
Skipped    : 1
Failed     : 0

Checkpoint updated.
```

Restore 예:

```text
RESTORE COMPLETE

Downloaded : 2
Updated    : 1
Deleted    : 3
Skipped    : 1
Local-only : 4
Failed     : 0
```

가능하면 파일별 결과는 `.result.json` 또는 동등한 결과 파일에도 남긴다.

---

# 12. 07 완료 기준

07은 다음 조건을 만족하면 완료로 본다.

```text
[x/ ] Setup 실제 프로젝트 E2E
[x/ ] Sync 실제 프로젝트 E2E
[ ] Restore 실제 프로젝트 E2E
[ ] 이전/current manifest 비교
[ ] ADDED / CHANGED / REMOVED 판정
[ ] Sync 삭제 확정 GUI
[ ] Restore 삭제 GUI
[ ] 모두(A) / 예(Y) / 아니오(N) / 취소(C)
[ ] LOCAL_ONLY 보호
[ ] 최신 HEAD/checkpoint에서만 삭제 확정 허용
[ ] 뒤처진 workstation이 tombstone 파일을 일반 Sync로 재등록하지 못함
[ ] REMOVED와 object purge 분리
[ ] Sync/Restore 결과 요약
[ ] build/test/PowerShell parser 검증
```

기존에 구현된 Setup/Sync 흐름은 유지하고 불필요하게 다시 작성하지 않는다.

Restore 운영 E2E와 삭제 동기화 보완을 우선한다.

---

# 13. 이후 작업

07 완료 후에는 08 Server 설치/이전 가이드만 수행한다.

```text
06 Large Data/NAS            완료
07 프로젝트 배포 패키지      진행
08 Server 설치/이전 가이드   대기

08 완료 후 ProjectHub v0.1 종료
```

추가 실시간 관리 기능, lease, dashboard, 자동 Git 변경, background upload/reconcile 확대는 종료 조건에 포함하지 않는다.

Codex는 이 피드백을 반영할 때 먼저 `CurrentWork.md`, `tasks/07-project-deployment-package.md`의 현재 상태와 충돌 여부를 확인하고, 07의 기존 구현을 최대한 재사용하면서 삭제 diff/승인/Restore 안전성부터 한 세부 작업씩 진행한다.
