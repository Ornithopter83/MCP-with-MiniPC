# GPT-6 Luna 인계 가이드

## 목적

GPT-5.6 Luna로 진행하던 Codex 작업을 GPT-6 Luna가 같은 스레드와 작업 폴더에서 이어받도록 한다. 실행 중인 CLI 프로세스의 모델을 중간에 변경하는 방식이 아니라, 다음 실행부터 동일 session을 `gpt-6-luna`로 resume한다.

## Worker에서 인계하는 방법

1. 실행 중인 작업이 있으면 먼저 `Cancel`로 종료한다. 실행 중인 프로세스의 모델은 중간 변경하지 않는다.
2. Codex 선택 콤보박스에서 기존 작업 스레드를 선택한다. 신규 스레드가 아니라 기존 스레드를 선택해야 session ID가 유지된다.
3. 모델 콤보박스에서 `GPT-6 Luna`를 선택한다. Worker의 초기 기본값도 `GPT-6 Luna`다.
4. 같은 작업 폴더와 필요한 후속 지시를 확인한 뒤 `Run Task`를 실행한다.
5. Worker는 저장된 session ID가 있으면 `codex exec resume ... --model gpt-6-luna` 형태로 실행하므로 기존 대화 문맥을 이어간다.

## 신규 스레드에서 시작할 때

- `GPT-6 Luna`를 선택하고 신규 스레드를 실행한다.
- CLI가 반환한 session ID는 Worker가 선택 상태에 저장한다.
- 다음 실행에서 해당 스레드를 선택하면 GPT-6 Luna로 계속 진행할 수 있다.

## 인계 시 보존되는 것

- Codex session ID와 대화 문맥
- 선택된 프로젝트 및 작업 폴더
- Worker의 JEV footer와 Git 기준 정보
- 작업 transcript 및 누적 토큰 기록

## 주의

- GPT-5.6 Luna의 이전 reasoning 상태를 GPT-6 Luna가 완전히 동일하게 재현한다고 보장하지 않는다. 모델은 같은 session 문맥을 받지만 모델별 reasoning 동작은 다르다.
- 인계 직후에는 `TASK START`와 `CLI STATUS`의 model 항목에서 실제 `gpt-6-luna` 사용 여부를 확인한다.
- 계정이나 CLI 버전에서 모델이 제공되지 않으면 exit code와 stderr를 확인하고 사용 가능한 모델로 선택한다.

## 2026-09-24 작업 인계 및 이주용 문구

### 현재 구현과 재현 결과

- CLI-to-CLI coordinator가 PLAN은 성공했지만 같은 관제 session ID를 얻지 못해 Luna 호출 전 BLOCKED 되는 재현이 있었다. `thread.started` JSONL 추출을 BOM/필드 대소문자에 강하게 했고, 누락 시 `CODEX_HOME`, `USERPROFILE\.codex`, .NET 프로필의 세션 루트를 모두 조사한다. 새 rollout의 `codex_exec`/`exec`, 동일 CWD, 시간 범위 조건을 확인하고 후보가 정확히 하나일 때만 복구한다.
- 2026-09-24 07:45:10 재현의 SOL PLAN은 exit 0이었다. 세션 기록은 `C:\Users\ornit\.codex\sessions`에 생성됐지만 Worker의 선택 경로와 다를 수 있었던 것이 차단 원인이었다.
- 검증은 Debug 빌드 성공(경고/오류 0), 전체 36개 테스트 통과, `git diff --check` 통과다. 설치 배포 때 작은 framework-dependent EXE를 잘못 복사한 적이 있어 DLL 누락으로 실행 실패했다. 단일 파일 publish EXE로 다시 교체했고 프로젝트 게시본과 `C:\AI-AGENT\Worker\ProjectHub.Worker.exe`의 SHA-256은 `843871D45623C8850090D6B0207C438B29E9D531F04579C5987E2E330A2F492B`로 일치한다.
- Computer Use의 native Windows 앱 목록이 이 세션에 노출되지 않아 사용자가 연 창의 시각 확인과 PLAN→REVIEW 실제 왕복은 아직 못 했다. 먼저 Explorer에서 설치본을 직접 확인하고, 실제 새 작업으로 PLAN 다음 동일 session REVIEW가 이어지는지 transcript와 rollout ID를 대조한다.

### 다음 Codex 작업으로 옮길 문구

