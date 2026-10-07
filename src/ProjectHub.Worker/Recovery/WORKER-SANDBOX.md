# WORKER-SANDBOX.md

## 목적

이 문서는 제한된 sandbox, container 또는 권한 제한 환경에서 동작하는 coding agent와 worker model을 위한 프로젝트 비종속 정책을 정의한다.

목표는 안전한 진단이나 지원되는 우회 방법이 아직 남아 있는데도 agent가 첫 sandbox, 권한, 경로, Git 또는 환경 오류에서 즉시 중단하는 일을 방지하는 것이다.

프로젝트별 지시가 명시적으로 우선하지 않는 한 이 정책은 repository, 언어, framework, 운영체제와 build system에 관계없이 적용한다.

---

## 1. 핵심 원칙

sandbox 또는 권한 오류는 **자동으로 terminal failure가 아니다**.

접근 제한, ownership, working-directory 상태, 사용할 수 없는 tooling, path visibility 또는 execution policy 때문에 작업이 실패하면 다음을 수행한다.

1. 정확한 오류를 읽고 분류한다.
2. 실패한 작업이 read-only인지 mutation인지 판단한다.
3. 안전한 read-only 명령으로 local environment를 조사한다.
4. 더 좁고 지원되며 비파괴적인 대안을 시도한다.
5. 원래 목표 또는 동등한 verification을 다시 수행한다.
6. 현재 권한 안에서 안전하게 진행할 수 없을 때만 elevated/user action을 요청한다.
7. 제한이 확인되고 안전한 지원 경로가 남아 있지 않을 때만 중단한다.

적용 가능한 위 단계들을 시도하기 전에는 단순히 "sandbox error"를 최종 결론으로 보고하지 않는다.

---

## 2. 기본 행동

Worker는 반드시 다음을 수행한다.

- 첫 command failure 뒤에도 reasoning을 계속한다.
- 실패한 command 자체보다 사용자의 원래 목표를 유지한다.
- mutation보다 read-only 진단을 우선한다.
- 가능한 가장 좁은 workaround를 선호한다.
- 명확히 필요하고 명시적으로 승인되지 않았다면 global machine state 변경을 피한다.
- 다음 오류를 서로 구분한다.
  - filesystem permission error
  - sandbox policy restriction
  - Git ownership/safe-directory error
  - missing tool
  - wrong working directory
  - missing file
  - environment-variable problem
  - dependency/setup failure
  - network restriction
  - process execution restriction
- workaround가 실제로 원래 문제를 해결했는지 확인한다.
- 수정이 작업 목표에 포함되지 않는 한 기존 source file과 repository state를 보존한다.

Worker는 다음을 해서는 안 된다.

- 환경이 추가 조사를 명시적으로 금지하지 않는 한 한 번의 failed command 뒤 즉시 중단한다.
- 모든 permission 계열 오류를 같은 문제로 취급한다.
- 아무 조건도 바꾸지 않고 동일한 실패 command를 반복한다.
- security control을 광범위하게 비활성화한다.
- 첫 해결책으로 `chmod 777`, 재귀 ownership 변경, blanket ACL reset 또는 이에 준하는 광범위 권한 변경을 사용한다.
- 하나의 command를 통과시키기 위해 system-wide configuration을 변경한다.
- 원인이라는 근거 없이 lock file, Git metadata, build cache 또는 configuration file을 삭제한다.
- 검증 없이 성공했다고 주장한다.

---

## 3. 오류 분류

수정을 적용하기 전에 failure를 분류한다.

### A. Filesystem permission 문제

대표 신호:

- `Permission denied`
- `Access is denied`
- `EPERM`
- `EACCES`
- 파일을 create/write/delete할 수 없음

확인할 사항:

- 현재 working directory
- file/folder 존재 여부
- target의 read 가능 여부
- write가 필요한 경우 target의 write 가능 여부
- path가 mounted/read-only 위치에 속하는지
- project workspace 안에서 동등한 작업을 수행할 수 있는지

즉시 재귀 권한 변경을 하지 않는다.

### B. Sandbox policy restriction

대표 신호:

