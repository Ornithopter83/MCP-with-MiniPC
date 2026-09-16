# GPT Web Feedback

Updated: 2026-09-16

## 목적

이 파일은 ChatGPT Web이 GitHub 저장소를 검토한 뒤 Codex에게 전달하는 전용 피드백/작업 제안 채널이다.

우선순위:

1. `AGENTS.md`
2. `ProjectHub_IMPLEMENTATION_PLAN.md`
3. `CurrentWork.md`
4. 현재 활성 `tasks/*.md`
5. `GPT-Web-Feedback.md`

충돌 시 앞선 관리 문서와 활성 task를 우선한다.

---

## 최종 목표

1. 어떤 프로젝트든 연결하고 Git에 적합하지 않은 대용량 데이터까지 NAS/별도 저장소와 연계하여 자동 버전관리한다.
2. 서버 PC에 연결된 모든 프로젝트 상태를 Web ChatGPT가 조회·분석하고 다음 작업·위험·충돌·재개 지점을 피드백할 수 있게 한다.

MCP/Connector는 연결 수단이며 ProjectHub 자체의 목적은 아니다.

---

## 현재 상태

최신 확인 구현 커밋:

```text
0c7b6e03d2394195767caeb4f4d53791710ffeb4
05-C 완료
```

현재 상태:

```text
[x] 05-A heartbeat 구현 + 외부 DEV PC E2E 완료
[x] 05-B GitStateCollector 구현/테스트 완료
[x] 05-C FileSystemWatcher + debounce 구현
[x] RegisteredProjects 지원
[x] Git 상태 자동 POST 구현
[x] build/test 통과 기록
[ ] 05-C 실제 DEV PC 파일 변경 -> Server -> Supabase 자동 갱신 E2E
```

05-C 실제 E2E는 짧게 완료하고 05를 닫는다.

---

# 다음 큰 작업: 06 Large Data / NAS End-to-End

사용자는 실제 NAS1DUAL까지 연결해 대용량 파일 탐지, 업로드, 저장, 버전 연결을 가능한 한 하나의 numbered task 안에서 연속 진행하기를 원한다.

현재 구현 계획에서 NAS Gateway가 금지 범위라면 사용자 요구 변경을 반영하여 계획/task를 정식 갱신한다.

권장 순서:

```text
05-C E2E 완료
  -> 06 Large Data / NAS End-to-End
  -> 기존 06 이후 작업은 후순위/재번호
```

---

# 중요 정정: 기존 DFBlackbox NAS Gateway는 외부 의존성이 아니다

사용자가 제공한 이전 프로젝트 문서는 **참고 설계와 보안 원칙**이다.

기존 흐름:

```text
DFBlackbox Desktop
  -> Supabase Edge Function
  -> DB RPC로 장치/카메라/NAS scope 확인
  -> RS256 signed NAS assertion 발급
  -> NAS Gateway provision.php
  -> 승인 prefix 아래 폴더 생성 + 쓰기 검사
```

이전 프로젝트의 실제 소스나 endpoint가 현재 ProjectHub 저장소에 없어도 **ProjectHub NAS 구현을 중단하지 않는다.**

다음 원칙으로 진행한다.

```text
기존 DFBlackbox 구현
= 보안/구조 참고자료

ProjectHub NAS Gateway
= ProjectHub 요구사항에 맞게 새로 정의하고 새로 구현
```

기존 endpoint/API와의 호환성은 목표가 아니다. 기존 프로젝트의 내부 계약을 추측하여 복제하지도 않는다.

즉 Codex는 기존 별도 프로젝트 소스를 기다리지 말고, 이 저장소 안에서 ProjectHub 전용 NAS Gateway와 계약을 구현한다.

실제 NAS1DUAL 환경값만 E2E 직전에 사용자에게 요청한다.

예:

```text
NAS filesystem root
Gateway 배포 URL
공개키/개인키 환경설정 위치
실제 NAS 서비스 실행 방식
```

---

# 권장 저장소 구조

초기 구현은 같은 저장소 안에서 Server/Agent/Gateway 계약 버전을 같이 맞춘다.

예:

```text
MCP-with-MiniPC/
├─ src/
│  ├─ ProjectHub.Server
│  ├─ ProjectHub.Agent
│  └─ ...
└─ nas-gateway/
   ├─ common.php
   ├─ provision.php
   ├─ upload-start.php
   ├─ upload-chunk.php
   ├─ upload-status.php
   └─ upload-finalize.php
```

파일명은 구현상 더 적합한 형태로 바꿔도 되지만 역할은 분리한다.

장기적으로 안정화되면 Gateway를 별도 저장소로 분리할 수 있다.

---

# ProjectHub용 아키텍처

## Control Plane

```text
DEV PC Agent
  -> ProjectHub.Server
     - Agent/workstation 인증
     - project 등록/권한 확인
     - upload session 생성
     - signed NAS assertion 발급
     - Supabase metadata
     - large-object 상태 관리
     - Git commit <-> LargeDataSet <-> checkpoint 연결
```

## Data Plane

```text
DEV PC Agent
  -> ProjectHub NAS Gateway
     - assertion 검증
     - provision
     - chunk upload/resume
     - size/hash 검증
     - atomic finalize
     - 향후 restore/download
  -> NAS1DUAL
```

실제 binary payload는 ProjectHub.Server를 통과하지 않는다.

```text
Agent -> ProjectHub.Server : control/authorization/metadata
Agent -> NAS Gateway       : large binary payload
NAS Gateway -> NAS1DUAL    : physical storage
```

DEV PC에 SMB/NAS 계정이나 NAS filesystem 절대경로를 배포하지 않는다.

---

# Assertion: 기존 RS256 보안 원칙 계승

유지할 원칙:

```text
- 발급자만 private key 보유
- Gateway는 public key만 보유
- Agent의 ProjectHub 인증정보를 Gateway에 전달하지 않음
- Gateway에는 short-lived scope-limited assertion만 전달
- assertion으로 허용 project/operation/object/session 범위를 제한
- secret/private key/NAS credential은 Git/문서/로그에 기록하지 않음
```

ProjectHub.Server가 RS256 signed assertion을 발급한다.

권장 claim:

```json
{
  "v": 1,
  "iss": "projecthub-server",
  "aud": "projecthub-nas-gateway",
  "sub": "DEV-PC-01",
  "project_id": "project-a",
  "workstation_id": "DEV-PC-01",
  "operation": "upload",
  "upload_session_id": "...",
  "object_hash": "sha256:...",
  "size_bytes": 8589934592,
  "storage_scope": "ProjectHub/project-a",
  "iat": 0,
  "exp": 0,
  "jti": "..."
}
```

필요하면 `gateway_id`, `kid`, `location_id`를 추가한다.

assertion 하나로 NAS 전체 browse/delete/admin 권한을 얻을 수 없어야 한다.

`provision`, `upload`, 향후 `download` 권한은 operation으로 분리한다.

---

# Gateway 검증 규칙

ProjectHub Gateway는 최소 다음을 검증한다.

```text
JWT 구조/RS256 signature
v
iss
aud
iat/exp
jti
project/workstation/session
operation allow-list
object hash 형식
size 범위
storage_scope 상대경로 안전성
절대경로/.. / 잘못된 separator 차단
symlink/reparse 우회 차단
최종 real path가 NAS root 밖으로 탈출하지 않는지 확인
```

검증 실패 시 파일 쓰기를 시작하지 않는다.

임의 filesystem browse, arbitrary path write, shell, delete API는 만들지 않는다.

---

# ProjectHub provision

기존 카메라용 `live/recordings/events/temp` 폴더 구조는 복사하지 않는다.

ProjectHub는 최소 다음 구조를 준비한다.

```text
<ProjectHubRoot>/
  objects/
    sha256/
  staging/
```

권장 object 구조:

```text
objects/sha256/ab/<full-sha256>
staging/<upload-session-id>/
```

provision 완료 조건:

```text
NAS root 접근 가능
필요 디렉터리 안전 생성
path/symlink 검증
임시 파일 write + flush
임시 파일 삭제
ready 응답
```

---

# Large Data 상태와 저장 방식

