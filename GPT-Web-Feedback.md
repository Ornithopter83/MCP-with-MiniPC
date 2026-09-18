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
36e3cddaa1b71c0c5ee57a9ab9ca9143895efe9b
Fix repeated deletion prompts in sync
```

현재 상태 요약:

```text
06 Large Data/NAS            완료 유지
07 프로젝트 배포 패키지      마무리 검증 중
08 Server 설치/이전 가이드   대기
```

최근 완료/확인:

```text
- 500MiB 실제 대용량 업로드 성공
- binary chunk PUT은 curl.exe --data-binary 사용
- assertion cache 유지
- 401 assertion 1회 refresh 유지
- STAGED / CHECKPOINTED / session COMPLETED 확인
- REMOVED lifecycle 숫자 표현 대응
- 삭제 tombstone 후 반복 삭제 확인창 문제 수정
- ProjectHub.Server operation logging 구현
```

대용량 upload transport 문제는 해결된 것으로 유지한다.
06을 다시 열어 구조를 재설계하지 않는다.

---

# 2. 07 마무리 우선순위

현재 07에서 남은 핵심은 실제 사용자 흐름 검증이다.

```text
1. Restore 실제 프로젝트 E2E
2. Sync REMOVED 실제 E2E 재확인
3. Restore 삭제 승인 E2E
4. LOCAL_ONLY 보호 확인
5. 뒤처진 workstation tombstone 재등록 방지 확인
6. Server operation log 최종 형식 정리
7. build/test/PowerShell parser 최종 확인
```

기존 구현을 다시 작성하지 말고 검증과 마무리에 집중한다.

---

# 3. Sync / Restore 삭제 정책 유지

현재 구현 방향을 유지한다.

```text
ADDED
CHANGED
UNCHANGED
REMOVED
FAILED
```

삭제 승인:

```text
[모두(A)] [예(Y)] [아니오(N)] [취소(C)]
```

정책:

```text
LOCAL_ONLY → 자동 삭제 금지
REMOVED != immediate object deletion
Staging GC != Object purge
```

project path 삭제/tombstone과 NAS canonical object purge는 계속 분리한다.

뒤처진 workstation의 일반 Sync가 tombstone 파일을 자동 재등록하지 못하게 하는 기존 정책을 유지한다.

---

# 4. Server logging 현재 상태

현재 구현은 방향이 맞다.

```text
ProjectHub.Server category → Information
Microsoft / ASP.NET Core → Warning
System.Net.Http.HttpClient → Warning
Console → SingleLine
```

주요 operation만 Information으로 남기는 현재 구조를 유지한다.

현재 사용 중인 대표 operation:

```text
SERVER_STARTED
PROJECT_STATE_UPDATED
ASSERTION_ISSUED
UPLOAD_SESSION_COMPLETED
FILE_STAGED
CHECKPOINT_CREATED
REMOVAL_CONFIRMED
TOMBSTONE_CREATED
```

heartbeat 성공, ASP.NET routing, Supabase HttpClient start/end, chunk별 Server 로그는 Information에서 계속 제외한다.

---

# 5. 최종 콘솔 로그 형식

사용자가 원하는 최종 표시 순서는 다음으로 고정한다.

```text
일자 시간 중요도 워크스테이션 프로젝트 내용 응답코드
```

표준 출력 형식:

```text
yyyy-MM-dd HH:mm:ss [LEVEL] [WORKSTATION] [PROJECT] MESSAGE [STATUS]
```

예:

```text
2026-09-18 10:25:11 [INFO ] [DEV-PC-01 ] [hw      ] FILE_STAGED path=forUpload.z01 size=500MiB [200]
2026-09-18 10:25:12 [INFO ] [DEV-PC-01 ] [hw      ] CHECKPOINT_CREATED commit=be3cff2 files=1 [200]
2026-09-18 10:31:04 [INFO ] [DEV-PC-01 ] [hw      ] REMOVAL_CONFIRMED path=forUpload.z01 [200]
2026-09-18 10:31:04 [INFO ] [DEV-PC-01 ] [hw      ] TOMBSTONE_CREATED path=forUpload.z01 [200]
2026-09-18 10:34:19 [WARN ] [DEV-PC-02 ] [hw      ] STALE_CHECKPOINT local=81bc712 latest=be3cff2 [409]
2026-09-18 10:36:07 [ERROR] [DEV-PC-01 ] [hw      ] CHECKPOINT_CREATE_FAILED Supabase request failed [502]
```

Server 자체 이벤트는:

```text
2026-09-18 10:20:59 [INFO ] [SERVER    ] [-       ] SERVER_STARTED url=http://127.0.0.1:5240 [OK]
2026-09-18 10:20:59 [INFO ] [SERVER    ] [-       ] CONFIG supabase=OK assertion_key=OK gateway=OK [OK]
```

---

# 6. 구현 방식

현재처럼 endpoint마다 문자열을 직접 제각각 작성하지 말고
**공통 operation log helper 또는 custom console formatter**로 최종 형식을 강제한다.

권장 입력 필드:

```text
Level
Workstation
Project
Operation
Detail
StatusCode
```

예시 개념:

```text
WriteOperationLog(
  level: Information,
  workstation: "DEV-PC-01",
  project: "hw",
  operation: "FILE_STAGED",
  detail: "path=forUpload.z01 size=500MiB",
  statusCode: 200)