- operation denied by sandbox
- command not permitted
- protected path
- blocked execution
- restricted syscall/process/network access

대응:

1. 정확히 어떤 operation이 차단됐는지 식별한다.
2. workspace 안에서 동등한 허용 operation이 있는지 확인한다.
3. 가능하면 shell-level bypass 대신 지원되는 tool/API를 사용한다.
4. elevation/approval이 지원된다면 필요한 최소 operation에 대해서만 요청한다.
5. sandbox enforcement를 회피하려 하지 않는다.

### C. Git repository ownership / safe-directory 문제

대표 신호:

```text
fatal: detected dubious ownership in repository
```

선호 대응:

1. repository root를 확인한다.
   - `git rev-parse --show-toplevel`
2. 해당 path가 의도한 repository인지 확인한다.
3. 지원된다면 다음과 같은 command-scoped safe-directory override를 우선한다.

```bash
git -c safe.directory="<repo-path>" status
```

4. persistent Git configuration 변경은 필요하고 적절한 경우에만 수행한다.
5. 임의 directory 또는 모든 repository를 wildcard로 `safe.directory`에 추가하지 않는다.

다음과 같은 광범위 수정은 피한다.

```bash
git config --global --add safe.directory '*'
```

사용자가 security implication을 이해하고 명시적으로 요청한 경우는 예외다.

### D. 잘못된 working directory

대표 신호:

- repository not found
- 예상한 파일인데 file not found
- package/build config missing
- relative path가 잘못 해석됨

다음을 확인한다.

```bash
pwd
```

또는 platform-equivalent command를 사용한 뒤 directory contents를 확인한다.

필요하면 retry 전에 project root를 찾는다.

### E. Missing command 또는 dependency

대표 신호:

- `command not found`
- executable not recognized
- module/package missing

대응:

1. 동등한 installed tool이 있는지 확인한다.
2. global install 전에 project-local tooling을 확인한다.
3. project-scoped installation을 선호한다.
4. 요청받았거나 명확히 필요하지 않다면 global package manager를 변경하지 않는다.
5. setup 뒤 version을 확인한다.

### F. Network restriction

대표 신호:

- DNS failure
- connection denied
- sandbox network blocked
- package registry unreachable

대응:

1. 작업이 실제로 network access를 필요로 하는지 판단한다.
2. 이미 존재하는 local dependency/cache/file을 우선한다.
3. 환경이 외부 접근을 명시적으로 차단한다면 반복해서 retry하지 않는다.
4. 진행이 불가능하면 정확히 어떤 dependency가 차단됐는지 명시한다.

---

## 4. 필수 진단 순서

command가 예상 밖으로 실패하면 적용 가능한 범위까지 다음 순서를 따른다.

### Step 1 — 정확한 failure 확보

다음을 기록한다.

- 시도한 command/action
- 가능한 경우 exit code
- stderr/error message
- 현재 working directory
- target path 또는 resource

실제 message를 읽기 전에 오류를 요약하지 않는다.

### Step 2 — 위치와 context 확인

platform에 맞는 안전한 inspection command를 사용한다.

```text
current directory
directory listing
repository root
target file existence
tool/version availability
```

예시는 다음과 같다.

```bash
pwd
ls
git rev-parse --show-toplevel
git status
```

Windows에서는 다음과 같은 동등 명령을 사용할 수 있다.

```powershell
Get-Location
Get-ChildItem
```

현재 환경에서 사용 가능한 command만 사용한다.

### Step 3 — read failure와 write failure 분리

read-only check는 성공하지만 write action만 실패한다면 프로젝트 전체가 접근 불가라고 결론내리지 않는다.

문제를 가장 작은 affected path 또는 operation으로 좁힌다.

### Step 4 — 비파괴 workaround 시도

예시:

- 올바른 working directory에서 실행
- absolute path 사용
- project-local temp/output directory 사용
- command-scoped Git option 사용
- 이미 설치된 interpreter/tool 사용
- mutation 전에 read-only inspection 수행
- raw shell access 대신 지원되는 file-editing/repository API 사용

### Step 5 — 원래 목표 재시도

workaround 자체만 따로 시험하고 끝내지 않는다.

예:

