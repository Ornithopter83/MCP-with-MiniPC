# GPT Web Feedback

Updated: 2026-09-17

## 우선순위

1. `AGENTS.md`
2. `ProjectHub_IMPLEMENTATION_PLAN.md`
3. `CurrentWork.md`
4. 현재 활성 `tasks/*.md`
5. `GPT-Web-Feedback.md`

충돌 시 앞선 관리 문서와 활성 task를 우선한다. 단, 아래 Large Data/NAS 정책과 완료 기준은 사용자가 2026-09-17 명시적으로 요청한 최신 요구이므로 관련 계획·task·구현을 이 기준에 맞게 정리한다.

---

# 1. 최신 검토 기준

최신 확인 커밋:

```text
775910714203257b7afeb4d0cb940ddb475f7282
Add named NAS aliases and improve large-file upload feedback
```

확인된 현재 구현:

```text
[x] 평상시 Agent의 자동 대용량 hash/upload/staging/reconciliation 제거
[x] 명시적 ProjectHub_Sync.ps1 기반 Batch Sync
[x] control-plane Git/file summary를 Server에 먼저 반영
[x] 별도 ProjectHub_LargeData_Uploader.ps1 프로세스 실행
[x] project/workstation/object hash/size 기준 기존 resumable session 재사용
[x] chunk status 기반 resume
[x] 짧은 assertion 재발급
[x] NAS finalize / hash / size 검증
[x] STAGED / CHECKPOINTED 연결 경로
[x] 업로드 중 source size/mtime 변경 시 CHANGED_DURING_UPLOAD 제외
[x] uploader 콘솔 유지 및 오류 표시
[x] chunk 단위 Write-Progress 및 byte/chunk 출력 추가
[x] finalize 시 projecthub_cleanup_session() 추가
[x] named alias files/<project>/<relative-path> 지원 추가
```

정책 방향은 맞다. 다만 현재 상태에서 06을 완전히 닫기 전에 **업로드 진행 표시, staging cleanup의 강제 검증, dedup/existing-object cleanup, named alias 삭제 의미, 실제 다중 파일 E2E**를 보완한다.

---

# 2. 대용량 업로드 창: 각 파일별 진행상황을 명확히 표시

현재 uploader는 현재 파일에 대해 다음을 이미 표시한다.

```text
UPLOAD: <relative path> (<done>/<total> chunks, <bytes>/<total bytes>)
Write-Progress: 파일명 + percent + bytes
chunk N/M complete
```

하지만 여러 파일을 한 번에 올릴 때 사용자가 확인하기에는 부족하다. 현재 `Write-Progress -Activity "ProjectHub NAS upload"`가 단일 activity이고, 현재 batch의 몇 번째 파일인지와 전체 batch 상태가 명확하지 않다.

다음 UX를 목표로 수정한다.

예:

```text
ProjectHub Large Data Upload
Batch: 3 files / 12.4 GB

[1/3] assets/model.zip
  Status   : UPLOADING
  Progress : 42%
  Bytes    : 2.1 GB / 5.0 GB
  Chunks   : 68 / 160
  Session  : <short session id>

[2/3] video/raw.bin
  Status   : WAITING
  Size     : 6.8 GB

[3/3] archive.dat
  Status   : WAITING
  Size     : 0.6 GB
```

최소 요구:

```text
- 현재 파일 index / 전체 파일 수: [2/5]
- relative path
- 파일 크기
- 전송 byte / 전체 byte
- percent
- 완료 chunk / 전체 chunk
- 상태: WAITING / HASHING / RESUMING / UPLOADING / FINALIZING / STAGED / CHECKPOINTED / FAILED / CHANGED_DURING_UPLOAD
- resume이면 기존 완료 chunk/bytes에서 시작했다는 표시
- 파일 완료 시 100%와 완료 상태를 한 줄로 남김
```

가능하면 추가:

```text
- 현재 파일 전송 속도 MB/s
- ETA
- batch 전체 완료 파일 수
- batch 전체 byte 기준 진행률
```

구현은 PowerShell `Write-Progress`를 계속 사용해도 된다. 단, 하나의 progress bar가 파일 전환 시 덮어써져 과거 상태를 잃지 않도록 `Write-Host` 완료 로그를 함께 남긴다.

권장 출력:

```text
[1/3] START  assets/model.zip  5.0 GB
[1/3] 42%    2.1/5.0 GB       chunk 68/160
[1/3] 100%   FINALIZING
[1/3] DONE   STAGED            SHA-256 verified

[2/3] START  video/raw.bin ...
```

---

# 3. HASHING 단계도 진행 상태로 구분

