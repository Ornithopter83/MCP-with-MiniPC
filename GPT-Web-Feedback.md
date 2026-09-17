# GPT Web Feedback

Updated: 2026-09-17

## 우선순위

1. `AGENTS.md`
2. `ProjectHub_IMPLEMENTATION_PLAN.md`
3. `CurrentWork.md`
4. 현재 활성 `tasks/*.md`
5. `GPT-Web-Feedback.md`

충돌 시 앞선 관리 문서와 활성 task를 우선한다. 아래 내용은 2026-09-17 기준 최신 Large Data/NAS 후속 개선사항이다.

---

# 1. 최신 저장소 상태

최신 확인 커밋:

```text
962dd07c706930c228853f2a5775cb8f9b6c3df9
Record final large-data verification status
```

06 Large Data/NAS는 기능 검증 완료로 본다.

현재 확인된 완료 범위:

```text
[x] 평상시 Agent는 heartbeat/Git 상태 관찰만 수행
[x] 명시적 ProjectHub_Sync.ps1 기반 Batch Sync
[x] 별도 ProjectHub_LargeData_Uploader.ps1
[x] resumable session 재사용
[x] chunk/status/resume/finalize
[x] SHA-256/size 검증
[x] STAGED / CHECKPOINTED
[x] finalize staging cleanup
[x] Staging GC dry-run/-Apply
[x] GC idempotency
[x] active UPLOADING session KEEP 보호
[x] 500MiB 실제 NAS upload E2E
[x] Server session COMPLETED
```

더 이상 보류 항목은 관리 대상에 두지 않는다. 06은 완료 상태로 정리하고 아래 항목은 후속 개선으로만 수행한다.

---

# 2. Assertion 발급 최적화

현재 `ProjectHub_LargeData_Uploader.ps1`은 다음 위치에서 assertion을 반복 발급한다.

```text
upload-start 전 1회
각 미완료 chunk 직전 1회
finalize 직전 1회
```

즉 32 chunk 파일이면 Server assertion endpoint 호출이 수십 번 발생한다.

이 호출은 chunk 진행률 보고가 아니다. 실제 chunk 완료 상태는 NAS `upload-status.php`/staging이 기준이며, Server는 upload session의 control-plane 상태를 관리한다.

## 변경 요구

같은 `project/workstation/object/session/operation` 범위에서는 assertion을 session 단위로 캐시해 재사용한다.

기본 흐름:

```text
1. upload session 시작 전 assertion 발급
2. upload-start에 사용
3. 같은 assertion을 여러 chunk에 재사용
4. 유효하면 finalize에도 재사용
5. 만료가 임박했거나 인증 실패가 발생한 경우에만 refresh
```

목표는 **per-chunk assertion issuance를 제거**하는 것이다.

---

# 3. Assertion 만료/재발급 처리

최초 assertion 하나를 무조건 전체 파일 업로드 동안 사용하는 방식도 피한다.

대용량 파일이나 느린 회선에서는 token이 업로드 중 만료될 수 있다.

다음 중 하나 또는 동등한 안전한 방식으로 구현한다.

```text
A. JWT exp 확인
- 만료 임박 시 assertion refresh

B. NAS 인증 실패 기반 refresh
- 401/token-expired 계열 응답 시 새 assertion 1회 발급
- 해당 요청만 1회 재시도
```

무한 retry는 금지한다.

refresh 후에도 실패하면 기존 uploader 오류 처리 경로로 종료/기록한다.

assertion scope 자체는 기존처럼 좁게 유지한다.

```text
project_id
workstation_id
object_hash
size_bytes
upload_session_id
operation
storage_scope
relative_path
```

---

# 4. Chunk progress와 Server 역할 경계 명확화

`[1/32] -> [2/32]` 같은 chunk 진행률은 Server DB에 매 chunk마다 저장하지 않는다.

역할은 다음과 같이 유지한다.

```text
NAS / Gateway
- completed_chunks
- bytes_received
- resume 위치
- 실제 staging/object 존재 여부

ProjectHub.Server
- upload session identity
- UPLOADING / COMPLETED
- STAGED
- CHECKPOINTED
- assertion 발급
```

