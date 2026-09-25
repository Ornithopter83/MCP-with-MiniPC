# 현재 작업

갱신일: 2026-09-25

정책 원본: Master-Polish.md

## 현재 구조

~~~text
HQ       -> WORK
WORK     -> HQ | JUDGE | RESOURCE_QUEUE
JUDGE    -> WORK
RESOURCE_QUEUE 접수 -> HQ
RESOURCE_QUEUE 실행 -> RESOURCE Web -> 완료 알림 queue
HQ ACTION=END -> 현재 실행 구간 의미 흐름 종료 고정 -> Worker 기계적 대기 작업 확인
기계적 대기 작업 있음 -> 대기 -> 모두 종료 -> DONE / DONE_WITH_ERROR
UNKNOWN  -> HQ 요약 1회 -> 재발 시 종료
~~~

- HQ = 설계·관제, ChatGPT Web 또는 CLI 제공자
- WORK = CLI 구현/수정/검증
- RESOURCE = 별도 ChatGPT Web, 생성 리소스 제작/파일 수집·다운로드/저장 사이드카 대기열
- JUDGE = JEV
- Worker = 역할/세션/연결/전송/프로세스/file 계측/프로토콜 오류와 RESOURCE 대기열 사실의 기계적 관리

## 활성 작업 — 16 동적 병렬 WORK Graph

복구 기준:
- commit `000a478f6e21c25e8d89020137e93abed1cab5e2`
- branch `recovery/pre-parallel-work-graph-20260925`

이번 활성 작업은 단일 WORK 직렬 흐름을 완전한 동적 DAG 기반 병렬 WorkGraph로 확장한다. 상세 구현 순서와 검증 게이트는 `tasks/16-parallel-work-graph.md`를 기준으로 한다.

현재 착수 범위:
- WorkGraph 도메인과 상태 전이 구현 완료
- dependency/cycle/revision 기계 검증과 READY 계산 구현 완료
- Integration을 같은 WORK 역할의 WorkItemKind로 표현
- 단계 1 단위 테스트 추가
- ParallelWorkScheduler와 maxConcurrentWork 슬롯 실행 기반 구현
- GitWorktreeManager와 WorkItem별 branch/worktree 격리 기반 구현
- 병렬 HQ GraphPatch transport와 병렬 WORK_ITEM_STATUS 보고 계약 기반 구현
- Worktree + Codex 역할 runner를 결합하는 CodexWorkItemExecutor 구현
- WorkItem checkpoint/resultRef와 dependency 결과 프롬프트 전달 구현
- MainWindow 병렬 관제 루프 연결 완료: maxConcurrentWork>1 또는 저장된 병렬 WorkGraph가 있으면 ParallelWorkSupervisor 경로를 사용
- RESOURCE/JUDGE/OBSERVATION 결과를 workItemId 기준으로 원래 WorkItem에 복귀
- WorkGraph snapshot persistence/recovery와 실행 중 session/branch/worktree 문맥 보존
- 설정 UI의 최대 동시 WORK 1~8, Pipeline의 RUNNING/MAX 및 READY/BLOCKED 상태 표시
- Integration COMPLETED 결과를 clean 주 작업공간 branch에 fast-forward로 landing하고 위험 상태에서는 INTEGRATION_LANDING_FAILED로 차단
- USER_FOLLOWUP 복구 시 WorkGraph 현재 상태를 HQ 본문에 직접 제공
- 성공한 Integration resultRef를 이후 WorkItem의 기본 baseRef로 승격하되 dependency만으로 의미적 base를 추론하지 않음
- 새 병렬 실행 구간에서는 현재 작업공간 Git HEAD를 다시 읽어 stale 기준 ref를 피함
- 다음 우선순위는 Windows dotnet test/build와 실제 max=4 병렬 E2E, Integration landing E2E

착수 commit:
- 정책/계획: `0c87a0021643efc00e147fead82ed45cb1bf4c91`
- WorkGraph 기반: `49a51cbd0e259dd43f7bd3fffe2484f2117dd0d8`

현재 실행 환경에는 .NET SDK가 없어 단계 1의 `dotnet test`/빌드는 아직 실행하지 못했다.

이전 구조 변경에서 코드상 다음 항목을 반영했다.