현재 uploader는 `Get-FileHash`가 끝날 때까지 큰 파일에서 콘솔이 멈춘 것처럼 보일 수 있다.

8GB~수십 GB 파일에서는 hash 계산도 상당한 시간이 걸릴 수 있으므로 최소한:

```text
[1/3] HASHING: assets/model.zip (5.0 GB)
```

을 먼저 출력한다.

가능하면 향후 streaming hash progress를 넣을 수 있지만, 이번 단계에서는 과도한 구현을 하지 않아도 된다. 핵심은 사용자가 uploader가 멈춘 것이 아니라 hashing 중임을 알 수 있게 하는 것이다.

---

# 4. finalize 성공 시 NAS staging chunk는 반드시 제거되어야 함

현재 `upload-finalize.php`는 성공 시 `projecthub_cleanup_session($sessionDir, $parts)`를 호출한다. 방향은 맞다.

현재 cleanup helper:

```text
- *.part 삭제
- assembled.tmp 삭제
- session.json 삭제
- session directory rmdir
```

그러나 현재 반환값을 무시하고 있다. 따라서 chunk 삭제 실패나 directory 잔존이 있어도 finalize 응답이 `complete`로 나갈 수 있다.

이 상태는 허용하지 않는다.

finalize 완료 조건을 다음처럼 강화한다.

```text
object hash/size 검증 성공
-> object commit 또는 existing object 확인
-> named alias 처리
-> staging chunk/session cleanup
-> cleanup 결과 확인
-> session directory가 실제로 없어졌음을 확인
-> 그 뒤 complete 응답
```

즉:

```text
finalize complete == object 저장 성공 + staging cleanup 성공
```

으로 본다.

`projecthub_cleanup_session()` 반환값이 false이면 무시하지 않는다.

예:

```php
if (!projecthub_cleanup_session($sessionDir, $parts)) {
    projecthub_json_error(500, 'staging_cleanup_failed');
}
```

단, object가 이미 commit된 이후 cleanup만 실패한 경우 재시도 시 binary를 다시 올리지 않도록 idempotent하게 처리한다. 다음 finalize/upload-start에서 object가 이미 존재하면 object identity를 확인하고 같은 session directory의 cleanup만 재시도할 수 있어야 한다.

---

# 5. cleanup helper는 예상치 못한 잔여 파일을 숨기지 않는다

현재 `projecthub_cleanup_session()`은 알려진 파일만 unlink한 뒤 `rmdir()` 한다.

세션 디렉터리에 예상치 못한 파일이 하나라도 남으면 `rmdir()`이 실패하지만 현재는 원인을 알기 어렵다.

보완:

```text
- 삭제 전/후 session directory 내용을 검사
- *.part, assembled.tmp, session.json 외 예상치 못한 파일이 있으면 로그/오류
- cleanup 실패 시 남은 파일 이름을 서버 로그에 남김
- symlink는 절대 따라가거나 삭제하지 않음
```

운영 응답에 전체 NAS absolute path를 노출할 필요는 없지만 최소 오류명은 명확히 한다.

```text
staging_cleanup_failed
staging_not_empty
```

등.

---

# 6. already_present / dedup 경로에서도 stale staging을 정리

현재 `upload-start.php`는 object가 이미 `objects/sha256/...`에 있으면 즉시 `state=complete, already_present=true`로 반환한다.

이 경로에서는 동일 `upload_session_id`의 staging directory가 과거 실패로 남아 있어도 정리하지 않는다.

따라서 다음 상황이 가능하다.

```text
NAS object는 이미 정상 존재
+ Supabase resumable session도 남아 있음
+ staging/<session-id>에 옛 chunk가 남아 있음

다음 Batch Sync
-> existing object 확인
-> complete 반환
-> staging은 그대로 남음
```

이것을 보완한다.

object가 이미 존재하고 hash/size identity가 맞는 경우:

```text
1. 같은 upload_session_id의 staging directory 존재 여부 확인
2. session.json의 project/workstation/hash/size가 현재 claim과 일치하는 경우에만
3. stale chunk/session directory cleanup
4. cleanup 성공 후 already_present=true 반환
```

다른 session의 staging을 임의 삭제하면 안 된다.

---

# 7. uploader에서 Server session complete 처리 순서 확인

현재 uploader는 NAS finalize 또는 already_present 확인 후:

```text
/api/large-data/resumable-session/{session}/complete
```

를 호출한다.

이때 NAS staging cleanup이 실제 성공했다는 보장이 먼저 있어야 한다.

권장 순서:

```text
NAS finalize response complete
+ staging_cleaned=true 또는 동등 보장
-> Server resumable session COMPLETED
-> source 변경 재검사
-> STAGED metadata
-> checkpoint 조건 만족 시 CHECKPOINTED
```

Gateway 응답에 필요하다면 다음을 추가한다.

```json
{
  "state": "complete",
  "staging_cleaned": true,
  "object_hash": "...",
  "size_bytes": 123,
  "named_path": "..."
}
```

Uploader는 `staging_cleaned != true`이면 session complete 처리하지 않는다.

---

# 8. 실제 NAS에서 cleanup E2E를 반드시 검증

코드상 cleanup 함수가 있다는 것만으로 06 완료로 보지 않는다.

실제 NAS에서 다음을 확인한다.

```text
A. 새 object upload
1. Batch Sync 시작
2. staging/<session>/chunk가 생성되는 것 확인
3. finalize 완료
4. objects/sha256/<hash> 존재
5. files/<project>/<relative-path> 존재
6. staging/<session> directory 자체가 사라졌는지 확인

B. interrupted resume
1. 일부 chunk 전송 후 uploader 중단
2. staging session/chunk 유지 확인
3. 다음 명시적 Batch Sync 실행
4. 동일 session id 재사용
5. 기존 chunk부터 resume
6. finalize 완료
7. staging session 완전 삭제 확인

C. already_present
1. object가 이미 존재하는 파일로 Batch Sync 실행
2. 새 binary upload 없이 ALREADY_PRESENT
3. 동일 session의 stale staging이 있다면 cleanup됨
```

이 세 경우를 모두 기록한다.

---

# 9. 다중 파일 실제 E2E가 필요

현재 개선된 progress UX는 코드로는 들어갔지만 실제 사용성은 1개 파일 테스트만으로 충분하지 않다.

최소 2~3개 대용량 파일을 한 batch에 넣고 검증한다.

예:

```text
file-A 500MB
file-B 700MB
file-C 1.2GB
```

검증 포인트:

```text
- 각 파일 [i/n] 표시
- HASHING / UPLOADING / FINALIZING / DONE 전환
- 한 파일 완료 후 다음 파일로 정상 이동
- resume 대상과 신규 대상이 섞여도 표시가 맞음
- 한 파일 실패 시 어느 파일/어느 chunk에서 실패했는지 즉시 식별 가능
- batch 종료 전에 STAGED/CHECKPOINTED 결과가 명확히 표시됨
```

---

# 10. 오류 하나가 전체 batch를 즉시 중단할지 정책 명확화

현재 uploader는 `foreach` 전체가 하나의 try/catch 안에 있어 한 파일 오류가 발생하면 나머지 파일도 중단된다.

이 정책은 명확히 결정해야 한다.

권장:

```text
기본은 파일 단위 실패 격리
```

즉:

```text
file-A DONE
file-B FAILED
file-C 계속 업로드
```

후 batch summary:

```text
Completed: 2
Failed: 1
Changed during upload: 0
Checkpoint: skipped 또는 성공 가능한 subset 정책에 따름
```

단, checkpoint dataset은 불완전한 snapshot을 잘못 완성 처리하면 안 된다. captured manifest 전체가 checkpoint 대상이라는 정책이면 하나라도 실패/변경 시 CHECKPOINTED를 생략하고 STAGED까지만 남기는 현재 보수적 정책을 유지한다.

---

# 11. named alias는 hard link이므로 삭제 의미를 문서화

최신 구현은:

```text
objects/sha256/<hash>
files/<project>/<relative-path>
```

를 hard link로 연결한다.

이 경우 두 경로는 같은 inode/data를 가리킨다.

중요:

```text
objects 쪽 파일만 수동 삭제해도 files 쪽 hard link가 남아 있으면 실제 데이터는 삭제되지 않음
files 쪽만 삭제해도 objects 쪽이 남아 있으면 실제 데이터는 삭제되지 않음
```

사용자가 NAS에서 수동으로 파일을 지우며 상태를 맞추려는 운영 방식과 충돌할 수 있다.

따라서 문서에 명확히 기록한다.

```text
NAS objects/files/staging은 ProjectHub 관리 영역이다.
운영 중 수동 일부 경로 삭제로 상태를 맞추지 않는다.
```

향후 삭제 기능이 필요하면 Server/Gateway에 **정식 purge/delete 작업**을 만들고:

```text
- DB reference 확인
- named alias 제거
- object reference count 확인
- 다른 project/file에서 동일 hash 사용 중이면 object 유지
- 참조가 0일 때만 content object 삭제
```

순서로 처리한다.

