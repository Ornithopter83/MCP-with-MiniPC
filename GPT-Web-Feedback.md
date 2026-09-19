# GPT Web Feedback

Updated: 2026-09-18

## 최신 확인

```text
9f05190dedaee23f13808d04e2bd995eca68ac59
Add ProjectHub conversation handoff
```

현재 07 배포 패키지 작업과 기존 ProjectHub 정책은 그대로 유지한다. 아래 내용은 **GPTWeb-Hub 브라우저 확장과 Local Worker 연동을 위한 신규 작업지시**다. 기존 Force Restore / Large Data 안정화 항목을 다시 되돌리지 말고, 확장 기능은 ProjectHub 핵심 로직과 분리된 어댑터 계층으로 구현한다.

# GPTWeb-Hub 목표

GPTWeb-Hub는 ChatGPT Web을 ProjectHub Worker와 연결하는 최소 브리지다.

확장 프로그램 자체가 작업 이력, 로그, Git 상태, 재시도 정책의 원본이 되어서는 안 된다.

```text
ProjectHub Worker
    ↕
GPTWeb-Hub Extension
    ↕
ChatGPT Web conversation
```

책임 분리:

```text
Worker
- 작업 큐와 상태의 원본
- Codex CLI 실행/결과 수집
- Git commit/push
- project/conversation binding 영구 저장
- exclusive lock / lease
- 재시작 복구
- 로그/이력

Extension
- Worker 연결
- 현재 ChatGPT conversation 식별
- binding 조회/초기 연결 UI
- Worker가 지정한 Web 작업만 입력
- ChatGPT 응답 생성 종료 감지
- 최종 응답 수집 후 Worker 반환
- Worker가 제공한 상태를 UI에 표시
```

확장 프로그램은 임의 판단으로 다른 작업을 시작하지 않는다.

# 1. 최종 UI

ChatGPT 페이지 우측 빈 공간에 **폭이 작은 고정형 패널**로 배치한다. 별도 로그 뷰, 상세 콘솔, 복잡한 버튼은 두지 않는다.

권장 형태:

```text
┌─────────────────────────────────┐
│ GPTWeb-Hub                      │
│                                 │
│ Project  MCP-with-MiniPC ● READY│
│ Worker   ProjectHub       ● READY│
│ Web      Connected        ● READY│
│ System   정상                     │
├─────────────────────────────────┤
│ CURRENT REQUEST                 │
│                                 │
│ ● WORKER → GPT WEB              │
│ Force Restore 결과 검토 요청     │
│                                 │
│ ChatGPT 응답 생성 중             │
└─────────────────────────────────┘
```

UI 원칙:

- 두께감 있는 외곽 테두리를 사용한다.
- 전체 패널은 간결하게 유지한다.
- 상단을 Header로 사용한다.
- Header에는 Project / Worker / Web 상태를 최대한 위쪽에 몰아서 표시한다.
- 각 상태는 텍스트와 작은 status dot을 함께 사용한다.
- System message는 별도 카드/영역을 만들지 않는다.
- Header 하단에 한 줄만 사용한다.
- 정상 시 짧게 `정상` 또는 빈 값으로 표현한다.
- 경고/오류가 있으면 그 한 줄에 원인만 표시한다.
- 로그는 Extension에 표시하지 않는다. 로그는 Worker 책임이다.
- CURRENT REQUEST는 Header 바로 아래에 붙인다.
- Request 상세 메타데이터를 여러 줄로 늘어놓지 않는다.
- 나머지 공간은 현재 흐름/진행 상태를 짧게 보여주는 데 사용한다.
- 초기 버전에는 수동 제어 버튼을 최소화하거나 두지 않는다.

# 2. Header 상태 의미

Header의 3개 READY는 서로 독립된 조건이다.

## Project

현재 ChatGPT conversation에 ProjectHub project binding이 존재하고 Worker가 해당 project를 정상 인식하면 READY.

## Worker

Extension이 Local Worker와 통신 가능하고 인증/세션이 유효하면 READY.

## Web

현재 페이지가 유효한 ChatGPT conversation이며 저장된 binding과 일치하고, 자동 입출력을 수행할 수 있는 상태이면 READY.

전체 작업 가능 상태는 아래 조건이 모두 만족되어야 한다.

```text
Project READY
AND Worker READY
AND Web READY
AND exclusive task conflict 없음
= 작업 가능
```

하나라도 실패하면 자동 입력을 시작하지 않는다.

예:

```text
System   Worker disconnected
System   Project mismatch
System   Conversation not bound
System   Task already claimed
System   Response timeout
```

# 3. Conversation별 binding

Binding은 **브라우저 탭이 아니라 ChatGPT conversation별**로 관리한다.

```text
Chat A ↔ Project A
Chat B ↔ Project B
Chat C ↔ unbound
```

초기 연결 흐름은 Web → Worker다.

```text
현재 ChatGPT conversation
→ Extension
→ Worker의 관리 project 목록 조회
→ 사용자가 project 선택
→ 현재 conversation과 project binding 저장
```

처음 방문한 conversation에서만 사용자가 연결한다.

연결 전 Web 행은 다음과 같이 동작할 수 있다.

```text
Web   [이곳을 프로젝트와 연결합니다]
```

한 번 binding된 conversation은 Worker에 영구 저장한다.

권장 최소 정보:

```text
conversation_id
conversation_url
project_id
worker_id
bound_at
last_seen
```

`tab_id`는 재시작 시 변경될 수 있으므로 영구 binding key로 사용하지 않는다. 현재 열린 탭을 추적하는 임시 값으로만 사용한다.

브라우저/PC 재시작 후 기존 conversation으로 돌아오면:

```text
conversation_id 식별
→ Worker binding 조회
→ 기존 project 자동 복원
→ 재설정 없이 READY
```

다른 conversation 페이지로 이동하면 그 페이지는 별도 binding을 갖는다. 기존 binding 페이지로 복귀하면 별도 사용자 작업 없이 이어서 사용할 수 있어야 한다.

# 4. CURRENT REQUEST 4상태

CURRENT REQUEST는 Web 전용 progress가 아니라 **Worker와 Web이 공유하는 하나의 task 흐름**을 표시한다.

UI와 내부 상태를 아래 4개로 통일한다.

```text
IDLE
WEB_TO_WORKER
WORKER_TO_WEB
FINISHED
```

표시 의미:

### 1. IDLE

```text
● 작업 없음
현재 처리할 요청 없음
```

### 2. WEB_TO_WORKER

```text
● GPT WEB → WORKER
Web 결과를 Worker에 전달/처리 중
```

Web이 생성한 피드백을 수집해 Worker가 다음 처리(Codex 재작업, Git 처리 등)를 수행하는 방향이다.

### 3. WORKER_TO_WEB

```text
● WORKER → GPT WEB
Worker 요청을 GPT Web에서 처리 중
```

Worker가 지정한 요청만 Extension이 현재 bound conversation에 입력한다.

세부 진행 문구는 한 줄이면 충분하다.

예:

```text
요청 준비 중
ChatGPT에 요청 전달 중
ChatGPT 응답 생성 중
응답 수집 중
Worker로 전달 중
```

### 4. FINISHED

정상 성공, 사용자 승인 필요, 오류를 UI 상태로 세분화하지 않고 모두 FINISHED로 통합한다.

```text
● 작업 종료
정상 완료
```

또는:

```text
● 작업 종료
사용자 승인 필요
```

또는:

```text
● 작업 종료
오류 발생 · 사용자 확인 필요
```

종료 원인은 Worker의 `finish_reason/message`를 한 줄로 표시한다.

# 5. 작업 실행 권한 / 동시 작업 제한

동시 작업 제어의 최종 권한은 Worker가 가진다.

Extension의 DOM 상태만으로 lock을 판단하지 않는다.

Worker task는 최소 다음 개념을 가진다.

```text
task_id
project_id
owner
lease_id
status
request
response
conversation_id
created_at
started_at
completed_at
```

실행 대상이 Web이면 Worker가 명시적으로 owner/lease를 부여한다.

```text
owner = WEB
conversation_id = <bound conversation>
lease_id = <unique>
```

Extension은 다음 조건을 모두 만족할 때만 ChatGPT 입력창을 조작한다.

```text
1. Worker connected
2. project binding matched
3. conversation_id matched
4. owner == WEB
5. valid lease
6. 다른 active task 없음
7. ChatGPT Web가 현재 입력 가능한 상태
```

하나라도 만족하지 않으면 작업하지 않는다.

여러 ChatGPT 탭이 같은 conversation 또는 project를 열고 있어도 하나의 lease만 claim 가능해야 한다. 다른 탭은 읽기/표시만 하고 입력하지 않는다.

# 6. Worker 연결과 복구

연결은 끊기지 않는 것을 전제로 하지 말고 **자동 재연결**을 전제로 한다.

- Worker는 Windows 시작 시 자동 실행 가능하도록 한다.
- Extension service worker가 중단/재시작되어도 binding/task 상태를 잃지 않는다.
- 상태의 원본은 Worker의 persistent storage다.
- Extension은 시작/탭 활성화/페이지 변경 시 Worker에 다시 연결하고 현재 conversation 상태를 조회한다.
- PC/브라우저 재시작 후 사용자가 binding을 다시 설정하게 하지 않는다.
- 초기 PoC는 localhost HTTP polling으로 충분하다.
- 이후 필요하면 WebSocket/SSE로 교체하되 계약은 유지한다.

초기 최소 API 예:

```text
GET  /bridge/status
GET  /bridge/bindings/{conversationId}
POST /bridge/bind
GET  /bridge/task
POST /bridge/task/{id}/claim
POST /bridge/task/{id}/result
POST /bridge/heartbeat
```

실제 endpoint 명칭은 기존 Server/Worker 구조와 충돌하지 않게 조정해도 된다.

# 7. ChatGPT Web 입출력

Extension은 content script에서 현재 conversation DOM만 담당한다.

필요 기능:

```text
- conversation 식별
- 입력 가능 여부 확인
- Worker 요청을 composer에 입력
- send
- 응답 생성 시작 감지
- 응답 생성 종료 감지
- 마지막 assistant 응답 수집
- task_id/lease와 함께 Worker에 반환
```

응답 완료 판단은 단순 timeout이나 DOM 무변화만 사용하지 않는다. 가능한 경우 생성 중 UI/Stop 상태와 assistant message 변화를 함께 사용한다.

ChatGPT DOM selector는 Worker나 Server 코드에 넣지 않는다. UI 변경 대응은 Extension 어댑터 내부로 격리한다.

# 8. Codex / Git 연계 원칙

Codex CLI 작업 실행, 결과 수집, Git commit/push는 Extension이 하지 않는다.

