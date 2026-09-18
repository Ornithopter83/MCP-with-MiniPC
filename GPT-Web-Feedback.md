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

# 1. 최신 상태

최신 확인 커밋:

```text
1475e18dafec300702906b9076d9d26d9b856e5c
Improve PowerShell confirmations and uploader exit handling
```

06 Large Data/NAS는 **검증 완료 상태를 유지**한다.

현재 활성 작업은 07 프로젝트 배포 패키지이며, Setup/Sync/Restore와 삭제/tombstone 흐름을 실제 사용 가능한 수준으로 마무리하는 단계다.

이번 피드백의 최우선 목적은 `hw` 테스트 프로젝트에서 발생한 **대용량 chunk upload 실패 원인 확정 및 최소 수정**이다.

---

# 2. 현재 장애 증상

현재 확인된 흐름:

```text
hw
→ ProjectHub.Server
→ assertion 발급 성공
→ Supabase upload session 생성 성공
→ NAS upload-start.php 성공
→ NAS upload-chunk.php 전송 중 연결 종료
```

실패 session은 Supabase에 `UPLOADING` 상태로 남고 `project_large_files`까지는 생성되지 않는다.

따라서 우선 조사 범위는 Server/Supabase가 아니라:

```text
DEV uploader → NAS Gateway upload-chunk.php
```

의 실제 chunk body 전송 구간이다.

---

# 3. 최우선 회귀 가능성

500MiB 실제 업로드가 성공했던 과거 구현에서는 chunk PUT에 다음 방식을 사용했다.

```powershell
curl.exe -sS --fail --max-time 300 `
  -X PUT `
  -H "Authorization: Bearer $token" `
  -H "Content-Type: application/octet-stream" `
  --data-binary "@$chunkPath" `
  $uploadChunkUrl
```

이후 assertion cache/refresh 개선 과정에서 chunk 전송이 다음 방식으로 변경됐다.

```powershell
Invoke-WebRequest `
  $uri `
  -Method Put `
  -InFile $chunkPath `
  -ContentType 'application/octet-stream' `
  -Headers @{ Authorization="Bearer $token" } `
  -TimeoutSec 600 `
  -UseBasicParsing
```

현재 장애가 이 변경 이후 발생했으므로 **`curl.exe --data-binary` → `Invoke-WebRequest -InFile` 변경에 의한 transport 회귀를 최우선으로 검증한다.**

원인으로 미리 확정하지 말고 A/B 테스트로 확인한다.

---

# 4. 최소 A/B 테스트

다른 로직은 바꾸지 않는다.

동일 조건에서 아래 두 방식만 비교한다.

```text
A. 현재 Invoke-WebRequest -InFile
B. 기존 curl.exe --data-binary
```

동일 조건:

```text
- 같은 ProjectHub.Server
- 같은 NAS Gateway
- 같은 파일
- 같은 chunk size
- 같은 assertion/session 계약
```

반드시 확인할 항목:

```text
1. upload-start 성공 여부
2. 첫 chunk PUT 성공 여부
3. 실패 시 HTTP status
4. TCP connection reset/closed 여부
5. NAS staging의 .tmp/.part 생성 여부
6. 생성된 파일의 실제 byte size
7. upload-status.php의 completed_chunks / bytes_received
```

판정:

```text
Invoke-WebRequest → connection closed/reset
curl.exe          → 정상
```

이면 chunk transport 회귀로 확정한다.

---

# 5. 최종 transport 방향

A/B 테스트에서 curl이 정상이라면 전체 HTTP 스택을 되돌리지 않는다.

권장 경계:

```text
upload-start
upload-status
upload-finalize
ProjectHub.Server API
→ 기존 Invoke-RestMethod / Invoke-WebRequest 유지

대용량 chunk PUT
→ curl.exe --data-binary 사용
```

즉 **binary data plane만 검증된 curl 경로로 복원**한다.

현재 구현된 assertion cache/refresh는 그대로 유지한다.

---

# 6. curl 사용 시 assertion refresh 유지

과거 curl 구현으로 단순 회귀하면서 현재의 401 refresh 기능을 잃으면 안 된다.

chunk PUT 동작:

```text
1. cached assertion 사용
2. curl PUT 실행
3. HTTP status 확인
4. 401이면 assertion 새로 발급
5. 같은 chunk 1회만 재시도
6. 재실패하면 FAILED
```

무한 retry 금지.

`$LASTEXITCODE`만 확인하지 말고 HTTP status를 별도로 확보한다.

예:

```text
2xx → 성공
401 → assertion refresh 후 1회 retry
기타 → 오류 기록 후 실패
```

assertion/token 원문은 로그에 출력하지 않는다.

---

# 7. chunk 실패 진단 로그 보강

현재 오류는 네트워크 예외 메시지만 남아 원인 추적이 어렵다.

