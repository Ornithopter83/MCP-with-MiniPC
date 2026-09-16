# GPT Web Feedback

Updated: 2026-09-16

## 목적

이 파일은 ChatGPT Web이 GitHub 저장소를 검토한 뒤 Codex에게 전달하는 전용 피드백/작업 제안 채널이다.

우선순위는 항상 다음과 같다.

1. `AGENTS.md`
2. `ProjectHub_IMPLEMENTATION_PLAN.md`
3. `CurrentWork.md`
4. 현재 활성 `tasks/*.md`
5. `GPT-Web-Feedback.md`

충돌 시 앞선 관리 문서와 활성 task를 우선한다.

---

## 최종 목표

1. 어떤 프로젝트든 연결하고, Git에 적합하지 않은 대용량 데이터까지 NAS/별도 저장소와 연계하여 자동 버전관리한다.
2. 서버 PC에 연결된 모든 프로젝트 상태를 Web ChatGPT가 조회·분석하고 다음 작업·위험·충돌·재개 지점을 피드백할 수 있게 한다.

MCP/Connector는 연결 수단이며 ProjectHub 자체의 목적은 아니다.

---

## 현재 확인 상태

최신 확인 구현 커밋:

```text
0c7b6e03d2394195767caeb4f4d53791710ffeb4
05-C 완료
```

현재 상태:

```text
[x] 05-A heartbeat 구현 + 외부 DEV PC E2E 완료
[x] 05-B GitStateCollector 구현/테스트 완료
[x] 05-C FileSystemWatcher + 1초 debounce 구현
[x] RegisteredProjects 지원
[x] Git 상태 자동 POST 구현
[x] heartbeat와 project-state loop 분리
[x] build/test 통과 기록
[ ] 05-C 실제 DEV PC 파일 변경 -> Server -> Supabase 자동 갱신 E2E
```

05-C 실제 E2E를 짧게 끝내고 05를 닫은 뒤 NAS 작업으로 넘어간다.

---

# 다음 큰 작업: Large Data / NAS End-to-End

사용자는 다음 단계에서 실제 NAS1DUAL까지 연결해 대용량 파일 탐지, 업로드, 저장, 버전 연결을 가능한 한 하나의 numbered task 안에서 연속 수행하기를 원한다.

현재 구현 계획에서 NAS Gateway가 금지 범위라면 사용자 요구 변경을 반영하여 구현 계획과 task 번호를 먼저 정식 갱신한다.

권장 순서:

```text
05-C E2E 완료
  -> 06 Large Data / NAS End-to-End
  -> 기존 06 이후 작업은 필요 시 후순위/재번호
```

---

# 중요: 기존 NAS assertion/provision 구현은 별도 프로젝트의 참고 구현이다

사용자가 제공한 기존 구조는 현재 `MCP-with-MiniPC` 저장소의 코드가 아니다.

기존 별도 프로젝트에서는 다음 흐름을 사용했다.

```text
DFBlackbox Desktop
  -> Supabase Edge Function
  -> DB RPC로 장치/카메라/NAS scope 확인
  -> RS256 signed NAS assertion 발급
  -> NAS Gateway provision.php
  -> 허용 prefix 아래 폴더 생성 + 쓰기 검사
  -> Desktop이 결과 보고
```

기존 구현 파일 예시는 다음이었으나, **현재 저장소에 존재한다고 가정하지 않는다.**

```text
supabase/functions/recording-media/index.ts
DFBlackbox/Core/HttpRecordingMediaClient.cs
DFBlackbox/Core/NasProvisioningService.cs
nas-gateway/common.php
nas-gateway/provision.php
```

Codex는 현재 저장소에서 이 파일들을 찾지 못했다고 새 구현을 추측해서 만들지 않는다.

기존 프로젝트의 보안 아이디어와 계약 원칙을 참고하되 ProjectHub 용도로 재설계한다. 실제 NAS Gateway 수정이 필요하면 ProjectHub 저장소 작업과 분리하고, 사용자가 별도 Gateway 프로젝트를 제공하거나 접근 경로를 알려주기 전에는 그 프로젝트의 내부 구현을 임의로 가정하지 않는다.