```text
Codex CLI
→ Worker가 stdout/JSON 결과 수집
→ 변경/검증 결과 정리
→ 필요한 Git commit/push
→ Web 검토가 필요하면 WORKER_TO_WEB task 생성
→ Extension이 bound conversation에서 처리
→ Web 결과를 Worker에 반환
→ 필요 시 다음 Codex task
```

Git 연동이 없는 테스트 프로젝트에서도 Extension/Worker 연결 자체는 동작할 수 있어야 한다. Git 작업은 해당 프로젝트의 capability가 있을 때만 수행한다.

# 9. 최소 PoC 검증 순서

기존 ProjectHub 07 작업을 방해하지 않는 작은 테스트 프로젝트/격리된 경로에서 먼저 검증한다.

```text
1. Extension 설치 후 Worker READY 확인
2. 처음 열린 ChatGPT conversation에서 project 선택/bind
3. 페이지 새로고침 후 binding 자동 복원
4. 다른 conversation으로 이동 → unbound 확인
5. 기존 conversation 복귀 → 자동 READY
6. Worker가 WORKER_TO_WEB 테스트 요청 1건 생성
7. Extension이 지정된 conversation에만 요청 입력
8. ChatGPT 응답 완료 감지
9. 응답을 Worker에 반환
10. CURRENT REQUEST가 WEB_TO_WORKER → FINISHED로 전환
11. 같은 task를 다른 탭에서 동시에 실행하지 못하는지 확인
12. 브라우저/PC 재시작 후 별도 재설정 없이 binding 복구 확인
```

PoC 성공 후 Codex CLI 결과와 연결한다.

# 구현 우선순위

첫 구현은 아래 범위만 수행한다.

```text
A. Worker 연결 + conversation binding 영구 복구
B. 최종 Header / System 한 줄 / CURRENT REQUEST 4상태 UI
C. Worker → Web 요청 1건 자동 입력 및 응답 반환
D. exclusive lease로 중복 실행 차단
```

로그 뷰, 상세 설정 페이지, 다중 사용자, 복잡한 알림, 자동 conflict 해결, 임의 shell 실행 등은 이번 범위에 넣지 않는다.


---

# ProjectHub Worker 신규 프로젝트 작업지시

아래 작업은 GPTWeb-Hub와 연결될 **Windows 데스크톱 Worker 애플리케이션**을 만드는 신규 범위다.

사용자가 별도로 전달하는 Worker UI 이미지를 **시각적 기준(source of truth)** 으로 사용한다. 이 문서에서는 기능/구조/동작만 정의하며, 전달된 UI 이미지가 있으면 레이아웃·간격·색·카드 구성·버튼 배치는 그 이미지를 우선한다.

기존 `ProjectHub.Agent`를 Worker로 개조하지 않는다. Agent는 기존 관찰/ProjectHub 관리 역할을 유지한다.

## 1. 프로젝트 형태

새 프로젝트를 솔루션에 추가한다.

권장:

```text
src/
├─ ProjectHub.Core
├─ ProjectHub.Infrastructure
├─ ProjectHub.Server
├─ ProjectHub.Agent
└─ ProjectHub.Worker
```

`ProjectHub.Worker`는 Windows 전용 데스크톱 앱으로 만든다.

초기 구현은 **WPF + .NET 9** 를 우선 사용한다.

이유:

```text
- Windows tray/background 실행 구현이 단순함
- 1024x768 고정 기준 UI 구현이 쉬움
- 기존 .NET 9 솔루션과 통합이 쉬움
- Codex CLI process 제어와 stdout/stderr 수집이 쉬움
- localhost bridge 및 persistent state 구현이 쉬움
```

필요하면 `System.Windows.Forms.NotifyIcon`을 WPF에서 사용해 tray 기능을 구현한다.

## 2. Worker의 역할

Worker는 자동화 흐름의 **실제 시작점이자 상태의 원본**이다.

```text
사용자 Command
      ↓
ProjectHub Worker
      ↓
Codex CLI
      ↓
결과 수집 / Git 처리
      ↓
필요 시 GPT Web 검토 요청
      ↓
GPTWeb-Hub Extension
      ↓
ChatGPT Web
      ↓
Web 결과
      ↓
Worker
      ↓
다음 Codex 작업 또는 종료
```

Extension은 UI adapter이고, task queue / lock / history / recovery의 원본은 Worker다.

## 3. 메인 UI 기능

메인 윈도우는 **1024 x 768 기준**으로 구성한다.

사용자가 제공한 최신 UI 이미지를 그대로 참고하되 기능적으로 아래 영역을 갖는다.

### Header

표시:

```text
ProjectHub Worker (WORKSTATION_NAME)     ● READY
[Codex Model ▼] [Reasoning ▼]            ⚙
```

필수 동작:

- Worker 전체 READY 상태
- workstation 이름
- Codex CLI 현재 모델
- Codex reasoning effort
- Model dropdown 변경 가능
- Reasoning dropdown 변경 가능
- 설정 아이콘은 존재하되 상세 Settings 요구사항은 별도 지시 전까지 최소 placeholder로 둔다.

모델/추론 선택값은 화면 표시용이 아니라 **다음 Codex CLI 실행에 실제 적용**돼야 한다.

## 4. 연결 상태 카드

메인 상단에 다음 상태를 표시한다.

```text
Project
- project name
- local path
- READY / NOT READY

GPT Web
- bound ChatGPT conversation title
- READY / NOT READY

Server
- ProjectHub Server
- READY / NOT READY

System
- overall runtime health
- READY / warning/error
```

GPT Web은 가능하면 URL/ID보다 **conversation title을 우선 표시**한다.

예:

```text
GPT Web
MCP 프로젝트 진척도 확인
● READY
```

conversation ID / URL은 내부 binding용이며 메인 화면에 노출할 필요 없다.

## 5. CURRENT TASK + TASK FLOW 통합

별도 두 카드로 나누지 않는다.

하나의 넓은 task 카드에서 현재 요청과 흐름을 같이 보여준다.

예:

```text
CURRENT TASK

● WORKER → GPT WEB
Force Restore 결과 검토 요청

CODEX       WORKER       GPT WEB
  ○ ---------- ● ---------- ●

ChatGPT 응답 생성 중
Round 2 / 3
00:01:42
```

내부 구현에는 owner/lease가 있어도 UI에는 `Owner:` 같은 기술 필드를 표시하지 않는다.

사용자에게 보이는 상태는 명확한 방향/행동 중심으로 표시한다.

권장 표시 상태:

```text
● 작업 없음
● CODEX 작업 중
● WORKER → GPT WEB
● GPT WEB → WORKER
● 작업 종료
```

세부 문구 예:

```text
Codex CLI 실행 중
Codex 결과 정리 중
GPT Web에 검토 요청 전달 중
ChatGPT 응답 생성 중
Web 결과 수집 중
Web 결과를 Worker에서 처리 중
정상 완료
사용자 승인 필요
오류 발생 · 확인 필요
```

## 6. LAST RESULT

Codex와 Web 결과를 동시에 좌우 분할해서 보여주지 않는다.

상단에 선택 버튼/탭만 둔다.

```text
[ Codex ] [ GPT Web ]
```

선택된 한 쪽의 결과를 **넓은 단일 영역**에 표시한다.

### Codex 탭 예

```text
Build PASS · Test PASS · Commit a5177f8
2026-09-18 14:32

Force Restore 관련 코드 수정 완료.
빌드/테스트 성공.
commit/push 완료.
```

### GPT Web 탭 예

```text
REVISE
2026-09-18 14:35

보호영역 검증 필요.
...
```

초기 선택은 가장 최근에 갱신된 결과 탭으로 한다.

전체 원본 로그를 이 영역에 넣지 않는다. 결과 summary와 필요한 상세만 표시한다.

## 7. COMMAND

Worker가 실제 작업의 시작점이므로 자연어 입력을 반드시 지원한다.

```text
COMMAND

┌────────────────────────────────────────────┐
│ 작업 지시 입력                            │
│                                            │
└────────────────────────────────────────────┘

                         [ Clear ] [ Run Task ]
```

`Run Task`:

```text
1. 새로운 task 생성
2. exclusive execution 확보
3. 현재 선택 Model / Reasoning 값 확정
4. Codex CLI 실행
5. stdout/stderr/JSON event 수집
6. 결과 상태 갱신
```

Command 영역은 raw shell console이 아니다.

사용자는 자연어 작업 지시를 입력하고 Worker가 Codex CLI invocation으로 변환한다.

임의 PowerShell/cmd 명령을 사용자 입력 그대로 shell에 실행하는 기능은 만들지 않는다.

## 8. Codex CLI 실행

기존 테스트에서 확인한 Desktop bundled CLI 탐색 방식을 사용할 수 있다.

Windows에서 기본 자동 탐색 후보:

```powershell
Get-ChildItem "$env:LOCALAPPDATA\OpenAI\Codex\bin" -Filter codex.exe -Recurse -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
```

hash 디렉터리를 하드코딩하지 않는다.

Worker는 최소 다음 정보를 수집한다.

```text
codex executable path
cli version
model
reasoning effort
session/thread id when available
start/end time
exit code
stdout
stderr
token usage when available
result summary
changed files when available
```

가능하면 structured output / JSON event mode를 사용한다.

## 9. 모델 / Reasoning

Header dropdown으로 변경 가능해야 한다.

Worker 내부 설정 예:

```text
CodexModel
CodexReasoningEffort
```

실행 직전에 현재 UI 값을 snapshot하여 task에 저장한다.

실행 중인 task의 모델/추론값은 중간 변경하지 않는다.

사용자가 dropdown을 바꾸면 **다음 task부터 적용**한다.

CLI가 해당 model/reasoning 값을 거부하면 task를 시작하지 말고 System 상태에 명확히 표시한다.

지원 모델 목록을 코드에 영구 고정하지 않는다. 가능한 경우 CLI/config에서 확인하거나 설정 가능한 목록으로 격리한다.

## 10. Background / Tray

Worker는 메인 창이 닫혀도 작업을 계속할 수 있어야 한다.

필수:

```text
- minimize to tray
- window close → 기본적으로 tray로 숨김
- tray 상태에서도 active task 계속 수행
- tray 상태에서도 Extension bridge 응답
- tray icon에서 Open / Pause / Exit 제공
- 명시적 Exit에서만 process 종료
```

PC 재부팅 뒤 자동 실행은 Settings 범위와 연결하되, startup registration 구현은 설정 요구사항 확정 후 마무리해도 된다.

단, 현재 architecture는 재시작 복구를 전제로 작성한다.

## 11. 상태 저장

Worker process memory만 믿지 않는다.

초기 persistent storage는 간단한 JSON 또는 SQLite 중 구현 복잡도가 낮은 쪽을 선택할 수 있다.

최소 저장:

```text
worker identity
known projects
conversation bindings
selected/default model
selected/default reasoning
task queue
active task state
lease
last Codex result
last Web result
round
timestamps
```

