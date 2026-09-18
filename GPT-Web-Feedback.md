# GPT Web Feedback

Updated: 2026-09-18

## 최신 확인

```text
36d40c993bf616bb9d3682ce0d3de044f415991a
Document v0.2 Git workflow and deployment entry points
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


## 강제 복구 기능 추가

일반 `ProjectHub_Fetch_Pull`과 별도로, 로컬 상태를 신뢰하지 않고 최신 원격 상태로 강제 복구하는 사용자용 명령을 추가한다.

권장 이름:

```text
ProjectHub_Force_Restore.cmd
```

의미:

> GitHub의 최신 Git 상태 + ProjectHub/NAS의 최신 대용량 상태를 정답으로 보고 현재 로컬 프로젝트를 강제로 맞춘다.

권장 흐름:

```text
1. 프로젝트/remote/branch 확인
2. 강제 복구 경고 및 사용자 승인
3. git fetch origin
4. git reset --hard origin/<branch>
5. git clean -fd
6. 최신 ProjectHub checkpoint 조회
7. 관리 대상 대용량 파일을 NAS 기준으로 강제 overwrite/download
8. REMOVED 파일은 로컬에서도 삭제
9. Git + Large Data 최종 상태 검증
10. 결과 표시 + pause
```

일반 Fetch-Pull과 달리 강제 복구는 의도적으로 다음 보호를 우회한다.

```text
- dirty working tree 보호
- 관리 대상 대용량 파일의 로컬 수정 보호
- LOCAL_ONLY 보호(단, ProjectHub 자체 설정 파일은 예외)
```

반드시 보호할 항목:

```text
.projecthub/project.json
필수 ProjectHub launcher/config
복구 실행에 필요한 최소 로컬 설정
```

사용자 UX는 다음 세 가지로 단순화한다.

```text
ProjectHub_Commit_Push
= 현재 작업을 원격에 저장

ProjectHub_Fetch_Pull
= 정상적으로 최신 상태를 받아옴

ProjectHub_Force_Restore
= 로컬 상태를 버리고 최신 상태로 강제 복구
```

강제 복구는 파괴적이므로 실행 전 Windows GUI 또는 명확한 콘솔 확인을 반드시 거친다.
