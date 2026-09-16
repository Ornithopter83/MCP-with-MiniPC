# GPT Web Feedback

Updated: 2026-09-16

## 목적

이 파일은 ChatGPT Web이 GitHub 저장소를 검토한 뒤 Codex에게 전달하는 전용 피드백/작업 제안 채널이다.

우선순위는 항상 다음과 같다.

1. `AGENTS.md`
2. `ProjectHub_IMPLEMENTATION_PLAN.md`
3. `CurrentWork.md`
4. 현재 활성 `tasks/*.md`
5. 이 `GPT-Web-Feedback.md`

이 파일은 기존 관리 파일을 대체하지 않는다. 충돌 시 기존 정책과 활성 task를 우선한다.

---

## 최종 목적 — 반드시 유지

ProjectHub의 최종 목표는 다음 두 가지다.

1. 어떤 프로젝트든 연결할 수 있고, Git에 적합하지 않은 대용량 데이터도 NAS/별도 저장소와 연계하여 자동 버전관리할 수 있어야 한다.
2. 서버 PC에 연결된 모든 프로젝트 상태를 Web ChatGPT가 조회·분석하고 다음 작업·위험·충돌·재개 지점을 피드백할 수 있어야 한다.

MCP/Connector는 연결 수단이지 ProjectHub 자체의 목적은 아니다.

---

## 최신 확인 상태

최신 구현 커밋:

```text
0c7b6e03d2394195767caeb4f4d53791710ffeb4
05-C 완료
```

검토 결과:

```text
[x] 05-A Agent heartbeat 구현 + 원격 E2E 완료
[x] 05-B GitStateCollector 구현/테스트 완료
[x] 05-C RegisteredProjects 설정 추가
[x] 05-C FileSystemWatcher 기반 ProjectActivityMonitor 구현
[x] 1초 debounce 구현
[x] .git/bin/obj/Library/Temp/Logs/node_modules 노이즈 제외
[x] GitStateCollector 재사용
[x] POST /api/projects/{projectId}/state 자동 전송
[x] heartbeat와 project-state loop 분리
[x] HTTP/Git 오류가 Agent 전체를 종료하지 않도록 처리
[x] 전체 build/test 통과 기록
[ ] 05-C 실제 DEV PC 파일 변경 -> Server -> Supabase 자동 갱신 E2E
```

따라서 **05-C 코드는 구현 완료**로 본다. 다만 `tasks/05-agent-state.md`와 구현 계획에도 기록된 것처럼 C-E2E는 아직 실제 사용자 환경 확인이 남아 있다.

---

## 다음 순서 변경 요청

사용자는 다음 단계에서 **실제 NAS까지 연결하여 대용량 파일의 탐지·전송·저장·버전 연결 흐름을 가능한 한 한 작업 묶음으로 검증**하고 싶어 한다.

현재 구현 계획에는 v0.1 변경 금지 경계에 `NAS Gateway`가 들어 있고 기존 06은 `Project 상태 API`다. 이제 사용자 요구가 명시적으로 변경됐으므로, Codex는 임의로 기존 계획을 무시하지 말고 먼저 관리 문서를 갱신하여 **NAS/대용량 데이터 작업을 새로운 공식 번호 작업으로 승격**한다.

권장 순서:

```text
05-C-E2E 짧게 완료
  -> 새 06 Large Data / NAS E2E
  -> 기존 06 Project 상태 API는 07 이후로 순번 이동
  -> 뒤 작업도 필요 시 순차 재번호
```

중요: NAS 작업을 기존 05-C에 섞지 않는다. 05는 Git/활동 상태 수집으로 닫고, NAS는 독립 numbered task로 관리한다.

---

## 05-C-E2E는 NAS 작업 전에 짧게 닫을 것

새 기능 구현 전에 실제 DEV PC에서 아래 한 번만 확인하면 된다.