secret/token은 평문 state 파일에 저장하지 않는다.

## 12. 단일 실행 / 동시성

한 Worker 인스턴스에서 동시에 여러 automation task를 실행하지 않는다.

초기 버전 정책:

```text
Active task = 최대 1개
```

새 command가 들어왔는데 active task가 있으면:

```text
- 즉시 병렬 실행 금지
- queue 또는 busy 거부 중 하나를 명확하게 선택
- 초기 구현은 queue 1개 이상보다 "현재 작업 종료 후 실행" FIFO가 바람직
```

Codex/Web 실행 대상은 Worker가 가진 lease가 결정한다.

Worker가 Web 차례라고 지정하지 않으면 Extension은 절대 ChatGPT에 입력하지 않는다.

## 13. Git 처리

Git commit/push는 Worker가 담당할 수 있지만 기존 ProjectHub 안전 규칙을 그대로 따른다.

자동으로 다음 상황을 해결하지 않는다.

```text
detached HEAD
merge/rebase in progress
conflict
push reject
dirty 상태의 위험한 pull
```

해당 상황은 task 종료/개입 필요 상태로 올린다.

Git 저장소가 아닌 프로젝트에서도 Worker와 Codex 실행 자체는 가능해야 한다.

따라서 capability를 구분한다.

```text
GitAvailable = true/false
WebBound = true/false
CodexAvailable = true/false
ServerAvailable = true/false
```

## 14. Extension bridge

Worker는 GPTWeb-Hub Extension용 localhost bridge를 제공한다.

Extension용 계약은 기존 GPTWeb-Hub 지시를 따른다.

Worker가 source of truth다.

최소 요구:

```text
status
project list
conversation binding
pending Web task
claim/lease
Web result submit
heartbeat/reconnect
```

초기 구현은 localhost HTTP polling으로 충분하다.

bridge는 loopback에만 bind하고 외부 LAN에 열지 않는다.

## 15. 설정 아이콘

메인 UI에는 Settings 아이콘을 배치한다.

이번 단계에서는 설정창 전체 기능을 임의로 설계하지 않는다.

최소 placeholder 또는 기본 설정창 shell만 만든다.

후속 요구사항에서 다음을 별도로 확정한다.

```text
Windows startup
tray policy
ProjectHub Server URL
Codex CLI path
default model/reasoning
automation rounds
Git behavior
Extension bridge
storage/recovery
```

## 16. 구현 단계

한 번에 전체 자동화 루프를 만들지 않는다.

### Worker-A — 프로젝트/메인 UI skeleton

```text
- ProjectHub.Worker 프로젝트 생성
- WPF 1024x768 main window
- 사용자 제공 UI 이미지 반영
- header/model/reasoning selectors
- Project/Web/Server/System cards
- integrated Current Task/Task Flow
- Last Result Codex/GPT Web toggle
- Command input
- tray/background skeleton
```

이 단계에서는 mock/demo state 사용 가능.

### Worker-B — Codex CLI

```text
- codex.exe auto-discovery
- version/status
- model/reasoning 적용
- command → codex exec
- async process
- stdout/stderr/JSON result
- cancel/exit handling
- Last Result 갱신
```

### Worker-C — persistent state / exclusive task

```text
- task state machine
- one active task
- persistent state
- restart recovery
- queue
- lease
```

### Worker-D — GPTWeb-Hub bridge

```text
- localhost bridge
- project list
- conversation binding
- WORKER_TO_WEB request
- WEB_TO_WORKER result
- reconnect
```

### Worker-E — Git

```text
- Git capability detect
- safe commit/push flow
- failure/approval state
- Codex result + Git result summary
```

각 단계가 독립적으로 빌드/실행 가능해야 한다.

## 17. 첫 작업 범위

**지금은 Worker-A부터 시작한다.**

Codex CLI, Extension, Git 자동화를 동시에 구현하지 않는다.

Worker-A 완료 기준:

```text
1. ProjectHub.sln에 ProjectHub.Worker 포함
2. dotnet build 성공
3. 1024x768 UI 실행
4. 제공 UI 이미지와 주요 구조 일치
5. Model / Reasoning dropdown 동작
6. Result Codex / GPT Web 전환 동작
7. Command 입력 / Clear / Run Task UI 동작
8. Run Task는 아직 mock task transition이어도 됨
9. 창 닫기 시 tray 숨김
10. tray Open / Exit 동작
11. 기존 Server/Agent/Core 동작 회귀 없음
```

검증 후 Worker-B로 진행한다.


---

# 2026-09-19 최신 상태 확인 및 Worker 진행 표시 피드백

## 최신 확인

현재 `main`의 최신 확인 커밋:

```text
7c9cfbf63db77a42b52c4adfb3e9d48afe8e06ce
Allow explicitly approved direct Git sync
```

직전 Worker 구현 커밋:

```text
624e22585eb16aceb66e2059e91c0dfe97bd473b
Implement Worker dashboard UI and icon flow
```

현재 확인된 Worker-A 상태:

```text
- src/ProjectHub.Worker WPF / net9.0-windows 프로젝트 생성 완료
- ProjectHub.sln 포함 완료
- tray 숨김 / Open / Pause / Exit skeleton 구현
- 메인 UI 및 Command / Result toggle mock 구현
- Codex / Worker / GPT Web 단계별 아이콘 적용
- 활성 단계는 컬러, 비활성 단계는 grayscale 처리
- 단계 사이 진행 표시를 >>> 형태로 변경
- >>> 우측 이동 + 점멸 애니메이션 구현
- mock task에서 Codex → Worker → GPT Web 순으로 활성 단계 전환
- Worker-A build/test 통과
- Worker-B(Codex CLI 실제 연결)는 아직 후속 범위
```

따라서 `>>>` 진행 표시는 새로 만드는 작업이 아니라 **현재 구현을 유지·정리하는 요구사항**으로 취급한다.

## Worker Task Flow 진행 표시 최종 원칙

Task Flow는 이미지 파일을 별도 애니메이션 자산으로 만드는 방식보다 WPF 벡터/텍스트 애니메이션으로 유지한다.

기본 표현:

```text
CODEX   >>>   WORKER   >>>   GPT WEB
```

현재 실행 방향에 해당하는 구간만 애니메이션한다.

예:

```text
Codex 작업 결과를 Worker가 받는 중
CODEX   >>>   WORKER   ---   GPT WEB

Worker가 Web에 요청하는 중
CODEX   ---   WORKER   >>>   GPT WEB

Web 결과가 Worker로 돌아오는 중
CODEX   ---   WORKER   <<<   GPT WEB
```

UI 원칙:

```text
- 활성 진행 구간: 파란색 계열 + 순차 점등/이동
- 비활성 구간: 회색 고정
- 완료된 단계: 컬러 아이콘 유지 가능
- 아직 실행되지 않은 단계: grayscale
- 오류/승인 필요는 화살표 애니메이션을 멈추고 종료 상태로 전환
- 애니메이션은 상태 표현용이며 task state 자체의 원본이 되어서는 안 됨
```

추천 애니메이션은 세 개의 `>`가 왼쪽에서 오른쪽으로 순차적으로 강조되는 방식이다.

```text
>..
>>.
>>>
.>>
..>
(repeat)
```

단순 opacity 변화 또는 짧은 translate 효과만 사용하고, CPU를 지속적으로 많이 사용하는 애니메이션은 피한다. Tray/background 상태에서는 메인 창이 숨겨져 있으면 시각 애니메이션을 중단해도 되며 Worker 실제 작업은 계속 진행한다.

## 현재 구현과 맞춰야 할 상태 전환

Worker-A mock의 시각 흐름은 향후 실제 task state와 아래처럼 연결한다.

```text
IDLE
  ↓
CODEX_RUNNING
  ↓
CODEX_TO_WORKER
  ↓
WORKER_TO_WEB
  ↓
WEB_RUNNING
  ↓
WEB_TO_WORKER
  ↓
FINISHED
```

메인 UI에는 내부 상태 이름을 그대로 노출할 필요 없다.

사용자 표시 예:

```text
● CODEX 작업 중
● CODEX → WORKER
● WORKER → GPT WEB
● GPT WEB → WORKER
● 작업 종료
```

Task Flow의 아이콘 활성/비활성 상태와 `>>>` 애니메이션은 반드시 동일 task state에서 파생시킨다. XAML 애니메이션 자체가 별도 상태를 만들지 않는다.

## 현재 정책 변경 반영

최신 `AGENTS.md`는 Git 동작을 더 이상 Explorer CMD에만 한정하지 않고 **사용자의 명시적 승인에 한해 Git commit/push/fetch/pull을 허용**하도록 수정됐다.

따라서 향후 Worker-E에서 Git 연동 시:

```text
- 사용자가 Worker에서 명시적으로 승인한 Git sync는 허용 가능
- detached HEAD / merge-rebase 진행 / conflict / push reject는 자동 해결 금지
- reset / checkout / 원격 shell 자동 실행 금지
```

를 기준으로 한다.

## 다음 구현 범위

Worker-A의 UI/아이콘/flow mock은 현재 단계에서 충분히 구현됐다.

다음은 기존 계획대로 **Worker-B — Codex CLI 실제 연결**을 우선한다.

Worker-B에서 반드시 현재 UI의 mock state를 실제 상태로 치환한다.

```text
- codex.exe 자동 탐색
- 선택된 Model / Reasoning 실제 적용
- async codex exec
- stdout / stderr / JSON event 수집
- CLI model / reasoning / token usage / session id 가능 범위 수집
- Codex 실행 시작 시 CODEX 단계 활성화
- Codex 완료 결과를 Worker가 받는 동안 CODEX >>> WORKER 표시
- Last Result의 Codex 탭을 실제 결과로 갱신
```

GPT Web bridge는 Worker-D 전까지 mock을 유지하며 Worker-B에서 임의로 브라우저 자동화를 함께 구현하지 않는다.


## >>> 진행 표시 시각 강조 수정

기존 `>>>` 구현은 유지하되, **현재 활성 구간이 한눈에 보이도록 더 굵고 크게 표현**한다.

최종 표시 원칙:

```text
CODEX   >>>   WORKER   >>>   GPT WEB
```

활성 구간의 `>>>`는 단순 텍스트가 아니라 **강한 진행 신호**로 보이게 한다.

권장 스타일:

```text
- FontWeight: Bold 또는 ExtraBold
- FontSize: 현재보다 1.3~1.5배 확대
- 활성 색상: 선명한 Blue 계열
- 비활성 색상: 연한 Gray
- 문자 간격을 너무 벌리지 말고 하나의 덩어리처럼 보이게
- 아이콘보다 작게 숨지 않도록 충분한 폭 확보
- 배경 카드 안에서 수직 중앙 정렬
```