- WorkerRoleState.High 제거, Resource 도입
- HIGH GOTO/허가/설정/UI/계약 제거
- 상태 그래프를 HQ→WORK, WORK→HQ/JUDGE/RESOURCE, JUDGE→WORK, RESOURCE→WORK로 변경
- HQ 설정에 ChatGPT Web / CLI 대상 선택 추가
- HQ Web 선택 시 CLI 제공자/모델/추론/세션 UI 숨김
- 과거 coordinator transport=web 강제 CLI 정규화 제거
- Bridge에 HQ/RESOURCE 역할→conversationId 명시적 연결 추가
- 최신 생존 신호 기반 작업 목적지 제거
- RESOURCE 자연어 본문의 기계적 유효성 검사와 사이드카 대기열 접수 추가
- RESOURCE 생성 결과를 브라우저 확장이 공통 `resultFiles[]`로 반환하고 Worker가 requestId별 작업공간 경로에 저장
- ResourceRequest REQUESTED→GENERATING→SAVED/FAILED 기록
- RESOURCE 접수 사실은 HQ로 전달하고, HQ END 전 필요한 완료 결과만 이후 WORK 입력에 기계적으로 반영
- WORK/HQ/JUDGE 역할 계약를 지속 가능한 프로토콜 중심으로 일반화
- HQ 설계 책임과 PAUSE 사용 예 추가
- Pipeline 네 번째 카드를 리소스/ChatGPT Web으로 교체
- 확장 패널에 HQ 연결 / RESOURCE 연결 명시적 버튼 추가
- HQ와 RESOURCE에 동일 대화을 연결하는 경우 거부

## 검증

현재 이 ChatGPT 실행 환경에는 .NET SDK가 없어 dotnet 빌드/test를 실행할 수 없다.

대신 commit 전 정적 검증으로:
- Worker/Test C#·XAML 파일의 HIGH/HighLevel/JobHighLevelPermit/old Parse signature 잔존 참조 검색
- 역할 계약 로더와 임베디드 리소스 이름 일치 확인
- MainWindow RESOURCE 상태, 명시적 Web 라우팅, HQ 대상 UI 참조 확인
- Bridge 역할 연결/RESOURCE 저장 코드와 확장 데이터 묶음 필드 이름 대조
- 기존 HIGH 전용 테스트를 RESOURCE/HQ Web 테스트로 치환

을 수행한다.

잔여 실검증:
- Windows에서 `dotnet test ProjectHub.sln` 실행
- Release 빌드/게시
- Explorer HQ CLI E2E 검증
- Explorer HQ Web E2E 검증
- HQ Web + RESOURCE Web 두 창 동시 생존 신호 격리
- WORK→RESOURCE 대기열 접수→실제 생성 파일 수집/복수 다운로드/저장→HQ/WORK/마무리 반영
- RESOURCE 저장 후 자동 코드 연결이 발생하지 않는지 확인
- JUDGE 회귀


## UI 후속 보정 — 2026-09-24

사용자 화면 확인 후 다음을 보정했다.

- 대기 상태의 Pipeline 5개 카드 전체 컬러 정책은 의도된 동작으로 유지
- WORK의 기본값은 OpenAI / GPT-6 Luna / Medium으로 유지
- 저장된 모델 값을 런타임에서 임의 치환하는 마이그레이션/스키마-version 로직은 두지 않음
- 설정 제공자 아이콘에 역할 컬러 배경을 추가해 OpenAI 흰 아이콘 대비 개선
- HQ Web / RESOURCE Web 각각의 연결, 생존 신호, 확장 동기화, 대화 정보를 설정 카드에 표시
- 실제 사전 점검도 전역 최신 Web 상태가 아니라 역할별 Web 상태를 사용
- Web 설명 카드의 긴 문장을 wrapping 처리
- 설정 본문 세로/가로 스크롤 및 화면 높이 기반 popup 크기 조절, 하단 적용/닫기 footer 유지
- UI 용어를 설계·관제 / 작업 / 리소스 / 판정으로 통일

Windows 빌드 및 실제 화면/E2E 검증은 여전히 필요하다.

## 2026-09-24 Windows 빌드/테스트/게시

- 원격 `main` `09ac0dc`에서 확인한 compile 오류를 수정했다: `AiRoleRunner.cs`의 `Directory`, `ResourceTransportContract.cs`의 `Path` 참조를 위해 `System.IO`를 명시했다.
- RESOURCE/HQ Web 호출의 `_bridgeServer` nullable 경고는 명시적 null 보호 로직으로 정리했다.
- Judge 비활성 시 WORK footer에서 JUDGE GOTO 전송 안내가 노출되던 내용을 `JUDGE_ON` 조건부 블록으로 이동했다.
- `dotnet test ProjectHub.sln --no-restore`: 통과 (Core 1, Agent 3, Server 1, Worker 70; 전체 75).
- `dotnet build ProjectHub.sln -c Debug --no-restore`: 성공, 경고 0 / 오류 0.
- `dotnet build ProjectHub.sln -c Release --no-restore`: 성공, 경고 0 / 오류 0.
- `dotnet publish src/ProjectHub.Worker/ProjectHub.Worker.csproj -c Release -r win-x64 --no-restore`: 성공.
- 실행 중인 Worker 프로세스가 없음을 확인하고 게시 EXE를 `C:\AI-AGENT\Worker\ProjectHub.Worker.exe`에 복사했다. 게시본과 복사본 SHA-256은 `072FAC4E47DF2F2DA568D9823221ED2E5FC33830962F0154B4D82E774D3712FD`로 일치한다.
- `git diff --check`: 통과.
- Explorer 실화면/HQ-Web·RESOURCE 왕복은 사용자 확인 잔여다.


