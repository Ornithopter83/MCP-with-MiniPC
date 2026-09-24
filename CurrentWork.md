# CurrentWork

Updated: 2026-09-24

정책 원본: Master-Polish.md

## Current architecture

~~~text
HQ       -> WORK
WORK     -> HQ | JUDGE | RESOURCE
JUDGE    -> WORK
RESOURCE -> WORK
UNKNOWN  -> HQ 요약 1회 -> 재발 시 종료
~~~

- HQ = 설계·관제, ChatGPT Web 또는 CLI Provider
- WORK = CLI 구현/수정/검증
- RESOURCE = 별도 ChatGPT Web, 현재 IMAGE 생성/저장
- JUDGE = JEV
- Worker = role/session/binding/transport/process/file telemetry/protocol 오류의 기계적 관리

## Active — 14 RESOURCE Web role + HQ Web restore

이번 구조 변경에서 코드상 다음 항목을 반영했다.

- WorkerRoleState.High 제거, Resource 도입
- HIGH GOTO/permit/설정/UI/contract 제거
- 상태 그래프를 HQ→WORK, WORK→HQ/JUDGE/RESOURCE, JUDGE→WORK, RESOURCE→WORK로 변경
- HQ settings에 ChatGPT Web / CLI target 선택 추가
- HQ Web 선택 시 CLI Provider/Model/Reasoning/session UI 숨김
- 과거 coordinator transport=web 강제 CLI normalize 제거
- Bridge에 HQ/RESOURCE role→conversationId explicit binding 추가
- latest heartbeat 기반 task destination 제거
- RESOURCE request JSON의 기계적 schema/path 검증 추가
- RESOURCE IMAGE 결과를 브라우저 확장이 base64로 반환하고 Worker가 workspace 하위 지정 경로에 저장
- ResourceRequest REQUESTED→GENERATING→SAVED/FAILED 기록
- RESOURCE 저장 후 같은 WORK session으로 기계적 복귀
- WORK/HQ/RESOURCE 역할 contract 갱신
- HQ 설계 책임과 PAUSE 사용 예 추가
- Pipeline 네 번째 카드를 리소스/ChatGPT Web으로 교체
- 확장 패널에 HQ 연결 / RESOURCE 연결 명시적 버튼 추가
- HQ와 RESOURCE에 동일 conversation을 binding하는 경우 거부

## Verification

현재 이 ChatGPT 실행 환경에는 .NET SDK가 없어 dotnet build/test를 실행할 수 없다.

대신 commit 전 정적 검증으로:
- Worker/Test C#·XAML 파일의 HIGH/HighLevel/JobHighLevelPermit/old Parse signature 잔존 참조 검색
- role contract loader와 embedded resource 이름 일치 확인
- MainWindow RESOURCE state, explicit Web routing, HQ target UI 참조 확인
- Bridge role binding/resource save 코드와 확장 payload field 이름 대조
- 기존 HIGH 전용 테스트를 RESOURCE/HQ Web 테스트로 치환

을 수행한다.

잔여 실검증:
- Windows dotnet test ProjectHub.sln
- Release build/publish
- Explorer HQ CLI E2E
- Explorer HQ Web E2E
- HQ Web + RESOURCE Web 두 창 동시 heartbeat 격리
- WORK→RESOURCE 실제 이미지 생성→지정 파일 저장→WORK 복귀
- RESOURCE 저장 후 자동 코드 연결이 발생하지 않는지 확인
- JUDGE 회귀


## UI follow-up — 2026-09-24

사용자 화면 확인 후 다음을 보정했다.

- 대기 상태의 Pipeline 5개 카드 전체 컬러 정책은 의도된 동작으로 유지
- WORK의 기본값은 OpenAI / GPT-6 Luna / Medium으로 유지
- 저장된 모델 값을 런타임에서 임의 치환하는 migration/schema-version 로직은 두지 않음
- 설정 Provider 아이콘에 역할 컬러 배경을 추가해 OpenAI 흰 아이콘 대비 개선
- HQ Web / RESOURCE Web 각각의 binding, heartbeat, 확장 동기화, conversation 정보를 설정 카드에 표시
- 실제 preflight도 전역 latest Web 상태가 아니라 역할별 Web 상태를 사용
- Web 설명 카드의 긴 문장을 wrapping 처리
- 설정 본문 세로/가로 스크롤 및 화면 높이 기반 popup 크기 조절, 하단 적용/닫기 footer 유지
- UI 용어를 설계·관제 / 작업 / 리소스 / 판정으로 통일

Windows build 및 실제 화면/E2E 검증은 여전히 필요하다.

## 2026-09-24 Windows build/test/publish

- 원격 `main` `09ac0dc`에서 확인한 compile 오류를 수정했다: `AiRoleRunner.cs`의 `Directory`, `ResourceTransportContract.cs`의 `Path` 참조를 위해 `System.IO`를 명시했다.
- RESOURCE/HQ Web 호출의 `_bridgeServer` nullable 경고는 명시적 null guard로 정리했다.
- Judge 비활성 시 WORK footer에서 JUDGE GOTO transport 안내가 노출되던 내용을 `JUDGE_ON` 조건부 블록으로 이동했다.
- `dotnet test ProjectHub.sln --no-restore`: 통과 (Core 1, Agent 3, Server 1, Worker 70; 전체 75).
- `dotnet build ProjectHub.sln -c Debug --no-restore`: 성공, 경고 0 / 오류 0.
- `dotnet build ProjectHub.sln -c Release --no-restore`: 성공, 경고 0 / 오류 0.
- `dotnet publish src/ProjectHub.Worker/ProjectHub.Worker.csproj -c Release -r win-x64 --no-restore`: 성공.
- 실행 중인 Worker 프로세스가 없음을 확인하고 게시 EXE를 `C:\AI-AGENT\Worker\ProjectHub.Worker.exe`에 복사했다. 게시본과 복사본 SHA-256은 `072FAC4E47DF2F2DA568D9823221ED2E5FC33830962F0154B4D82E774D3712FD`로 일치한다.
- `git diff --check`: 통과.
- Explorer 실화면/HQ-Web·RESOURCE 왕복은 사용자 확인 잔여다.


## 2026-09-24 RESOURCE natural-language + observability fix

- RESOURCE Web 전송에서 ROLE/JSON/RESOURCE contract wrapper를 제거하고 WORK의 자연어 본문을 그대로 전달.
- RESOURCE 저장 경로는 Worker가 `assets/resources/resource-<requestId>.png`로 기계적으로 생성.
- Web task의 역할 Owner는 claim 이후에도 HQ/RESOURCE로 유지하고 실제 claim 주체는 ClaimedBy=WEB로 분리.
- Worker outbound HQ Web/RESOURCE Web 메시지와 RESOURCE REQUESTED/GENERATING/FAILED/SAVED lifecycle을 transcript에 기록.
- RESOURCE 오류를 생성 없음/캡처/다운로드/저장/Web 전달 단계로 구분.
- WORK WORK transcript source를 WORK CLI로 수정.
- 오류를 거친 Job이 HQ END로 끝나면 TASK RESULT status를 DONE_WITH_ERROR로 기록.
- 확장 0.1.5와 함께 image 완료 조건/progress ordering을 보강한다.
