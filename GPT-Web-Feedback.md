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

# 1. 최신 확인 상태

최신 확인 커밋:

```text
08389d0a55e8a495eb02264fe89a72b80404e6e8
Standardize Server operation logs with custom console formatting
```

현재 상태:

```text
06 Large Data/NAS            완료 유지
07 프로젝트 배포 패키지      마무리 검증 중
08 Server 설치/이전 가이드   대기
```

최근 완료:

```text
- 500MiB 실제 Sync 업로드 성공
- curl.exe --data-binary 기반 chunk PUT 안정화
- assertion cache / 401 1회 refresh 유지
- STAGED / CHECKPOINTED / session COMPLETED 확인
- REMOVED lifecycle 처리 및 반복 삭제 확인창 문제 수정
- ProjectHubConsoleFormatter 적용
- 공통 operation log 형식 적용
- build/test 통과
```

현재 콘솔 형식:

```text
yyyy-MM-dd HH:mm:ss [LEVEL] [WORKSTATION] [PROJECT] MESSAGE [STATUS]
```

이 형식은 유지한다.

---

# 2. 07 마무리 방향

현재 07은 큰 구조를 다시 만들 단계가 아니다.

남은 일은 실제 사용자 흐름을 단순하게 완결하는 것이다.

우선순위:

```text
1. Sync 승인 삭제가 NAS 실제 데이터 삭제까지 이어지게 한다.
2. Restore 실제 프로젝트 E2E
3. LOCAL_ONLY 보호 확인
4. 뒤처진 workstation의 tombstone 재등록 방지 확인
5. Server 일자별 Full-log 파일 기록
6. 최종 build/test/PowerShell parser 검증
```

실시간 관리, lease, dashboard, 별도 관리자 purge UI 같은 확장은 이번 단계에 추가하지 않는다.

---

# 3. 삭제 정책 변경: 별도 Purge를 만들지 않는다

이전 정책은:

```text
사용자 로컬 파일 삭제
→ Sync
→ 삭제 승인
→ DB tombstone / REMOVED
→ NAS canonical object 유지
```

였다.

현재 사용자 의도는 더 단순하다.

Sync에서 이미 Windows GUI로 삭제 여부를 명시적으로 묻고 있으므로,
**사용자가 삭제를 승인한 그 동작 자체를 NAS 실제 삭제 승인으로 간주한다.**

새 기본 흐름:

```text
로컬 대용량 파일 삭제
→ ProjectHub_Sync 실행
→ 삭제 확인 GUI
→ 사용자가 승인
→ project path를 REMOVED/tombstone 처리
→ NAS named alias 삭제
→ 다른 현재 활성 path가 같은 SHA-256 object를 참조하는지 확인
→ 활성 참조가 0이면 NAS canonical object 삭제
→ 결과 기록
```

별도 `ProjectHub_Purge.cmd` 또는 관리자 purge 승인 단계는 만들지 않는다.

현재 단계에서는 지나친 복구/보존 정책보다
**사용자가 승인한 삭제가 실제 저장공간 삭제까지 자연스럽게 이어지는 것**을 우선한다.

---

# 4. 같은 object를 다른 현재 파일이 사용하는 경우

content-addressed object이므로 같은 SHA-256을 여러 path/project가 사용할 수 있다.

따라서 Sync 삭제 승인이 있어도 다음 최소 안전조건은 유지한다.

```text
현재 활성 project_large_files 참조 > 0
→ 해당 path의 named alias만 삭제
→ canonical object 유지

현재 활성 project_large_files 참조 = 0
→ named alias 삭제
→ canonical object 삭제
```

여기서 "활성"은 REMOVED가 아닌 현재 관리 path를 의미한다.

과거 checkpoint의 장기 보존을 이유로 이번 v0.1 삭제를 막지 않는다.
현재 Restore UX는 historical revision browser가 아니라 최신 ProjectHub 상태를 현재 탐색기에 적용하는 기능이다.