---

# ProjectHub용 개선 구조

기존 흐름의 좋은 점은 유지하되 역할을 ProjectHub에 맞게 바꾼다.

## Control Plane

```text
DEV PC Agent
  -> ProjectHub.Server
     - Agent/workstation 인증
     - project 권한/등록 확인
     - Supabase에서 현재 NAS location/storage scope 확인
     - upload session 생성
     - signed NAS assertion 발급
     - large-object/checkpoint metadata 관리
     - Git commit <-> LargeDataSet 연결
```

## Data Plane

```text
DEV PC Agent
  -> NAS Gateway
     - assertion 검증
     - provision
     - chunk upload/resume
     - final size/hash 검증
     - atomic finalize
     - 향후 restore/download
  -> NAS1DUAL
```

따라서 실제 대용량 binary payload는 ProjectHub.Server를 통과하지 않는다.

```text
Agent -> ProjectHub.Server : 권한/세션/assertion/metadata
Agent -> NAS Gateway       : 실제 대용량 파일 데이터
NAS Gateway -> NAS1DUAL    : 실제 저장
```

ProjectHub.Server는 control plane이고 1GB~수십 GB 파일 relay 서버가 아니다.

DEV PC에는 SMB 계정, NAS filesystem 절대경로, NAS 관리자 자격증명을 배포하지 않는다.

---

# Assertion 설계: 기존 RS256 패턴을 ProjectHub에 맞게 계승

기존 프로젝트의 핵심 보안 원칙은 그대로 유효하다.

```text
- 발급 서버만 개인키 보유
- NAS Gateway는 공개키만 보유
- Agent는 ProjectHub 인증정보를 NAS Gateway에 전달하지 않음
- NAS Gateway에는 짧은 수명의 scope-limited assertion만 전달
- NAS 경로/작업 범위를 assertion claim으로 제한
```

ProjectHub.Server가 RS256 또는 동등한 비대칭 서명으로 assertion을 발급한다.

초기에는 기존 검증 구현과 호환성이 중요하면 RS256을 그대로 우선한다. 키 교체가 필요해질 경우 `kid`를 지원할 수 있게 계약 버전을 둔다.

## 권장 ProjectHub assertion claim

기존의 `camera_id`, 카메라 `prefix` 대신 ProjectHub 개념으로 바꾼다.

```json
{
  "v": 1,
  "iss": "projecthub-server",
  "aud": "projecthub-nas-gateway",
  "sub": "DEV-PC-01",
  "project_id": "project-a",
  "workstation_id": "DEV-PC-01",
  "location_id": "nas-location-id",
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

필요하면 `gateway_id` 또는 기존 방식과 유사한 gateway binding claim을 추가한다. 다만 hostname 문자열 하나만 신뢰하는 구조보다 `aud + gateway_id + Server/Gateway 설정`의 조합으로 제한하는 방향을 우선 검토한다.

### claim 최소 권한 원칙

assertion 하나로 NAS 전체를 사용할 수 없어야 한다.

최소 다음이 고정되어야 한다.

```text
특정 project
특정 workstation/Agent
특정 NAS location
특정 operation
특정 upload session
특정 object hash
정확한 또는 최대 허용 size
특정 storage scope
짧은 exp
고유 jti
```

업로드 assertion으로 delete/list/admin 동작을 허용하지 않는다.

---

# ProjectHub.Server가 assertion을 발급하는 조건

기존 프로젝트는 Edge Function이 DB RPC로 승인 scope를 확인한 뒤 assertion을 발급했다.

ProjectHub에서도 같은 원칙을 유지한다.

```text
Agent -> ProjectHub.Server upload request
  -> Agent/workstation 인증 확인
  -> project 등록 확인
  -> 해당 project에 허용된 NAS location 조회
  -> large-data policy 확인
  -> object hash/size/upload session 확인
  -> Supabase metadata에 session 생성
  -> 승인된 scope만 assertion에 넣어 발급
