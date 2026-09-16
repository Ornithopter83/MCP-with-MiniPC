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

05-C의 남은 실제 E2E는 짧게 완료하고 05를 닫는다.

---

# 다음 큰 작업: Large Data / NAS End-to-End

사용자는 다음 단계에서 실제 NAS1DUAL까지 연결해 대용량 파일 탐지, 업로드, 저장, 버전 연결을 가능한 한 하나의 큰 numbered task 안에서 연속 수행하기를 원한다.

현재 로드맵에서 NAS Gateway가 금지 범위라면 사용자 요구 변경을 반영하여 구현 계획과 task 번호를 먼저 정식 갱신한다. 기존 Project 상태 API 작업은 뒤로 미뤄도 된다.

권장 순서:

```text
05-C E2E 완료
  -> 06 Large Data / NAS End-to-End
  -> 기존 06 이후 작업 재번호/후순위 이동
```

---

## 가장 중요한 아키텍처 수정

대용량 binary payload를 ProjectHub.Server가 중계하지 않는다.

### Control Plane

```text
DEV PC Agent
  -> ProjectHub.Server
     - workstation/project 검증
     - upload session 생성
     - signed NAS assertion 발급
     - Supabase metadata
     - checkpoint 관리
     - Git commit <-> LargeDataSet 연결
```

### Data Plane

```text
DEV PC Agent
  -> NAS Gateway
     - signed assertion 검증
     - chunk upload/resume
     - size/hash 검증
     - finalize
     - download/restore
  -> NAS1DUAL
```

즉 기존 NAS1DUAL 흐름:

```text
앱 -> Supabase Edge Function -> signed NAS assertion -> NAS Gateway -> NAS
```

을 ProjectHub에서는 다음처럼 발전시킨다.

```text
Agent -> ProjectHub.Server -> signed NAS assertion
Agent ======================> NAS Gateway -> NAS1DUAL
          실제 대용량 payload 직접 전송
```

ProjectHub.Server는 control plane이며 1GB~수십 GB 파일 relay 서버가 아니다.

DEV PC에는 SMB/NAS 계정이나 NAS filesystem 경로를 제공하지 않는다.

---

## 기존 NAS Gateway 재사용

기존 `provision.php` 및 assertion 검증 체계를 가능한 한 재사용한다. 처음부터 NAS 서비스를 새로 만들지 않는다.

Gateway는 필요에 따라 다음 정도로 확장한다.

```text
POST /provision
POST /upload/start
PUT  /upload/{session}/{chunk}
GET  /upload/{session}
POST /upload/{session}/finalize
GET  /objects/{hash}          # restore/download용, 후속 가능
```

실제 endpoint 명칭은 기존 NAS Gateway 구조에 맞게 조정한다.

---

## signed NAS assertion

ProjectHub.Server가 짧은 수명의 최소 권한 assertion을 발급한다.

개념 예:

```json
{
  "projectId": "project-a",
  "workstationId": "DEV-PC-01",
  "operation": "upload",
  "uploadSessionId": "...",
  "objectHash": "sha256:...",
  "maxSize": 8589934592,
  "exp": "...",
  "nonce": "..."
}
```

권한은 최소한 다음으로 제한한다.

```text
특정 project
특정 workstation
특정 operation
특정 upload session/object hash
허용 size
짧은 만료시간
nonce/replay 방지
```

비밀키, NAS 자격증명, assertion signing key는 저장소/문서/로그에 기록하지 않는다.

---

## Agent 실행 시점에 의존하지 않는 구조 — 필수

Agent는 항상 켜져 있을 필요가 없다. 따라서 FileSystemWatcher만으로 상태를 유지하면 안 된다.

**Startup Reconciliation을 필수 구현한다.**

Agent 시작 시:

```text
1. 등록 프로젝트 현재 Git 상태 재수집
2. 현재 HEAD / dirty 재확인
3. 대용량 파일 inventory 재스캔
4. Server의 마지막 known metadata와 비교
5. 미완료 upload session 조회
6. 가능한 upload 재개
7. 누락된 large object 탐지/보정
8. 현재 상태를 Server에 갱신
9. 그 다음 FileSystemWatcher 시작/계속
```

따라서 Agent가 꺼진 사이에 다음이 발생해도 재실행 시 복구 가능해야 한다.

```text
- 새 대용량 파일 생성
- 기존 대용량 파일 변경
- Git commit 발생
- upload 중단
```

Agent uptime이 버전 정합성의 전제조건이 되어서는 안 된다.

