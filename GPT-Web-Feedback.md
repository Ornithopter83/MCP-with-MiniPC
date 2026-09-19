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