예:

```text
CODEX    >>>    WORKER    ---    GPT WEB
         ^^^
         현재 활성 구간은 굵고 선명하게
```

애니메이션은 이전 제안대로 **세 개의 화살표가 왼쪽에서 오른쪽으로 순차 점등되는 방식**으로 한다.

권장 프레임:

```text
>..
>>.
>>>
.>>
..>
(repeat)
```

다만 실제 UI에서는 점(`.`)을 표시하지 말고 opacity 차이로 표현한다.

즉 시각적으로는:

```text
>  >  >
↑
첫 번째 강조

>  >  >
   ↑
두 번째 강조

>  >  >
      ↑
세 번째 강조
```

으로 보이게 한다.

추가 원칙:

```text
- 활성 화살표는 100% opacity
- 나머지 화살표는 약 25~35% opacity
- 각 단계 간격은 약 120~180ms
- 전체 반복은 빠르되 조급해 보이지 않게 약 0.7~1.0초 주기
- 필요하면 2~4px 정도의 짧은 X축 이동을 함께 사용
- Glow/Shadow는 약하게 사용 가능하나 과도한 네온 효과는 금지
- 완료 또는 대기 상태에서는 애니메이션 정지
- 창이 tray/background로 숨겨지면 시각 애니메이션 정지 가능
```

방향 전환 시 문자 방향도 실제 흐름과 일치시킨다.

```text
CODEX → WORKER
CODEX   >>>   WORKER

WORKER → GPT WEB
WORKER   >>>   GPT WEB

GPT WEB → WORKER
WORKER   <<<   GPT WEB
```

중요: `>>>`는 작은 보조 장식이 아니라 **Task Flow에서 가장 먼저 눈에 들어오는 진행 상태 표시**여야 한다. 현재 단계 아이콘 사이에서 충분한 크기와 굵기를 확보한다.


---

# 2026-09-19 GPTWeb-Hub 기능 구체화 — Settings / 연결 설정

## 최신 상태 확인

현재 `main` 최신 확인 커밋:

```text
631c5272f054eca0da09f2fdd9922e2e35bbc379
Finalize Worker bridge and GPTWeb-Hub status UI
```

현재 구현 기준:

```text
- Worker loopback bridge: http://127.0.0.1:43821
- Extension polling: 1.5초
- Project / Worker 값은 bridge에서 실제 값 수신 시 표시
- bridge 단절 시 Project / Worker 마지막 확인값은 유지
- Status만 Disconnected로 전환
- CURRENT REQUEST는 아직 bridge task 표시 중심
- ChatGPT DOM 자동입력 / 응답 수집은 후속 범위
- 톱니바퀴 버튼은 현재 mock 상태 순환 용도
```

사용자가 제공한 현재 UI 시안을 기준으로, 다음 작업은 **톱니바퀴 mock 동작을 제거하고 실제 Settings 창으로 교체**하는 것이다.

## 1. 톱니바퀴 동작 변경

현재:

```text
⚙ 클릭
→ IDLE / WORKER_TO_WEB / WEB_TO_WORKER / FINISHED 상태 수동 순환
```

이 동작은 제거한다.

변경:

```text
⚙ 클릭
→ GPTWeb-Hub Settings 표시
```

상태 전환은 더 이상 설정 버튼이나 사용자의 수동 클릭으로 만들지 않는다.

```text
CURRENT REQUEST 상태
= Worker bridge의 실제 task 상태 + 이후 ChatGPT DOM 상태에서만 결정
```

## 2. Settings UI

메인 패널과 같은 디자인 언어를 사용한 작은 modal/popover 형태로 만든다.

권장 형태:

```text
┌────────────────────────────────────┐
│ GPTWeb-Hub Settings             × │
├────────────────────────────────────┤
│ WORKER BRIDGE                      │
│                                    │
│ Host     [ 127.0.0.1             ] │
│ Port     [ 43821                 ] │
│ BasePath [ /bridge              ] │
│                                    │
│ Worker   [ 자동 감지 / optional ] │
│                                    │
│ [ Test Connection ]                │
│                                    │
│ Status   ● Connected               │
├────────────────────────────────────┤
│                         [Cancel] [Save] │
└────────────────────────────────────┘
```

메인 UI보다 복잡하게 만들지 않는다.

## 3. 설정값

초기 버전에서 실제로 동작해야 할 설정:

```text
bridgeHost
bridgePort
bridgeBasePath
```

기본값:

```text
bridgeHost     = 127.0.0.1
bridgePort     = 43821
bridgeBasePath = /bridge
```

최종 base URL:

```text
http://{bridgeHost}:{bridgePort}{bridgeBasePath}
```

예:

```text
http://127.0.0.1:43821/bridge
```

현재 구현의 하드코딩된 `http://127.0.0.1:43821` 사용부는 전부 이 설정값에서 생성하도록 변경한다.

## 4. Host 보안 정책

초기 버전에서는 기본적으로 loopback만 허용한다.

허용:

```text
127.0.0.1
localhost
::1
```

LAN IP나 외부 URL은 이번 범위에서 허용하지 않는다.

이유:

```text
Worker bridge는 현재 인증 없는 로컬 bridge 설계이며 외부 노출을 전제로 하지 않음
```

Settings에서 외부 주소가 입력되면 Save 전에 validation으로 거부한다.

## 5. Worker Path

사용자가 말하는 "경로"는 별도 필드로 제공할 수 있으나, **현재 Extension 기능에는 필수값이 아니다.**

권장 필드:

```text
Worker executable path
C:\...\ProjectHub.Worker.exe
```

용도:

```text
- 사용자에게 설치 위치 기록
- 향후 Worker 자동 실행 / Native Messaging / custom protocol 연동 준비
```

현재 단계에서는 브라우저 확장이 이 경로의 EXE를 직접 실행하지 않는다.

따라서:

```text
Worker Path = optional
Bridge Host/Port = 실제 연결에 사용
```

으로 명확히 구분한다.

경로 선택 UI는 이번 단계에서는 text input + 저장만 허용해도 충분하다.

## 6. 설정 저장

Extension 설정은 `chrome.storage.local`에 저장한다.

예:

```json
{
  "bridgeHost": "127.0.0.1",
  "bridgePort": 43821,
  "bridgeBasePath": "/bridge",
  "workerPath": ""
}
```

페이지별 project/conversation binding과 bridge 접속 설정은 분리한다.

```text
Extension global settings
- bridge host
- bridge port
- base path
- optional worker path

Conversation binding
- conversation_id
- project_id
- binding state
```

설정 저장 후 페이지 새로고침 없이 polling endpoint가 즉시 새 설정으로 전환되게 한다.

## 7. Test Connection

Settings에 `Test Connection` 버튼을 둔다.

테스트:

```text
GET {baseUrl}/status
```

성공 조건:

```text
HTTP 200
bridge == ready
loopback == true
```

표시:

```text
● Connected
ProjectHub Worker
127.0.0.1:43821
```

실패:

```text
● Disconnected
Worker bridge에 연결할 수 없습니다.
```

에러 원인은 한 줄만 보여준다.

예:

```text
Connection refused
Invalid port
Invalid host
Bridge response invalid
```

전체 stack trace는 Extension UI에 표시하지 않는다.

## 8. Save / Cancel

`Save`:

```text
1. 입력값 validation
2. chrome.storage.local 저장
3. bridge client base URL 재생성
4. 즉시 refreshBridge()
5. Settings 닫기
```

`Cancel`:

```text
현재 입력 변경 폐기
Settings 닫기
```

Save 후 연결 실패 시 메인 패널:

```text
Status  Disconnected
System  Worker bridge에 연결할 수 없습니다.
```

를 즉시 표시한다.

## 9. 메인 패널 상태 의미 유지

현재 UI의 세 행 구조는 유지한다.

```text
Project   MCP-with-MiniPC
Worker    ProjectHub Worker
Status    Connected / Disconnected
```

중요:

```text
Project
= 마지막으로 Worker가 실제 반환한 project

Worker
= 마지막으로 Worker가 실제 반환한 worker name

Status
= 현재 bridge 통신 상태
```

bridge가 잠시 끊겨도 Project / Worker를 즉시 `—`로 되돌리지 않는다.

```text
Project   MCP-with-MiniPC        (last known)
Worker    ProjectHub Worker      (last known)
Status    Disconnected
```

현재 구현 방향을 유지한다.

## 10. CURRENT REQUEST mock 제거

Settings 작업과 함께 톱니바퀴 기반 mock state 순환 코드를 제거한다.

preview용 수동 상태 변경은 production extension에서 제거한다.

대신:

```text
bridge task 없음
→ 작업 없음

bridge pending/claimed task 존재
→ Worker → GPT Web

Web 응답을 Worker로 제출하는 단계
→ GPT Web → Worker

task terminal state
→ 작업 종료
```

로 실제 상태만 사용한다.

아직 ChatGPT DOM 자동입력이 구현되지 않았으므로, Worker task가 들어왔을 때:

```text
WORKER → GPT WEB
요청 대기 / Web 자동처리 미연결
```

처럼 사실대로 표시한다. 실제 응답 생성 중이라고 허위 표시하지 않는다.

## 11. 연결 설정과 Conversation Binding 구분

Settings는 Worker bridge 접속 설정이다.

프로젝트 binding은 현재 ChatGPT conversation별 동작으로 유지한다.

```text
⚙ Settings
= Worker bridge에 어떻게 접속하는가

Project binding
= 이 ChatGPT conversation을 어떤 ProjectHub project와 연결하는가
```

두 기능을 한 화면에 섞지 않는다.

향후 binding UI는 메인 Project 행 또는 별도 Connect 동작으로 처리한다.

## 12. 구현 우선순위

이번 작업은 아래까지만 수행한다.

```text
A. gear mock state cycling 제거
B. Settings modal/popover 구현
C. Host / Port / BasePath / optional WorkerPath
D. chrome.storage.local 저장/복원
E. Test Connection
F. polling URL을 저장 설정 기반으로 변경
G. 잘못된 설정 / disconnected 상태 표시
H. page reload 없이 Save 즉시 반영
```

아직 구현하지 않는다:

```text
- Worker EXE 자동 실행
- Native Messaging
- LAN/외부 Worker 연결
- ChatGPT DOM 자동 입력
- 이미지 첨부
- Web 응답 자동 수집
```

## 13. 검증

```text
1. extension reload
2. ChatGPT 페이지 refresh
3. ⚙ 클릭 → Settings 표시
4. 기본값 127.0.0.1 / 43821 / /bridge 확인
5. Test Connection → Connected
6. 잘못된 port 입력 → Disconnected 확인
7. Cancel → 기존 설정 유지
8. 올바른 port 저장 → 즉시 Status Connected 복구
9. 페이지 새로고침 → 저장값 유지
10. 브라우저 재시작 후에도 저장값 유지
11. gear 클릭이 CURRENT REQUEST 상태를 변경하지 않는지 확인
12. node --check extension/gptweb-hub/content.js 통과
```