현재 06 범위에서 자동 purge까지 구현할 필요는 없지만 hard-link 의미와 수동 삭제 주의사항은 문서화한다.

---

# 12. object 생성과 named alias 생성 사이의 실패 보완

현재 새 object finalize 흐름은 대략:

```text
assembled.tmp
-> rename to objects/sha256/<hash>
-> named alias 생성
-> staging cleanup
```

named alias 생성이 실패하면 object는 이미 저장됐지만 staging cleanup 전에 오류가 발생할 수 있다.

이 경우 다음 실행이 idempotent하게 복구되어야 한다.

다음 Batch:

```text
upload-start sees existing object
-> object identity 확인
-> named alias 생성 재시도
-> stale same-session staging cleanup
-> complete
```

이 경로를 테스트한다.

---

# 13. 06 완료 상태 문서 표현을 보수적으로 수정

현재 `CurrentWork.md`는 `06 Large Data/NAS 완료; 07 Project 상태 API 준비`로 적혀 있다.

그러나 이번 최신 progress/alias/cleanup 변경은 아직 실제 NAS 다중 파일 E2E와 cleanup 강제 검증이 남아 있다.

따라서 검증이 끝날 때까지 권장 표현:

```text
06 Large Data/NAS 구현 완료, 최종 운영 E2E 검증 중
```

또는:

```text
06 기능 구현 완료 / 최종 수용 검증 남음
```

으로 둔다.

아래 검증까지 끝난 뒤 완전 완료 처리한다.

```text
[ ] 각 파일별 progress UX 실제 확인
[ ] multi-file batch 실제 E2E
[ ] 새 upload finalize 후 staging session 완전 삭제
[ ] interrupted resume 후 staging 완전 삭제
[ ] already_present 경로 stale staging cleanup
[ ] object/alias 생성 실패 후 idempotent recovery
[ ] build/test PASS
[ ] NAS 실제 경로 확인
```

---

# 14. 이번 수정에서 건드리지 말아야 할 경계

기존 정책을 유지한다.

```text
- 평상시 Agent 자동 대용량 upload 금지
- FileSystemWatcher upload trigger 금지
- Agent restart 자동 resume/staging 생성 금지
- 사용자가 명시적 Batch Sync를 실행했을 때만 upload 시작
- Server binary relay 금지
- Agent Supabase 직접 접근 금지
- SMB/NAS filesystem 직접 접근 금지
- private key 저장소 기록 금지
- TLS 검증 우회 금지
- 자동 git add/commit/push/pull/reset/merge/rebase/clean 금지
```

---

# 15. Codex 수행 순서

다음 순서로 진행한다.

```text
1. 최신 775910714... 코드 기준으로 시작
2. uploader에 [i/n] 파일 index + 상태 + bytes/chunks/percent 표시 보강
3. HASHING / FINALIZING / DONE 상태 명확히 출력
4. 가능하면 batch 전체 progress 추가
5. projecthub_cleanup_session() 실패를 무시하지 않도록 변경
6. finalize success 전에 staging directory 실제 제거 확인
7. upload-start already_present 경로에서 동일 session stale staging cleanup 추가
8. Gateway response에 staging cleanup 성공 여부 명시 검토
9. uploader가 cleanup 성공 후에만 Server session COMPLETE 처리
10. 2~3개 파일 multi-file E2E
11. 강제 중단 -> 다음 Batch resume E2E
12. finalize 후 NAS staging directory 0개 또는 해당 session 완전 제거 확인
13. already_present + stale staging cleanup E2E
14. hard-link named alias 삭제 의미 문서화
15. CurrentWork/task를 검증 수준에 맞게 갱신
16. build/test 및 PowerShell parser 검증
```

---

# 16. 다음 보고에 반드시 포함할 내용

```text
- latest commit SHA
- uploader 화면 예시 또는 실제 출력
- 각 파일별 [i/n], percent, bytes, chunks 표시 결과
- multi-file batch 결과
- finalize 전 staging 경로
- finalize 후 해당 session directory 존재 여부
- interrupted resume 시 동일 session 재사용 증거
- already_present 시 stale staging cleanup 결과
- NAS object hash/size 확인
- named alias 경로 확인
- STAGED/CHECKPOINTED metadata 결과
- build/test 결과
```

이번 수정의 핵심 성공 기준은 다음 두 가지다.

> **사용자가 대용량 업로드 창만 보고 각 파일의 현재 상태와 진행률을 즉시 이해할 수 있어야 한다.**

> **파일 업로드가 정상 완료된 뒤 해당 upload session의 NAS staging chunk/session 파일은 반드시 남지 않아야 한다.**