```text
등록된 real Git repo 파일 1개 수정
  -> Agent watcher 감지
  -> debounce
  -> GitStateCollector
  -> HTTPS projecthub.ornithopter.bid
  -> ProjectHub.Server
  -> Supabase project_states 자동 갱신
```

확인 항목:

```text
- branch/HEAD가 실제 repo와 일치
- dirty=true 및 변경 수 반영
- heartbeat는 동시에 계속 정상
- Agent 재시작 없이 자동 반영
```

성공하면 05 전체를 완료 처리하고 바로 NAS 작업으로 넘어간다.

---

# 새 06 제안: Large Data / NAS End-to-End

## 목표

프로젝트를 처음 등록했을 때와 작업 중 새 대용량 파일이 생겼을 때를 모두 처리하여 다음 실제 흐름을 한 numbered task 안에서 끝까지 만든다.

```text
DEV PC project
  -> Agent large-file detection
  -> 안정화 확인
  -> SHA-256
  -> chunked/resumable upload
  -> ProjectHub.Server
  -> 실제 NAS 저장
  -> Supabase metadata
  -> Git HEAD/commit과 LargeData checkpoint 연결
```

사용자 관점에서는 가능한 한 다음처럼 보여야 한다.

```text
프로젝트 등록
  -> 큰 파일 자동 탐지
  -> NAS staging/upload
  -> 이후 Git commit 감지
  -> commit SHA와 대용량 데이터 세트 자동 연결
```

작업 중 새 큰 파일이 생겨도 같은 파이프라인을 재사용한다.

---

## 핵심 원칙: LIVE STATE와 확정 VERSION을 분리

Git commit 전까지 모든 전송을 막지 않는다.

```text
LIVE STATE
  - branch
  - HEAD
  - dirty
  - changed/untracked/deleted
  - large-file 발견/staging 상태
  -> 즉시 Server로 전송 가능

CONFIRMED VERSION / CHECKPOINT
  - Git commit SHA
  - LargeDataSet
  - file path -> content hash mapping
  -> commit 확인 후 확정
```

대용량 파일 자체는 commit 전에 NAS로 미리 올릴 수 있지만 그 상태는 `STAGED/UNCOMMITTED`로 둔다. Git HEAD가 새 commit으로 바뀌고 필요한 조건을 만족할 때 checkpoint로 확정한다.

Agent가 자동 `git add/commit/push`를 수행해서는 안 된다.

---

## NAS 저장 방식

실제 payload는 Git/Supabase에 넣지 않는다.

권장 저장 구조는 content-addressed 방식이다.

```text
<NasRoot>/
  objects/
    sha256/
      ab/
        <full-hash>
  staging/
    <upload-session-id>/
```

동일 hash 객체가 이미 있으면 재업로드/중복 저장을 피한다.

최종 object는 원래 파일명보다 **SHA-256을 identity**로 사용한다. 원래 상대 경로/파일명은 metadata에서 관리한다.

절대 NAS 경로를 프로젝트 manifest나 Git 문서에 박지 않는다. 외부에 노출되는 값은 `storageObjectId/hash/relative key` 중심으로 둔다.

---

## Mini PC가 NAS Gateway 역할

DEV PC가 NAS에 직접 접근하도록 하지 않는다.

기본 경로:

```text
DEV PC Agent
  -> HTTPS/chunk API
  -> ProjectHub.Server (Mini PC)
  -> NAS
```

이유:

```text
- DEV PC들은 다른 네트워크에 있을 수 있음
- NAS 자격증명을 각 DEV PC에 배포하지 않아도 됨
- NAS 위치/프로토콜을 Agent에서 숨길 수 있음
- Server에서 hash 검증, staging, atomic finalize를 통제 가능
```

NAS root는 Server 설정/환경 변수로만 둔다.

예:

```text
PROJECTHUB_NAS_ROOT=<UNC 또는 local-mounted path>
```

실제 값은 사용자가 Mini PC에서 설정한다. 저장소에 NAS 계정/비밀번호를 기록하지 않는다.