이 작업 완료 후 다음 기능은 **conversation binding UX 구체화 → ChatGPT DOM 자동입력/응답수집** 순서로 진행한다.


---

# 2026-09-19 GPTWeb-Hub TASK 영역 통합 피드백

현재 GPTWeb-Hub UI의 `CURRENT REQUEST`와 `RESULT MESSAGE`를 하나의 작업 영역으로 통합한다.

## 1. 영역 명칭 통합

기존:

```text
CURRENT REQUEST
RESULT MESSAGE
```

변경:

```text
TASK
```

TASK 영역은 현재 작업의 상태, Worker가 보낸 요청 내용, GPT Web 응답 내용을 모두 표시하는 단일 영역이다.

## 2. 수동 테스트 입력 UI 제거

이제 자동화 연결 검증이 진행됐으므로 아래 수동 테스트용 UI는 제거한다.

```text
- 작업 지시 textarea
- 파일 드래그 영역
- 전송 버튼
- 수동 테스트 전송 로직
```

GPTWeb-Hub는 더 이상 사용자가 직접 프롬프트를 입력하는 도구가 아니다.

작업은 Worker에서 생성하고 Extension은 Worker task를 받아 ChatGPT Web에 전달하는 역할만 수행한다.

## 3. Worker가 보낸 원문 표시

TASK 영역에는 Worker가 전달한 요청 내용을 반드시 표시한다.

예:

```text
TASK

● WORKER → GPT WEB
요청 전달 완료

Worker Message
Force Restore 보호영역 검증 결과를 검토하고 문제점만 정리해줘.
```

Worker prompt가 길 경우 해당 메시지 영역에도 고정 높이 + 내부 스크롤을 적용한다.

## 4. 전달 직후 상태 변경

Worker 또는 GPT Web로 내용을 전달한 직후 UI 상태를 즉시 변경한다.

### Worker → GPT Web

Worker task를 ChatGPT 입력창에 실제로 넣고 Send를 실행한 직후:

```text
● WORKER → GPT WEB
메시지 전달 완료 · GPT Web 응답 대기
```

으로 바꾼다.

단순히 Worker task를 조회한 시점에는 "전달 완료"로 표시하지 않는다.

### GPT Web → Worker

GPT Web의 최종 응답을 Worker result API로 POST한 직후:

```text
● GPT WEB → WORKER
응답 전달 완료 · Worker 처리 대기
```

으로 바꾼다.

즉 UI 상태는 실제 I/O 이벤트와 정확히 일치해야 한다.

## 5. 메시지 출력 완료 시 상태 변경

각 실행 대상이 메시지를 완전히 출력한 시점에도 상태를 명확히 변경한다.

### GPT Web 응답 생성 완료

최종 assistant 메시지의 스트리밍이 완전히 끝난 뒤:

```text
● GPT WEB → WORKER
GPT Web 응답 완료
```

로 바꾸고, 같은 TASK 영역 안에 최종 응답을 표시한다.

예:

```text
Web Response
보호영역 제외 규칙은 정상이나 ...
```

그 다음 Worker result endpoint로 실제 전송한다.

### Worker 응답 완료

Worker가 Web 결과를 수신해 후속 처리를 완료하고 terminal 상태를 반환하면:

```text
● 작업 종료
정상 완료
```

또는:

```text
● 작업 종료
사용자 확인 필요
```

또는:

```text
● 작업 종료
오류 발생 · 확인 필요
```

로 변경한다.

중요: Worker의 terminal 상태 확인 전에는 임의로 FINISHED를 표시하지 않는다.

## 6. TASK 영역 권장 구조

예:

```text
TASK

● WORKER → GPT WEB
GPT Web 응답 생성 중

Worker Message
Force Restore 보호영역 검증 결과를 검토해줘.

────────────────────────

Web Response
보호영역 검증 결과 ...
```

작업 단계에 따라 아직 없는 부분은 숨긴다.

예:

```text
Worker task 수신 직후
→ Worker Message만 표시

GPT Web 응답 완료 후
→ Worker Message + Web Response 표시

Worker terminal 완료 후
→ 상태를 작업 종료로 변경
```

## 7. 고정 높이 / 내부 스크롤

TASK 영역 때문에 GPTWeb-Hub 패널 전체 높이가 계속 늘어나면 안 된다.

필수 원칙:

```text
- TASK 카드 전체 최대 높이 고정
- Worker Message 영역 max-height 지정
- Web Response 영역 max-height 지정
- 긴 메시지는 각 영역 내부 overflow-y: auto
- 브라우저 전체 패널 높이는 유지
- 긴 응답 때문에 패널이 아래로 계속 늘어나지 않음
```

권장:

```css
.task-message {
  max-height: 140px;
  overflow-y: auto;
}

.task-response {
  max-height: 220px;
  overflow-y: auto;
}
```

실제 수치는 현재 패널 높이에 맞춰 조정 가능하다.

응답이 짧으면 scrollbar는 보이지 않고, 길 때만 자동 생성한다.

## 8. 상태 전환 예시

전체 1회 왕복:

```text
IDLE
작업 없음

↓ Worker task 수신

WORKER_TO_WEB
Worker 요청 대기

↓ ChatGPT 입력 + Send 실제 실행

WORKER_TO_WEB
메시지 전달 완료 · GPT Web 응답 대기

↓ assistant streaming 시작

WORKER_TO_WEB
GPT Web 응답 생성 중

↓ assistant streaming 완전 종료

WEB_TO_WORKER
GPT Web 응답 완료

↓ Worker result POST 성공

WEB_TO_WORKER
응답 전달 완료 · Worker 처리 대기

↓ Worker terminal 상태 확인

FINISHED
작업 종료
```

이 상태 전환은 실제 이벤트 기반으로 구현한다.

## 9. 응답 완료 판정

단순 MutationObserver에서 텍스트가 한 번 바뀌었다고 완료 처리하지 않는다.

최종 응답 완료는 가능한 경우 다음을 함께 확인한다.

```text
- assistant message 존재
- 생성 중/Stop UI 종료
- 일정 시간 동안 assistant message 내용 변화 없음
```

권장 안정화 시간:

```text
500~1000ms
```

이미지 생성처럼 별도 generation UI가 존재하는 경우 후속 범위에서 별도 완료 조건을 추가한다.

## 10. 구현 범위

이번 변경에서 수행:

```text
A. CURRENT REQUEST + RESULT MESSAGE → TASK 통합
B. textarea / drag & drop / 전송 버튼 제거
C. Worker Message 표시
D. Web Response 표시
E. 실제 send/result POST 직후 상태 변경
F. GPT Web streaming 종료 시 상태 변경
G. Worker terminal 상태 수신 시 FINISHED
H. 긴 메시지 내부 scrollbar
I. 패널 전체 높이 증가 방지
```

이번 변경에서 제외:

```text
- 이미지 생성 결과 asset 수집
- 복수 Web task 병렬화
- 사용자 수동 prompt 입력 복구
```

## 11. 검증

```text
1. Worker task 1건 생성
2. TASK 영역에 Worker Message 표시 확인
3. ChatGPT send 직후 상태가 응답 대기로 변경되는지 확인
4. streaming 중 "응답 생성 중" 표시 확인
5. streaming 종료 후 Web Response가 TASK 영역에 표시되는지 확인
6. Worker result POST 후 "Worker 처리 대기" 표시 확인
7. Worker terminal 상태 후 "작업 종료" 표시 확인
8. 긴 Worker Message에서 내부 scrollbar 확인
9. 긴 Web Response에서 내부 scrollbar 확인
10. 긴 응답에도 패널 전체 높이가 증가하지 않는지 확인
11. textarea / 파일 드롭 / 전송 버튼이 제거됐는지 확인
12. node --check extension/gptweb-hub/content.js 통과
```


---

# 2026-09-19 GPTWeb-Hub 통합 자동 왕복 구현 지시

이 항목은 직전 TASK UI 통합 피드백과 이전 Web↔Worker 자동화 제안을 **하나의 구현 범위로 합친 최종 지시**다.

이번에는 기능을 잘게 나눠 중간중간 멈추지 말고, 아래 항목을 **한 번에 구현한 뒤 마지막에 통합 검증 1회를 수행**한다.

기존 ProjectHub 정책과 충돌하지 않는 범위에서 이 작업은 하나의 GPTWeb-Hub 통합 기능 작업으로 취급한다.

## 1. 최종 목표

이번 작업의 성공 기준은 단순 UI 변경이 아니다.

반드시 아래 1회 왕복이 실제로 끝까지 동작해야 한다.

```text
Worker task 생성
→ 연결된 ChatGPT conversation에서 Extension이 task 수신
→ Worker Message 표시
→ ChatGPT 입력창에 자동 입력
→ 필요한 attachment 자동 첨부
→ Send
→ GPT Web 응답 생성 감지
→ 최종 응답 완료 판정
→ TASK 영역에 Web Response 표시
→ POST /bridge/task/{taskId}/result
→ Worker가 결과 수신
→ Worker terminal 상태
→ Extension FINISHED 표시
```

즉 이번 작업이 끝나면 최소한 **Worker → GPT Web → Worker 1회 완전 왕복 E2E**가 가능해야 한다.

## 2. TASK 단일 영역

기존:

```text
CURRENT REQUEST
RESULT MESSAGE
```

를 제거하고:

```text
TASK
```

하나로 통합한다.

TASK 영역은 아래 정보를 한 카드 안에서 표시한다.

```text
- 현재 상태
- Worker Message
- Web Response
- task id
- 필요한 최소 메타데이터
```

수동 입력 테스트용 UI는 삭제한다.

삭제 대상:

```text
- 작업 지시 textarea
- 파일 drag/drop 테스트 영역
- 수동 전송 버튼
- 테스트 전송 전용 코드
- Extension 사용자가 직접 prompt를 입력하는 경로
```

GPTWeb-Hub는 이제 **Worker가 만든 task를 처리하는 자동 bridge UI**다.

## 3. 전송 엔진 단일화

현재까지 구현한 ChatGPT composer 탐색, text 삽입, file attach, Send 클릭 로직을 재사용한다.

Worker 자동 task용 별도 전송 로직을 새로 복제하지 않는다.

내부적으로 하나의 공통 함수 흐름을 사용한다.

예:

```text
sendToChatGPT(prompt, attachments)
```

이 공통 경로에서:

```text
1. composer 탐색
2. prompt 입력
3. attachment 첨부
4. send button 탐색
5. 실제 click
6. 성공/실패 반환
```

을 처리한다.

## 4. Worker PENDING task 자동 처리