```

Agent가 임의의 NAS 상대경로나 arbitrary prefix를 assertion 요청에 넣고 그대로 승인받는 구조를 만들지 않는다.

`storage_scope` 또는 storage key는 Server가 project/NAS 정책으로 결정한다.

---

# NAS Gateway 검증 규칙: 기존 구조를 적극 재사용

기존 Gateway가 수행하던 다음 검증 원칙은 ProjectHub에서도 유지한다.

```text
- JWT 구조/서명 검증
- v 확인
- iss 확인
- aud 확인
- iat/exp 확인
- jti 형식 및 필요 시 replay 방지
- project/workstation/location/session 식별자 검증
- operation allow-list 검증
- storage_scope 상대경로 검증
- 절대경로/.. / 빈 segment / 비정상 separator 차단
- symlink/reparse 우회 방지
- 최종 real path가 NAS root 밖으로 탈출하지 않는지 확인
```

검증 실패는 인증/권한 오류로 명확히 실패시키고 파일 쓰기를 시작하지 않는다.

Gateway endpoint에는 임의 filesystem browse, arbitrary path, shell, delete 기능을 같이 넣지 않는다.

---

# provision.php 개념은 ProjectHub용 provision endpoint로 축소/변경

기존 `provision.php`는 카메라별 다음 폴더를 만들었다.

```text
live
recordings
events
temp
```

ProjectHub에서는 이 구조를 복사하지 않는다. 카메라 제품용 디렉터리 구조이기 때문이다.

ProjectHub provision은 최소한 다음 NAS root 구조만 준비/검증한다.

```text
<ProjectHubRoot>/
  objects/
    sha256/
  staging/
```

필요 시 project별 metadata namespace를 둘 수 있지만 실제 binary object identity는 SHA-256 content-addressed storage를 우선한다.

예:

```text
objects/sha256/ab/<full-hash>
staging/<upload-session-id>/
```

provision 성공 조건은 단순 폴더 존재가 아니라 기존 방식처럼 실제 쓰기 검사를 포함한다.

```text
1. NAS root 존재/접근 확인
2. 필요한 ProjectHub root/subdir 안전 생성
3. symlink/reparse/path escape 검증
4. 작은 임시 파일 생성
5. write + flush
6. 임시 파일 삭제
7. ready 응답
```

이미 존재하는 정상 디렉터리는 성공으로 처리한다.

---

# 기존 구조에서 개선할 점

## 1. Agent 결과 보고만으로 성공 처리하지 않는다

기존 흐름에는 Desktop이 결과를 서버에 보고하는 단계가 있었다.

ProjectHub에서는 Agent의 `성공` 보고만 신뢰하지 않는다.

최종 업로드 성공 기준은 NAS Gateway가 실제로 다음을 검증한 결과다.

```text
object 존재
size 일치
SHA-256 일치
finalize 성공
```

ProjectHub.Server는 Gateway status/finalize result를 확인한 뒤 Supabase metadata를 `STAGED`로 전환한다.

## 2. provision assertion과 upload assertion을 분리한다

폴더 준비/쓰기 검사용 권한과 실제 대용량 object upload 권한을 하나의 광범위 assertion으로 합치지 않는다.

```text
operation = provision
operation = upload
operation = download (future)
```

각 operation에 필요한 claim만 허용한다.

## 3. 1시간 고정 만료를 그대로 복제하지 않는다

기존 장치 assertion은 1시간 유효했다.

ProjectHub에서는 용도별 TTL을 설정 가능하게 한다.

```text
provision assertion : 짧게
upload assertion    : 대용량 전송을 완료할 수 있을 만큼, 단 최소 범위
```

대용량 업로드가 TTL보다 길어질 수 있으므로 `resume/session refresh`를 지원한다. 만료된 assertion 때문에 처음부터 파일을 다시 올리게 만들지 않는다.

## 4. hash가 identity가 된다

기존 시스템은 카메라 상대경로 중심이었다.

ProjectHub에서는 대용량 파일의 실체는 다음으로 식별한다.

```text
SHA-256 + size
```

원래 프로젝트 상대경로/파일명은 metadata에서 관리한다.

동일 hash object가 있으면 업로드를 생략할 수 있어야 한다.

---

# chunked/resumable upload

1GB 이상을 단일 HTTP request로 보내지 않는다.

Gateway 계약은 실제 기존 프로젝트 구현 확인 후 이름을 정하되 개념적으로 다음 기능이 필요하다.

```text
provision
upload session start/accept
chunk upload
received chunk/status 조회
resume
finalize
object verification
```

초기 chunk size는 16~64 MiB 정도의 설정 가능한 값으로 시작할 수 있다.

Agent와 Gateway 모두 파일 전체를 RAM에 올리지 않고 streaming 처리한다.

업로드가 끊기면 같은 upload session에서 받은 chunk 이후부터 재개한다.

---

# 대용량 파일 lifecycle

최소 상태:

```text
LOCAL_ONLY          로컬 PC에만 존재
HASHING             hash 계산 중
UPLOADING           NAS Gateway 업로드 중
STAGED              NAS 저장 + Gateway 검증 완료, 아직 commit 미연결
CHECKPOINTED         Git commit + LargeDataSet과 연결 완료
ORPHANED            NAS에는 있으나 checkpoint에서 참조되지 않음
MISSING             metadata는 있으나 NAS object가 없음
MIGRATION_REQUIRED  이미 Git이 추적 중인 대용량 파일
```

핵심:

```text
NAS upload 완료 != 버전 확정
```

대용량 데이터는 commit 전에 미리 `STAGED`까지 올릴 수 있다.

사용자가 Git commit을 하면:

```text
Git commit SHA
+
현재 LargeDataSet snapshot
+
NAS object hashes
=
CHECKPOINTED
```

Agent는 자동 git add/commit/push/reset/merge를 하지 않는다.

---

# Agent는 상시 실행을 전제로 하지 않는다

FileSystemWatcher만 믿지 않는다.

Startup Reconciliation을 필수 구현한다.

```text
Agent 시작
  -> 등록 프로젝트 Git 상태 재수집
  -> HEAD/dirty 확인
  -> large-file inventory scan
  -> Server의 마지막 metadata와 비교
  -> unfinished upload session 조회
  -> resume 가능 작업 재개
  -> 누락된 large object 탐지
  -> commit/checkpoint 누락 보정
  -> 현재 상태 전송
  -> watcher 지속