## 2026-09-24 RESOURCE 자연어 전송 + 관측성 보정

- RESOURCE Web 전송에서 역할/JSON/RESOURCE 계약 래퍼를 제거하고 WORK의 자연어 본문을 그대로 전달.
- RESOURCE 저장 경로는 Worker가 `assets/resources/resource-<requestId>.png`로 기계적으로 생성.
- Web 작업의 역할 소유자는 클레임 이후에도 HQ/RESOURCE로 유지하고 실제 클레임 주체는 ClaimedBy=WEB로 분리.
- Worker 송신 HQ Web/RESOURCE Web 메시지와 RESOURCE REQUESTED/GENERATING/FAILED/SAVED 생명주기을 기록에 기록.
- RESOURCE 오류를 생성 없음/캡처/다운로드/저장/Web 전달 단계로 구분.
- WORK 기록 출처를 WORK CLI로 수정.
- 오류를 거친 작업이 HQ END로 끝나면 작업 결과 상태를 DONE_WITH_ERROR로 기록.
- 확장 0.1.5와 함께 이미지 완료 조건/진행 순서을 보강한다.


## 2026-09-24 RESOURCE 이미지 수집 어댑터 완료 후속 보정

- 중간 변경은 `ab6f831`로 main에 먼저 커밋/푸시했다.
- 당시 이미지 수집 어댑터에서 이미지 element가 DOM에 먼저 생기고 load 완료만 나중에 발생하는 경우 load event로 재검사.
- DOM mutation이 추가로 없어도 최대 120초 후 실제 로드된 이미지가 있으면 성공 전송.
- 확장 초기화 시 소유자/응답/진행 상황 관련 상태를 모두 초기화.
- CLI HQ/WORK에 Worker가 실제 전송한 프롬프트도 기록에 기록.
- 확장 빌드 2026-09-24.3.


- HQ/RESOURCE 소유 Web 작업은 관제 우선 흐름 종료 시점과 무관하게 레거시 Web 처리기에서 항상 제외하여 늦게 도착한 종료 이벤트의 중복 로그/UI 갱신을 차단.
- HQ Web progress 중 Pipeline이 작업 단계로 바뀌지 않고 설계·관제 활성 stage를 유지하도록 보정.
- 관제 우선 흐름 종료 후 늦게 도착한 비종료 진행 이벤트는 레거시 UI를 다시 활성화하지 않음.


## 2026-09-24 RESOURCE 사이드카 대기열 + 복수 이미지 수집 어댑터

- RESOURCE를 메인 역할 상태의 직렬 대기에서 분리하여 single-reader FIFO 사이드카 대기열로 변경.
- WORK의 RESOURCE 요청은 대기열에 즉시 접수되고 같은 WORK 세션은 계속 진행.
- RESOURCE Web은 동시에 1건만 실행하며 실행 중 새 요청은 실패 대신 대기열에 적재.
- 최신 assistant turn의 생성 이미지 여러 장을 모두 다운로드하고 requestId별 폴더에 image-01, image-02 ...로 저장.
- HQ ACTION=END 이후 RESOURCE 미완료 작업이 1건 이상이면 FINALIZING으로 남고 대기열이 유휴 상태가 된 이후에만 DONE 생성.
- RESOURCE 카드의 gold orbit은 메인 Pipeline 활성 역할과 독립적으로 동작하고 대기열 상태/대기 수를 표시.
- MutationObserver 외 1초 watchdog을 추가해 이미지 생성 후 다운로드 단계로 전이되지 않는 고착을 방지.
- 확장 0.1.6 / 빌드 2026-09-24.4.

- 이미지 URL을 콘텐츠 스크립트에서 직접 가져오지 못하면 확장 백그라운드 서비스 워커가 허용된 ChatGPT/OpenAI 이미지 호스트에서 재시도한다.
- RESOURCE 실제 Web 전송 프롬프트와 bridge 작업 id를 기록에 계속 기록한다.


## 2026-09-24 계약 일반화 정리

