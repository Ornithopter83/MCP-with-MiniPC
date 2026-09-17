# GPT Web Feedback

Updated: 2026-09-17

## 우선순위

1. `AGENTS.md`
2. `ProjectHub_IMPLEMENTATION_PLAN.md`
3. `CurrentWork.md`
4. 현재 활성 `tasks/*.md`
5. `GPT-Web-Feedback.md`

충돌 시 앞선 관리 문서와 활성 task를 우선한다. 아래 내용은 ProjectHub v0.1 종료를 위한 최신 정리 지시다.

---

# 1. 현재 상태 확정

최신 확인 커밋:

```text
14a7466cc56b34c4025ebfd3b09d29fe6d93476b
Cache upload assertions and isolate batch failures
```

06 Large Data/NAS는 **검증 완료**로 확정한다.

이미 완료된 것으로 본다.

```text
[x] 명시적 Batch Sync
[x] 별도 uploader
[x] resumable session
[x] chunk/status/resume/finalize
[x] SHA-256/size 검증
[x] STAGED / CHECKPOINTED
[x] staging cleanup
[x] GC dry-run/-Apply/idempotency
[x] active UPLOADING 보호
[x] 500MiB 실제 NAS E2E
[x] assertion session cache
[x] 만료 임박/401 기반 assertion refresh
[x] 파일별 uploader 실패 격리
[x] mixed batch 보수적 checkpoint 정책
[x] STAGING_CLEANED / OBJECT_RETAINED 구분
```

06 관련 추가 검증이나 보류 항목은 더 이상 현재 작업으로 관리하지 않는다.

---

# 2. 불필요한 기존 후속 작업 제거

ProjectHub의 목적은 실시간 프로젝트 관리가 아니다.

따라서 기존 로드맵의 다음 범위는 제거한다.

```text
- 실시간 상태관리 확장
- lease 기반 멀티 PC 동시작업 판정
- 실시간 충돌 감지
- 실시간 dashboard
- 지속적인 background large-data 관리
- 추가적인 06 운영 보류 검증
```

기존 07/08/09 번호와 목적은 아래 두 작업으로 재편한다.

```text
06 Large Data/NAS                 검증 완료
07 프로젝트 배포 패키지 및 가이드 대기
08 Server 설치/이전 가이드        대기

08 완료 후 ProjectHub v0.1 종료
```

---

# 3. 07 — 다른 프로젝트에 배포할 파일과 가이드

## 목적

독립 Git 저장소를 가진 다른 프로젝트를 ProjectHub 관리 대상 중 하나로 간단히 등록하고 사용할 수 있게 한다.

새 프로젝트는 자체 Git repository를 그대로 사용하며 ProjectHub 저장소로 합치지 않는다.

예:

```text
hw.git
  ↓ ProjectHub Setup
ProjectHub project_id 등록
  ↓
Git 상태 + Large Data checkpoint를 ProjectHub에서 관리
```

## 사용자 관점 목표 UX

사용자가 기억해야 할 동작은 세 개로 제한한다.

```text
Setup   = ProjectHub 관리 대상으로 등록
Sync    = 현재 Git SHA + Large Data manifest를 checkpoint로 저장
Restore = 선택한 checkpoint를 복원
```

## 배포 파일

최초 진입점은 가능하면 하나로 만든다.

```text
ProjectHub_Setup.cmd
```

이 파일을 신규 Git 프로젝트 루트에 복사해 실행하면 setup이 진행돼야 한다.

Setup 완료 후 필요한 파일은 자동 생성 또는 배치한다.

권장 결과:

```text
<project-root>/
├─ .projecthub/
│  └─ project.json
├─ ProjectHub_Sync.cmd
├─ ProjectHub_Restore.cmd
├─ AGENTS.md          # 없을 때만 생성
├─ CurrentWork.md     # 없을 때만 생성
└─ tasks/             # 없을 때만 생성
```

기존 `AGENTS.md`, `CurrentWork.md`, `tasks/` 및 사용자 파일은 절대 덮어쓰지 않는다.

## Setup이 자동으로 확인할 항목

가능한 항목은 사용자 입력 없이 Git에서 읽는다.

