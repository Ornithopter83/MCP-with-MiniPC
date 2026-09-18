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
99a51d255ee3b62d56c9d3f206007ba5360c328c
Fix large chunk uploads with curl transport and diagnostics
```

06 Large Data/NAS는 **검증 완료 상태를 유지**한다.

현재 활성 작업은 07 프로젝트 배포 패키지다.

2026-09-18 기준 대용량 chunk transport 문제는 해결된 것으로 본다.

확인된 최신 결과:

```text
- binary chunk PUT을 curl.exe --data-binary 경로로 복원
- assertion cache 유지
- 401에서 assertion 1회 refresh 유지
- chunk 실패 진단 로그 보강
- hw Explorer Sync 경로에서 500MiB 실제 업로드 성공
- 실패 0
- STAGED 확인
- CHECKPOINTED 확인
- Server upload session COMPLETED 확인
```

따라서 이전 피드백의 transport A/B 진단은 **완료된 문제**로 정리하고 추가 확장하지 않는다.

---

# 2. 07 마무리 방향

07에서 남은 핵심은 실제 사용자 관점의 Setup / Sync / Restore 완성이다.

유지할 방향:

```text
Setup을 쉽게
Sync 결과를 명확하게
삭제를 안전하게
Restore를 탐색기 기준으로 완결
```

실시간 관리, lease, dashboard, background reconcile 확대는 추가하지 않는다.

---

# 3. Sync / Restore 삭제 정책 유지

기존에 구현한 manifest diff / tombstone / 승인 GUI 방향을 유지한다.

```text
ADDED
CHANGED
UNCHANGED
REMOVED
FAILED
```

삭제 확정 시 실제 변경 전에 GUI 확인:

```text
[모두(A)] [예(Y)] [아니오(N)] [취소(C)]
```

Restore 중 로컬 관리 파일 삭제 시에도 같은 승인 흐름을 사용한다.

LOCAL_ONLY 파일은 삭제 대상에 넣지 않는다.

```text
REMOVED != immediate object deletion
Staging GC != Object purge
```

NAS canonical object 삭제와 project path 삭제는 계속 분리한다.

---

# 4. Server 로그 정책 수정

최근 Server 로그를 줄이기 위해 `Microsoft.AspNetCore` 계열을 `Warning`으로 제한하면서,
콘솔이 지나치게 조용해져 **서버가 현재 무슨 작업을 처리 중인지 보이지 않는 문제**가 생겼다.

현재 목표는 다음 두 가지를 동시에 만족하는 것이다.

```text
1. Supabase HttpClient / ASP.NET Core framework 잡음은 줄인다.
2. ProjectHub의 실제 주요 작업은 Information 수준으로 짧게 보인다.
```

framework 전체 Information 로그를 다시 켜서 예전처럼 콘솔을 도배하지 않는다.

대신 ProjectHub 자체의 **의미 있는 operation log**를 명시적으로 추가한다.

---

# 5. 권장 appsettings.json 로그 수준

권장 기본값:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.AspNetCore": "Warning",
      "System.Net.Http.HttpClient": "Warning",
      "ProjectHub": "Information"
    },
    "Console": {
      "FormatterName": "simple",
      "FormatterOptions": {
        "SingleLine": true,
        "TimestampFormat": "HH:mm:ss "
      }
    }
  }
}
```

목표:

```text
- System.Net.Http.HttpClient.Supabase.LogicalHandler 반복 로그 숨김
- Microsoft.AspNetCore 일반 request noise 숨김
- ProjectHub 자체 Information 로그는 표시
- 한 로그를 한 줄로 표시
- 시간 표시
```

주의:

`ProjectHub` category Information 설정만 추가해도 실제 코드에서 해당 category로 로그를 남기지 않으면 아무것도 보이지 않는다.

따라서 아래 operation log 구현이 같이 필요하다.

---

# 6. ProjectHub 자체 operation log 추가

Server startup 시 명시적인 logger category를 하나 만든다.

권장 category:

```text
ProjectHub.Server
```

예시:

```csharp
var operationLogger = app.Services
    .GetRequiredService<ILoggerFactory>()
    .CreateLogger("ProjectHub.Server");
```

또는 endpoint에서 `ILogger<Program>` / 동등한 DI logger를 사용해도 된다.

중요한 것은 category가 ProjectHub namespace/category로 남아 appsettings의:

```json
"ProjectHub": "Information"
```

에 잡히도록 하는 것이다.

---

# 7. Information으로 남길 최소 operation

Server가 실제로 무엇을 하고 있는지 알 수 있도록 아래 수준만 Information으로 남긴다.

```text
SERVER_STARTED
PROJECT_REGISTERED
PROJECT_STATE_UPDATED

ASSERTION_ISSUED
UPLOAD_SESSION_CREATED
UPLOAD_SESSION_REUSED
UPLOAD_SESSION_COMPLETED

FILE_STAGED
CHECKPOINT_CREATED

REMOVAL_CONFIRMED
TOMBSTONE_CREATED

RESTORE_CHECKPOINT_READ

ERROR
```