- HQ/WORK/JUDGE 역할 계약에서 특정 시나리오에 종속된 예시와 일회성 대응 문구를 제거했다.
- 계약에는 지속 가능한 역할 책임, ACTION/GOTO 문법, 일반 전송 문법, Worker/AI 경계만 남겼다.
- RoleContractLoader의 메타데이터는 평문 형식을 유지하며 실제 ACTION/GOTO 제어만 대괄호를 사용한다.
- JUDGE QID 파서는 QID:NAME과 기존 [QID:NAME]을 모두 허용하지만 역할 계약에는 자리표시자 문법만 제시한다.
- Master-Polish.md와 AGENTS.md에 계약 일반화 규칙을 추가해 특정 사용자 요청/장애 사례를 계약로 승격하지 못하게 했다.


## 2026-09-25 RESOURCE 다운로드 고착 방지 강화

- 당시 이미지 수집 어댑터에서 RESOURCE 첫 작업이 RESPONSE_START 이후 IMAGE_READY/DOWNLOAD_START 없이 고착되는 경로를 수정.
- 이미지 수집 어댑터는 RESOURCE 시작 시 기존 main image URL을 기준선으로 잡고 새 이미지 탐색 범위를 최신 assistant + main 영역으로 확대.
- snapshot 변화에 의해 재시작되지 않는 절대 120초 이미지 마감 시간 추가.
- 로드 완료 image가 있으면 전역 스트리밍 flag가 남아 있어도 안정화 후 다운로드 진입.
- IMAGE_DETECTED 후보/로드 완료 계측 추가.
- Worker 사이드카에 5분 전송 시간 초과 추가. 시간 초과 시 해당 bridge 작업 ID만 FAILED(resource_timeout) 처리하여 다음 FIFO 작업의 슬롯 충돌를 방지.
- 확장 0.1.7 / 빌드 2026-09-25.1.


## 2026-09-25 RESOURCE 완료 HQ 깨우기 폐기

- RESOURCE 완료마다 HQ를 깨우는 별도 이벤트 대기열는 반복 흐름을 만들 수 있어 제거 대상으로 확정했다.
- HQ ACTION=END를 의미 작업 종료의 단일 확정점으로 사용한다.
- END 이후 같은 실행 구간에서는 Worker가 HQ/WORK/JUDGE 의미 흐름을 자동으로 다시 실행하지 않는다. 사용자가 작업 추가를 실행하면 기존 세션으로 새 실행 구간을 시작할 수 있다.
- RESOURCE를 포함한 남은 기계적 대기 작업은 Worker가 대기 상태에서 직접 추적한다.
- 모든 기계적 대기 작업이 끝나면 Worker가 DONE 또는 DONE_WITH_ERROR로 전환한다.
- END 이후 WORK 보고가 HQ로 향하는 경우 Worker가 "HQ의 작업은 종료되었습니다."로 차단한다.
- [GOTO : RESOURCE]는 새로운 생성 리소스 요청 한 건 전용이며 기존 요청 조회·취소·추적 용도로 사용하지 않는다.


## 2026-09-25 HQ 종료와 기계적 대기 분리

- RESOURCE 완료 HQ 자동 깨우기 제거.
- HQ END 뒤 재확인 END 요구 제거.
- Worker 내부의 일반 기계적 대기 작업 집계 지점을 두고 현재 RESOURCE outstanding을 연결.
- 대기 상태는 사용자 입력 대기뿐 아니라 AI 의미 작업 종료 후 기계적 비동기 작업 완료 대기에도 사용.
- 관련 역할 계약과 역할 프롬프트 설명을 한글로 통일.


## 2026-09-25 PAUSE/END 후 작업 추가

- PAUSE와 END/DONE을 세션 폐기가 아닌 현재 실행 구간의 중단/완료로 분리했다.
- `CoordinatorContinuationState`에 작업 ID, 작업공간, HQ/WORK 설정과 세션 ID, 마지막 상태와 HQ 메시지를 보존한다.
- 사용자가 후속 메시지를 입력하고 `작업 추가`를 누르면 `USER_FOLLOWUP`으로 같은 HQ/WORK 세션에서 새 실행 구간을 시작한다.
- `메시지 및 작업 이력` 그룹의 전체 크기는 유지하고, 중단/완료 상태에서 목록 아래에 이력 카드 약 두 개 높이의 후속 입력 영역을 표시한다.
- 후속 입력 영역이 표시될 때 하단 기존 버튼 왼쪽에 녹색 계열 `작업 추가` 버튼을 표시한다.
- 기존 `새 작업` 동작은 명시적 세션 초기화 지점으로 유지하며, 새 작업을 선택하기 전까지 기존 이력과 세션을 보존한다.
- 작업 기록 파일은 같은 작업에서 후속 실행이 추가될 때 동일 파일을 갱신할 수 있도록 경로를 보존한다.
- 자동 재개는 하지 않는다. PAUSE/DONE 이후 의미 흐름은 사용자 `작업 추가` 입력이 있을 때만 다시 열린다.

