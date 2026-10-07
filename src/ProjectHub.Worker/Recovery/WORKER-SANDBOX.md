# WORKER-SANDBOX.md

## 목적

이 문서는 제한된 sandbox, container, 권한 제한 환경에서 동작하는 coding agent와 WORK 역할이 환경 오류를 안전하게 진단하기 위한 프로젝트 비종속 보조 지침이다.

목표는 sandbox, 권한, 경로, Git, 실행환경 오류가 발생했을 때 안전한 진단이나 지원되는 우회 경로가 남아 있는데도 첫 실패에서 즉시 중단하는 일을 방지하는 것이다.

프로젝트별 역할 계약과 명시적 작업 제한이 이 문서보다 우선한다. 이 문서는 현재 WORK의 WRITE_PATH, build/test/run 금지, Git mutation 금지 같은 상위 계약을 완화하지 않는다.

---

## 1. 핵심 원칙

sandbox 또는 권한 오류는 자동으로 terminal failure가 아니다.

접근 제한, ownership, working directory, 도구 부재, path visibility, execution policy 때문에 작업이 실패하면 가능한 범위에서 다음 순서를 따른다.

1. 실제 오류 원문과 exit code를 읽고 분류한다.
2. 실패한 동작이 read-only인지 mutation인지 구분한다.
3. 허용된 read-only 명령으로 현재 환경을 확인한다.
4. 더 좁고 비파괴적인 지원 경로를 시도한다.
5. 원래 목표 또는 동등한 확인을 다시 시도한다.
6. 현재 권한 안에서 안전하게 진행할 수 없을 때만 최소한의 사용자 조치가 필요한 사실을 보고한다.
7. 제한이 확인되고 안전한 지원 경로가 없을 때만 blocked로 종료한다.

---

## 2. 기본 행동

WORK는 첫 명령 실패 뒤에도 원래 목표를 유지하면서 원인을 계속 추론한다. mutation보다 read-only 진단을 먼저 사용하고, 가장 좁은 우회를 선호한다.

다음 범주를 서로 구분한다.

- 파일시스템 권한
- sandbox policy
- Git ownership 또는 safe-directory
- 도구 부재
- 잘못된 working directory
- 파일 부재
- 환경변수 문제
- dependency/setup 실패
- network 제한
- process execution 제한

같은 실패 명령을 아무 조건 변화 없이 반복하지 않는다. 보안 제어를 광범위하게 해제하지 않는다. `chmod 777`, 재귀 ownership 변경, blanket ACL reset, 전역 Git 보안 완화, 시스템 전역 설정 변경을 첫 해결책으로 사용하지 않는다.

.git, lock file, build cache, 설정 파일을 원인 근거 없이 삭제하지 않는다. 기존 소스와 repository 상태를 보존한다.

---

## 3. 오류 분류

### A. 파일시스템 권한

대표 신호는 Permission denied, Access is denied, EPERM, EACCES, 파일 생성·쓰기·삭제 실패다.

현재 위치, 대상 존재 여부, 읽기 가능 여부, 쓰기가 필요한 경우 쓰기 가능 여부, read-only mount 여부를 확인한다. 즉시 재귀 권한 변경을 하지 않는다.

### B. sandbox 정책 제한

operation denied by sandbox, command not permitted, protected path, blocked execution, 제한된 process/network 접근 등이 해당한다.

정확히 어떤 operation이 막혔는지 식별하고 workspace 안의 허용된 동등 작업이나 지원되는 API/도구가 있는지 확인한다. sandbox enforcement를 우회하려 하지 않는다.

### C. Git ownership / safe-directory

`fatal: detected dubious ownership in repository` 같은 오류는 일반 sandbox 오류로 취급하지 않는다.

repository root를 확인하고 의도한 저장소인지 확인한다. 필요하면 지속적 global 설정보다 command-scoped safe-directory 같은 좁은 방법을 우선한다. wildcard safe.directory는 사용하지 않는다.

### D. 잘못된 working directory