```

Agent가 꺼져 있던 동안 새 파일 생성, 파일 변경, Git commit이 발생해도 다음 실행에서 정합성을 복구할 수 있어야 한다.

---

# 최초 프로젝트 등록 처리

기본 threshold는 설정 가능하게 하고 최초값은 1 GiB로 둘 수 있다.

```text
LargeDataThresholdBytes = 1073741824
```

최초 등록 시 전체 inventory를 1회 스캔한다.

```text
Git untracked/ignored large file
  -> hash -> upload -> STAGED 가능

Git tracked large file
  -> 자동 git rm 금지
  -> 자동 .gitignore 수정 금지
  -> MIGRATION_REQUIRED
```

기존 Git 상태를 몰래 변경하지 않는다.

---

# Supabase metadata 경계

실제 binary는 NAS에만 둔다.

Supabase에는 개념적으로 다음만 둔다.

```text
nas_locations / storage_locations
upload_sessions
large_objects
project_large_files
large_data_sets
large_data_set_items
checkpoint metadata
```

예시 핵심 필드:

```text
large_objects
- id
- sha256 unique
- size_bytes
- storage_key
- verified_at

upload_sessions
- id
- project_id
- workstation_id
- object_hash
- size_bytes
- location_id
- state
- created_at
- expires_at

large_data_sets
- id
- project_id
- commit_sha
- created_at