잔여 실검증:
- Windows 빌드/테스트
- PAUSE → 작업 추가 → 동일 HQ/WORK 세션 ID 유지 확인
- END → DONE → 작업 추가 → 동일 HQ/WORK 세션 ID 유지 확인
- 새 작업 → 이전 세션/이력 초기화 확인
- 후속 입력 UI가 이력 그룹 높이를 변경하지 않고 목록을 위로 밀어 올리는지 실화면 확인

## 2026-09-25 RESOURCE 생성 파일 일반화

- RESOURCE 의미를 IMAGE 전용에서 ChatGPT Web이 생성해 파일로 반환하는 모든 생성 리소스로 일반화한다.
- 이미지·오디오·문서 등 형식은 역할이 아니라 `resultFiles[]`의 MIME 형식과 파일명으로 구분한다.
- Worker는 리소스 종류를 의미적으로 판정하지 않고 공통 파일 저장 규칙만 적용한다.
- 저장 루트는 기존과 동일한 `assets/resources/<requestId>/`를 유지한다.
- 반환 파일명이 안전하면 정규화해 사용하고, 사용할 수 없으면 `resource-NN.<확장자>` 형식으로 저장한다.
- 이미지 DOM 감시 로직은 RESOURCE 전체 의미가 아니라 이미지 형식용 수집 어댑터로 유지한다.
- 오디오·문서·기타 생성 파일은 ChatGPT Web에서 실제 다운로드 가능한 파일/첨부 요소로 제공되는 경우 같은 공통 결과 배열로 수집한다.

## 2026-09-25 RESOURCE 생성 파일 공통 파이프라인 구현

- 새 RESOURCE 요청의 `ResourceRequest.Type`을 `RESOURCE`로 변경했다.
- 구버전 실행 상태 호환을 위해 Worker 저장 계층은 기존 `IMAGE` 형식도 계속 허용한다.
- 확장은 RESOURCE 결과를 파일 형식과 무관한 `RESOURCE_FILES` / `resultFiles[]`로 반환한다.
- Worker는 각 결과의 base64, MIME 형식, 선택적 파일명을 기계적으로 검증해 `assets/resources/<requestId>/` 아래에 저장한다.
- 반환 파일명이 안전하면 이름을 유지하고 MIME 형식에 맞는 확장자를 적용한다. 파일명이 없으면 `resource-NN.<확장자>`를 사용한다.
- 파일명은 경로 성분, 제어 문자, Windows 예약 이름을 제거하고 중복 이름에는 순번을 붙인다.
- 기존 생성 이미지 DOM 탐지는 이미지 수집 어댑터로 유지한다.
- 다운로드 가능한 ChatGPT/OpenAI 파일 링크, 첨부 링크, 오디오·비디오 소스는 일반 생성 파일 수집 어댑터로 추가했다.
- 외부 일반 웹 링크를 생성 파일로 오인하지 않도록 ChatGPT/OpenAI 계열 호스트와 blob/data URL로 후보 범위를 제한한다.
- 백그라운드 서비스 워커의 다운로드 메시지를 `fetch-resource-file`로 일반화하고 기존 `fetch-resource-image`도 호환용으로 허용한다.
- 확장 버전은 0.1.8, 빌드는 2026-09-25.2로 갱신했다.
- `content.js`, `background.js`는 V8 구문 컴파일 검사를 통과했다.
- 현재 실행 환경에는 `.NET SDK`가 없어 C# 빌드와 `dotnet test`는 실행하지 못했다. 혼합 이미지/오디오/PDF `RESOURCE_FILES` 직렬화 테스트는 소스에 추가했다.

잔여 실검증:
- Windows에서 `dotnet test ProjectHub.sln`
- RESOURCE Web에서 실제 이미지 생성 후 저장 회귀
- RESOURCE Web에서 실제 오디오 또는 다운로드 가능한 일반 생성 파일을 만든 뒤 `assets/resources/<requestId>/` 저장 확인
- 복수 형식이 한 응답에 함께 있을 때 파일명/MIME/복수 저장 확인

## 2026-09-25 CLI 작업 진행 카드