현재 conversation ID로 조회한 Worker task가 `PENDING`이고 아래 조건이 모두 맞을 때만 자동 실행한다.

```text
- bridge connected
- current conversation id 존재
- conversation binding 일치
- task conversation id 일치
- active Web task 없음
- task status == PENDING
- ChatGPT composer 사용 가능
```

조건이 맞으면:

```text
PENDING
→ claim
→ Worker Message 표시
→ ChatGPT 자동 전송
```

순서로 진행한다.

단순히 task를 polling에서 발견했다는 이유만으로 먼저 send하면 안 된다.

claim 성공 이후에만 실제 ChatGPT 입력을 수행한다.

## 5. 한 conversation당 active Web task 1개

반드시 다음 규칙을 적용한다.

```text
conversation_id 하나당 active Web task 최대 1개
```

다음 상태 중 하나라도 존재하면 새 task를 자동 전송하지 않는다.

```text
CLAIMED
SENT_TO_WEB
WEB_GENERATING
WEB_RESULT_READY
RESULT_SUBMITTING
```

새로운 PENDING task는 Worker queue에서 대기시킨다.

동일 task를 여러 탭에서 동시에 처리하지 않는다.

Worker의 claim/lease가 실행 권한의 최종 기준이다.

## 6. 페이지 새로고침 / SPA 이동 중복 전송 방지

ChatGPT 페이지 새로고침 또는 conversation 간 SPA 이동이 발생해도 이미 claim된 task를 다시 보내지 않는다.

복구 흐름:

```text
페이지 로드 / URL 변경
→ conversation_id 확인
→ Worker binding 조회
→ 해당 conversation의 최신 task 조회
→ task status 확인
→ UI 상태 복원
```

중요:

```text
PENDING
→ 자동 처리 가능

CLAIMED 이후
→ 기존 진행 상태 복구
→ 같은 prompt 재전송 금지
```

task id를 Extension 내부 현재 task와 비교하고 중복 send 방지 guard를 둔다.

## 7. Worker Message 표시

Worker task의 실제 prompt를 TASK 영역에 표시한다.

예:

```text
TASK

● WORKER → GPT WEB
요청 전달 준비

Worker Message
Force Restore 보호영역 검증 결과를 검토하고 문제점만 정리해줘.
```

긴 prompt는 내부 scrollbar 사용.

Worker Message는 Extension이 자체 요약하거나 변형하지 않는다.

실제 Worker task prompt를 표시한다.

## 8. 실제 전달 직후 상태 변경

상태 표시는 실제 이벤트와 정확히 맞아야 한다.

### Worker → GPT Web

ChatGPT Send 버튼의 실제 click이 성공한 직후:

```text
● WORKER → GPT WEB
메시지 전달 완료 · GPT Web 응답 대기
```

로 즉시 변경한다.

task를 발견했거나 claim만 했을 때는 `전달 완료`로 표시하지 않는다.

### GPT Web → Worker

Worker result endpoint에 최종 응답 POST가 성공한 직후:

```text
● GPT WEB → WORKER
응답 전달 완료 · Worker 처리 대기
```

로 즉시 변경한다.

## 9. GPT Web streaming 상태 감지

ChatGPT assistant 메시지가 생성되기 시작하면:

```text
● WORKER → GPT WEB
GPT Web 응답 생성 중
```

으로 표시한다.

단순히 MutationObserver 이벤트 1회 발생만으로 완료 처리하지 않는다.

## 10. GPT Web 최종 응답 완료 판정

최종 완료는 가능한 범위에서 아래 조건을 조합한다.

```text
1. 현재 task send 이후 새 assistant message 존재
2. Stop/Generating 계열 UI가 더 이상 active하지 않음
3. assistant message 본문이 안정화 시간 동안 변하지 않음
```

권장 안정화 시간:

```text
500~1000ms
```

완료되면:

```text
● GPT WEB → WORKER
GPT Web 응답 완료
```

로 변경하고 TASK 영역에 최종 Web Response를 출력한다.

중간 streaming text는 Worker 최종 result로 보내지 않는다.

## 11. Web Response 표시

TASK 안에 Worker Message와 Web Response를 함께 표시한다.

예:

```text
TASK

● GPT WEB → WORKER
GPT Web 응답 완료

Worker Message
Force Restore 보호영역 검증 결과를 검토해줘.

────────────────────────

Web Response
보호영역 제외 규칙은 정상이며 ...
```

아직 응답이 없을 때 Web Response 영역은 숨길 수 있다.

## 12. Worker result 실제 반환

GPT Web 최종 응답 완료 후 반드시 현재 task id에 대해:

```text
POST /bridge/task/{taskId}/result
```

를 호출한다.

최소 result payload에는:

```text
task_id
conversation_id
response_text
result_type
completed_at
```

를 전달한다.

현재 서버 계약과 맞게 실제 schema를 조정하되 의미는 유지한다.

POST 성공 전에는 Worker 전달 완료로 표시하지 않는다.

## 13. Worker terminal 상태 확인

result POST 성공 후 Extension이 임의로 즉시 FINISHED 처리하지 않는다.

Worker가 후속 처리를 끝내고 terminal 상태를 반환할 때까지:

```text
● GPT WEB → WORKER
응답 전달 완료 · Worker 처리 대기
```

를 유지한다.

Worker terminal 상태 예:

```text
COMPLETED
FAILED
WAITING_USER
CANCELLED
```

UI에서는 단순화해서:

```text
● 작업 종료
정상 완료

● 작업 종료
사용자 확인 필요

● 작업 종료
오류 발생 · 확인 필요
```

로 표시한다.

## 14. attachment 자동 전달

수동 drag/drop UI는 삭제하지만 attachment 기능 자체는 제거하지 않는다.

Worker task에 attachment가 있으면 Extension이 자동으로 ChatGPT에 첨부한다.

권장 구조:

```text
attachments[]
- id
- fileName
- mimeType
- size
- source
- downloadUrl 또는 bridge asset endpoint
```

Extension 흐름:

```text
Worker task
→ attachment metadata 확인
→ loopback bridge에서 blob 다운로드
→ File 객체 생성
→ 기존 attachFilesToChat() 경로 사용
→ prompt와 함께 Send
```

텍스트 전용 task면 attachment 단계는 건너뛴다.

## 15. result type 구분

향후 이미지 생성 등을 고려해 result를 최소 다음 타입으로 확장 가능하게 설계한다.

```text
TEXT_RESULT
IMAGE_RESULT
FILE_RESULT
```

이번 구현의 필수 완료 범위는 `TEXT_RESULT`다.

다만 schema나 상태 구조가 향후 IMAGE/FILE result를 막지 않게 작성한다.

이미지 생성 결과 asset 수집 자체는 이번 필수 검증에서 제외해도 된다.

## 16. 이미지 작업의 직렬 처리 전제

ChatGPT Web 한 conversation에서는 이미지 생성과 다른 요청을 동시에 실행하지 않는다.

향후 이미지 task가 붙더라도:

```text
WEB_IMAGE
→ 생성 완료
→ asset 회수
→ 다음 WEB_TEXT 또는 다음 task
```

순서로 직렬 처리한다.

같은 conversation에 동시에 여러 Web request를 넣지 않는다.

## 17. 긴 메시지 UI

TASK 카드 때문에 GPTWeb-Hub 전체 패널 높이가 늘어나면 안 된다.

필수:

```text
Worker Message
- max-height 고정
- overflow-y: auto

Web Response
- max-height 고정
- overflow-y: auto

TASK 전체
- 전체 최대 높이 제한
- 패널 전체 높이 유지
```

예:

```css
.task-message {
  max-height: 140px;
  overflow-y: auto;
}

.task-response {
  max-height: 220px;
  overflow-y: auto;
}
```

짧은 메시지에는 scrollbar가 보이지 않고 긴 경우에만 표시한다.

## 18. 상태 머신 최종 기준

이번 구현에서 Extension UI 상태는 최소 다음 흐름을 따른다.

```text
IDLE
↓
PENDING
↓
CLAIMED
↓
SENT_TO_WEB
↓
WEB_GENERATING
↓
WEB_RESULT_READY
↓
RESULT_SUBMITTING
↓
WAITING_WORKER
↓
FINISHED
```

사용자 표시 문구는 단순화한다.

```text
작업 없음
Worker 요청 대기
메시지 전달 완료 · GPT Web 응답 대기
GPT Web 응답 생성 중
GPT Web 응답 완료
GPT Web → Worker 전달 중
응답 전달 완료 · Worker 처리 대기
작업 종료
```

내부 enum 명칭은 현재 코드 구조에 맞춰 달라도 되지만 의미와 전환 순서는 유지한다.

## 19. 실패 처리

다음 실패를 각각 terminal 또는 retry 가능 상태로 구분한다.

```text
- composer 미검출
- send button 미검출
- attachment download 실패
- file attach 실패
- bridge claim 실패
- result POST 실패
- ChatGPT response timeout
- conversation mismatch
- bridge disconnected
```

같은 task를 무조건 다시 send하지 않는다.

특히 send 성공 여부가 불확실한 경우 duplicate prompt 전송 방지가 더 중요하다.

사용자 개입이 필요한 경우 FINISHED/사용자 확인 필요로 표시하거나 Worker가 WAITING_USER 상태를 반환하도록 한다.

## 20. 이번에는 한 번에 구현

이번 지시는 기능별로 하나씩 구현 후 중간 검증하고 멈추는 방식으로 진행하지 않는다.

다음 범위를 한 번에 묶어서 구현한다.

```text
A. TASK UI 통합
B. 수동 입력/drag/drop/send UI 제거
C. Worker Message 표시
D. Worker PENDING 자동 claim
E. 공통 ChatGPT 전송 엔진으로 자동 send
F. Worker attachment 자동 첨부 경로
G. send 직후 상태 변경
H. streaming 시작/종료 판정
I. Web Response 표시
J. result POST
K. Worker terminal 상태 반영
L. conversation별 single active task
M. 새로고침/SPA 이동 중복 send 방지
N. 긴 메시지 scrollbar / 고정 패널 높이
```

중간 단계마다 별도 사용자 확인을 요구하지 않는다.

구현을 모두 끝낸 뒤 한 번에 통합 검증한다.

단, 실제 destructive Git/Force Restore/NAS delete 같은 별도 위험 작업을 자동 실행하라는 의미는 아니다.

## 21. 최종 통합 검증 1회

구현 완료 후 아래 시나리오를 **한 번에 연속으로 검증**한다.