---

## 대용량 파일 정책

기본 threshold는 설정 가능하게 하고 초기 기본값은 1 GiB로 둘 수 있다.

```text
LargeDataThresholdBytes = 1073741824
```

향후 프로젝트별 include/exclude pattern 확장을 막지 않는다.

초기 프로젝트 등록 시 전체 inventory scan을 1회 수행하고, 이후 watcher는 재검사 trigger로 사용한다.

Git 상태 수집과 Large Data 처리는 분리한다.

```text
FileActivityMonitor
  -> GitStateCollector
  -> LargeDataCoordinator
```

---

## 파일 안정화와 hashing

큰 파일은 생성 이벤트 직후 전송하지 않는다.

최소 조건:

```text
- 일정 stability window 동안 size/mtime 변화 없음
- read 가능
- 이후 streaming SHA-256
```

수 GB~수십 GB 파일을 메모리에 전체 로드하지 않는다. hash/upload는 streaming이며 취소 가능해야 한다.

---

## 실제 NAS 저장 방식

content-addressed object 저장을 우선한다.

예:

```text
<ProjectHub NAS root>/
  objects/
    sha256/
      ab/
        <full-hash>
  staging/
    <upload-session-id>/
```

동일 hash object가 이미 있으면 중복 업로드/중복 저장을 피한다.

원래 파일명과 프로젝트 상대경로는 Supabase metadata/manifest에서 관리한다. Git manifest에 실제 NAS 절대경로/SMB 경로를 넣지 않는다.

---

## chunked/resumable upload 필수

1GB 이상 파일을 단일 HTTP request로 보내지 않는다.

초기에는 16~64 MiB 정도의 고정 chunk 크기로 시작할 수 있다.

필수 특성:

```text
- chunk 단위 업로드
- 이미 받은 chunk 조회
- 중단 후 재개
- staging 후 atomic finalize
- 최종 size 검증
- 최종 SHA-256 검증
```

ProjectHub.Server는 upload session과 authorization/metadata를 관리하고, chunk payload는 Agent가 NAS Gateway로 직접 보낸다.

---

## 대용량 데이터 상태 모델

최소 다음 상태를 구분한다.

```text
LOCAL_ONLY   개발 PC에만 존재
HASHING      hash 계산 중
UPLOADING    NAS Gateway로 전송 중
STAGED       NAS 저장/검증 완료, 아직 Git commit과 연결 안 됨
CHECKPOINTED Git commit + LargeDataSet과 연결 완료
ORPHANED     NAS에는 있으나 어떤 checkpoint에서도 참조되지 않음
MISSING      metadata는 있으나 실제 NAS object가 없음
MIGRATION_REQUIRED  기존 Git tracked 대용량 파일
```

핵심 원칙:

```text
NAS upload 완료 != 버전 확정
```

---

## LIVE STATE와 확정 VERSION 분리

commit 전에도 실시간 상태는 Server로 보낸다.

```text
LIVE STATE
- branch
- HEAD
- dirty
- changed/untracked/deleted
- large-file detection/upload/STAGED 상태
```

대용량 binary는 commit 전에 NAS로 미리 올릴 수 있다. 그러나 이 시점에는 `STAGED`다.

확정 버전은 다음이 연결될 때 생성한다.

```text
Git commit SHA
+
LargeDataSet snapshot
+
NAS object hashes
=
CHECKPOINT
```

Agent는 자동 `git add`, `git commit`, `git push`, `git reset`, `git merge` 등을 수행하지 않는다.

---

## Git commit과 LargeDataSet 연결

예:

```text
HEAD AAA / dirty=true
8GB object -> STAGED

사용자가 commit
HEAD BBB / clean

Agent/Server
-> current large-file mapping snapshot
-> LargeDataSet 생성
-> BBB <-> LargeDataSet 연결
-> CHECKPOINTED
```

Supabase metadata 개념:

```text
large_objects
- id
- sha256 unique
- size_bytes
- storage_key
- created_at

project_large_files / large_file_states
- project_id
- workstation_id
- relative_path
- object_id
- state
- last_seen

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

실제 binary는 NAS에만 둔다.

---

## 최초 등록 시 기존 큰 파일 처리

프로젝트 등록 시 threshold 이상 파일을 스캔한다.

```text
A. Git untracked/ignored 큰 파일
   -> hash/upload -> STAGED 가능

B. 이미 Git tracked 큰 파일
   -> 자동 git rm 금지
   -> 자동 .gitignore 수정 금지
   -> MIGRATION_REQUIRED로 보고