단 DB FK 때문에 `large_objects` row를 바로 삭제할 수 없는 경우
역사 metadata를 억지로 CASCADE 삭제하지 않는다.

권장 최소 처리:

```text
NAS physical object 삭제 성공
→ large_objects row는 FK가 남아 있으면 유지
→ lifecycle을 MISSING 또는 현재 모델에서 동등한 상태로 갱신
→ current project path는 REMOVED 유지
```

향후 historical checkpoint 보존/만료 정책은 별도 확장으로 둔다.

---

# 5. NAS Gateway delete 동작

NAS Gateway에 canonical object 삭제용 명시적 endpoint를 추가한다.

예:

```text
delete-object.php
```

권한은 upload와 분리해 명확히 한다.

예:

```text
operation = delete
```

또는 현재 assertion enum/계약과 자연스럽게 맞는 별도 삭제 operation을 추가한다.

삭제 대상은 Server가 계산하고,
DEV PC가 NAS filesystem에 직접 접근하지 않는다.

흐름:

```text
DEV Sync
→ ProjectHub.Server
→ 참조 검사
→ delete assertion
→ NAS Gateway
→ named alias 삭제
→ 필요 시 canonical object 삭제
→ Server에 결과 반영
```

TLS 우회나 SMB 직접 삭제는 사용하지 않는다.

---

# 6. 삭제 실패 처리

사용자 승인을 받은 뒤 NAS 삭제가 실패할 수 있다.

이 경우 거짓으로 성공 처리하지 않는다.

권장:

```text
DB path tombstone 성공
NAS alias/object 삭제 실패
→ Sync result에 FAILED 또는 PARTIAL 표시
→ Server log ERROR
→ 다음 명시적 Sync에서 다시 정리 가능하도록 상태 유지
```

canonical object 삭제 실패만으로 tombstone을 자동 되돌리지는 않는다.

동작은 idempotent하게 만든다.

이미 alias/object가 없는 상태에서 재호출해도 성공 또는 ALREADY_DELETED로 취급한다.

---

# 7. 삭제 관련 로그

현재 공통 console 형식을 그대로 사용한다.

예:

```text
2026-09-18 11:20:01 [INFO ] [DEV-PC-01 ] [hw      ] REMOVAL_CONFIRMED path=data/A.bin [200]
2026-09-18 11:20:01 [INFO ] [DEV-PC-01 ] [hw      ] TOMBSTONE_CREATED path=data/A.bin [200]
2026-09-18 11:20:02 [INFO ] [DEV-PC-01 ] [hw      ] NAS_ALIAS_DELETED path=data/A.bin [200]
2026-09-18 11:20:02 [INFO ] [DEV-PC-01 ] [hw      ] NAS_OBJECT_DELETED hash=e93ac6ff [200]
```

같은 object가 다른 현재 path에서 사용 중이면:

```text
2026-09-18 11:20:02 [INFO ] [DEV-PC-01 ] [hw      ] NAS_OBJECT_RETAINED hash=e93ac6ff active_refs=2 [200]
```

실패:

```text
2026-09-18 11:20:02 [ERROR] [DEV-PC-01 ] [hw      ] NAS_OBJECT_DELETE_FAILED hash=e93ac6ff reason=gateway_error [502]
```

---

# 8. 일자별 Server Full-log 파일 추가

콘솔은 지금처럼 사람이 보기 좋은 주요 operation만 간결하게 유지한다.

별도로 Server에는 일자별 Full-log 파일을 남긴다.

고정 경로:

```text
ProjectHub\src\ProjectHub.Server\log\yyyymmdd.log
```

실제 구현은 Server content root 기준으로:

```text
<ContentRoot>\log\yyyyMMdd.log
```

를 사용한다.

예:

```text
C:\AI-Server\ProjectHub\src\ProjectHub.Server\log\20260918.log
```

`log/` 디렉터리가 없으면 자동 생성한다.