```text
1. Worker와 Extension 실행
2. 기존 conversation binding 복원 확인
3. Worker에서 테스트 task 1건 생성
4. Extension TASK에 Worker Message 표시
5. task claim 성공
6. ChatGPT에 자동 prompt 입력
7. 필요한 경우 test attachment 자동 첨부
8. 실제 Send
9. Send 직후 상태 변경 확인
10. assistant streaming 시작 상태 확인
11. assistant 최종 출력 완료 판정
12. Web Response TASK 영역 표시
13. POST /bridge/task/{taskId}/result 성공
14. 응답 전달 완료 · Worker 처리 대기 표시
15. Worker terminal 상태 수신
16. FINISHED 표시
17. Worker UI Last Result에 Web 결과 반영 확인
18. 페이지 새로고침 후 동일 task 재전송되지 않는지 확인
19. 다른 conversation으로 이동 시 다른 task가 섞이지 않는지 확인
20. 긴 Worker Message / Web Response scrollbar 확인
21. 패널 전체 높이가 늘어나지 않는지 확인
22. 수동 textarea / drag/drop / send 버튼 제거 확인
23. node --check extension/gptweb-hub/content.js
24. dotnet build src/ProjectHub.Worker/ProjectHub.Worker.csproj --no-restore
25. git diff --check
```

필요하면 테스트 task prompt는 짧은 Hello World 수준으로 사용한다.

attachment 검증은 현재 Worker asset endpoint가 아직 없으면 구현 가능한 최소 bridge asset endpoint를 함께 추가해 실제 1회 첨부까지 확인한다.

최종 보고에는 중간 진행 로그를 길게 나열하지 말고 아래만 요약한다.

```text
- 구현 완료 항목
- 실제 E2E 결과
- 실패/제약이 있으면 정확한 원인
- 변경 파일
- build/check 결과
- 남은 후속 범위
```

이번 작업의 완료 기준은 **UI만 바뀐 것**이 아니라 **Worker → GPT Web → Worker의 실제 1회 왕복이 끝까지 성공하는 것**이다.

---

# 2026-09-19 GPTWeb-Hub Web-Controlled Loop Protocol v1

## 목적

현재 구현된 1회 왕복:

```text
Codex
→ Worker
→ GPT Web
→ Worker
→ FINISHED
```

을 Web ChatGPT가 다음 동작을 결정하는 반복 가능한 자동 작업 흐름으로 확장한다.

핵심 원칙:

```text
GPT Web   = Controller
Worker    = Orchestrator / Executor
Extension = Transport
Codex CLI = Task Executor
```

ACTION의 원본은 반드시 **GPT Web의 응답**이어야 한다.

Extension은 ACTION을 생성하거나 판단하지 않는다.
Worker도 Web의 ACTION을 임의로 변경하거나 보정하지 않는다.

## ACTION 계약

### [ACTION=BEGIN]

새 자동 작업의 최초 명령이다.

Web 응답의 첫 번째 유효 제어행이 `[ACTION=BEGIN]`이면 Worker는 그 뒤의 본문을 첫 Codex CLI 명령으로 전달하고 job을 시작한다.

```text
GPT Web
[ACTION=BEGIN]
첫 작업 지시...

→ Worker
→ Codex CLI 실행
→ 결과 수집
→ GPT Web 전달
```

BEGIN은 job 시작 시 1회만 유효하다.
이미 진행 중인 job에서 다시 BEGIN이 나오면 protocol error로 처리한다.

### [ACTION=CONTINUE]

Web이 현재 Codex 결과를 검토한 뒤 추가 Codex 작업이 필요하다고 판단한 상태다.

ACTION 이후의 본문을 다음 Codex CLI 명령으로 전달한다.

```text
GPT Web
[ACTION=CONTINUE]
다음 작업 지시...

→ Worker
→ Codex CLI
→ 결과 수집
→ 동일 GPT Web conversation으로 전달
→ 다음 ACTION 대기
```

이 흐름을 반복한다.

### [ACTION=PAUSE]

자동화만으로 안전하게 계속할 수 없고 사용자의 입력·판단·승인이 필요함을 의미한다.

PAUSE 응답은 Codex CLI에 전달하지 않는다.

Worker는 자동 loop를 종료하고 사용자 개입이 필요한 종료 상태로 전환한다.

```text
[ACTION=PAUSE]
→ Codex 미전달
→ FINISH_PAUSED
```

Web 응답 본문은 사용자에게 그대로 표시한다.

### [ACTION=END]

Web이 현재 목표가 완료됐다고 판단한 정상 종료다.

END 응답은 Codex CLI에 전달하지 않는다.

```text
[ACTION=END]
→ Codex 미전달
→ FINISH_SUCCESS
```

Web 응답 본문은 최종 결과로 표시한다.

## ACTION과 기존 Task Status 분리

현재 bridge의 상태:

```text
PENDING
CLAIMED
COMPLETED
FAILED
...
```

는 그대로 유지한다.

이는 한 번의 Web 요청이 전달·처리되는 **transport lifecycle**이다.

ACTION은 Web 응답이 **다음에 무엇을 해야 하는지**를 나타내는 control protocol이다.

예:

```text
Bridge Task:
PENDING
→ CLAIMED
→ GPT Web 실행
→ result POST
→ COMPLETED

Web Response:
[ACTION=CONTINUE]
다음 작업...

Worker:
→ 다음 Codex round 실행
```

둘을 하나의 enum으로 합치지 않는다.

## 응답 형식

Web 응답의 첫 번째 유효 제어행으로 아래 중 정확히 하나를 사용한다.

```text
[ACTION=BEGIN]
[ACTION=CONTINUE]
[ACTION=PAUSE]
[ACTION=END]
```

BEGIN/CONTINUE의 경우 ACTION 이후 본문만 Codex 명령 payload로 사용한다.

예:

```text
[ACTION=CONTINUE]
현재 결과에서 실패 원인을 확인하고 필요한 수정만 수행해.
```

PAUSE/END 본문은 사용자에게 표시할 설명/최종 결과이며 Codex에는 전달하지 않는다.

한 Web 응답에는 ACTION이 정확히 하나만 존재해야 한다.

## Protocol Error

다음 상황에서 Worker가 의미를 추측하지 않는다.

```text
- ACTION 없음
- ACTION 2개 이상
- 알 수 없는 ACTION
- ACTION 형식 손상
- 진행 중 job에서 BEGIN 재등장
- BEGIN/CONTINUE인데 전달할 본문이 비어 있음
```

이 경우:

```text
FINISH_PROTOCOL_ERROR
```

로 종료하며 응답을 Codex에 전달하지 않는다.

## ACTION과 실행 예외 분리

다음 오류가 발생했다고 Worker나 Extension이 임의로 `[ACTION=PAUSE]` 또는 `[ACTION=END]`를 생성하면 안 된다.

```text
- bridge 연결 실패
- GPT Web timeout
- GPT Web DOM 처리 실패
- Codex 실행 실패
- conversation mismatch
- Worker 내부 예외
- Extension 내부 예외
```

ACTION은 오직 Web 응답에서만 온다.

실행 계층 오류는 별도의 Worker terminal state로 처리한다.

초기 권장 상태:

```text
FINISH_SUCCESS
FINISH_PAUSED
FINISH_PROTOCOL_ERROR
FINISH_CODEX_ERROR
FINISH_WEB_ERROR
FINISH_WORKER_ERROR
FINISH_LIMIT
FINISH_CANCELLED
```

## 반복 안전장치

Web이 계속 CONTINUE를 반환하더라도 무한 실행되지 않도록 Worker가 독립적인 hard limit을 가진다.

초기 기본값 권장:

```text
MAX_ROUNDS  = 30
MAX_RUNTIME = 30 minutes
```

제한을 초과하면 Web ACTION과 무관하게:

```text
FINISH_LIMIT
```

로 종료한다.

테스트처럼 정확히 10회 왕복이 필요한 작업은 job 생성 시 `maxRounds=10`으로 지정할 수 있다.

## Job / Round 관리

round 번호를 AI의 출력에 의존하지 않는다.

Worker가 다음 값을 관리한다.

```text
jobId
round
maxRounds
startedAt
lastWebTaskId
lastAction
actionConsumed
codexSessionId
```

Web이나 Codex가 `3/10` 같은 숫자를 출력하더라도 실제 round의 정답은 Worker 상태다.

권장 전이:

```text
IDLE
  ↓
BEGIN
  ↓
CODEX_RUNNING
  ↓
WORKER_TO_WEB
  ↓
WEB_RUNNING
  ↓
WEB_TO_WORKER
  ↓
ACTION

ACTION=CONTINUE
  → round + 1
  → CODEX_RUNNING
  → ...

ACTION=PAUSE
  → FINISH_PAUSED

ACTION=END
  → FINISH_SUCCESS
```

## 동일 ACTION 중복 소비 방지

각 Web result의 ACTION은 `taskId` 기준으로 정확히 한 번만 소비한다.

같은 taskId의 result가 polling, 새로고침, retry 때문에 다시 보이더라도 다음 Codex round를 두 번 실행하지 않는다.

최소한 다음 값을 저장한다.

```text
jobId
round
webTaskId
action
actionConsumed
```

`actionConsumed=true`인 task는 다시 실행하지 않는다.

Worker 재시작 후에도 가능한 범위에서 이 상태를 복원해 duplicate Codex 실행을 방지한다.

## 통신 Retry와 실행 Retry 분리

Web 전송이나 result POST가 실패했다고 동일 Codex 명령을 다시 실행해서는 안 된다.

올바른 흐름:

```text
Codex 실행 1회
→ 결과 로컬 보관
→ Web 전달 실패
→ 동일 결과를 Web에 재전송
```

금지:

```text
Codex 실행
→ Web 전달 실패
→ 동일 Codex 명령 재실행
```

파일 변경, Git 작업 등 side effect가 있는 명령은 재실행 시 위험할 수 있다.

## PAUSE와 CANCEL 구분

```text
PAUSE
= GPT Web이 정상 프로토콜로 사용자 개입 필요를 결정

CANCEL
= 사용자가 Worker UI에서 현재 job을 명시적으로 취소
```

둘을 같은 상태로 처리하지 않는다.

사용자 Cancel은:

```text
FINISH_CANCELLED
```

로 종료한다.

## Extension 책임

Extension은 다음 역할만 수행한다.

```text
- Worker task 수신
- GPT Web에 전달
- 최종 Web 응답 수집
- 원문 result를 Worker에 반환
- 화면에 ACTION/상태를 표시할 수 있음
```

Extension이 다음을 해서는 안 된다.

```text
- 응답 내용을 보고 CONTINUE/END를 자체 판단
- ACTION이 없을 때 자동 보정
- PAUSE/END를 임의 생성
- 동일 task 재전송
```

## Worker 책임

Worker가 수행한다.

```text
- ACTION parser
- protocol validation
- jobId / round / maxRounds 관리
- BEGIN/CONTINUE의 Codex 전달
- PAUSE/END의 terminal 처리
- ACTION idempotency
- MAX_ROUNDS / MAX_RUNTIME hard limit
- Codex 결과 보관
- transport retry와 Codex 실행 retry 분리
```

