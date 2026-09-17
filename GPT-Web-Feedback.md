# GPT Web Feedback

Updated: 2026-09-17

## 우선순위

1. `AGENTS.md`
2. `ProjectHub_IMPLEMENTATION_PLAN.md`
3. `CurrentWork.md`
4. 현재 활성 `tasks/*.md`
5. `GPT-Web-Feedback.md`

충돌 시 앞선 관리 문서와 활성 task를 우선한다. 단, 아래 내용은 2026-09-17 사용자가 실제 NAS1DUAL에서 수행한 최신 운영 테스트 결과와 그에 따른 06 수용 기준 보완이다.

---

# 1. 최신 저장소 상태 확인

최신 확인 커밋:

```text
1dfb7304577077bb5455f064778df8e4aa8f9525
Filter null sessions during large data GC
```

최근 06 관련 구현은 다음까지 진행됐다.

```text
[x] 평상시 Agent의 자동 대용량 hash/upload/staging/reconciliation 제거
[x] 명시적 ProjectHub_Sync.ps1 기반 Batch Sync
[x] control-plane Git/file summary 선반영
[x] 별도 ProjectHub_LargeData_Uploader.ps1
[x] resumable session 재사용
[x] 파일별 [i/n], HASHING, UPLOADING, FINALIZING, DONE 진행 표시
[x] finalize 성공 시 staging cleanup 강제 확인
[x] already_present 경로의 동일 session stale staging cleanup
[x] files/<project>/<relative-path> named alias
[x] objects/sha256/<hash> content-addressed object
[x] explicit ProjectHub_GC.ps1 dry-run/-Apply 구조
[x] cleanup-session.php scoped Gateway cleanup endpoint
[x] large upload session lifecycle/last activity 기반 GC 판단
[x] GC 세션 목록의 null row 필터링 보완
```

로드맵은 06을 아직 `검증 중`으로 두고 있으며, 현재 상태도 실제로 그 표현이 맞다. `CurrentWork.md` 본문에는 `06 Large Data/NAS 완료; 07 Project 상태 API 준비`라는 문장이 남아 있어 상태 표현이 서로 어긋난다. 06은 구현 완료에 가깝지만 실제 NAS 운영 cleanup/GC 수용 검증이 남아 있으므로 당분간 `06 기능 구현 완료 / 최종 운영 검증 중`으로 통일한다.

---

# 2. 이번 실제 NAS 테스트 결과 — 중요한 운영 사실

사용자가 NAS의 `files`, `objects`, `staging` 영역에서 기존 테스트 데이터를 수동 삭제하는 과정에서 다음 현상을 확인했다.

```text
- Windows ipDISK Drive 서비스를 통해 삭제했을 때는 삭제한 파일/폴더가 다시 나타나는 것처럼 보였다.
- 특히 staging 내부 파일을 ipDISK Drive 쪽에서 삭제한 뒤 해당 경로 접근이 비정상적으로 막히는 현상도 있었다.
- 동일 데이터를 NAS1DUAL 관리페이지에서 직접 삭제한 경우 정상 삭제됐다.
- NAS의 `Network Trashes Folder` 내용도 함께 비운 뒤 기존 데이터가 정상적으로 제거된 상태를 확인했다.
```

현재 증거만으로 ipDISK Drive 자체가 반드시 파일을 “복원했다”고 단정하지 않는다. 다만 실제 운영 결과상 **ProjectHub NAS 관리영역의 삭제 검증 기준으로 ipDISK Drive를 사용하면 안 된다.** NAS 관리페이지/실제 filesystem 상태를 기준으로 판단해야 한다.

또한 `Network Trashes Folder`가 활성화된 공유폴더에서는 파일 삭제가 곧바로 물리 블록 해제로 이어지지 않을 수 있으므로, 용량 회수 확인 시 NAS 휴지통 영역까지 고려한다.

---

# 3. 운영 정책 보완 — 수동 삭제 검증 기준

ProjectHub 관리영역:

```text
/mnt/HDD1/ProjectHub/files
/mnt/HDD1/ProjectHub/objects
/mnt/HDD1/ProjectHub/staging
```

에 대해 다음 원칙을 문서화한다.

```text
1. ipDISK Drive 화면상의 삭제 여부만으로 실제 NAS 삭제 성공을 판정하지 않는다.
2. 운영 진단/복구가 필요할 때는 NAS1DUAL 관리페이지 또는 Gateway가 보는 실제 filesystem 상태를 기준으로 확인한다.
3. 공유폴더의 Network Trashes Folder가 활성화돼 있다면 용량 회수/잔존 여부 확인 시 반드시 함께 확인한다.
4. 정상 운영에서는 files/objects/staging을 사용자가 직접 정리하지 않고 ProjectHub finalize/GC/purge 기능으로 관리한다.
5. 수동 삭제는 테스트/복구 목적의 예외 절차로 취급한다.
```