기본 threshold는 설정 가능하게 하고 초기 기본값은 1 GiB로 둔다.

```text
LargeDataThresholdBytes = 1073741824
```

대용량 실제 object identity는:

```text
SHA-256 + size
```

원래 파일명/프로젝트 상대경로는 Supabase metadata에서 관리한다.

상태 예:

```text
LOCAL_ONLY
HASHING
UPLOADING
STAGED
CHECKPOINTED
ORPHANED
MISSING
MIGRATION_REQUIRED
```

핵심:

```text
NAS upload 완료 != 버전 확정
```

`STAGED`는 NAS 저장/검증 완료지만 Git commit과 아직 연결되지 않은 상태다.

`CHECKPOINTED`는 Git commit SHA + LargeDataSet + NAS object hashes가 연결된 상태다.

---

# chunked/resumable upload

1GB 이상 파일을 single request로 보내지 않는다.

ProjectHub용 Gateway 계약을 새로 정의한다.

개념 endpoint:

```text
POST /projecthub/provision
POST /projecthub/uploads
GET  /projecthub/uploads/{sessionId}
PUT  /projecthub/uploads/{sessionId}/chunks/{index}
POST /projecthub/uploads/{sessionId}/finalize
HEAD /projecthub/objects/{sha256}
GET  /projecthub/objects/{sha256}   # restore 단계에서 사용
```

정확한 URL 형태는 Codex가 일관된 규칙으로 정할 수 있다.

필수 특성:

```text
chunk upload
received chunk/status 조회
resume
streaming
staging
final size 검증
final SHA-256 검증
atomic finalize
동일 hash object 중복 저장 방지
```

초기 chunk size는 16~64 MiB 범위에서 설정 가능하게 둔다.

assertion이 만료돼도 upload session은 남아 있어야 하며, Server에서 새 assertion을 발급받아 이어서 resume할 수 있어야 한다.

---

# Startup Reconciliation 필수

Agent uptime이 정합성의 전제조건이 되어서는 안 된다.

Agent 시작 시 최소 다음을 수행한다.

```text
1. 등록 프로젝트 Git 상태 재수집
2. HEAD/dirty 확인
3. large-file inventory scan
4. Server의 마지막 known metadata 비교
5. 미완료 upload session 조회
6. 가능한 upload resume
7. 누락된 large object 탐지/보정
8. current state Server 갱신
9. 그 다음 watcher 동작
```

Agent가 꺼져 있는 동안 새 파일 생성/수정/Git commit/upload 중단이 발생해도 재실행 시 정합성을 복구해야 한다.

---

# 최초 등록과 작업 중 새 대용량 파일

초기 project 등록:

```text
전체 inventory scan
-> threshold/policy 적용
-> stable 확인
-> streaming SHA-256
-> object existence check
-> 필요 시 upload
-> STAGED
```

작업 중 새 파일:

```text
FileSystemWatcher event
-> stability window
-> threshold/policy
-> SHA-256
-> Server upload session/assertion
-> Agent -> Gateway chunk upload
-> Gateway finalize/hash verify
-> STAGED
```

이미 Git tracked인 큰 파일은 자동 `git rm`, `.gitignore` 수정 금지.

```text
MIGRATION_REQUIRED
```

로 표시하고 사용자 승인 작업으로 남긴다.

---

# Commit과 LargeData checkpoint 연결

commit 전 large binary는 미리 NAS에 올릴 수 있다.

예:

```text
HEAD AAA / dirty=true
8GB object -> STAGED

사용자가 commit
HEAD BBB

Agent/Server
-> current large-file mapping snapshot
-> LargeDataSet 생성
-> BBB <-> LargeDataSet 연결
-> CHECKPOINTED
```

Agent가 자동 `git add/commit/push/pull/reset/merge/rebase/clean`을 수행해서는 안 된다.

Supabase에는 metadata만 저장한다.

개념 스키마:

```text
large_objects
large_upload_sessions
project_large_files
large_data_sets
large_data_set_items
```

실제 binary는 NAS에만 둔다.

---

# Gateway 결과를 기준으로 성공 판정

Agent가 `upload success`라고 말했다는 이유만으로 `STAGED` 처리하지 않는다.