```text
git status failed
→ ownership 진단
→ command-scoped safe-directory option 적용
→ git status 재실행
→ repository state를 읽을 수 있는지 확인
```

### Step 6 — 필요한 경우에만 escalation

operation이 여전히 진행되지 않으면 다음을 수행한다.

- 정확히 무엇이 blocked인지 설명한다.
- 필요한 가장 작은 permission/action을 식별한다.
- 광범위 unrestricted access를 요청하지 않는다.
- 환경에서 요구되는 경우 user approval을 요청한다.

---

## 5. Retry 정책

failed operation은 blind repetition이 아니라 **adaptive retry**를 유발해야 한다.

권장 순서:

1. First attempt.
2. Failure 조사.
3. 안전한 diagnostic path 하나.
4. 더 좁은 workaround 하나.
5. 원래 goal 재시도.
6. 타당하다면 실질적으로 다른 두 번째 안전한 접근.
7. 확인된 limitation을 escalation 또는 report.

무한 loop를 만들지 않는다.

기본적으로 약 2~4개의 실질적으로 다른 접근을 시도한 뒤 restriction이 structural한지 다시 판단한다.

input, path, permission, tooling 또는 method가 바뀌지 않은 반복 시도는 유효한 retry로 계산하지 않는다.

---

## 6. Permission escalation 규칙

Permission elevation은 기본 해결책이 아니라 마지막 단계다.

elevated access를 요청하기 전에 Worker는 다음을 설명할 수 있어야 한다.

- 정확히 어떤 operation이 blocked인지
- 그 operation이 왜 필요한지
- 어떤 resource/path가 영향을 받는지
- 더 좁은 방법이 왜 충분하지 않은지
- 요청 변경이 temporary인지 persistent인지

다음 순서를 선호한다.

```text
single file > directory > project > user-level > system-wide
```

그리고 다음을 선호한다.

```text
temporary / command-scoped > persistent
```

작업에 필요한 범위보다 넓은 privilege를 요청하거나 적용하지 않는다.

---

## 7. 안전한 mutation 정책

file 또는 configuration을 수정하기 전에:

1. target이 의도한 project 일부인지 확인한다.
2. 가능한 경우 현재 value/content를 조사한다.
3. 필요한 부분만 수정한다.
4. 관계없는 formatting/cleanup을 피한다.
5. 결과를 확인한다.
6. 변경 내용을 설명하거나 revert할 수 있는 상태를 유지한다.

configuration change는 다음 순서를 선호한다.

1. command-scoped option
2. project-local configuration
3. user-level configuration
4. system-wide configuration

system-wide change에는 강한 근거가 필요하다.

---

## 8. Git 전용 정책

Git failure는 generic sandbox failure로 취급하지 않고 진단한다.

유용한 read-only check 예시는 다음과 같다.

```bash
git rev-parse --show-toplevel
git rev-parse --is-inside-work-tree
git status --short
git diff --stat
git diff
```

Git metadata는 읽을 수 있지만 ownership/safety check 때문에 command가 실패하면 scoped solution을 선호한다.

다음은 수행하지 않는다.

- `.git` 삭제
- repository 재초기화
- branch reset
- `git clean -fdx`
- 변경사항 폐기
- history rewrite
- global Git security setting 변경

사용자가 관련 destructive action을 명시적으로 요청한 경우는 예외다.

uncommitted user change는 가치 있는 상태이므로 보존한다.

---

## 9. Build 및 dependency failure

build가 실패하면:

1. failure가 무엇인지 구분한다.
   - source-code error
   - missing dependency
   - toolchain mismatch
   - generated-file problem
   - permission issue
   - path issue
   - sandbox restriction
2. 기본적으로 전체 environment를 재설치하지 않는다.
3. repository가 정의한 dependency mechanism을 선호한다.
4. version 변경 전에 lockfile/configuration을 조사한다.
5. 수정 뒤 가능한 가장 좁고 의미 있는 build/test를 다시 수행한다.

예:

```text
single test > affected module > project test suite > full rebuild
```

이 순서가 안전하게 변경을 검증할 수 있을 때 사용한다.

---

## 10. Platform 독립성

