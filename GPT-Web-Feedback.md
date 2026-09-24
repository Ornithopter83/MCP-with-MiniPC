# GPT Web Feedback — RESOURCE role migration

Updated: 2026-09-24
Reference policy: Master-Polish.md
Active task: tasks/14-resource-web-role.md

## Current direction

ProjectHub의 current role graph는 다음과 같다.

~~~text
HQ       -> WORK
WORK     -> HQ | JUDGE | RESOURCE
JUDGE    -> WORK
RESOURCE -> WORK
~~~

HIGH 역할과 one-shot permit 구조는 제거한다.

## Implementation focus

- HQ는 ChatGPT Web 또는 CLI Provider를 선택할 수 있다.
- WORK는 CLI Provider 기반 구현 역할이다.
- RESOURCE는 별도 ChatGPT Web conversation에 고정한다.
- HQ Web과 RESOURCE Web은 explicit role binding을 사용한다.
- heartbeat는 liveness용이며 destination 선택에 사용하지 않는다.
- RESOURCE 초기 실제 범위는 IMAGE 생성 -> 지정 workspace 경로 저장 -> WORK 복귀다.
- RESOURCE가 저장한 파일은 사용자의 별도 연결 명령 전까지 코드/CSS/HTML에 자동 연결하지 않는다.
- SOUND는 schema만 예약하고 실제 transport는 deferred다.

## Worker boundary

Worker는 ACTION/GOTO, 상태 전이, process/session, Web binding, transport schema, workspace path safety, file/usage telemetry만 기계적으로 처리한다.

Worker가 판단하지 않는 것:
- 리소스 품질
- 리소스 사용 위치
- JUDGE 결과의 의미
- 작업 완료 여부
- 다음 작업의 의미적 우선순위

## Validation still required

현재 Web 작업 환경에는 .NET SDK/Windows Explorer 실행 환경이 없으므로 다음은 Windows에서 실검증해야 한다.

- dotnet test ProjectHub.sln
- Release build/publish
- HQ CLI flow
- HQ Web flow
- HQ/RESOURCE 두 Web conversation 동시 heartbeat 격리
- WORK -> RESOURCE -> 실제 이미지 생성/저장 -> same WORK session 복귀
- 저장된 RESOURCE가 자동 integration되지 않는지 확인
- JUDGE 회귀


## 2026-09-24 UI 확인 후 보정

- 대기 상태에서 5개 Pipeline 카드가 모두 컬러인 것은 의도된 정책이다.
- WORK에 남은 legacy GPT-6 Astra / Low 조합은 Luna / Medium으로 1회 migration한다.
- HQ/RESOURCE Web 상태는 각각 독립 binding 기준으로 표시하고 preflight한다.
- 설정창 Provider 아이콘 대비, 긴 Web 설명 wrapping, 작은 화면 scroll/footer, 설계·관제/판정 용어를 보정한다.