- Codex CLI stdout을 종료 후 일괄 수집하는 방식에서 JSONL 한 줄 단위 수집으로 변경했다.
- `item.completed`이면서 `item.type=agent_message`인 주 응답 이벤트만 프로토콜 타입으로 기계적으로 추출한다.
- HQ CLI와 WORK CLI의 주 응답 이벤트가 발생할 때마다 `ROLE_PROGRESS` / `작업 진행` 카드를 이력에 새로 추가한다.
- 진행 카드 본문은 의미 요약하지 않고 줄바꿈을 유지한 기계적 미리보기를 사용한다.
- 진행 카드에서는 토큰과 파일 행을 숨기고 제목과 다중 줄 본문만 표시한다.
- 진행 카드 개수 제한을 두지 않기 위해 기존 이력 250개 자동 삭제 로직을 제거했다.
- command 실행, usage, session metadata 등은 `agent_message`가 아니므로 진행 카드로 만들지 않는다.
- 최종 역할 응답 카드는 기존 `작업 요청`, `수행 결과`, `리소스 요청` 형식을 그대로 유지한다.
- parser와 다중 줄 미리보기 단위 테스트를 추가했다.

## 2026-09-25 실행 중 취소 후 동일 세션 보존

- `CANCELED`를 `PAUSED`, `DONE`, `DONE_WITH_ERROR`와 같은 사용자 후속 재개 가능 상태로 추가했다.
- 사용자가 실행 중 취소하면 현재 실행 프로세스를 중단하되 현재 JobId, 작업공간, HQ/WORK 설정, 확보된 세션 ID, 마지막 완료 HQ 메시지를 `CoordinatorContinuationState`에 저장한다.
- Codex `--json`의 `thread.started` / `thread_id`를 실행 중 즉시 수집해 새 CLI 세션도 최종 응답 전에 보존한다.
- 취소 뒤 후속 입력 영역을 표시하고 `작업 추가`로 같은 HQ/WORK 문맥의 `USER_FOLLOWUP` 새 실행 구간을 시작한다.
- Worker는 취소 후 자동으로 AI를 다시 호출하지 않는다.
- 현재 실행 구간과 함께 취소된 RESOURCE 대기 작업은 자동 재실행하지 않으며 이미 저장된 파일과 작업공간 이력은 유지한다.
- `CANCELED` 후속 입력과 `thread.started` 실시간 파서 테스트를 추가했다.

## 2026-09-25 RESOURCE 종류 분리와 실패 WORK 복귀

- WORK가 RESOURCE를 요청할 때 `RESOURCE_TYPE: IMAGE|AUDIO|VIDEO|DOCUMENT|FILE`을 명시한다.
- 한 RESOURCE 요청에는 한 종류만 포함하며 이미지와 오디오처럼 생성 방식이 다른 리소스는 별도 요청으로 분리한다.
- Worker는 본문 의미로 종류를 추론하지 않고 명시된 분류 토큰만 기계적으로 파싱한다.
- RESOURCE Web에는 분류 헤더를 제거한 자연어 생성 요청만 전달한다.
- RESOURCE 성공과 실패는 모두 completion queue를 통해 다음 WORK 입력의 `RESOURCE_RESULT`로 전달한다.
- RESOURCE 실패는 더 이상 `UNKNOWN -> HQ ERROR_SUMMARY` 경로로 우회하지 않는다.
- 실패 결과에는 requestId, RESOURCE_TYPE, 오류 코드, Web/transport 결과 메시지를 포함해 같은 WORK 세션이 재요청·분리·보고 여부를 결정한다.
- HQ END 이후에는 기존 정책대로 AI를 다시 깨우지 않고 Worker가 기계적 종료 상태만 정리한다.

## 2026-09-25 RESOURCE 분류/실패 복귀 구현 완료

- `ResourceTransportContract`가 `RESOURCE_TYPE: IMAGE|AUDIO|VIDEO|DOCUMENT|FILE` 첫 줄을 필수로 파싱한다.
- Worker는 분류 토큰을 제거한 자연어 본문만 RESOURCE Web에 전달한다.
- `ResourceSidecarRequest`와 completion에 RESOURCE 종류를 보존한다.
- `ResourceRequest.Type`에는 명시된 종류를 기록하고 Bridge 저장 계층은 해당 종류들을 공통 파일 저장 방식으로 처리한다.
- RESOURCE 성공/실패 completion은 requestId, 종류, 상태, 오류 코드와 결과 메시지를 다음 WORK 입력의 `RESOURCE_RESULT`에 포함한다.
- 기존 RESOURCE 실패의 `RouteUnknown(Resource) -> HQ ERROR_SUMMARY` 경로를 제거했다.
- 같은 WORK 세션이 Web의 생성 제한이나 미지원 형식 오류를 보고 요청 분리·재시도·보고 여부를 결정한다.
- HQ가 이미 END한 뒤에는 기존 정책대로 AI를 다시 호출하지 않고 기계적 종료 상태만 정리한다.
- 분류 파서와 지원 종류 테스트를 갱신했다.
- 최신 소스 정적 대조에서 분류 파서, queue 종류 보존, Bridge 허용, 실패 WORK 복귀, 기존 UNKNOWN 분기 제거를 확인했다.

