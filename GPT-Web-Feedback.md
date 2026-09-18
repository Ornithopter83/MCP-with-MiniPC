# GPT Web Feedback

Updated: 2026-09-18

## 최신 확인

```text
92f6c6d10ff79ffaafddb9eb5eedd23e9fb97831
Improve CMD entrypoint exit-code handling and pause behavior
```

최근 완료:
- NAS object deletion + daily full logging 구현
- delete assertion 운영 E2E 성공
- 문자열 LargeDataOperation 호환 수정
- 사용자 CMD pause / exit-code 처리 보강

## 추가 피드백

1. 사용자용 CMD 이름을 실제로 변경한다.

```text
ProjectHub_Sync.cmd
→ ProjectHub_Commit_Push.cmd

ProjectHub_Restore.cmd
→ ProjectHub_Fetch_Pull.cmd
```

기존 `.ps1` 내부 엔진은 당장 이름을 바꾸지 말고 재사용한다.

2. Git 기능을 사용자 진입 CMD에 통합한다.

```text
ProjectHub_Commit_Push
→ git add -A
→ commit
→ fetch
→ 필요 시 rebase/pull
→ push
→ 기존 ProjectHub_Sync.ps1 실행
→ large-data checkpoint

ProjectHub_Fetch_Pull
→ local dirty 확인
→ fetch
→ pull
→ 기존 ProjectHub_Restore.ps1 실행
```

충돌, detached HEAD, push reject, dirty 상태 등은 자동 해결하지 말고 중지 후 메시지를 남긴다.

3. 모든 사용자 CMD는 현재 적용한 pause / exit-code 처리 방식을 유지한다.

4. `ProjectHub_update.cmd` 넓은 콘솔 설정도 유지한다.

```bat
mode con: cols=220 lines=50
```

5. 07은 재설계하지 말고 Restore / LOCAL_ONLY / stale workstation / Full-log 최종 E2E를 끝낸 뒤 08 Server 이전으로 넘어간다.