Windows 서비스 운영을 고려하면 mapped drive 문자보다 UNC 또는 서비스가 확실히 접근 가능한 mount path를 우선한다.

---

## 1GB+ 전송은 단일 HTTP request 금지

Cloudflare/네트워크/프로세스 timeout 때문에 1GB 이상 파일을 한 요청에 올리지 않는다.

반드시 chunk/resumable 구조를 사용한다.

예시 API 경계:

```text
POST /api/large-data/uploads
  -> uploadSessionId, chunkSize

PUT /api/large-data/uploads/{id}/chunks/{index}
  -> 작은 고정 크기 chunk

POST /api/large-data/uploads/{id}/complete
  -> size/hash 검증
  -> NAS object finalize

GET /api/large-data/uploads/{id}
  -> 재개용 received chunks/status
```

초기 chunk size는 16~64MB 범위의 단순 고정값이면 충분하다. 정확한 값은 테스트로 정한다.

Agent/Server 모두 streaming을 사용하며 파일 전체를 RAM에 올리지 않는다.

업로드가 끊기면 이미 받은 chunk부터 재개할 수 있어야 한다.

---

## 큰 파일 탐지 정책

기본 threshold는 설정 가능하게 만들고 첫 기본값은 1GB로 둘 수 있다.

```text
LargeDataThresholdBytes = 1073741824
```

프로젝트별 확장 가능성은 유지한다.

```text
LargeDataPolicy
  threshold
  include patterns (future/optional)
  exclude patterns
```

초기 프로젝트 등록 시 전체 inventory scan을 한 번 수행한다.

이후에는 05-C watcher 이벤트를 trigger로 재사용하되, 대용량 파일 처리 로직을 Git collector 내부에 넣지 않는다.

```text
FileActivityMonitor
  -> GitStateCollector
  -> LargeDataDetector/Coordinator
```

---

## 파일 안정화 확인

큰 파일은 생성/변경 event 직후 hash/upload하지 않는다. 아직 쓰는 중일 수 있다.

최소 안정화 조건:

```text
- 일정 debounce/stability window 동안 size/mtime 변화 없음
- read open 가능
- 이후 streaming hash
```

수십 GB 파일의 hash 계산도 취소 가능해야 하며 UI/로그에서 상태를 볼 수 있어야 한다.

---

## 초기 등록 시 이미 존재하는 큰 파일

프로젝트 최초 등록 시 threshold 이상 파일을 스캔한다.

두 경우를 구분한다.

```text
A. Git이 추적하지 않는 큰 파일
   -> 자동 STAGED 후보
   -> hash/upload 가능

B. 이미 Git이 추적 중인 큰 파일
   -> 자동 git rm/.gitignore 변경 금지
   -> "migration required" 상태로 보고
   -> NAS 복사/hash는 가능하더라도 Git index 변경은 사용자 승인 작업으로 남김
```

ProjectHub가 사용자 소스 tree나 Git index를 몰래 수정해서는 안 된다.

---

## 작업 중 새 큰 파일이 생겼을 때

```text
FileSystemWatcher event
  -> 안정화 확인
  -> threshold/policy 판정
  -> SHA-256 streaming
  -> Server에 hash 존재 여부 질의
  -> 이미 존재하면 upload skip
  -> 없으면 chunked upload
  -> NAS object finalize
  -> STAGED metadata 저장
```

기존 큰 파일이 바뀌면 새 hash는 새 object다. 이전 object를 덮어쓰지 않는다.

---

## Commit과 LargeData version 연결

Agent는 Git HEAD 변화를 이미 수집할 수 있다.

권장 개념:

```text
WORKING      = current Git state dirty/working
STAGED       = NAS에는 존재하지만 commit과 아직 연결 안 됨
CHECKPOINTED = Git commit SHA + LargeDataSet snapshot 확정
ORPHANED     = 업로드됐지만 어떤 checkpoint에서도 참조되지 않음
```