```

출력:

```text
2026-09-18 10:25:11 [INFO ] [DEV-PC-01 ] [hw      ] FILE_STAGED path=forUpload.z01 size=500MiB [200]
```

목표는 endpoint마다 필드 순서나 표기가 달라지지 않게 하는 것이다.

---

# 7. 필드 규칙

## DateTime

```text
yyyy-MM-dd HH:mm:ss
```

로컬 Server 시간 기준으로 표시한다.

## Level

폭을 고정한다.

```text
[INFO ]
[WARN ]
[ERROR]
```

Debug는 기본 콘솔에 출력하지 않는다.

## Workstation

가능하면 실제 workstation ID/display name을 사용한다.

없으면:

```text
[SERVER]
[-]
```

중 하나를 문맥에 맞게 사용한다.

## Project

project가 없는 server-global event는:

```text
[-]
```

로 표시한다.

## Message

```text
OPERATION detail=value detail=value
```

형식으로 짧게 유지한다.

SHA/session ID는 화면에서는 앞 8자 정도만 표시 가능하다.
단 structured property에는 전체 값을 유지해도 된다.

## Status

HTTP endpoint 결과는:

```text
[200]
[400]
[401]
[409]
[500]
[502]
[503]
```

처럼 맨 끝에 둔다.

HTTP 응답 코드가 없는 내부 startup/config event는:

```text
[OK]
```

또는 동등한 고정 표현을 사용한다.

---

# 8. workstation 정보 보완

현재 일부 operation은 project만 있고 workstation 정보가 로그에 빠질 수 있다.

가능한 경우 다음에서 보완한다.

```text
- request.WorkstationId
- upload session metadata
- project/workstation state
```

예:

```text
REMOVAL_CONFIRMED
→ request.WorkstationId 사용

ASSERTION_ISSUED
→ request.WorkstationId 사용

UPLOAD_SESSION_COMPLETED
→ session metadata에서 workstation 확인
```

반대로 `FILE_STAGED`, `CHECKPOINT_CREATED`처럼 현재 endpoint payload만으로 workstation을 신뢰성 있게 알 수 없으면 억지로 추정하지 않는다.

그 경우:

```text
[-]
```

를 사용한다.

틀린 workstation을 찍는 것보다 비워 두는 것이 낫다.

---

# 9. Warning / Error 표준

Warning 예:

```text
STALE_CHECKPOINT
ASSERTION_REFRESH
RETRY
CHECKPOINT_SKIPPED
GATEWAY_RECOVERABLE_ERROR
```

Error 예:

```text
SUPABASE_WRITE_FAILED
ASSERTION_ISSUE_FAILED
UPLOAD_SESSION_UPDATE_FAILED
CHECKPOINT_CREATE_FAILED
TOMBSTONE_CREATE_FAILED
```

오류 로그에는 가능한 경우:

```text
workstation
project
session
relative_path
status
exception message
```

를 남긴다.

비밀값, Service Role Key, private key, assertion token 원문은 절대 출력하지 않는다.

---

# 10. Information에서 제외

계속 제외:

```text
- heartbeat 성공 반복
- 모든 ASP.NET routing/endpoint 시작/종료
- 모든 Supabase HTTP request start/end
- chunk 하나마다 Server Information
- request/response body 전체
- assertion/token 원문
```

대용량 전송 progress는 uploader가 담당한다.

Server log는 control-plane의 주요 상태 전환만 보여준다.

---

# 11. logging 완료 기준

```text
[ ] yyyy-MM-dd HH:mm:ss 표시
[ ] Level 폭 고정
[ ] Workstation 위치 고정
[ ] Project 위치 고정
[ ] Message/operation 위치 고정
[ ] Response/Status 맨 끝
[ ] ProjectHub 주요 operation만 Information
[ ] framework/HttpClient noise 없음
[ ] heartbeat 성공 반복 없음
[ ] chunk별 Server 로그 없음
[ ] secret/token 원문 없음
[ ] 실제 hw Sync/삭제/Restore 실행 시 콘솔만 보고 주요 흐름 파악 가능
```

최종 목표:

> 콘솔 한 줄만 봐도 언제, 어떤 중요도로, 어느 workstation이, 어느 project에서, 무슨 작업을 했고, 결과가 무엇이었는지 알 수 있어야 한다.

---

# 12. 07 완료 기준

```text
[x] Setup 실제 프로젝트 E2E
[x] Sync 500MiB 실제 업로드 E2E
[x] 대용량 transport 회귀 수정
[x] tombstone 후 반복 삭제 확인창 수정
[x] operation logging 1차 구현
[ ] Restore 실제 프로젝트 E2E
[ ] Restore 삭제 승인 E2E
[ ] LOCAL_ONLY 보호 실제 확인
[ ] 뒤처진 workstation tombstone 재등록 방지 확인
[ ] 최종 logging format 적용
[ ] build/test/PowerShell parser 최종 검증
```

---

# 13. 이후 종료 순서

```text
06 Large Data/NAS            완료
07 프로젝트 배포 패키지      진행
08 Server 설치/이전 가이드   대기

08 완료 후 ProjectHub v0.1 종료
```

실시간 관리, lease, dashboard, 자동 Git 변경, background upload/reconcile 확대는 종료 조건에 포함하지 않는다.