## 첫 구현 범위

현재의 Worker → GPT Web → Worker 1회 왕복 코드는 최대한 유지한다.

다음만 추가한다.

```text
1. Web Response ACTION parser
2. BEGIN / CONTINUE / PAUSE / END validation
3. Worker jobId / round / maxRounds
4. BEGIN → 첫 Codex 실행
5. CONTINUE → 기존 Codex 실행 경로 재진입
6. PAUSE → FINISH_PAUSED
7. END → FINISH_SUCCESS
8. protocol error → FINISH_PROTOCOL_ERROR
9. MAX_ROUNDS / MAX_RUNTIME
10. 동일 webTaskId ACTION 중복 소비 방지
11. Codex 결과 저장 후 Web transport retry
12. Worker UI에 현재 Job / Round / terminal 상태 최소 표시
```

Extension에는 ACTION 의사결정 로직을 추가하지 않는다.

## 우선 검증 시나리오

곱셈 문제를 이용해 10회 왕복을 검증한다.

```text
1. Web이 [ACTION=BEGIN] + 첫 문제 생성 지시 반환
2. Worker가 Codex에 전달
3. Codex 결과를 Web에 전달
4. Web이 [ACTION=CONTINUE] + 다음 지시 반환
5. 2~4를 반복
6. Worker round가 정확히 10에 도달하는지 확인
7. 마지막 Web 응답이 [ACTION=END]
8. END가 Codex로 전달되지 않는지 확인
9. FINISH_SUCCESS 확인
10. 동일 마지막 task result를 재조회해도 추가 Codex 실행이 없는지 확인
```

추가 예외 검증:

```text
- ACTION 누락 → FINISH_PROTOCOL_ERROR
- ACTION 중복 → FINISH_PROTOCOL_ERROR
- 진행 중 BEGIN 재등장 → FINISH_PROTOCOL_ERROR
- maxRounds 초과 시 CONTINUE여도 FINISH_LIMIT
- PAUSE → Codex 미전달 + FINISH_PAUSED
- 사용자 Cancel → FINISH_CANCELLED
- Web 전달 재시도 시 Codex 재실행 없음
```

완료 기준은 단순 파싱 성공이 아니라 **Web이 Controller가 되어 Codex와 여러 round를 실제 반복하고, END/PAUSE/오류/limit에서 안전하게 자동 loop가 종료되는 것**이다.


---

# 2026-09-19 GPTWeb-Hub 최우선 과제 — 실제 ChatGPT 메시지 왕복 E2E

최신 main 기준 Worker/Codex 제어 루프와 ACTION 반복 프로토콜은 이미 구현되어 있다. 현재 가장 중요한 미완료 항목은 GPTWeb-Hub Extension이 실제 ChatGPT Web 대화창에 Worker 메시지를 안정적으로 전달하고, 최종 응답을 정확히 회수해 Worker로 되돌리는 브라우저 E2E다.

이번 작업에서는 새로운 기능을 확장하지 말고 Web transport 안정화와 실제 화면 검증을 최우선으로 한다.

## 현재 병목

현재 코드 흐름:

```text
Worker task
→ conversation binding
→ Extension polling
→ claim
→ ChatGPT composer 탐색
→ prompt 주입
→ attachment
→ Send
→ assistant 응답 감시
→ result POST
→ Worker 후속 Codex
→ ACTION 반복
```

문제는 이 전체 흐름이 실제 Explorer Worker + Chrome Extension + 실제 ChatGPT 대화에서 끝까지 검증되지 않았다는 점이다.

## 이번 완료 기준

최소 2회 실제 Web 왕복을 사용자 키보드 입력 없이 성공시킨다.

```text
Worker
→ Extension
→ 현재 바인딩된 ChatGPT conversation
→ 자동 prompt 입력
→ 실제 Send
→ GPT Web 최종 응답 완료 감지
→ result POST
→ Worker
→ 같은 Codex session 후속 실행
→ 두 번째 Web task
→ 같은 conversation 자동 전송
→ 두 번째 최종 응답 회수
→ ACTION=END 또는 정상 종료
```

1회 성공만으로 완료 처리하지 않는다.

## 메시지 전달 검증

1. composer는 실제 ChatGPT 입력창만 선택한다. Extension 자체 input, 숨은 contenteditable, 검색창, modal input을 잡으면 안 된다.
2. prompt 주입 후 실제 ChatGPT composer에 동일 문자열이 표시됐는지 확인한다.
3. 실제 Send selector를 우선 사용하고 넓은 fallback selector는 마지막 수단으로 둔다.
4. sendButton.click()만으로 성공 처리하지 않는다. composer clear, 새 user message 생성, assistant generation 시작 중 가능한 신호를 조합해 실제 전송 성공을 확인한다.

## 비활성 탭

활성 ChatGPT 탭과 비활성 ChatGPT 탭을 각각 실제로 검증한다.

비활성 탭에서 브라우저 정책 때문에 전송이 불안정하면 무리하게 우회하지 말고 WEB_REQUIRES_FOREGROUND 같은 명확한 상태로 표시한다. 사용자가 해당 탭을 활성화하면 이어서 처리할 수 있게 한다.

## 응답 완료 판정

assistant message가 처음 나타났다고 완료 처리하지 않는다.

최종 응답 확정 조건:

```text
1. 현재 task 전송 이후 새 assistant message 존재
2. Stop/중지 또는 streaming/busy 신호 종료
3. assistant 본문이 안정화 시간 동안 변하지 않음
4. task baselineAssistant와 다른 새 응답임
```

현재 5초 안정화 대기는 실제 화면에서 충분한지 확인하되, 단순히 시간을 더 늘리는 방식보다 streaming 신호를 정확히 잡는 것을 우선한다.

## 이전 응답 재사용 방지

task별로 최소 다음 값을 관리한다.

```text
taskId
conversationId
baselineAssistant
sentTaskId
sentAt
```

새로고침, Extension reload, SPA 이동 후 CLAIMED task를 복원해도 기존 assistant 응답을 새 결과로 사용하지 않는다.

## 중복 전송 방지

다음 상황에서 동일 prompt를 다시 보내지 않는다.

```text
Extension refresh
ChatGPT page refresh
SPA navigation
polling 재조회
heartbeat 재연결
result POST retry
```

taskId + sentTaskId + claim 상태 + conversationId를 기준으로 중복 send를 막는다. Send 성공 여부가 불확실하면 자동 재전송보다 사용자 확인 상태를 우선한다.

## conversation 일치 검증

전송 직전에 반드시 다음을 다시 확인한다.

```text
task.conversationId == currentConversationId
binding.projectId == task.projectId
```

불일치 시 다른 방에 보내지 말고 CONVERSATION_MISMATCH로 중단한다.

## Attachment 검증 순서

먼저 TEXT_ONLY 2회 왕복을 완전히 성공시킨 뒤 IMAGE_ATTACHMENT 1회 왕복을 검증한다.

attachment task는 bridge download → File 생성 → ChatGPT 첨부 UI 반영 → upload 완료 → Send 순서를 지킨다.

## 실제 검증 방식

최신 AGENTS.md 기준과 동일하게 가능한 경우 반드시 다음 실환경에서 검증한다.

```text
빌드된 ProjectHub.Worker.exe
+ Chrome에 실제 로드된 GPTWeb-Hub Extension
+ 실제 ChatGPT conversation
```

CLI/API 단독 검증은 보조 검증일 뿐 최종 완료 근거로 사용하지 않는다.

화면 자동화 런타임이 실패하면 실제 Worker/Chrome을 실행하고 사용자에게 필요한 최소 클릭만 요청해서라도 실제 E2E를 끝까지 확인한다. '자동화 런타임 문제로 미검증' 상태를 완료로 기록하지 않는다.

## 우선 테스트 시나리오

복잡한 이미지/코드 대신 단순 텍스트로 2회 왕복부터 검증한다.

Round 1:

```text
Worker → Web
Codex 결과 검토 요청

Web → Worker
[ACTION=CONTINUE]
두 번째 작업 지시
```

Round 2:

```text
Worker/Codex → Web
두 번째 결과 검토 요청

Web → Worker
[ACTION=END]
테스트 완료
```

END가 Codex에 다시 전달되지 않고 FINISH_SUCCESS로 끝나는지 확인한다.

## 실패 단계 기록

실패를 'Web 실패' 하나로 묶지 말고 아래 stage 중 어디서 멈췄는지 기록한다.

```text
BINDING
TASK_POLL
CLAIM
COMPOSER_FIND
TEXT_INSERT
ATTACH
SEND_BUTTON_FIND
SEND_CLICK
SEND_CONFIRM
RESPONSE_START
STREAMING
RESPONSE_STABLE
RESULT_POST
WORKER_RECEIVE
NEXT_ROUND
```

## 이번 우선순위에서 제외

Web transport 검증을 방해하지 않는 한 다음은 이번 작업에서 확장하지 않는다.

```text
새 Worker UI 기능
새 Codex archive 기능
token usage 추가 확장
Server/NAS 기능
Force Restore
추가 ACTION 종류
이미지 생성 결과 asset 수집
다중 conversation 병렬 실행
```

## 문서 최신화

실제 2회 왕복 E2E가 성공하면 CurrentWork.md와 ProjectHub_IMPLEMENTATION_PLAN.md의 오래된 상태 문구도 함께 최신화한다.

특히 'GPT Web DOM 입출력은 후속 범위', 'Chrome polling UI E2E 1건', 'Worker-B 진행' 같은 오래된 표현을 현재 구현 상태에 맞게 제거한다.

## 최종 체크리스트

```text
[ ] 실제 Worker EXE에서 task 시작
[ ] Extension이 실제 현재 conversation에서 claim
[ ] 사용자 입력 없이 ChatGPT composer에 prompt 입력
[ ] 실제 Send 성공
[ ] assistant streaming 시작 확인
[ ] 잘리지 않은 최종 응답 회수
[ ] Worker result POST 성공
[ ] Worker가 Web result 수신
[ ] 같은 Codex session 후속 처리
[ ] 두 번째 Web task 자동 생성
[ ] 같은 conversation으로 두 번째 자동 전송
[ ] 두 번째 최종 응답 회수
[ ] ACTION=END 또는 정상 종료
[ ] duplicate prompt 없음
[ ] 이전 assistant 응답 재사용 없음
[ ] conversation mismatch 없음
[ ] 새로고침/재조회 중복 실행 없음
[ ] build/test/node check 성공
[ ] CurrentWork/Implementation Plan 최신화
```

핵심 완료 기준은 코드상 가능해 보이는 것이 아니라 실제 이 ChatGPT Web 대화에서 Extension이 2회 이상 자동 메시지 왕복을 끝까지 성공하는 것이다.