가능하면 NAS 운영 문서에 `Network Trashes Folder`의 적용 대상 공유폴더와 실제 물리 용량 회수 시점을 명시한다.

---

# 4. 현재 GC 구현에서 실제로 검증해야 할 것

`ProjectHub_GC.ps1` 코드가 존재하는 것과 실제 NAS 공간이 정리되는 것은 별개다. 다음 E2E를 실제로 수행해 기록한다.

```text
A. Dry-run
- ProjectHub_GC.ps1 실행
- SAFE / KEEP / REVIEW 판정 확인
- session id, lifecycle, lastActivityAt, reclaimable bytes 확인

B. Apply
- 안전한 과거 테스트 session 하나를 SAFE 대상으로 준비
- ProjectHub_GC.ps1 -Apply 실행
- cleanup-session.php 응답 확인
- NAS1DUAL 관리페이지에서 staging/<session-id> 실제 소멸 확인
- Network Trashes Folder에 동일 chunk가 남는지 확인
- 실제 NAS 사용량이 기대만큼 줄었는지 확인

C. Idempotency
- 동일 GC를 다시 실행
- already_clean 또는 후보 0개로 안전하게 끝나는지 확인

D. Active protection
- 실제 UPLOADING session은 KEEP 처리되고 절대 삭제되지 않는지 확인
```