이 정책은 특정 shell에 묶인 것이 아니라 개념 정책이다.

Worker는 active platform에 맞춰 command를 조정한다.

- Linux/macOS shell
- PowerShell
- Windows Command Prompt
- container
- CI runner
- remote development environment

다음을 가정하지 않는다.

- `sudo`가 존재함
- administrator access가 존재함
- Windows에서도 POSIX permission이 관련 있음
- Linux에도 PowerShell이 존재함
- package manager가 설치되어 있음
- home directory가 writable임
- repository가 현재 process user 소유임

행동하기 전에 실제 환경을 확인한다.

---

## 11. 낮은 추론 능력 Worker 지침

추론 능력이 제한된 Worker는 다음 deterministic fallback을 따른다.

환경 command가 실패하면:

```text
1. 전체 오류를 읽는다.
2. 현재 directory를 확인한다.
3. target 존재 여부를 확인한다.
4. operation이 read인지 write인지 확인한다.
5. Git 관련이면 repository root와 Git-specific error type을 확인한다.
6. 더 좁은 non-destructive method 하나를 시도한다.
7. 원래 objective를 재시도한다.
8. 여전히 blocked면 실질적으로 다른 안전한 method 하나를 시도한다.
9. 그 뒤에만 limitation을 report하거나 최소 필요한 permission을 요청한다.
```

다음처럼 바로 점프하지 않는다.

```text
command failed
```

에서

```text
task cannot be completed
```

로 넘어가지 않는다. 추가 diagnostic 자체가 금지된 경우는 예외다.

---

## 12. 중단 조건

다음 중 하나에 해당하면 local workaround 시도를 중단한다.

- environment가 필요한 operation을 명시적으로 금지함
- 계속하려면 security control 우회가 필요함
- 다음 단계가 user data 삭제 또는 손상 위험을 가짐
- 필요한 credential/secret을 사용할 수 없음
- operation에 명시적 user approval이 필요함
- 합리적인 안전한 접근을 모두 시도함
- project-specific policy가 해당 행동을 금지함

중단할 때는 다음을 보고한다.

1. 원래 objective
2. 정확히 무엇이 실패했는지
3. 무엇을 시험했는지
4. 무엇이 여전히 blocked인지
5. 필요한 최소 next action

다음처럼만 보고하지 않는다.

```text
Sandbox error.
```

---

## 13. 성공 기준

sandbox 관련 작업은 다음 조건 중 하나가 충족될 때만 완료다.

### 성공적인 복구

- 원래 operation이 동작하거나
- 동등한 지원 method가 요청한 결과를 달성하며
- 그 결과가 검증됨

### 확인된 restriction

- failure가 분류됨
- 합리적인 안전한 workaround를 시도함
- 남은 restriction을 명확히 식별함
- 필요한 정확한 최소 user/environment action을 알고 있음

---

## 14. 간단 결정 트리

```text
Command/action failed
        |
        v
Read exact error
        |
        v
Is it path/context related?
  | yes -> correct path/context -> retry
  |
  no
  v
Is it Git-specific?
  | yes -> diagnose Git ownership/safe-directory/repo state -> scoped fix -> retry
  |
  no
  v
Is it filesystem permission related?
  | yes -> inspect read/write scope -> narrower writable path or minimum permission -> retry
  |
  no
  v
Is it sandbox-policy related?
  | yes -> use supported API/tool/allowed equivalent
  |          |
  |          +-> still blocked -> request minimum required approval
  |
  no
  v
Is tooling/dependency missing?
  | yes -> project-local/supported setup -> retry
  |
  no
  v
Investigate actual error category instead of labeling it "sandbox"
```

---

## 15. Worker 지침 요약

이 정책 아래에서 동작하는 Worker는 다음 원칙을 내재화한다.

> sandbox와 싸워 우회하지 말고, 너무 빨리 sandbox에 굴복하지도 않는다.  
> 정확한 restriction을 진단하고, 지원되는 경계 안에 머물며, 사용자의 목표로 가는 가장 좁고 안전한 경로를 찾고, 결과를 검증한 뒤 정말 필요한 경우에만 escalation한다.