large_data_set_items
- set_id
- relative_path
- object_id
```

NAS 절대경로나 credential을 Git manifest에 넣지 않는다.

---

# 다음 numbered task를 가능한 한 통으로 수행

권장 단계:

```text
A. 05-C 실제 E2E 마무리 및 05 완료
B. 기존 별도 NAS Gateway 계약을 참고한 ProjectHub용 assertion/storage contract 설계
C. Supabase metadata/schema + ProjectHub.Server assertion/session API
D. NAS Gateway의 실제 기존 코드/endpoint가 제공되면 adapter/필요 확장 구현
E. provision/read-write 실제 NAS1DUAL 검증
F. LargeDataScanner + Startup Reconciliation + streaming hash/stability
G. chunk/resume + STAGED upload
H. Git commit -> LargeDataSet -> checkpoint 확정
I. Agent 종료/재시작 포함 실제 NAS1DUAL E2E
```

가능한 한 같은 numbered task 안에서 연속 진행한다. 각 단계 build/test 및 실제 결과를 기록한다.

단, **기존 NAS Gateway 프로젝트의 실제 코드나 endpoint 계약이 필요한 시점에는 추측 구현하지 말고 사용자에게 해당 프로젝트/파일을 요청한다.**

---

# 실제 E2E 완료 기준

```text
DEV PC에 1GB 초과 테스트 파일 생성
-> Agent 탐지/안정화
-> streaming SHA-256
-> ProjectHub.Server session/assertion 발급
-> Agent가 assertion만 NAS Gateway에 전달
-> Agent -> NAS Gateway 직접 chunk upload
-> NAS1DUAL actual object 생성
-> Gateway가 size/hash/finalize 검증
-> Server/Supabase STAGED 확인

사용자가 Git commit
-> Agent HEAD 변화 감지 또는 startup reconciliation
-> LargeDataSet snapshot
-> commit SHA <-> LargeDataSet <-> NAS objects 연결
-> CHECKPOINTED 확인

Agent 종료/재실행
-> startup reconciliation
-> 미완료 upload 또는 누락 상태 복구
```

이 흐름까지 성공하면 Git과 실제 NAS 대용량 데이터가 하나의 복원 가능한 ProjectHub 버전으로 연결된 것이다.

---

## Codex 수행 지침

1. `AGENTS.md` -> 구현 계획 -> `CurrentWork.md` -> 활성 task -> 이 파일 순으로 읽는다.
2. 05-C 실제 E2E를 짧게 완료하고 05를 닫는다.
3. NAS/large-data 요구를 공식 numbered task로 승격하고 기존 계획의 NAS 금지 경계를 사용자 요구에 맞게 갱신한다.
4. 기존 assertion/provision 구조는 **별도 프로젝트의 검증된 참고 구현**이며 현재 repo 내부 구현으로 간주하지 않는다.
5. 기존 구조의 RS256 공개키 검증, 제한된 prefix/scope, path traversal/symlink 방어, 쓰기 probe 원칙을 ProjectHub용으로 계승한다.
6. 카메라용 claim/폴더 구조를 복사하지 말고 ProjectHub의 project/workstation/object/session/checkpoint 개념으로 재설계한다.
7. ProjectHub.Server는 control plane이고 실제 large binary를 relay하지 않는다.
8. 실제 payload는 Agent -> NAS Gateway 직통으로 전송한다.
9. provision과 upload 권한을 분리하고 최소 권한 assertion을 사용한다.
10. Agent 성공 보고가 아니라 Gateway의 실제 size/hash/finalize 검증을 저장 완료의 기준으로 삼는다.
11. Agent uptime에 의존하지 않도록 Startup Reconciliation을 구현한다.
12. 대용량 업로드는 chunk/resume/streaming 구조로 구현한다.
13. NAS upload(STAGED)와 Git commit에 연결된 version(CHECKPOINTED)을 분리한다.
14. Git tracked large file은 자동 제거하지 않고 MIGRATION_REQUIRED로 보고한다.
15. Git mutation, arbitrary NAS browse/delete, remote shell을 추가하지 않는다.
16. 비밀키/NAS credential/서비스 키를 저장소와 로그에 기록하지 않는다.
17. 기존 NAS Gateway 프로젝트의 실제 코드/계약이 필요한 시점에는 추측하지 말고 사용자에게 자료를 요청한다.
18. 가능한 한 B~I를 하나의 큰 task에서 연속 수행하되 실제 검증 실패 시 다음 단계로 억지로 진행하지 않는다.