로그 파일은 Git 관리 대상이 아니므로 `.gitignore`에 추가한다.

---

# 9. Full-log에 기록할 범위

Full-log는 콘솔보다 상세하게 기록한다.

최소 포함:

```text
- SERVER_STARTED / SERVER_STOPPING
- CONFIG 상태 요약(비밀값 제외)
- heartbeat 수신/처리 성공 및 실패
- project state update
- assertion 발급/refresh
- upload session 생성/재사용/완료
- STAGED / CHECKPOINT
- Sync removal/tombstone
- NAS alias/object delete 결과
- Restore 관련 주요 처리
- Warning / Error / Exception
```

특히 **heartbeat도 Full-log에는 포함**한다.

단 heartbeat는 콘솔 Information에는 계속 표시하지 않는다.

예:

```text
2026-09-18 11:20:00 [INFO ] [DEV-PC-01 ] [-       ] HEARTBEAT_RECEIVED hostname=DEV-PC-01 [200]
```

Microsoft/ASP.NET Core/Supabase HttpClient의 모든 내부 Information 로그까지 무제한 복제할 필요는 없다.

"Full-log"의 의미는 ProjectHub의 전체 운영 흐름을 재구성할 수 있는 application full log로 잡는다.

framework는 Warning/Error 이상만 파일에 포함하면 충분하다.

---

# 10. 로그 파일을 매 이벤트마다 열고 닫지 않는다

매 heartbeat/log event마다:

```text
File.Open
→ Write
→ Close
```

하는 방식도 현재 부하에서는 동작은 한다.

하지만 권장하지 않는다.

이유:

```text
- 불필요한 open/close system call 반복
- heartbeat가 여러 workstation에서 들어오면 파일 경합 증가
- 향후 로그량 증가 시 확장성이 나쁨
- 날짜 rollover와 shutdown 처리가 더 복잡해짐
```

현재 ProjectHub 규모에서는 **하루 동안 StreamWriter/FileStream 하나를 열어 두는 방식**이 가장 단순하고 충분하다.

권장:

```text
- FileMode.Append
- FileAccess.Write
- FileShare.ReadWrite 또는 FileShare.Read
- StreamWriter 1개 유지
- lock으로 짧게 동기화
- AutoFlush=true
```

heartbeat가 15초마다 발생하는 현재 구조에서는 이 정도로 성능 문제가 없다.

고성능 비동기 queue/Channel 기반 logger는 지금 단계에서는 필요하지 않다.
나중에 로그량이 크게 늘면 교체할 수 있다.

---

# 11. 일자 변경 rollover

현재 writer가 가진 날짜와 현재 날짜를 비교한다.

```text
현재 날짜 == writer 날짜
→ 같은 파일에 append

현재 날짜 != writer 날짜
→ 기존 writer flush/close
→ log\새날짜.log를 append mode로 open
```

예:

```text
2026-09-18 → log\20260918.log
자정 이후
2026-09-19 → log\20260919.log
```

로그 한 건을 쓰기 직전에 날짜를 확인하면 별도 timer는 없어도 된다.

---

# 12. Server 중단 / 재시작 파일 처리

사용자 요구:

```text
Server 중단 시 개행
같은 날짜에 재시작하면 기존 파일 뒤에 append
```

구현:

Server가 정상 종료될 때:

```text
SERVER_STOPPING 로그 기록
빈 줄 1줄 기록
Flush
Dispose
```

예:

```text
2026-09-18 12:00:00 [INFO ] [SERVER    ] [-       ] SERVER_STOPPING [OK]

```

같은 날 재시작:

```text
FileMode.Append
→ 기존 20260918.log 뒤에서 계속 기록
```

예:

```text
2026-09-18 12:00:00 [INFO ] [SERVER    ] [-       ] SERVER_STOPPING [OK]

2026-09-18 12:05:13 [INFO ] [SERVER    ] [-       ] SERVER_STARTED url=http://127.0.0.1:5240 [OK]
```