Uploader 재시작 시 resume 판단은 NAS의 `upload-status.php`를 기준으로 한다.

Server는 대용량 binary를 relay하지 않고, chunk별 progress 저장소가 되지 않는다.

---

# 5. Object 보존 정책을 사용자 관점에서 명확화

`staging` cleanup 완료 후에도 `objects/sha256/<hash>`의 canonical object는 정상적으로 남는다.

이를 GC 실패처럼 보이지 않도록 출력/문서에서 구분한다.

권장 표현:

```text
STAGING_CLEANED
OBJECT_RETAINED
STAGED
CHECKPOINTED
```

또는 동등한 사용자 친화적 메시지.

중요:

```text
Staging GC != Object deletion
```

현재 Staging GC가 `objects/` 또는 `files/`를 삭제하도록 확장하지 않는다.

Object 삭제가 필요하면 별도 명시적 purge/Object GC 계약으로 분리한다.

---

# 6. Uploader 파일별 실패 격리

현재 uploader는 전체 `foreach ($item in $m.Items)`를 하나의 큰 outer `try/catch`가 감싸고 있어, 한 파일 실패가 이후 파일 처리까지 중단시킬 수 있다.

여러 대용량 파일 Batch에서 다음 동작으로 개선한다.

```text
file A success -> STAGED
file B failure -> FAILED 기록
file C continue
file D continue
```

파일별 try/catch를 사용하고 실패 파일은 별도 결과로 남긴다.

단, checkpoint 정책은 보수적으로 유지한다.

```text
manifest 항목이 모두 정상 STAGED
-> CHECKPOINTED

하나라도 FAILED
또는 CHANGED_DURING_UPLOAD
-> 전체 manifest checkpoint 생략
```

Git 자동 변경은 계속 금지한다.

---

# 7. 문서 상태 정리

더 이상 보류 항목을 관리하지 않는다.

관리 문서의 06 상태는 다음처럼 단순화한다.

```text
06 Large Data/NAS 완료
```

후속 개선은 완료 여부와 별개로 별도 항목으로 관리한다.

```text
- session assertion cache
- assertion refresh
- uploader file-level failure isolation
- object-retained / staging-cleaned UX 정리
```

과거 장애/검증 이력은 필요하면 기존 기록으로 남겨도 되지만, 현재 상태 요약에서 반복적으로 노출하지 않는다.

---

# 8. Codex 실행 순서

한 번에 하나의 세부 작업 원칙을 유지한다.

권장 순서:

```text
1. 현재 uploader assertion 호출 위치 확인
2. session-scoped cached assertion 구현
3. exp 또는 401 기반 refresh + 1회 retry 구현
4. per-chunk Server assertion 호출 제거 확인
5. 500MiB/32 chunk upload에서 assertion 호출 횟수 측정
6. resume upload에서도 cached assertion 동작 확인
7. finalize에서도 유효 token 재사용 확인
8. 파일별 try/catch로 실패 격리
9. 실패 1건 + 성공 1건 이상 mixed batch 테스트
10. 실패가 있으면 checkpoint 생략 확인
11. staging cleanup / object retained 사용자 메시지 정리
12. CurrentWork/task 문서를 06 완료 + 후속 개선 상태로 정리
13. dotnet build/test 및 PowerShell parser 검증
```

---

# 9. 다음 보고에 포함할 것

Codex는 다음 작업 완료 후 아래를 보고한다.

```text
- 구현 커밋 SHA
- 변경 파일 목록
- assertion 발급 횟수 before/after
- 32 chunk 기준 Server assertion 호출 수
- token refresh 조건
- 401/expired 1회 retry 처리
- resume 동작 결과
- finalize token 재사용 결과
- mixed batch 파일별 성공/실패 결과
- checkpoint 생략 조건 확인
- staging cleanup / object retained 출력 예시
- build/test/parser 결과
```

핵심 목표는 다음 두 가지다.

> **chunk마다 Server assertion을 새로 발급하지 않고, 같은 upload session에서는 assertion을 재사용하며 필요할 때만 갱신한다.**

> **한 파일 실패가 Batch 전체 전송을 즉시 중단시키지 않되, 전체 checkpoint는 모든 manifest 항목이 정상 완료됐을 때만 생성한다.**
