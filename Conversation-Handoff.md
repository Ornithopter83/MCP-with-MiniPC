# ProjectHub Conversation Handoff

Updated: 2026-09-18

## 목적

이 문서는 긴 Web ChatGPT 대화를 새 채팅으로 옮길 때 사용하는 인수인계 요약이다.
새 대화에서는 전체 과거 대화를 다시 재구성하려 하지 말고, 아래 저장소 문서를 최신 기준으로 다시 읽은 뒤 이 문서를 보조 컨텍스트로 사용한다.

우선순위:
1. AGENTS.md
2. ProjectHub_IMPLEMENTATION_PLAN.md
3. CurrentWork.md
4. 활성 tasks/*.md
5. GPT-Web-Feedback.md
6. Conversation-Handoff.md

## 현재 단계

- 활성 작업: `tasks/07-project-deployment-package.md`
- 06 Large Data/NAS 핵심 기능은 검증 완료
- 07 프로젝트 배포 패키지는 마무리 단계
- 현재 최신 작업 기록상 남은 실제 검증은 `hw`에서 Force Restore GUI의 계속/취소 동작 확인 1건
- 이후 08 Server 설치/이전 단계로 이동 예정

## 현재 사용자 UX

관리 프로젝트 루트에는 아래 3개 CMD만 유지하는 방향으로 정리했다.

```text
ProjectHub_Commit_Push.cmd
ProjectHub_Fetch_Pull.cmd
ProjectHub_Force_Restore.cmd
```

나머지 ProjectHub 내부 구현은 프로젝트 내부의 `ProjectHub\` 폴더로 집중한다.

```text
<ProjectRoot>\
├─ ProjectHub_Commit_Push.cmd
├─ ProjectHub_Fetch_Pull.cmd
├─ ProjectHub_Force_Restore.cmd
└─ ProjectHub\
   ├─ bin\
   ├─ config\
   ├─ state\
   └─ log\
```

관리 프로젝트의 위치 자체는 바꾸지 않으며 별도 경로 지정을 요구하지 않는다.

## Git + ProjectHub 동작

### Commit Push

```text
git add -A
→ commit
→ fetch
→ 필요 시 pull --rebase
→ push
→ ProjectHub Large Data Sync
→ checkpoint
```

### Fetch Pull

```text
dirty 상태 확인
→ fetch
→ pull --rebase
→ ProjectHub Large Data Restore
```

### Force Restore

로컬 상태를 신뢰하지 않고 GitHub + ProjectHub/NAS의 최신 상태를 정답으로 강제 복구한다.

```text
사용자 GUI 승인
→ fetch
→ reset --hard origin/<branch>
→ clean
→ Large Data Restore
→ 최종 검증
```

중요:
- `ProjectHub\` 전체와 루트 3개 CMD는 강제복구에서 절대 손상시키지 않는다.
- Git clean 시 위 관리영역을 명시적으로 보호한다.
- 임의 conflict 자동 해결은 하지 않는다.

## Large Data / NAS

- Git에 적합하지 않은 대용량 파일은 NAS에 저장
- canonical object는 SHA-256 content-addressed 방식
- named alias는 프로젝트 경로 기준
- resumable chunk upload 지원
- Server가 assertion 발급, NAS Gateway가 검증
- DEV Agent가 Supabase나 NAS filesystem에 직접 접근하지 않는다.

운영 Gateway:
```text
https://dfblackbox-nas.duckdns.org:8443/projecthub/
```

NAS storage root:
```text
/mnt/HDD1/ProjectHub
```

## Restore 무결성

Restore는 다음 구조로 강화됐다.

```text
PREPARE
→ 필요한 파일 전부 temp 다운로드
→ size + SHA-256 전체 검증
→ 하나라도 실패하면 실제 프로젝트 파일 미변경

APPLY
→ 전체 검증 성공 시 실제 프로젝트에 적용
→ REMOVED 반영
→ 최종 전체 검증
```

성공 조건:
```text
mismatched = 0
missing    = 0
```

500MiB 테스트 파일 기준 Restore E2E는 최종적으로 성공했으며,
로컬 SHA-256과 NAS canonical object가 일치하는 것을 확인했다.

## NAS download.php 문제와 해결

한때 Restore에서 `RESTORE_SIZE_MISMATCH`와 download 500 문제가 있었다.

원인:
- NAS 웹 루트에 구버전 `download.php`가 남아 있었고 readfile 처리 문제가 겹침

조치:
- canonical object 경로 통일
- object_not_found / object_size_mismatch / object_not_readable / object_read_failed 구분
- readability/size/readfile 진단 보강
- 수정 PHP를 실제 NAS 웹 루트에 재배포

재검증:
- canonical object 524,288,000 bytes
- assertion 단독 download HTTP 200
- Content-Length 일치
- Restore matched=1, mismatched=0, missing=0
- SHA-256 일치

## 삭제 정책

사용자 승인 Sync 삭제는 별도 Purge UI 없이 바로 삭제 흐름으로 이어진다.

```text
local large file 삭제
→ Sync
→ 삭제 확인
→ REMOVED/tombstone
→ NAS named alias 삭제
→ 현재 active reference 확인
→ ref=0이면 canonical object 삭제
→ 결과 기록
```

현재 ref는 workstation-local presence count가 아니라 서버의 현재 active project path reference 기준이다.
workstation별 presence ref 모델은 아직 도입하지 않는다.

## Server / Logging

Server:
```text
http://127.0.0.1:5240
https://projecthub.ornithopter.bid
```

운영 로그 형식:
```text
yyyy-MM-dd HH:mm:ss [LEVEL] [WORKSTATION] [PROJECT] MESSAGE [STATUS]
```

Full log:
```text
<ProjectHub.Server ContentRoot>\log\yyyyMMdd.log
```

정책:
- 같은 날짜 재시작 시 append
- heartbeat는 file-only
- 정상 종료 시 SERVER_STOPPING + 구분
- secret/token/Authorization 원문 기록 금지

## Agent 상태

Heartbeat + Git state watcher 기능은 구현 및 과거 E2E 기록이 있지만,
현재 사용자 판단상 이 기능은 보류사항으로 취급한다.

따라서 ProjectHub와 GitHub MCP 비교나 다음 설계 논의에서
Heartbeat/Watcher를 현재 핵심 장점으로 전제하지 않는다.

## Web ChatGPT와 ProjectHub 직접 연결 방향

사용자가 장기적으로 가장 원하는 기능:

> Web ChatGPT가 자신의 ProjectHub Server에 직접 접근해 프로젝트 상태를 읽고 분석하는 것

현재 결론:
- 기존 구조를 바꾸는 것이 아니라 ChatGPT용 MCP/Connector 계층을 추가하는 방향
- 기존 Server / Supabase / NAS / CMD 구조는 유지
- 먼저 read-only로 시작

추천 초기 MCP 도구:
```text
project_context
project_changes
project_file_read
project_large_data_status
project_log_tail
```

후속 write/action 후보:
```text
submit_web_feedback
request_commit_push
request_fetch_pull
request_force_restore
```

임의 shell을 Web ChatGPT에 노출하지 않는다.
write/action은 제한된 API 및 사용자 승인형 흐름을 우선한다.

## Web ChatGPT와 GitHub MCP 비교 시 핵심

Heartbeat/Watcher를 제외해도 ProjectHub의 차별점은 다음과 같다.

- GitHub에 넣기 어려운 대용량 파일 관리
- Git 파일 + NAS 대용량 파일을 하나의 프로젝트 상태/checkpoint로 결합
- Commit Push / Fetch Pull / Force Restore의 통합 사용자 UX
- GitHub + NAS를 함께 복구하는 전체 프로젝트 복구
- 대용량 object lifecycle / tombstone / reference 기반 삭제
- ProjectHub 전용 정책 및 향후 GitHub 외 확장 가능성

요약:
```text
GitHub MCP = GitHub를 다루는 인터페이스
ProjectHub = GitHub + NAS를 묶어 실제 프로젝트 전체를 저장/복구하는 운영 계층
```

## 새 채팅에서 바로 확인할 것

새 채팅에서는 반드시 GitHub 최신 상태를 다시 확인한다.
이 문서의 커밋 SHA나 남은 작업을 그대로 최신이라고 가정하지 않는다.

특히 먼저 확인:
```text
1. 최신 commit
2. CurrentWork.md
3. 활성 task
4. GPT-Web-Feedback.md
5. Force Restore GUI 검증 완료 여부
6. 07 종료 및 08 전환 여부
```

## 대화 운용 원칙

- Web ChatGPT가 "최신 확인"을 말할 때는 반드시 GitHub를 다시 조회
- 사용자가 "피드백 올려줘"라고 하면 실제로 `GPT-Web-Feedback.md`를 수정/커밋
- 장기 설계 판단은 문서화하고, 긴 대화 자체를 기억 저장소로 사용하지 않는다.