```text
- local project root
- git remote origin
- current branch
- current HEAD full SHA
- repository identity
- workstation identity
- ProjectHub.Server 연결 상태
```

Setup이 ProjectHub.Server에 새 project를 등록하고 반환된 `project_id`를 `.projecthub/project.json`에 저장한다.

## Large Data 등록

Git으로 관리하기 어려운 대용량 파일을 ProjectHub Large Data 대상으로 지정할 수 있어야 한다.

이미 Git tracked 상태인 파일을 자동으로 `git rm`, commit, push 하지 않는다.

필요한 Git 추적 해제나 `.gitignore` 변경은 명시적으로 안내하거나 사용자 승인 후에만 수행한다.

## 통합 버전 정의

ProjectHub의 통합 버전 정의를 다음으로 고정한다.

```text
Checkpoint = Git commit SHA + Large Data manifest
```

Large Data manifest에는 최소한 다음을 포함한다.

```text
relative path
sha256
size
```

동일 hash object는 NAS에서 재사용한다.

## Sync

`ProjectHub_Sync.cmd`는 사용자가 명시적으로 실행할 때만 동작한다.

최소 흐름:

```text
1. 현재 Git branch/HEAD/dirty 확인
2. Large Data manifest 고정
3. 변경된 Large Data만 NAS upload/resume/finalize
4. Git SHA + Large Data manifest checkpoint 생성
```

자동 `git add/commit/push/pull/reset/merge`는 하지 않는다.

## Restore

`ProjectHub_Restore.cmd`는 checkpoint를 선택해 다음을 복원한다.

```text
- checkpoint의 Git commit
- checkpoint의 Large Data manifest
```

초기 버전의 기본 복원 방식은 현재 작업 폴더를 덮어쓰지 않고 별도 restore directory에 만드는 방식을 우선한다.

## 07 완료 기준

```text
[ ] ProjectHub_Setup.cmd 하나로 신규 프로젝트 연결 시작
[ ] Git remote/branch/HEAD 자동 감지
[ ] ProjectHub project 등록
[ ] project_id 로컬 저장
[ ] 기존 관리 문서/사용자 파일 비파괴
[ ] ProjectHub_Sync.cmd 생성 및 실행 가능
[ ] ProjectHub_Restore.cmd 생성 및 실행 가능
[ ] Large Data 대상 지정 가능
[ ] Checkpoint = Git SHA + Large Data manifest 계약 문서화
[ ] 실제 작은 테스트 Git 프로젝트 + 500MiB 파일로 Setup → Sync → Restore E2E
[ ] 신규 사용자가 별도 내부 지식 없이 가이드만 보고 수행 가능
```

---

# 4. 08 — Server가 변경될 때의 Windows 11+ 세팅 방법 정의

## 목적

현재 Mini PC/Server PC가 고장나거나 교체되어도 Windows 11 이상 새 PC에서 ProjectHub.Server를 다시 구축하고 기존 Supabase/NAS 데이터에 연결할 수 있게 한다.

이 작업은 새로운 Server 기능 개발이 아니라 **재설치/이전 절차를 재현 가능하게 만드는 것**이 목적이다.

## 기준 환경

```text
OS: Windows 11 이상
Server: ASP.NET Core ProjectHub.Server
Metadata: Supabase
Large Data: NAS Gateway / NAS1DUAL
External HTTPS: 현재 사용 중인 방식 기준 문서화
```

## 신규 Server PC 준비 절차에 반드시 포함할 것

### A. 기본 도구 설치

```text
- Git
- 요구 .NET SDK/runtime
- PowerShell 실행 기준
```

설치 확인 명령까지 문서화한다.

### B. ProjectHub 저장소 배치

```text
1. ProjectHub repository clone
2. 지정된 Server 실행 경로 준비
3. dotnet build/test
```

특정 기존 PC 절대경로에 종속되지 않도록 한다.

### C. Server 비밀값/환경 변수

Server에 필요한 값 목록과 입력 위치를 정의한다.

예:

```text
- Supabase URL
- Supabase Service Role Key
- assertion private key
- 기타 Server-only secret
```

비밀값 자체는 문서/저장소에 기록하지 않는다.

새 PC에서 어디에 어떻게 설정하는지만 설명한다.

