# CurrentWork

Updated: 2026-09-24

정책 원본: Master-Polish.md

## Current architecture

~~~text
HQ       -> WORK
WORK     -> HQ | JUDGE | RESOURCE_QUEUE
JUDGE    -> WORK
RESOURCE_QUEUE 접수 -> HQ
RESOURCE_QUEUE 실행 -> RESOURCE Web -> 완료 알림 queue
UNKNOWN  -> HQ 요약 1회 -> 재발 시 종료
~~~

- HQ = 설계·관제, ChatGPT Web 또는 CLI Provider
- WORK = CLI 구현/수정/검증
- RESOURCE = 별도 ChatGPT Web, IMAGE 생성/복수 다운로드/저장 sidecar queue
- JUDGE = JEV
- Worker = role/session/binding/transport/process/file telemetry/protocol 오류와 RESOURCE queue 사실의 기계적 관리

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
- RESOURCE 자연어 body의 기계적 유효성 검사와 sidecar queue 접수 추가
- RESOURCE IMAGE 결과를 브라우저 확장이 복수 image payload로 반환하고 Worker가 requestId별 workspace 경로에 저장
- ResourceRequest REQUESTED→GENERATING→SAVED/FAILED 기록
- RESOURCE 접수 사실은 HQ로 전달하고, 완료 결과는 이후 WORK 입력 또는 finalization에 기계적으로 반영
- WORK/HQ/JUDGE 역할 contract를 durable protocol 중심으로 일반화
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
- WORK→RESOURCE queue 접수→실제 이미지 생성/복수 다운로드/저장→HQ/WORK/finalization 반영
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


## 2026-09-24 RESOURCE image completion follow-up

- 중간 변경은 `ab6f831`로 main에 먼저 커밋/푸시했다.
- RESOURCE 이미지 element가 DOM에 먼저 생기고 load 완료만 나중에 발생하는 경우 load event로 재검사.
- DOM mutation이 추가로 없어도 최대 120초 후 실제 로드된 이미지가 있으면 성공 전송.
- extension reset 시 owner/response/progress 관련 상태를 모두 초기화.
- CLI HQ/WORK에 Worker가 실제 전송한 prompt도 transcript에 기록.
- extension build 2026-09-24.3.


- HQ/RESOURCE 소유 Web task는 coordinator-first 종료 시점과 무관하게 legacy Web handler에서 항상 제외하여 늦게 도착한 terminal event의 중복 로그/UI 갱신을 차단.
- HQ Web progress 중 Pipeline이 작업 단계로 바뀌지 않고 설계·관제 active stage를 유지하도록 보정.
- coordinator-first 종료 후 늦게 도착한 non-terminal progress는 legacy UI를 다시 활성화하지 않음.


## 2026-09-24 RESOURCE sidecar queue + multi-image

- RESOURCE를 메인 역할 상태의 직렬 대기에서 분리하여 single-reader FIFO sidecar queue로 변경.
- WORK의 RESOURCE 요청은 queue에 즉시 접수되고 같은 WORK session은 계속 진행.
- RESOURCE Web은 동시에 1건만 실행하며 실행 중 새 요청은 실패 대신 대기열에 적재.
- 최신 assistant turn의 생성 이미지 여러 장을 모두 다운로드하고 requestId별 폴더에 image-01, image-02 ...로 저장.
- HQ ACTION=END 이후 RESOURCE outstanding이 1건 이상이면 FINALIZING으로 남고 queue idle 이후에만 DONE 생성.
- RESOURCE 카드의 gold orbit은 메인 Pipeline active role과 독립적으로 동작하고 queue 상태/대기 수를 표시.
- MutationObserver 외 1초 watchdog을 추가해 이미지 생성 후 다운로드 단계로 전이되지 않는 고착을 방지.
- extension 0.1.6 / build 2026-09-24.4.

- 이미지 URL을 content script에서 직접 fetch하지 못하면 extension background service worker가 허용된 ChatGPT/OpenAI image host에서 재시도한다.
- RESOURCE 실제 Web 전송 prompt와 bridge task id를 transcript에 계속 기록한다.


## 2026-09-24 contract generalization cleanup

- HQ/WORK/JUDGE 역할 contract에서 특정 시나리오에 종속된 예시와 일회성 대응 문구를 제거했다.
- 계약에는 durable role responsibility, ACTION/GOTO syntax, generic transport grammar, Worker/AI boundary만 남겼다.
- RoleContractLoader의 metadata는 평문 형식을 유지하며 실제 ACTION/GOTO control만 대괄호를 사용한다.
- JUDGE QID parser는 QID:NAME과 기존 [QID:NAME]을 모두 허용하지만 역할 contract에는 placeholder grammar만 제시한다.
- Master-Polish.md와 AGENTS.md에 contract generalization rule을 추가해 특정 사용자 요청/장애 사례를 contract로 승격하지 못하게 했다.


## 2026-09-25 RESOURCE download stall hardening

- 로그에서 RESOURCE 첫 task가 RESPONSE_START 이후 IMAGE_READY/DOWNLOAD_START 없이 고착되는 경로를 수정.
- RESOURCE 시작 시 기존 main image URL을 baseline으로 잡고 새 이미지 탐색 범위를 latest assistant + main 영역으로 확대.
- snapshot 변화에 의해 재시작되지 않는 절대 120초 image deadline 추가.
- loaded image가 있으면 전역 streaming flag가 남아 있어도 settle 후 다운로드 진입.
- IMAGE_DETECTED candidate/loaded telemetry 추가.
- Worker sidecar에 5분 transport timeout 추가. timeout 시 해당 bridge task ID만 FAILED(resource_timeout) 처리하여 다음 FIFO 작업의 slot conflict를 방지.
- extension 0.1.7 / build 2026-09-25.1.