## 2026-09-25 JUDGE 질문 원자화 실험 정책

- HQ의 역할을 `JUDGE 사용 필요성 게이트`에서 `JUDGE 질문 원자화·정제`로 임시 변경했다.
- WORK가 의미 판정 질문을 HQ에 올리면 HQ는 JUDGE가 필요한지 다시 판단하지 않는다.
- HQ는 하나의 질문에 하나의 판단 대상만 남기고, 독립적으로 답할 수 있는 항목은 별도 질문으로 분리한다.
- 각 질문에는 필요한 범위에서만 evidence, 응답 형식, 측정·수치 기준을 붙인다.
- HQ는 이 실험 동안 `JUDGE 불필요`, `JUDGE를 사용하지 말라`는 선택을 하지 않는다.
- HQ가 정제한 판정 질문을 받은 WORK는 필요성을 다시 판단하지 않고 `[GOTO : JUDGE]`로 전송한다.
- Worker 라우팅/전송 코드는 변경하지 않는다. 역할 프롬프트 정책만 바꿔 실제 JEV 호출 빈도와 질문 품질을 관찰한다.

## 2026-09-25 JUDGE용 Form 위임 계약 간략화

- WORK 계약에서 NOUL/SCORE/CHOICE Form 문법 설명과 장문의 판정 절차를 제거했다.
- WORK는 검증할 내용을 질문 목록과 현재 근거로 정리해 HQ에 JUDGE용 Form 생성을 요청한다.
- HQ는 질문을 독립 판단 단위로 정리하고 필요한 범위, evidence, 응답 형태, 기준을 포함한 Form을 작성해 WORK에 돌려준다.
- WORK는 받은 Form을 `[GOTO : JUDGE]`로 전송한다.
- HQ 계약의 `WORK가 의미 판정 질문을 올리면...` 문구와 JUDGE 필요성 재판단 관련 실험 문구를 제거했다.
- Worker 라우팅 및 JUDGE transport 코드는 변경하지 않았다.

## 2026-09-25 WORK 계약 제어행 대괄호 정리

- WORK 계약의 설명 문장에서는 GOTO 목적지를 대괄호 제어행 형태로 쓰지 않는다.
- 대괄호는 실제 ACTION/GOTO 제어행에만 사용하고, 설명 문장에서는 HQ, JUDGE, RESOURCE 역할명으로만 지칭한다.
- 실제 라우팅 제어행 문법과 Worker 파서는 변경하지 않았다.

## 2026-09-25 WORK 계약 간략화 동기화 및 배포 복사

- 원격 `main` `bb0239f`까지 fast-forward 동기화하고 최신 `GPT-Web-Feedback.md`를 확인했다.
- 새 변경은 HQ의 JUDGE용 Form 위임과 WORK 설명 문장의 대괄호 제어 표기 정리이며 Worker 라우팅 코드는 변경하지 않는다.
- `dotnet build ProjectHub.sln -c Release --no-restore`: 성공, 경고 0 / 오류 0.
- `dotnet publish src/ProjectHub.Worker/ProjectHub.Worker.csproj -c Release -r win-x64 --no-restore`: 성공.
- 복사 직전 Worker 프로세스가 없음을 확인하고 게시 실행 파일을 `C:\AI-AGENT\Worker\ProjectHub.Worker.exe`에 복사했다. 원본/복사본 SHA-256 일치: `6A28F871982CAC2B79C00FB5F8F748B2F075A8DD0F4E9B8409B066837D440CD1`.
- 자동 테스트와 Explorer 실화면/E2E는 실행하지 않았다. `artifacts/tower_defense_bgm.wav` 추적되지 않은 파일은 그대로 보존했다.

## 2026-09-25 RESOURCE transport 시간 제한 연장

- `ResourceSidecarQueue.ResourceTransportTimeout`을 5분에서 30분으로 변경했다.
- 기존 timeout 처리와 실패 코드는 유지되며 제한 시간과 오류 메시지는 같은 상수를 사용한다.
- 빌드·테스트는 이번 변경에서 실행하지 않았다.

## 2026-09-25 HQ JUDGE Form 전송 문법 보강

- WORK 계약은 질문 목록과 근거를 HQ에 보내 Form 생성을 요청하는 간략한 책임만 유지한다.
- HQ 계약에 JUDGE 전송 문법을 추가했다.
- HQ는 NOUL/SCORE/CHOICE와 QID 형식으로 실제 전송 가능한 Form을 작성한다.
- SCORE에는 정수=기준, CHOICE에는 선택지=기준이 하나 이상 포함되어야 한다.
- Worker는 의미를 판단하지 않고 기존 JudgeTransportContract로 Form 구조만 기계적으로 검사한다.
- 이번 변경은 계약과 문서/테스트만 갱신하며 JUDGE parser 코드는 변경하지 않는다.