PowerShell ExecutionPolicy 때문에 `.\ProjectHub_GC.ps1` 실행이 차단될 수 있음이 실제 테스트에서 확인됐다. 개발/운영 스크립트는 사용자가 시스템 정책을 영구 변경하지 않아도 다음처럼 실행 가능하도록 문서화한다.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\ProjectHub_GC.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\ProjectHub_GC.ps1 -Apply
```

가능하면 `ProjectHub_GC.cmd` launcher를 추가해 이 실행 방식을 감추고 사용자 조작을 줄인다.

---

# 5. finalize cleanup과 GC를 구분

정상 업로드에서는 GC가 필요하지 않아야 한다.

```text
chunk upload
-> finalize
-> hash/size 검증
-> object commit
-> named files alias 확인
-> staging session 즉시 삭제
-> staging_cleaned=true
-> Server metadata 완료 처리
```

이것이 1차 정상 경로다.

GC는 다음 예외만 처리한다.

```text
- 구버전 코드가 남긴 staging
- 네트워크/프로세스 중단
- 사용자 취소
- finalize cleanup 실패
- metadata lifecycle이 완료/취소/abandoned인데 실제 staging이 남은 경우
```

따라서 향후 테스트에서 정상 finalize 후 매번 GC가 필요하다면 실패로 본다. 정상 finalize 자체가 staging을 완전히 제거해야 한다.

---

# 6. Network Trashes Folder와 Gateway cleanup의 관계 확인

이번 사용자 테스트 때문에 반드시 확인할 항목이다.

Gateway의 PHP `unlink()`/`rmdir()`가 `/mnt/HDD1/ProjectHub/staging`에서 파일을 삭제할 때:

```text
- Network Trashes Folder를 우회해 즉시 unlink되는지
- NAS 휴지통으로 이동되는지
- 파일 관리 UI에서만 사라지고 실제 블록은 휴지통에 남는지
```

실제 NAS1DUAL에서 작은 테스트 session으로 확인한다.

ProjectHub GC 성공의 정의는 단순히 staging path가 안 보이는 것이 아니라:

```text
1. staging/<session-id> 없음
2. Network Trashes Folder에 동일 chunk가 불필요하게 누적되지 않음
3. 실제 filesystem 사용량/가용 공간이 회수됨
```

까지 보는 것이 바람직하다.

만약 Gateway 삭제가 NAS 휴지통으로 들어간다면, ProjectHub 전용 저장영역에는 휴지통 기능을 적용하지 않거나 ProjectHub GC가 안전하게 영구 삭제되는 NAS 설정을 사용하는 방안을 검토한다. 단, NAS 설정 변경은 사용자 승인/수동 운영 작업으로 분리한다.

---

# 7. files / objects는 staging과 다르게 취급

현재 `files/<project>/<relative-path>`와 `objects/sha256/<hash>`는 hard-link alias 구조이므로 UI에서 양쪽 모두 원본 전체 크기로 보여도 실제 데이터 블록은 하나일 수 있다.

따라서:

```text
staging GC -> 현재 06에서 실제 검증해야 함
object GC  -> 별도 후속 작업, 자동 삭제 금지
files purge -> object reference와 함께 정식 purge 계약으로만 처리
```

을 유지한다.

이번 수동 삭제 테스트에서 `files`, `objects`, `staging`을 모두 지운 것은 테스트 초기화 목적이었다. 이를 정상 운영 절차로 문서화하면 안 된다.

---

# 8. 문서 정합성 수정 요청

`CurrentWork.md`에는 현재 서로 충돌하는 표현이 있다.

```text
진행: 잔여 작업 = 06 최종 운영 검증, 이후 07·08·09
현재 작업: 06 Large Data/NAS 완료; 07 Project 상태 API 준비
```

후자를 다음과 같이 수정한다.

```text
06 Large Data/NAS 기능 구현 완료 / 최종 운영 검증 중
```

그리고 `tasks/06-large-data-nas.md`의 최신 운영 결과에 아래를 추가한다.

```text
- ProjectHub_GC.ps1/cleanup-session.php 구현 완료
- ExecutionPolicy 우회 launcher/명령 운영 절차
- ipDISK Drive를 삭제 검증 기준으로 사용하지 않음
- NAS1DUAL 관리페이지 직접 삭제 + Network Trashes Folder 비움에서 수동 초기화 성공
- 이 결과는 GC 성공 증거가 아니라 수동 NAS 초기화 성공 증거임
- 실제 GC -Apply E2E는 별도로 검증해야 함
```

로드맵의 `06 = 검증 중` 상태는 실제 GC E2E가 끝날 때까지 유지한다.

---

# 9. 06 최종 완료를 위한 남은 수용 테스트

다음 항목이 모두 통과해야 06을 완전히 완료 처리한다.

```text
[ ] 최신 Gateway PHP가 실제 NAS에 배포돼 있음
[ ] 새 대용량 파일 Batch 업로드 실제 E2E
[ ] uploader 파일별 진행률/상태 표시 실제 확인
[ ] finalize 직후 해당 staging session이 자동으로 사라짐
[ ] finalize cleanup 후 Network Trashes Folder에 chunk가 누적되지 않는지 확인
[ ] 중단된 upload -> 동일 session resume -> finalize -> staging 완전 정리
[ ] ProjectHub_GC.ps1 dry-run 실제 결과 확인
[ ] ProjectHub_GC.ps1 -Apply로 과거 SAFE session 실제 삭제
[ ] GC 재실행 idempotency 확인
[ ] 활성 UPLOADING session이 GC에서 보호됨
[ ] files/objects hard-link 실제 NAS 특성 확인
[ ] build/test/PowerShell parser PASS
```

GC 테스트와 정상 upload cleanup 테스트는 별개의 수용 항목으로 기록한다.

---

# 10. Codex 다음 작업 지시

한 번에 하나의 세부 작업 원칙을 유지한다. 다음 작업은 **새 기능 확장보다 06 cleanup/GC 실제 NAS 검증을 우선**한다.

권장 순서:

```text
1. CurrentWork/task 문서의 완료/검증중 상태 정합성 수정
2. ProjectHub_GC.cmd 또는 동등 launcher 추가해 ExecutionPolicy UX 보완
3. NAS Gateway 배포 버전과 Git 최신 PHP 일치 확인 절차 작성
4. 작은 disposable staging session으로 GC dry-run -> -Apply E2E
5. NAS 관리페이지에서 staging 삭제 확인
6. Network Trashes Folder 영향 확인
7. 정상 Batch upload 하나를 새로 수행해 finalize 자동 cleanup 검증
8. interrupted resume -> cleanup 검증
9. 결과를 CurrentWork/tasks/06에 실제 로그 수준으로 기록
10. 모두 통과한 뒤에만 06 완료 및 07 진입
```

이번 최신 테스트의 핵심 결론은 다음이다.

> **ipDISK Drive에서 보이는 삭제 결과를 ProjectHub NAS 데이터의 실제 삭제 성공으로 간주하지 않는다. NAS1DUAL 관리페이지와 실제 filesystem/휴지통 상태를 기준으로 검증한다.**

> **사용자가 NAS 관리페이지에서 직접 삭제하고 Network Trashes Folder를 비운 뒤 기존 테스트 데이터가 정상 제거된 것은 확인됐지만, 이는 ProjectHub GC 기능의 성공 검증이 아니다. GC dry-run/-Apply와 정상 finalize cleanup은 별도로 실제 NAS E2E를 통과해야 한다.**