Gateway가 실제로 다음을 검증해야 한다.

```text
object 존재
size 일치
SHA-256 일치
finalize 성공
```

ProjectHub.Server는 Gateway verification/status 결과를 근거로 Supabase 상태를 전환한다.

---

# 구현을 어디까지 바로 진행할 것인가

NAS1DUAL의 실환경 정보가 없어도 다음은 즉시 구현 가능하다.

```text
A. 05-C 실제 E2E 마무리
B. 06 task/로드맵 공식화
C. ProjectHub.Server assertion issuer
D. ProjectHub 전용 NAS Gateway verifier/provision
E. content-addressed storage abstraction
F. upload session + chunk/resume/finalize
G. Supabase large-data metadata
H. Agent LargeDataScanner/stability/hash
I. Startup Reconciliation
J. STAGED 처리
K. Git commit -> LargeDataSet -> CHECKPOINTED
L. local/temp filesystem adapter를 사용한 자동 통합 테스트
```

Codex는 기존 별도 Gateway 프로젝트가 없다는 이유로 C~L을 중단하지 않는다.

실제 NAS1DUAL 연결 단계에서만 필요한 환경값을 사용자에게 요청한다.

```text
- 실제 NAS root path
- Gateway가 NAS1DUAL에서 실행되는 방식
- Gateway 외부/내부 URL
- 실제 public key/private key 환경변수 위치
- 테스트용 저장 위치
```

환경 의존 값은 코드에 하드코딩하지 않는다.

---

# 실제 NAS1DUAL E2E 완료 기준

최종 단계에서는 실제 환경에서 확인한다.

```text
DEV PC에 1GB 초과 테스트 파일 생성
-> Agent 탐지
-> stable/hash
-> Server upload session/assertion
-> Agent -> ProjectHub NAS Gateway chunk upload
-> NAS1DUAL object 생성
-> Gateway size/hash/finalize 검증
-> STAGED metadata

사용자가 Git commit
-> HEAD 변화 또는 startup reconciliation
-> LargeDataSet 생성
-> Git commit SHA <-> LargeDataSet <-> NAS object 연결
-> CHECKPOINTED

Agent 종료/재시작
-> startup reconciliation
-> 미완료 upload/누락 상태 resume 또는 복구
```

---

# Codex 수행 지침

1. `AGENTS.md` -> 구현계획 -> `CurrentWork.md` -> 활성 task -> 이 파일 순으로 읽는다.
2. 05-C 실제 DEV PC -> Server -> Supabase E2E를 먼저 짧게 닫는다.
3. 05를 완료 처리한 뒤 Large Data/NAS를 새 공식 numbered task로 추가한다.
4. **기존 DFBlackbox NAS Gateway 소스는 ProjectHub NAS 구현의 선행조건이 아니다.**
5. 제공된 이전 구조에서 RS256 assertion, 최소권한 scope, public-key verification, path traversal/symlink 방어, 실제 write 검증 원칙만 계승한다.
6. ProjectHub 전용 NAS Gateway 계약과 구현을 이 저장소 안에서 새로 만든다.
7. 기존 endpoint/API compatibility를 추측하거나 목표로 하지 않는다.
8. binary payload는 ProjectHub.Server를 통과시키지 않는다.
9. Agent -> NAS Gateway direct data path를 사용한다.
10. FileSystemWatcher만 믿지 말고 Startup Reconciliation을 구현한다.
11. 큰 파일은 streaming SHA-256 및 resumable chunk upload를 사용한다.
12. upload 완료는 STAGED이고 commit 연결 후에만 CHECKPOINTED다.
13. Git mutation은 수행하지 않는다.
14. Agent 성공 보고만 믿지 말고 Gateway size/hash/finalize를 검증한다.
15. NAS credential/private key/service key를 Git/문서/로그에 기록하지 않는다.
16. 실제 NAS1DUAL 환경값이 필요한 마지막 E2E 시점에만 사용자에게 구체적인 수동 설정 항목을 요청한다.
17. B~L은 가능한 한 한 numbered task에서 연속 진행하되 각 단계의 build/test 실제 결과를 기록한다.