## 2026-09-25 HQ JUDGE CHOICE 선택지 키 규칙 보강

- 실제 JUDGE transport parser는 CHOICE 선택지 키를 영문자로 시작하는 ASCII 토큰으로만 인식한다.
- HQ 계약에 선택지 키가 영문자로 시작하고 영문자, 숫자, 밑줄, 하이픈만 사용할 수 있다는 규칙을 추가했다.
- 한글 선택지 키는 사용하지 않도록 명시하고 Form 문법 예시는 A, B 키를 사용하도록 바꿨다.
- Master-Polish.md에도 같은 기계적 전송 규칙을 반영했다.
- 계약 테스트에 HQ가 해당 규칙과 A/B 예시를 노출하는지 확인하는 검사를 추가했다.
- parser 회귀 테스트에 한글 CHOICE 키가 CHOICE_CRITERIA_MISSING으로 거부되는 현재 전송 규칙을 고정했다.
- JudgeTransportContract 구현 코드는 변경하지 않았다.

## 2026-09-25 판단 제어 · 프로젝트 기억 · 실시간 이벤트 로그

- WORK JUDGE 계약을 최소 판단 경계로 정리했다. 관측 사실 자체는 JUDGE에 보내지 않고, 현재 근거만으로 기계적으로 확정할 수 없는 판단이 다음 작업/완료에 영향을 줄 때 HQ에 질문과 근거를 올린다.
- 이전 판정 뒤 근거가 의미 있게 바뀌면 새 근거로 다시 Form을 요청한다.
- HQ는 이미 확정된 관측 사실을 JUDGE 문항으로 반복하지 않고 추가 해석이 필요한 판단만 Form으로 만든다.
- WORK가 Form 요청을 빠뜨려도 HQ가 WORK 보고에서 다음 작업/완료에 영향을 주는 미판정 비기계적 판단을 발견하면 해당 판단만 Form으로 만들어 WORK에 돌려준다.
- 작업공간 `.projecthub/session-state.json`과 `last-handoff.md`에 재개 상태와 마지막 HQ 관제 문맥을 영속화한다.
- Worker 재시작 시 재개 가능한 상태를 복구하며, 저장된 Codex session이 로컬에 없으면 session ID를 버리고 프로젝트 기억 파일/이벤트 로그를 새 HQ 문맥 복구 입력에 포함한다.
- 모든 Worker 관측 메시지를 `.projecthub/events/<jobId>.jsonl`에 실시간 append하고, transcript는 `.projecthub/transcripts/<jobId>.txt`에 저장한다.
- History 항목에 Full Message를 보존하고 두 번 클릭해 별도 읽기 창으로 확인할 수 있게 했다.
- 명시적 새 작업은 활성 session-state만 제거하고 과거 이벤트/handoff/transcript는 보존한다.
- 관련 계약 및 프로젝트 기억/event log 단위 테스트를 추가했다.
- 이번 변경에 대해 Windows `dotnet test`/빌드는 아직 실행하지 않았다.

## 2026-09-25 RESOURCE Send 확인 고착 복구

- 실제 요청이 ChatGPT Web에 전송되고 이미지가 생성됐지만 확장이 SEND_CONFIRM에서 새 사용자 메시지를 확인하지 못해 WAIT_RESPONSE로 넘어가지 못하는 로그를 확인했다.
- 전송 성공의 기계적 증거를 새 사용자 메시지 외에 composer 비움, 새 assistant turn, 새 RESOURCE 후보까지 확대했다.
- 새 생성 결과가 이미 나타난 경우 프롬프트를 재전송하지 않고 바로 RESOURCE 수집으로 전환한다.
- WAIT_RESPONSE 진입과 동시에 120초 절대 수집 timeout을 시작한다.
- 확장 패널에 `현재 결과 다시 수집` 버튼을 추가했으며 재전송 없이 현재 결과만 스캔한다.
- Worker RESOURCE Pipeline의 단일 `생성·다운로드 중` 표시는 Web 확장 progress에 따라 전송 확인/생성 결과 대기/결과 확인/다운로드/Worker 전달로 세분화했다.
- 확장 버전 0.1.9 / 빌드 2026-09-25.3, Worker가 요구하는 확장 버전도 동일하게 갱신했다.
- 실제 RESOURCE Web 이미지 생성 E2E와 Windows `dotnet test`는 아직 실행 확인이 필요하다.