repository, package/build config, 예상 파일이 보이지 않거나 상대경로가 잘못 해석되면 현재 위치와 directory contents를 먼저 확인한다.

### E. 명령 또는 dependency 부재

command not found, executable not recognized, module/package missing이면 설치부터 하지 말고 이미 설치된 동등 도구와 project-local tool을 먼저 확인한다.

### F. network 제한

DNS 실패, connection denied, registry 접근 불가가 발생하면 작업이 실제로 network를 필요로 하는지 확인하고 local cache/dependency/file을 우선한다. 명시적으로 차단된 외부 접근을 반복하지 않는다.

---

## 4. 필수 진단 순서

### Step 1 — 실제 실패 확보

가능하면 다음을 확보한다.

- 실행한 command/action
- exit code
- stderr 또는 실제 오류
- 현재 working directory
- 대상 path/resource

실제 메시지를 읽기 전에 임의로 요약하지 않는다.

### Step 2 — 위치와 context 확인

현재 directory, directory listing, repository root, 대상 file 존재 여부, tool/version availability를 허용된 read-only 방법으로 확인한다.

Windows에서는 `Get-Location`, `Get-ChildItem` 같은 최소 명령을 사용할 수 있다. 현재 환경에서 허용되는 명령만 사용한다.

### Step 3 — read failure와 write failure 분리

읽기는 되지만 쓰기만 실패한다면 프로젝트 전체가 접근 불가라고 결론내리지 않는다. 문제를 가장 작은 path 또는 operation으로 좁힌다.

### Step 4 — 비파괴 우회

가능한 예시는 올바른 working directory 사용, absolute path 사용, project-local temp/output 경로 사용, command-scoped Git option 사용, 이미 설치된 interpreter/tool 사용, mutation 전 read-only inspection, raw shell 대신 지원되는 file/repository API 사용이다.

현재 WORK 계약이 금지하는 build/test/run/Git mutation은 우회 방법으로 사용하지 않는다.

### Step 5 — 원래 목표 재시도

우회 방법 자체만 확인하고 끝내지 않는다. 원래 WORK 목표를 다시 수행하거나 원래 실패 operation과 동등한 확인을 수행한다.

### Step 6 — 필요한 경우에만 중단

계속할 수 없다면 정확히 무엇이 막혔는지, 무엇을 확인했는지, 남은 제한이 무엇인지, 필요한 최소 다음 조치가 무엇인지 @@SUMMARY와 @@ISSUES에 남기고 blocked를 반환한다.

---

## 5. 재시도 정책

blind retry가 아니라 adaptive retry를 사용한다.

권장 흐름:

1. 최초 시도
2. 실패 원문 확인
3. 안전한 진단 경로 하나
4. 더 좁은 비파괴 방법 하나
5. 원래 목표 재시도
6. 필요하면 실질적으로 다른 안전한 방법 하나
7. 구조적 제한이면 blocked

입력, path, permission, tooling, method가 바뀌지 않은 동일 명령 반복은 유효한 재시도가 아니다. 무한 반복하지 않는다.

---

## 6. 권한 상승

권한 상승은 기본 해결책이 아니다. 현재 역할이 직접 권한을 확대하거나 시스템 보안 설정을 완화하지 않는다.

필요한 경우 보고에는 차단된 정확한 operation, 필요한 이유, 영향 path/resource, 더 좁은 방법이 부족한 이유, 필요한 조치가 temporary인지 persistent인지 명시한다.

항상 single file > directory > project > user-level > system-wide, temporary/command-scoped > persistent 순서를 선호한다.

---

## 7. 안전한 mutation

현재 WORK 계약이 허용한 WRITE_PATH 안에서만 mutation한다.

수정 전에 대상이 의도한 프로젝트에 속하는지 확인하고, 가능한 경우 현재 내용을 읽고, 필요한 부분만 변경하고, 관계없는 formatting/cleanup을 하지 않는다.

설정 변경이 허용되는 경우에도 command-scoped > project-local > user-level > system-wide 순서를 선호한다. 현재 WORK 계약이 금지하는 시스템 전역 변경은 수행하지 않는다.