모든 API 호출을 무조건 기록하지 않는다.

특히 heartbeat처럼 자주 발생하는 동작은 기본 Information에서 반복 출력하지 않는다.

필요하면 Debug로 둔다.

---

# 8. 권장 로그 형식

한 줄에서 핵심만 파악할 수 있게 한다.

예:

```text
10:14:03 info: ProjectHub.Server[0] ASSERTION_ISSUED project=hw session=681fe394...
10:14:04 info: ProjectHub.Server[0] UPLOAD_SESSION_REUSED project=hw session=681fe394...
10:15:22 info: ProjectHub.Server[0] UPLOAD_SESSION_COMPLETED project=hw session=681fe394...
10:15:23 info: ProjectHub.Server[0] FILE_STAGED project=hw path=data/forUpload.z01
10:15:23 info: ProjectHub.Server[0] CHECKPOINT_CREATED project=hw commit=be3cff2...
```

너무 긴 전체 SHA/session ID를 매번 출력할 필요는 없다.
사람이 식별 가능한 앞부분만 출력해도 된다.

단 DB 조회나 장애 추적에 전체 ID가 필요한 경우 Structured Logging property에는 전체 값을 넣고,
화면 표시 문자열만 짧게 만드는 방식도 가능하다.

---

# 9. Error / Warning 기준

Warning:

```text
- stale checkpoint
- retry 발생
- assertion refresh 발생
- recoverable Gateway 문제
- skipped checkpoint
```

Error:

```text
- Supabase 저장 실패
- assertion 발급 실패
- upload session lifecycle 저장 실패
- checkpoint 생성 실패
- tombstone 저장 실패
```

예외 발생 시 최소:

```text
operation
project_id
workstation_id
session_id (있을 때)
relative_path (있을 때)
HTTP status (있을 때)
exception message
```

를 남긴다.

비밀값, Service Role Key, private key, assertion token 원문은 절대 출력하지 않는다.

---

# 10. 로그에서 제외할 것

Information 콘솔에서 다음은 반복 출력하지 않는다.

```text
- 모든 Supabase HTTP request start/end
- 모든 ASP.NET Core routing/endpoint 실행 로그
- heartbeat 성공 로그
- chunk 하나마다 Server 로그
- assertion token 내용
- request/response body 전체
```

특히 대용량 chunk 진행률은 기존 정책대로 NAS/uploader 영역이며,
Server console을 chunk progress DB/로그처럼 사용하지 않는다.

---

# 11. Server startup 로그

Server가 정상 시작했는지는 한 번 명확히 보이게 한다.

예:

```text
10:10:00 info: ProjectHub.Server[0] SERVER_STARTED url=http://127.0.0.1:5240
```

가능하면 다음 상태도 startup 시 한 번만 요약한다.

```text
Supabase configured = true
Assertion key configured = true
NAS Gateway = configured
```

단 secret 자체는 출력하지 않는다.

예:

```text
10:10:00 info: ProjectHub.Server[0] CONFIG supabase=OK assertion_key=OK gateway=OK
```

---

# 12. 로깅 완료 기준

```text
[ ] Console SingleLine + timestamp 적용
[ ] Supabase HttpClient Information 반복 로그 제거
[ ] ASP.NET Core framework noise 제거
[ ] SERVER_STARTED 표시
[ ] assertion 발급/세션 완료/STAGED/CHECKPOINT 주요 단계 표시
[ ] tombstone/removal 주요 단계 표시
[ ] heartbeat 성공 반복 출력 없음
[ ] chunk별 Server Information 출력 없음
[ ] Warning/Error에는 충분한 식별 정보 포함
[ ] secret/token 원문 로그 없음
[ ] 실제 hw Sync 1회 실행 시 콘솔만 보고 주요 처리 흐름 식별 가능
```

최종 목표:

> 콘솔이 조용하되 비어 있지는 않아야 한다. 사용자는 Server 창만 보고 ProjectHub가 현재 어떤 주요 작업을 처리했는지 알 수 있어야 한다.

---

# 13. 07 완료 기준

업로드 transport는 해결된 것으로 보고 다음 실제 E2E에 집중한다.

```text
[x] Setup 실제 프로젝트 E2E
[x] Sync 500MiB 실제 업로드 E2E
[ ] Restore 실제 프로젝트 E2E
[ ] Sync REMOVED 실제 E2E
[ ] Restore 삭제 승인 실제 E2E
[ ] LOCAL_ONLY 보호 실제 확인
[ ] 뒤처진 workstation tombstone 재등록 방지 확인
[ ] operation logging 적용/확인
[ ] build/test/PowerShell parser 검증
```

기존 구현을 다시 작성하지 말고 실제 동작 검증과 마무리만 한다.

---

# 14. 이후 종료 순서

```text
06 Large Data/NAS            완료
07 프로젝트 배포 패키지      진행
08 Server 설치/이전 가이드   대기

08 완료 후 ProjectHub v0.1 종료
```

추가 실시간 관리 기능, lease, dashboard, 자동 Git 변경, background upload/reconcile 확대는 종료 조건에 포함하지 않는다.