### D. assertion key 재구성

Server private key와 NAS Gateway public key의 관계를 문서화한다.

두 가지 상황을 구분한다.

```text
1. 기존 key pair를 안전하게 이전하는 경우
2. 새 key pair를 생성하고 NAS public key를 교체하는 경우
```

각 경우 필요한 Server/NAS 작업을 분리해 적는다.

### E. Supabase 재연결

기존 Supabase를 그대로 사용하는 경우 DB migration 없이 기존 프로젝트/project_id/checkpoint/session metadata를 계속 사용할 수 있어야 한다.

새 Server가 기존 Supabase 데이터에 연결되는 검증 절차를 정의한다.

### F. NAS Gateway 재연결

Server PC 교체 시 NAS의 canonical object를 다시 업로드하지 않는다.

새 Server는 기존 NAS Gateway에 연결하고 기존 metadata/object identity를 계속 사용한다.

필요한 endpoint/public-key/config 확인 절차를 문서화한다.

### G. 외부 HTTPS 주소

Server PC가 바뀌어도 가능하면 기존 public hostname을 유지하는 절차를 우선한다.

기존 hostname을 유지할 수 없는 경우에만 신규 주소로 변경하고, 등록 프로젝트의 ProjectHub Server endpoint를 갱신하는 절차를 정의한다.

### H. 자동 시작

Windows 11 이상에서 재부팅 후 ProjectHub.Server가 자동 실행되도록 한 가지 표준 방법을 선택해 문서화한다.

여러 방식을 나열하지 말고 실제 운영용 기본 방법 하나를 정한다.

### I. 검증 순서

신규 Server 세팅 완료 후 최소 다음을 확인한다.

```text
1. local /api/status = 200
2. public HTTPS /api/status = 200
3. Supabase 기존 project 조회 가능
4. assertion 발급 가능
5. NAS Gateway 인증 가능
6. 기존 Large Data object 재사용 가능
7. 등록 프로젝트의 Sync 가능
```

## Server 주소 변경 대응

프로젝트별 `.projecthub/project.json` 또는 동등 설정에서 Server endpoint를 변경할 수 있어야 한다.

여러 프로젝트가 등록돼 있을 경우 한 프로젝트씩 수동 편집하지 않도록 가능한 범위에서 일괄 갱신 방법을 제공한다.

단 자동 원격 변경은 하지 않는다.

## 08 완료 기준

```text
[ ] Windows 11 이상 신규 PC 기준 설치 가이드 작성
[ ] Git/.NET/PowerShell 요구사항 명시
[ ] ProjectHub clone/build/run 절차 명시
[ ] 모든 Server 환경 변수 목록과 설정 위치 정의
[ ] private/public assertion key 이전 또는 재발급 절차 정의
[ ] 기존 Supabase 재연결 절차 정의
[ ] 기존 NAS Gateway 재연결 절차 정의
[ ] 기존 public hostname 유지/변경 절차 정의
[ ] Windows 자동 시작 방식 하나 확정
[ ] Server endpoint 변경 시 project 설정 갱신 방법 정의
[ ] 신규 PC에서 /api/status → assertion → NAS → Sync 순서 검증
[ ] 비밀값이 저장소/가이드 예시에 노출되지 않음
```

---

# 5. 종료 기준

07과 08이 완료되면 ProjectHub v0.1은 종료한다.

추가 실시간 관리 기능, lease, dashboard, 자동 Git 변경, MCP 자체 구현을 본 프로젝트의 종료 조건에 포함하지 않는다.

최종 상태는 다음이면 충분하다.

```text
ProjectHub v0.1

- 다른 Git 프로젝트에 Setup 가능
- 필요할 때 Sync 가능
- Git SHA + Large Data checkpoint 생성 가능
- checkpoint Restore 가능
- ProjectHub를 새 Windows 11+ Server PC에 재설치/이전 가능
- 기존 Supabase/NAS 자산을 계속 사용 가능
```

Codex는 다음 작업에서 먼저 관리 문서와 task 구조를 위 두 작업 중심으로 단순화한 뒤, **07 프로젝트 배포 패키지 및 가이드**부터 한 세부 작업씩 진행한다.