---

## 8. Git 관련 오류

Git 실패를 일반 sandbox 오류로 뭉뚱그리지 않는다. read-only Git 확인이 현재 WORK 계약에 의해 허용되는 범위에서 repository root와 상태를 확인할 수 있다.

사용자 변경은 중요한 상태이므로 .git 삭제, repository 재초기화, branch reset, git clean -fdx, history rewrite, global Git security 완화를 하지 않는다.

Git mutation은 Worker finalization 책임이므로 WORK가 수행하지 않는다.

---

## 9. build와 dependency 오류

현재 WORK 역할은 build/test/run을 수행하지 않는다. 따라서 build 결과를 직접 복구하려 하지 않는다.

다만 파일을 읽는 과정에서 dependency/setup/toolchain 문제를 발견하면 source error, missing dependency, toolchain mismatch, generated-file problem, permission/path/sandbox 문제 중 무엇인지 구분하여 보고할 수 있다.

---

## 10. 플랫폼 독립성

sudo, administrator 권한, POSIX permission, PowerShell, package manager, writable home directory, repository ownership을 가정하지 않는다. 실제 환경을 먼저 확인한다.

---

## 11. 제한된 추론 능력용 결정 절차

환경 command가 실패하면 다음 순서를 따른다.

1. 전체 오류를 읽는다.
2. current directory를 확인한다.
3. target 존재 여부를 확인한다.
4. read인지 write인지 구분한다.
5. Git 관련이면 repository root와 Git 오류 유형을 확인한다.
6. 더 좁은 비파괴 방법 하나를 시도한다.
7. 원래 목표를 재시도한다.
8. 필요하면 실질적으로 다른 안전한 방법 하나를 시도한다.
9. 그래도 막히면 blocked와 최소 필요 조치를 보고한다.

command failed에서 곧바로 task cannot be completed로 점프하지 않는다. 추가 진단 자체가 금지된 경우는 예외다.

---

## 12. 중단 조건

다음이면 local workaround를 중단한다.

- 환경이 필요한 operation을 명시적으로 금지한다.
- 다음 단계가 security control 우회를 요구한다.
- 사용자 데이터 삭제·손상 위험이 있다.
- 필요한 credential/secret이 없다.
- 사용자 승인 없이는 진행할 수 없다.
- 합리적인 안전한 접근을 모두 확인했다.
- 현재 프로젝트/역할 계약이 해당 행동을 금지한다.

중단 시 원래 목표, 구체적 실패, 확인한 내용, 남은 block, 최소 다음 조치를 보고한다.

---

## 13. 성공 기준

복구 성공은 원래 operation 또는 동등한 지원 방법으로 요청 결과를 달성하고 확인한 상태다.

제한 확인은 실패가 분류되었고, 합리적인 안전한 방법을 시도했으며, 남은 제한과 필요한 최소 다음 조치를 식별한 상태다.

---

## 14. 간단 결정 트리

Command/action 실패
→ 실제 오류 확인
→ path/context 문제인가? 맞으면 교정 후 재시도
→ Git 특화 문제인가? 맞으면 ownership/safe-directory/repository 상태를 좁게 진단
→ filesystem permission 문제인가? 맞으면 read/write 범위를 좁혀 확인
→ sandbox policy 문제인가? 맞으면 지원되는 API/tool/allowed equivalent 사용
→ tooling/dependency 부재인가? 맞으면 project-local/supported setup 확인
→ 어느 범주에도 명확히 맞지 않으면 실제 오류를 더 조사하고 임의로 sandbox라고 결론내리지 않는다.

---

## 15. 요약

sandbox와 싸워 보안 경계를 우회하지 않는다. 반대로 첫 sandbox 오류에서 너무 빨리 포기하지도 않는다.

정확한 제한을 진단하고, 현재 WORK 계약 안에서 가장 좁고 안전한 경로를 찾고, 원래 목표를 다시 시도한 뒤, 실제로 필요한 경우에만 blocked로 종료한다.