```

Git index/source tree 변경은 별도 사용자 승인 없이는 하지 않는다.

---

## Gateway 성공을 기준으로 finalize

Agent가 "성공"이라고 보고했다는 이유만으로 NAS 저장 완료 처리하지 않는다.

최종 완료 기준은 NAS Gateway가 실제로 다음을 검증한 상태다.

```text
object 존재
size 일치
SHA-256 일치
finalize 성공
```

가능하면 ProjectHub.Server가 Gateway의 finalize/status 결과를 확인한 뒤 Supabase 상태를 `STAGED` 또는 `CHECKPOINTED`로 전환한다.

---

## 새 Large Data/NAS task를 가능한 한 통으로 진행

권장 세부 단계:

```text
A. NAS 계약 + Supabase metadata/schema + 최소 Agent 인증
B. 기존 NAS Gateway assertion 연동 및 실제 NAS1DUAL provision/read-write 확인
C. LargeDataScanner + Startup Reconciliation + hash/stability
D. chunk/resume/STAGED upload
E. Git commit -> LargeDataSet -> checkpoint 확정
F. 실제 NAS1DUAL E2E 검증
```

A~F는 같은 numbered task 안에서 연속 진행한다. 단, 각 단계에서 build/test와 실제 결과를 기록하고 실패 시 다음 단계로 억지로 넘어가지 않는다.

---

## 실제 E2E 완료 기준

최종적으로 실제 환경에서 최소 다음을 확인한다.

```text
DEV PC에 1GB 초과 테스트 파일 생성
-> Agent 탐지
-> 안정화/hash
-> ProjectHub.Server upload session/assertion 발급
-> Agent -> NAS Gateway 직접 chunk upload
-> NAS1DUAL 실제 object 생성
-> Gateway size/hash 검증
-> STAGED metadata 확인

사용자가 Git commit
-> Agent HEAD 변화 감지 또는 startup reconciliation
-> LargeDataSet 생성
-> Git commit SHA <-> LargeDataSet <-> NAS object 연결
-> CHECKPOINTED 확인

Agent 종료/재시작
-> startup reconciliation
-> 누락/미완료 상태 자동 복구 또는 재개
```

이 단계가 끝나면 ProjectHub는 최초로 Git과 실제 NAS 대용량 데이터를 하나의 복원 가능한 프로젝트 버전으로 연결하게 된다.

---

## 05-C 마무리

NAS 작업 시작 전 05-C 실제 E2E를 짧게 끝낸다.

```text
real repo 파일 수정
-> watcher/debounce
-> GitStateCollector
-> POST project state
-> ProjectHub.Server
-> Supabase project_states 자동 갱신
```

성공하면 05를 완료 처리한다.

---

## Codex 수행 지침

1. `AGENTS.md` → 구현 계획 → `CurrentWork.md` → 활성 task → 이 파일 순으로 읽는다.
2. 05-C 코드는 구현 완료 상태로 취급하고 실제 E2E만 짧게 검증한다.
3. 성공하면 05를 완료 처리한다.
4. 사용자 요구 변경을 반영해 NAS/Large Data 작업을 독립 numbered task로 공식 로드맵에 추가한다.
5. 기존 v0.1 `NAS Gateway 금지` 경계는 사용자 요구에 맞게 문서에서 정식 수정한다.
6. 대용량 payload를 ProjectHub.Server로 relay하지 않는다. Agent가 NAS Gateway로 직접 전송한다.
7. ProjectHub.Server는 control plane: authorization, signed assertion, upload session, metadata, checkpoint를 담당한다.
8. 기존 NAS1DUAL assertion/provision.php 구조를 우선 재사용하고 필요한 upload/resume/finalize 기능만 확장한다.
9. Startup Reconciliation을 필수로 구현해 Agent 실행 시점에 의존하지 않게 한다.
10. upload 완료와 version/checkpoint 확정을 분리한다. commit 전 object는 STAGED다.
11. Agent가 Git mutation을 수행하지 않는다.
12. NAS credential/signing secret/service token을 저장소나 로그에 넣지 않는다.
13. 가능한 한 A~F를 하나의 큰 NAS task에서 연속 구현/검증한다.
14. 실제 NAS1DUAL E2E까지 완료한 뒤 다음 Project 상태 API/MCP 방향으로 진행한다.
15. 사용자 승인 없이 외부 서비스 설정, 배포, commit/push 등의 별도 외부 변경은 수행하지 않는다.