```text
ProjectHub 저장소의 미완료 후속을 이어서 확인해줘. 우선 AGENTS.md, ProjectHub_IMPLEMENTATION_PLAN.md, CurrentWork.md, tasks/11-cli-coordinator-first.md, GPT-Web-Feedback.md, GPT-6-LUNA-HANDOFF.md를 읽고 git status를 확인해.

최근 수정은 coordinator CLI 세션 ID 복구야. CODEX_HOME, USERPROFILE\\.codex, .NET user-profile 세션 루트를 모두 검색하고, codex_exec/exec + 동일 CWD + 시간 조건을 만족하는 유일한 새 rollout만 세션으로 채택해. 07:45:10 transcript에서는 PLAN exit 0 뒤 REVIEW 세션 식별에 실패했으며 rollout은 C:\\Users\\ornit\\.codex\\sessions 아래에 실제 생성돼 있었어.

설치 실행 실패는 작은 framework-dependent Release EXE를 복사했던 실수였어. 올바른 self-contained 단일 파일 EXE를 C:\\AI-AGENT\\Worker\\ProjectHub.Worker.exe에 배포했고 게시/설치 해시는 843871D45623C8850090D6B0207C438B29E9D531F04579C5987E2E330A2F492B야. 실행 UI/Explorer 시각 확인 및 PLAN→같은 Sol session REVIEW 실 왕복은 아직 미완료이니 이 두 가지를 먼저 검증하고 실패 원인을 수정해.

현재 수정 사항은 main 브랜치의 unstaged 작업 트리에 남아 있었고, 2026-09-24 git fetch origin은 성공했지만 git pull --rebase는 unstaged 변경 때문에 중단됐어. 원격 main과 HEAD는 fetch 시점에 동일했어. 이 인계 세션에서 아직 commit/push하지 않았어. 먼저 변경 전체를 검토하고 저장소 지침에 맞춰 pull/rebase 및 최신 GPT-Web-Feedback을 확인한 뒤 커밋/푸시 상태를 정리해. 인증정보는 문서·로그에 기록하지 마.
```

## 2026-09-24 추가 확인

- 위 이주용 문구의 `unstaged 작업 트리`와 `pull --rebase 중단` 설명은 작성 당시 상태다. 현재 HEAD와 로컬 `origin/main`은 `49203b6`으로 같았고, 이번 후속 변경 전 작업 트리는 깨끗했다. 이번 작업에서는 fetch/pull/commit/push를 수행하지 않았다.
- 08:01/08:55 설치본 재현에서 Sol PLAN exit 0 후 session ID 연결이 멈춘 확정 원인은 저장된 `threadSessionId`의 빈 문자열이었다. Runner와 관제의 빈 ID를 null로 정규화했다. 이어진 실검증에서 발견한 CLI 진행 이벤트의 `exit_code: null` 파싱도 수정했다. 검증 명령은 최대 두 겹의 shell wrapper를 정확히 대조하며, 출력 요약을 Sol REVIEW에 전달한다. LocalAppData 프로필 루트 추가는 보조 복구 경로다.
- Debug 빌드 경고/오류 0, 전체 38개 테스트 통과, Release 단일 파일 게시 성공. 승인받은 설치본과 게시본 SHA-256 `CFCF23323352A51FA6975CC656F088DEC39E1F50961E6277672A24DF20EAA0CF` 일치. Explorer 실작업에서 Sol PLAN→Luna IMPLEMENT→같은 Sol 세션 REVIEW, 명령 출력 `MODEL_ACCESS_OK`, 검증 PASS, 최종 `DONE · REVIEW ACCEPTED`를 확인했다. 잔여 `11-C-DEPLOY`, `11-C-LIVE-SESSION` 완료. 상세는 CurrentWork.md와 task 11 문서를 참조한다.

## 공식 모델 비교

- GPT-5.6 Luna: 입력 $0.20/1M, 출력 $1.20/1M, 1.05M context, 최대 출력 128K
- GPT-6 Luna: 입력 $0.10/1M, 출력 $0.50/1M, 1.05M context, 최대 출력 128K
- 두 모델 모두 공식 문서상 `medium` reasoning이 기본값이다.

공식 문서:
- https://developers.openai.com/api/docs/models/gpt-5.6-luna
- https://developers.openai.com/api/docs/models/gpt-6-luna