실패 시 최소 다음을 기록한다.

```text
relative_path
upload_session_id
chunk_index
chunk_size
request_uri
HTTP status
exception type
inner exception
assertion refresh 여부
```

예:

```text
CHUNK_UPLOAD_FAILED
file=data\forUpload.z01
session=...
chunk=0
size=16777216
http_status=0
exception=WebException
inner=The underlying connection was closed...
refreshed=false
```

비밀값과 assertion token 자체는 기록하지 않는다.

---

# 8. NAS Gateway 측 확인

현재 `upload-chunk.php`는 `php://input`을 읽어 `.tmp`에 기록한 뒤 `.part`로 rename한다.

실패 직후 NAS에서 다음을 확인한다.

```text
staging/<session>/
session.json
00000000.part
00000000.part.tmp
```

판단 기준:

```text
아무 파일도 없음
→ 요청 body가 PHP까지 정상 전달되지 않았을 가능성

.tmp 일부 존재
→ body 전송 중 연결 종료 가능성

.part 정상 크기 존재
→ 서버는 수신했지만 client가 응답을 받는 과정에서 연결 종료 가능성
```

가능하면 동일 시각 Apache/PHP access/error log도 같이 비교한다.

---

# 9. Chunk size source of truth 정리

현재 uploader 코드:

```powershell
$chunkSize = 16MB
```

최근 Server 설정 예:

```json
"ChunkSizeBytes": "33554432"
```

즉 32MiB 값과 실제 uploader 16MiB가 불일치한다.

현재 TCP reset의 직접 원인으로 단정하지는 않는다.

다만 종료 전에 chunk size의 source of truth는 반드시 하나로 통일한다.

권장:

```text
- manifest/config에서 uploader가 chunk size를 받거나
- ProjectHub 공통 기본값 하나로 고정
```

Server 설정과 실제 uploader 값이 서로 다른 상태를 남기지 않는다.

---

# 10. 이번 장애에서 하지 않을 것

원인 확인 전에 아래와 같은 광범위 변경은 하지 않는다.

```text
- NAS Gateway 전체 재작성
- PHP upload 로직 대규모 변경
- assertion 계약 변경
- Supabase schema 변경
- chunk size 임의 축소로 문제 숨기기
- TLS 검증 우회
```

먼저 transport A/B 테스트로 범위를 좁힌다.

---

# 11. 장애 수정 완료 기준

다음이 확인되면 이번 업로드 장애를 해결한 것으로 본다.

```text
[ ] Invoke-WebRequest / curl A-B 결과 확보
[ ] 실제 실패 지점 확정
[ ] chunk 전송 안정 경로 결정
[ ] 401 assertion refresh 1회 retry 유지
[ ] chunk/session/http 진단 로그 추가
[ ] chunk size 설정 통일
[ ] hw 실제 대용량 파일 upload 성공
[ ] upload-status chunk 진행 확인
[ ] finalize 성공
[ ] STAGED 생성
[ ] CHECKPOINTED 생성
[ ] Supabase session COMPLETED 확인
```

핵심:

> 기존에 실제 500MiB 업로드가 성공했던 `curl.exe --data-binary`와 현재 `Invoke-WebRequest -InFile`을 먼저 A/B 테스트한다. 원인 확인 전에 NAS/PHP/DB 구조를 넓게 수정하지 않는다.

> curl 방식이 정상이라면 assertion cache/refresh는 유지하고, 대용량 chunk PUT만 검증된 curl transport로 복원한다.

---

# 12. 07의 기존 마무리 방향 유지

업로드 장애 해결 후 기존 07 마무리 작업으로 돌아간다.

유지할 방향:

```text
- Setup 실제 프로젝트 E2E
- Sync 실제 프로젝트 E2E
- Restore 실제 프로젝트 E2E
- 이전/current manifest diff
- ADDED / CHANGED / REMOVED
- Sync 삭제 확정 GUI
- Restore 삭제 GUI
- 모두(A) / 예(Y) / 아니오(N) / 취소(C)
- LOCAL_ONLY 보호
- 최신 HEAD/checkpoint에서만 삭제 확정
- 뒤처진 workstation의 tombstone 재등록 방지
- REMOVED와 object purge 분리
- Sync/Restore 결과 요약
```

삭제 확정은 NAS canonical object 즉시 삭제가 아니다.

```text
REMOVED != immediate object deletion
Staging GC != Object purge
```

06은 다시 열지 않는다.

---

# 13. 이후 종료 순서

```text
06 Large Data/NAS            완료
07 프로젝트 배포 패키지      진행
08 Server 설치/이전 가이드   대기

08 완료 후 ProjectHub v0.1 종료
```

실시간 관리, lease, dashboard, 자동 Git 변경, background upload/reconcile 확대는 종료 조건에 포함하지 않는다.