필요하면 startup에서도 기존 파일이 비어 있지 않을 때 빈 줄 1개를 보장할 수 있지만,
정상 shutdown에서 이미 separator를 넣었다면 중복 빈 줄은 만들지 않는다.

강제 종료/crash에서는 shutdown callback이 실행되지 않을 수 있다.
그 경우 다음 startup은 그냥 append하며,
`SERVER_STARTED` timestamp 자체가 session 경계를 나타낸다.

---

# 13. Full-log writer 구현 경계

Console formatter와 파일 writer의 책임을 분리한다.

권장 개념:

```text
ProjectHubConsoleFormatter
→ 현재 콘솔 표시 담당

ProjectHubDailyFileLoggerProvider
또는 동등한 서비스
→ 일자별 file append 담당
```

한 operation을 기록하면 console/file 양쪽으로 전달할 수 있게 한다.

heartbeat처럼 file-only 로그가 필요한 경우:

```text
console=false
file=true
```

또는 logger category/filter로 구분한다.

파일 저장 실패 때문에 Server 전체 요청 처리가 실패하지 않게 한다.

로그 파일 write 실패:

```text
→ 가능한 경우 Console Warning/Error
→ 본 요청 자체는 로그 실패만으로 500 처리하지 않음
```

단 반복 실패가 콘솔을 도배하지 않게 throttle 또는 1회 경고 정도로 제한한다.

---

# 14. 보안 / 파일 관리

Full-log에도 다음은 기록 금지:

```text
- Supabase Service Role Key
- private key PEM
- assertion/JWT 원문
- Authorization header
- request/response body 전체
```

허용:

```text
- project id
- workstation id
- path
- short SHA/hash/session
- HTTP status
- size
- lifecycle
- exception message
```

초기 v0.1에서는 자동 삭제/압축/retention까지 추가하지 않는다.

일자별 파일 생성만 구현한다.

나중에 필요하면:

```text
retention days
zip/archive
max total size
```

정책을 별도 확장한다.

---

# 15. 완료 기준

## NAS 실제 삭제

```text
[ ] Sync 삭제 GUI 승인
[ ] DB tombstone/REMOVED
[ ] NAS named alias 삭제
[ ] active current SHA reference count 확인
[ ] active ref=0이면 canonical object 삭제
[ ] active ref>0이면 canonical object 유지
[ ] NAS delete idempotent
[ ] 삭제 실패 결과가 Sync에 표시
[ ] Server operation log 출력
```

## Full-log

```text
[ ] src/ProjectHub.Server/log 자동 생성
[ ] yyyyMMdd.log 일자별 생성
[ ] 동일 날짜 재시작 시 append
[ ] 정상 종료 시 SERVER_STOPPING + 빈 줄
[ ] 날짜 변경 시 새 파일 rollover
[ ] heartbeat 포함
[ ] Console 주요 로그 필터는 기존대로 유지
[ ] file writer를 로그마다 open/close하지 않음
[ ] 파일 write가 요청 처리 실패 원인이 되지 않음
[ ] secret/token 기록 없음
[ ] log/ gitignore
```

---

# 16. 07 종료 순서

```text
[x] Setup 실제 프로젝트 E2E
[x] Sync 500MiB 실제 업로드 E2E
[x] upload transport 안정화
[x] 반복 삭제 확인창 수정
[x] 최종 Console formatter
[ ] Sync 승인 → NAS 실제 삭제 E2E
[ ] Restore 실제 프로젝트 E2E
[ ] LOCAL_ONLY 보호 확인
[ ] stale workstation tombstone 보호 확인
[ ] 일자별 Full-log E2E
[ ] build/test/PowerShell parser 최종 확인
```

07 완료 후 08 Server 설치/이전 가이드로 이동한다.

추가 실시간 관리, dashboard, lease, 별도 purge UI, 자동 background upload/reconcile은 v0.1 종료 조건에 넣지 않는다.