새 commit이 감지되면 current large-file mapping snapshot을 만들어 다음 관계를 Supabase에 기록한다.

```text
project_id
commit_sha
large_data_set_id
created_at
```

그리고 set 내부에는:

```text
relative_path
sha256
size
storage_object_id
```

를 둔다.

이렇게 하면 향후:

```text
git checkout <old commit>
  -> 해당 commit의 LargeDataSet 조회
  -> 필요한 NAS objects를 로컬로 복원
```

할 수 있다.

이번 06에서 자동 restore까지 반드시 구현할 필요는 없지만, **복원이 가능한 metadata/checkpoint까지는 만들어야 버전관리라고 볼 수 있다.**

---

## Supabase 최소 metadata 제안

스키마 이름은 기존 naming convention에 맞게 Codex가 조정한다.

개념적으로 최소 다음이 필요하다.

```text
large_objects
  id
  sha256 unique
  size_bytes
  storage_key
  created_at

large_file_states / project_large_files
  project_id
  workstation_id
  relative_path
  object_id
  state (STAGED/CHECKPOINTED/...)
  last_seen

large_data_sets
  id
  project_id
  commit_sha
  created_at

large_data_set_items
  set_id
  relative_path
  object_id
```

Supabase에는 metadata만 넣고 실제 binary는 NAS에 둔다.

---

## 인증은 이번 NAS 작업의 최소 선행 조건에 포함

현재 public hostname의 write API를 장시간 무인증으로 유지한 채 대용량 upload API까지 추가하면 안 된다.

가급적 같은 06 안에서 최소 Agent write 인증을 붙인다.

단순한 첫 단계 예:

```text
PROJECTHUB_AGENT_API_KEY (Server env)
Agent 설정/환경 변수의 동일 secret
X-ProjectHub-Agent-Key 또는 Authorization 헤더
```

적용 대상 최소:

```text
heartbeat POST
project state POST
large-data upload/create/complete
```

`/api/status` 같은 read health endpoint는 별도 정책으로 둘 수 있다.

비밀값은 저장소/appsettings 샘플에 실제 값으로 남기지 않는다.

Cloudflare Access Service Token은 이후 외부 계층으로 추가 가능하지만 ProjectHub 자체 인증과 역할을 섞지 않는다.

---

## 가능한 한 "통으로" 진행하는 방식

저장소 규칙의 `한 numbered task / 한 subtask` 원칙은 유지하되, 사용자가 중간마다 직접 개입하지 않도록 **새 06 하나 안에서 A~F를 연속 수행**한다.

권장 세부 순서:

```text
06-A 설계/스키마 + 최소 Agent write 인증
06-B NAS storage adapter + health/status + 실제 NAS write/read/hash 검증
06-C large-file initial scan/stability/hash/dedupe
06-D chunked/resumable Agent -> Server -> NAS upload
06-E STAGED metadata + commit 감지 + LargeDataSet checkpoint
06-F 실제 NAS E2E
```

각 단계 build/test가 통과하면 Codex가 다음 subtask로 바로 진행해도 된다.

**중단해서 사용자 입력이 필요한 지점은 실제 NAS root/접근 권한 설정뿐**이어야 한다.

그때 사용자에게 필요한 수동 작업을 정확히 한 번에 정리한다.

예:

```text
Mini PC:
1. NAS 공유 폴더 생성/확인
2. Server 실행 계정에 읽기/쓰기 권한 부여
3. PROJECTHUB_NAS_ROOT 설정
4. 필요 시 PROJECTHUB_AGENT_API_KEY 설정

DEV PC:
5. Agent에 동일 API key 설정
```

NAS 계정/비밀번호 자체를 코드나 Git에 넣지 않는다.

---

## 06 실제 완료 기준

다음이 실제 NAS에서 확인돼야 완료다.

```text
[ ] 05-C real E2E 완료 및 05 종료
[ ] Mini PC에서 configured NAS root 접근 성공
[ ] Server가 NAS에 test object write/read/delete 또는 안전한 health 검증 성공
[ ] DEV PC 프로젝트 최초 scan에서 큰 파일 탐지
[ ] 파일 전체를 메모리에 올리지 않고 SHA-256 계산
[ ] 동일 hash object dedupe
[ ] chunked upload 중단 후 재개 테스트
[ ] Agent -> HTTPS -> Server -> 실제 NAS object 생성
[ ] NAS 최종 object hash/size가 원본과 동일
[ ] Supabase metadata 저장
[ ] commit 전 STAGED 상태 확인
[ ] 사용자 Git commit 후 새 HEAD 감지
[ ] commit SHA와 LargeDataSet checkpoint 연결
[ ] 기존 object는 overwrite하지 않음
[ ] Git mutation 없음
[ ] Server/Agent 장애가 있어도 기존 데이터 손상 없음
[ ] build/test 성공
```

테스트 파일은 실제 1GB 이상 파일 1개로 최종 확인하되, 개발 초기 단위/통합 테스트에서는 작은 threshold override로 작은 fixture를 사용할 수 있다.

---

## 이번 단계에서 하지 말 것

```text
- 자동 git add/commit/push/pull/reset/merge
- 기존 Git-tracked large file을 자동으로 index에서 제거
- NAS credential을 Agent에 배포
- NAS 절대경로를 manifest에 하드코딩
- binary를 Supabase에 저장
- 단일 1GB+ HTTP upload
- 전체 파일 RAM buffering
- 자동 restore/checkout까지 한꺼번에 확장
- 멀티 PC conflict final policy
- MCP endpoint
```

---

## Codex 수행 지침

1. `AGENTS.md` → `ProjectHub_IMPLEMENTATION_PLAN.md` → `CurrentWork.md` → `tasks/05-agent-state.md` → 이 파일 순으로 읽는다.
2. 최신 커밋 `0c7b6e03...`의 05-C 구현을 검토하고 코드 구현은 완료로 취급한다.
3. 먼저 05-C real E2E를 짧게 수행/사용자에게 검증 절차를 제공하고 성공 결과를 기록한다.
4. 사용자가 NAS 작업을 다음 우선순위로 명시했으므로 구현 계획의 기존 `NAS Gateway 금지` 경계를 그대로 둔 채 코드를 시작하지 않는다. 먼저 로드맵을 명시적으로 갱신한다.
5. 새 공식 numbered task로 `Large Data / NAS End-to-End`를 만든다. 기존 06 이후 번호는 문서 일관성을 깨지 않도록 정리한다.
6. 새 06 내부 A~F는 테스트 통과 시 가능한 한 연속 수행하여 사용자 개입 횟수를 줄인다.
7. Mini PC Server가 NAS gateway 역할을 하고 DEV PC Agent는 NAS credential/path를 알지 못하게 한다.
8. 대용량 전송은 chunked/resumable + streaming으로 구현하고 SHA-256 content identity를 사용한다.
9. live project state와 confirmed checkpoint를 분리한다. commit 전 object는 STAGED, commit 후 LargeDataSet checkpoint로 확정한다.
10. Git을 자동 수정하지 않는다.
11. public write API에 large upload를 추가하기 전에 최소 Agent API-key 인증을 같은 작업 범위에서 구현한다.
12. 실제 NAS root/권한 설정이 필요한 순간에만 사용자에게 수동 절차를 요청한다. 그 전까지 구현/테스트는 local temp storage adapter로 진행 가능하다.
13. 실제 NAS E2E가 끝나기 전에는 이 task를 완료 처리하지 않는다.
14. 모든 변경 후 build/test와 실제 검증 근거를 문서에 기록한다.
15. 사용자 승인 없이 외부 서비스 설정, 임의 배포, Git mutation, NAS 파일 삭제/정리 정책을 수행하지 않는다.
