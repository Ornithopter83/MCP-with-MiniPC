# Worker-Polish — ProjectHub Worker 정책


이 문서는 Worker의 장기 책임, 사용자 작업과 WORKITEM의 생명주기, 마일스톤 실행 경계를 정의한다. 역할의 출력 문법과 필드 제한은 전용 라우팅 계약, 기계적 판정의 세부 구현은 코드와 테스트를 따른다.

제1조 (역할과 책임)

① HQ는 목표 해석·계획·WORKITEM 설계 및 다음 WORK·PAUSE·END 판단을 맡는다. HQ Web/CLI는 로컬 프로젝트를 변경하지 않는다.
② #10+ WORK는 지시받은 작은 작업의 실제 실행을 담당하며 필요한 셸·Unity CLI/MCP·Editor·빌드·테스트 실행 수단을 사용할 수 있다. 할당된 작업과 좁은 파일 범위 밖의 임의 작업·수정은 허용하지 않는다. 세션·진행 상태는 Worker가 보존한다.
③ #0 DESIGN은 UI·씬·그래픽 실제 제작·시각 수정을, #1 ASSET MANAGER는 에셋 검색·획득·Import·연결을 담당한다. 구형 이미지 추출 RESOURCE sidecar는 비활성이다.
④ 기계적 BUILD/TEST 근거는 HIGH가 검증한다.
⑤ HIGH(PL)는 구현·BUILD/TEST·DESIGN 증거를 검증하고 직접 보완한다. HIGH에서 DESIGN 재호출은 하지 않는다. HIGH가 직접 수정·검토한 뒤 남은 결함·증거를 HQ에 보고한다.
⑥ Worker는 HQ의 직접 WORK 배정과 병렬 실행, 세션·체크포인트, 기계적 BUILD·Git 및 오류·중단 상태를 관리한다. 전문 역할이 자체 위임한 임시 WORK는 그 부모 역할 내부에서 실행·회수하며 HQ에는 별도 WORKITEM 또는 번호로 전달하지 않는다.
⑦ 현재 역할은 HQ, #1 ASSET MANAGER, #0 DESIGN, #10+ WORK, HIGH다. 비활성 이미지 추출용 RESOURCE sidecar는 실행하지 않는다.

제2조 (HQ 설계와 역할 전달)

① HQ 역할 계약 전문은 사용자 명령으로 새 작업을 시작할 때 한 번 주입한다. 후속 마일스톤과 형식 복구에는 현재 결과 및 프로젝트·원격 상태만 전달하고, 같은 세션의 계약을 유지한다.
② HQ는 첫 설계에서 목표와 완료 기준을 정리하고, 이후에는 필요한 마일스톤과 WORKITEM·쓰기 경로·검증 지시를 구성한다.
③ 현행 HQ 계획은 HIGH가 필수이며 #1 ASSET MANAGER, #0 DESIGN, #10+ WORK 중 작업에 필요한 역할을 설계한다.
④ 같은 마일스톤의 병렬 GENERAL WORK는 서로의 미완료 결과에 의존하지 않게 설계한다. 충돌하거나 선행 의존이 있는 작업은 범위를 합치거나 후속 마일스톤으로 나눈다.
⑤ 쓰기 WORK의 WRITE_PATH는 서로 겹치지 않게 구성하고 Worker가 경로 충돌을 기계적으로 판정한다.
⑥ WORK는 실제 확보·검증된 ASSET MANAGER 결과와 DESIGN 산출물에 의존할 때 적절한 순서로 설계한다. 비활성 RESOURCE 완료를 기다리도록 배정하지 않는다.
⑦ HQ 응답 파싱 오류는 전체 응답 재요청과 정의된 형식 복구 경로로 처리한다. 형식 오류를 이유로 이미 수행한 실제 파일 작업을 다시 실행하지 않는다.
⑧ Worker는 각 역할에 해당 작업의 목표·지시·범위·필요한 결과를 전달하고, Git 처리와 기계적 상태 전이는 Worker 내부에서 관리한다.

제3조 (프로젝트 루트와 범위)

① 사용자가 지정한 로컬 폴더가 유일한 프로젝트 루트다. 별도 clone·worktree를 실제 작업 루트의 대체물로 사용하지 않는다.
② 현재 간결한 HQ 문법은 GIT_INIT 필드를 제공하지 않는다. 초기화 여부는 현재 기계적 preflight·설정 계약에 따른다.
③ WORK의 WRITE_PATH는 작고 구체적인 작업·파일 소유 범위를 정의한다. 실제 수행 수단(셸·Editor·CLI/MCP·빌드·테스트)과는 구별되며, 범위 외 파일 변경이 필요하면 부모 역할 또는 HQ에 경로·사유를 먼저 보고한다. 사용자 선행 변경은 보존한다.
④ Worker는 마일스톤 시작·종료 시점의 파일 상태를 비교해 기계적으로 이번 마일스톤의 변경을 계산한다.
⑤ HIGH는 필요한 소스·설정 변경과 검증을 수행하며 DESIGN은 시각적 결과물을 만든다. 검증하지 않은 성공은 선언하지 않으며 Git 최종화는 Worker가 담당한다.
⑥ 해상도 요구가 사용자 지시나 해당 프로젝트 명세에 명시되지 않은 UnityWorker 신규 제작 프로그램의 기본 해상도는 **1920×1080(16:9)**이다. 명시된 해상도 요구가 있으면 그 요구를 우선한다. 이 제작 기본값을 Game View 실제 렌더 크기, 캡처 파일 크기 또는 모든 창 크기의 강제 검증 조건으로 오인하지 않는다.

제4조 (WORKITEM 식별·세션·재개)

① WORKITEM ID는 사용자 작업(Job) 안에서의 고유 식별자이며 실행 순서를 뜻하지 않는다. 신규 WORK는 미사용 ID(10 이상)를 NEW로 발급한다. ID의 단조 증가는 요구하지 않는다.
② RUNNING·IN_PROGRESS 상태의 기존 WORKITEM은 같은 ID, 동일한 WRITE_PATH·읽기 전용 범위·Provider·모델로 CONTINUE를 지정하여 다시 수행할 수 있다.
③ COMPLETED·BLOCKED·CANCELED WORKITEM은 동일 ID로 재배정하거나 CONTINUE하지 않는다. 후속 변경은 다른 신규 WORKITEM으로 설계한다.
④ Worker는 WORKITEM별로 실행 세션과 체크포인트를 격리하고 ID, 작업 범위, 모델, 세션, 실행 횟수, 최근 보고, 상태와 재해결 사용 횟수를 프로젝트의 .projecthub 안에 영구 기록한다.
⑤ WORK 결과의 in_progress는 진행 중인 작업의 상태다. Worker는 실제 작업 범위의 파일 변경으로 진전이 확인되는 경우 한정된 횟수만 동일 세션에서 추가 실행한다. 파일 진전이 없거나 호출 한도에 도달하면 HQ의 다음 판단에 맡긴다.
⑥ Worker는 WORK의 기존 연속 수행이 끝나는 시점에 마무리 보고서의 미해결 항목을 확인한다. 해결 가능한 blocked·in_progress 또는 completed의 ISSUES에 명시된 미완료 구현·미실행 검증에 한하여, 해당 마무리 보고서만 동일 WORKITEM의 기존 세션에 전달하여 재해결을 최대 1회 수행한다. 정상 완료, 실행 환경·권한 장애, 수동 승인 대기, 도구 실행 실패 및 유효하지 않은 보고서는 재해결하지 않는다.
⑦ 재해결은 새 WORKITEM 발급이나 완료된 ID의 재배정이 아니다. 최초 보고서는 재해결 입력에만 사용하고, 결과는 기존 @@REPORT 형식의 마무리 보고서로 갱신한다. Worker는 재해결이 끝나기 전에 해당 WORKITEM을 최종 종료 처리하지 않으며, 체크포인트에 사용 횟수를 기록하여 재시작·CONTINUE 후에도 추가 재해결을 허용하지 않는다. 재해결 후에도 미완료이면 실제 상태를 그대로 보고한다.
⑧ 마일스톤 종료는 IN_PROGRESS WORKITEM의 완료나 폐기를 뜻하지 않는다. HQ는 다음 마일스톤에서 명시적으로 CONTINUE를 지정할 수 있다.
⑨ 프로그램이 비정상 중단됐을 때 Worker는 저장된 사용자 작업과 WORK 체크포인트를 복원하고, 필수 역할 연결 조건이 갖춰지면 기존 작업 재개를 시도한다. 새 사용자 작업·명시적 취소·PAUSE를 임의 재시도로 바꾸지 않는다.
⑩ 사용자 작업의 END는 해당 작업에 RUNNING·IN_PROGRESS WORKITEM이 남아 있는 경우 거부한다.

제5조 (마일스톤 실행)

① HQ의 유효한 WORK 설계를 받으면 실행 가능한 독립 WORK를 병렬 슬롯 안에서 배정한다. 동시 실행 수는 사용자 설정을 따르며, Worker는 고정 상한이나 자원량 기반 자동 축소를 적용하지 않는다.
② #1 ASSET MANAGER는 필요 시 WORK에 앞서 에셋을 탐색·획득·적용한다. 실패는 HQ/HIGH에 기록하며 독립 WORK는 계속할 수 있다.
③ #0 DESIGN은 WORK 전에 실제 그래픽·씬을 제작하고 미변경인 계획서만으로 완료를 주장하지 않는다.
④ Editor·Scene·Import 등 공유 호출은 공통 게이트로 조정하며 독립 C# 소스 WORK는 병렬 실행한다. Editor 레인의 직접 WORK가 보유한 게이트는 그 WORK의 중첩 MCP 명령에 한해 재사용하며, 역할 자체의 판단은 대신하지 않는다.
⑤ Worker는 소스·어셈블리·관련 설정 파일 변경이 있는 경우에만 Unity 기계 재컴파일을 수행한다. 변경 없는 조사·문서·검증 스크립트·미적용 시각 자산만 있는 마일스톤은 BUILD_SKIPPED로 기록하며 이를 검증 PASS라고 주장하지 않는다. 환경 오류를 코드 수정으로 우회하지 않는다.
⑥ in_progress WORKITEM은 완료 처리하지 않고 HIGH/HQ에 실제 상태·체크포인트·남은 작업을 전달한다. 후속 CONTINUE는 HQ가 판단한다.
⑦ HIGH가 BUILD/TEST와 WORK/DESIGN 결과를 검증한다. HIGH가 DESIGN을 재수정 호출하지 않는다. 해결이 어려운 시각 문제는 증거와 대상을 HQ에 보고하고, HQ만 후속 마일스톤에 DESIGN을 배정한다.
⑧ HIGH 결과가 완료·차단되거나 BUILD에 문제가 있어도 READ_ONLY 외 마일스톤은 규정된 Git 최종화 판단으로 이어진다. Git 오류는 별도 HIGH 진단과 한정 재시도를 따른다.
⑨ Worker는 마일스톤 결과와 Git 상태를 기계적으로 축약해 HQ에 돌려주고, 다음 마일스톤·PAUSE·END는 HQ가 결정한다.

제6조 (역할 결과와 오류)

① ASSET MANAGER, DESIGN, WORK, HIGH는 @@REPORT와 역할별 중분류 결과를 반환하며 대괄호 제어·종료 형식은 전용 계약을 따른다.
② WORK·DESIGN·ASSET MANAGER의 STATUS는 completed, in_progress, blocked다. HIGH는 completed 또는 blocked로 Worker에 보고한다. DESIGN 인계 라우팅은 사용하지 않는다.
③ 실행 timeout·도구·전송 실패는 실제 기계 상태로 기록한다. 역할의 자연어 SUMMARY·ISSUES를 기계적 성공 증거로 오인하지 않는다.
④ 명백한 문법 오류는 원문의 STATUS·증거를 바꾸지 않는 기계적 복구를 우선 적용하고, 필요한 경우 현재 역할 계약에 맞춘 형식 재출력을 요청한다.
⑤ WORK 형식 재출력은 동일 WORKITEM의 구현을 다시 실행시키지 않는다. 복구 뒤에도 유효한 결과가 없으면 해당 WORK는 blocked로 보고한다.
⑥ HIGH는 실제 검증 증거와 미검증을 분리한다. 완료 보고만으로 실행하지 않은 PlayMode·Game View 검증을 PASS 처리하지 않는다.

제7조 (Git)

① 사용자 *게임 프로젝트*의 마일스톤 Git 기준은 main/origin/main이다. Worker 제품 소스의 UnityWorker 개발 브랜치와 별개다.
② Git metadata·원격 동기화·commit·push는 Worker 기계 책임이다. WORK·DESIGN·ASSET MANAGER·HIGH는 최종화하지 않는다.
③ Worker는 실제 변경을 기준으로 WORK·DESIGN·ASSET MANAGER 산출물을 수집하고 기존 사용자 변경을 보존한다. CHANGED_PATH만으로 Git 대상을 결정하지 않는다.
④ 최초 설계 문서는 필요한 경우 별도 초기 커밋으로 저장하며, 마일스톤 최종 결과는 Worker가 commit·push한다. 실패는 HQ에 실제 상태와 함께 보고한다.
⑤ READ_ONLY_NO_FILE_CHANGES는 SKIPPED_READ_ONLY로 처리한다.

제8조 (HQ 결과와 완료)

① Worker는 추가 AI 요약자 없이 WORK·DESIGN·ASSET MANAGER·BUILD/TEST·HIGH·Git의 실제 상태를 정리한다.
② blocked·timeout·in_progress, BUILD/TEST 실패, HIGH의 미해결 시각 결함과 증거를 HQ에 손실 없이 보고한다.
③ HQ의 END는 사용자 목표의 전체 완료 판단이며, Worker는 진행 중인 WORKITEM 체크포인트가 남아 있으면 이를 수락하지 않는다.
④ HQ는 최초 목표 대비 실질 진전이 없는 마일스톤이 연속 2회 발생하면 역할 계약에 따라 PAUSE한다. Git 커밋·문서·검증 실행기 증가는 그 자체로 목표 진전이 아니며, Worker는 결정 근거를 보고하되 HQ의 PAUSE/END 의미 판단을 대행하지 않는다.

제9조 (PAUSE·사용자 메시지·인수인계)

① main으로 수렴하지 못했거나 checkout이 main이 아니면 사용자 개입이 필요한 상태를 보고한다.
② 기존 dirty 파일이나 동시 변경은 그 사실만으로 실행 중단 사유가 아니다. 안전한 진행이 불가능한 실제 충돌은 사용자 판단에 맡긴다.
③ PAUSE는 HQ가 결정하는 후속 입력 대기 상태다. 추가 사용자 메시지는 원문으로 다음 AI에 단 한 번 전달하며 Worker가 자연어 중단 의도를 추론하지 않는다. 화면의 명시적 취소는 별도다.
④ 새 사용자 작업은 새 HQ CLI 세션으로 시작해 `.projecthub/last-handoff.md`를 참고한다. 동일 작업의 중단 복구는 기존 세션·`.projecthub/session-state.json`·체크포인트를 재사용한다.

제10조 (UI와 설정)

① UI는 HQ, #1 ASSET MANAGER, #0 DESIGN, WORK, HIGH 및 기계 상태를 구분한다. 구형 역할 설정·표시 잔존은 활성 스테이지의 증거가 아니다.
② 실제 작업 단위의 이력 카드는 배정·진행·응답을 동일 카드에서 갱신하고, 기계 이벤트는 transcript·로그에 기록한다.
③ HQ·WORK·DESIGN·ASSET MANAGER·HIGH는 역할별 모델·Provider 설정을 지원한다.
④ 메인 창의 X 버튼은 트레이 숨김이며, 명시적 Exit가 종료를 요청한다.

제11조 (문자 인코딩)

① Worker 입력, 전송, 보고, transcript, 상태 파일과 로그는 UTF-8을 기본으로 한다.
② 대상 형식에 별도 요구가 없으면 새 텍스트 파일은 UTF-8 BOM 없이 기록한다.
③ JSON은 Worker 내부 직렬화에 사용할 수 있고 비ASCII 문자를 실제 Unicode 문자로 기록한다. AI 역할 응답에 JSON 출력을 요구하는 것과 구분한다.
④ Windows PowerShell 5.1의 텍스트 입출력은 UTF-8을 명시한다.
⑤ 디코딩 실패는 원문을 보존하고 오류로 보고한다.

제12조 (UnityWorker 활성 실행 정책)

① 이 브랜치는 Unity 프로젝트 자동 개발 특화 버전이다. Unity CLI/MCP는 Editor와 연결하는 외부 도구이며 Unity Editor는 씬·프리팹 변경과 실제 실행 환경이다.
② Unity HQ는 Gameplay 소스 작성과 Scene/Prefab 변경을 별도 WORK 또는 마일스톤으로 설계한다. Toolchain·Editor 연결 확인은 장기적으로 Worker의 기계 관제 책임이며 독립 상시 AI 역할을 새로 만들지 않는다.
③ 독립 C# 소스 WORK는 병렬 실행하며 Editor·Scene·Import/AssetDatabase 작업은 공통 프로젝트 게이트로 순서를 조정한다. 독립 외부 CLI까지 OS 수준 강제 격리는 아니다.
④ Unity 컴파일·Console·PlayMode의 실제 증거가 없으면 BUILD_SKIPPED나 WORK 완료를 검증 성공으로 선언하지 않는다. Worker의 BUILD/TEST 근거를 HIGH가 판정하고 미검증을 따로 보고한다.
⑤ Assets와 대응 .meta, Packages, ProjectSettings는 허용된 변경 범위를 따른다. Library, Temp, Logs, UserSettings 등의 생성물은 Git 대상에서 제외한다.
⑥ Unity CLI/MCP 연결 오류를 코드 결함으로 오인해 자동 소스 복구를 반복하지 않는다.
⑦ UnityWorker 제품 코드 브랜치와 사용자의 게임 프로젝트 작업 브랜치는 별개다. 게임 프로젝트의 기존 main/origin/main Git 계약을 임의 변경하지 않는다.
⑧ 현재 구현은 역할 카드·계약·소스 WORK 병렬/Editor 게이트, Unity CLI 재컴파일 BUILD, HQ 사전 점검, DESIGN 제작과 HIGH 검증을 제공한다. Windows Editor/Player·포커스 보호의 성공 여부는 개별 실행 증거로 판정하며 한 번의 PASS를 전체 검증으로 확대하지 않는다.

제13조 (#1 ASSET MANAGER 실행 정책)

① HQ는 게임에 에셋이 필요한 경우 첫 마일스톤부터 @@ASSET_MANAGER=1을 우선 구성한다. 에셋 불필요 시 생략한다. #1만 있는 마일스톤을 지원하며 #1은 #10+ WORK 전에 실행한다.
② #1은 DISCOVER, ACQUIRE, INTEGRATE 3단계를 동일 Request ID·AI 세션에서 순서대로 수행한다. DISCOVER는 읽기 전용 에셋 탐색, ACQUIRE는 PROJECT_ONLY 재사용 또는 CC0_AUTO의 승인 가능한 획득·Import, INTEGRATE는 실제 씬 저장·참조 확인이다.
③ #1 단계별 쓰기 범위·세션·실행 상태를 .projecthub에 기록한다. 프로젝트 내부에서 필요한 추가 변경은 증거와 함께 보고하며 중단된 변경 단계를 무조건 재실행하지 않는다.
④ #1은 Editor 공유 상태 보호를 위해 직렬 실행한다. FULL_AUTONOMY에서 명령 차단 Hook을 적용하지 않으며 Git 최종화는 Worker가 담당한다.
⑤ 공식 공급원 검색/다운로드 및 Editor 실기 성공은 Windows에서 별도 검증한다. CI 성공만으로 실제 에셋 설치·씬 배치 완료를 주장하지 않는다.

제14조 (Unity 새 작업의 충돌 파일 강제 교체)

① NEW 작업은 첫 HQ 전달 전에 Git main 준비와 Unity .gitignore 규칙 설치를 완료한다. 이전 로컬 브랜치 master 등에서 origin/main 전환이 미추적 또는 변경 파일의 경로 충돌로 실패하면, Worker가 충돌 파일만 원격 main 내용으로 강제 교체하고 재시도한다.
② 충돌 파일에 대한 백업·stash·임시 복제본은 만들지 않는다. 이미 다른 위치에 별도 백업이 있다고 가정한다. 원격 대상 파일과 겹치는 오래된 로컬 파일 및 디렉터리 충돌 잔여물을 삭제한다.
③ origin/main과 충돌하지 않는 미스테이징/스테이징 파일은 변경하지 않는다. Git 메타데이터(.git), ProjectHub 지속 상태(.projecthub)와 임의의 프로젝트 외부 경로는 삭제하지 않는다. 전체 저장소에 git clean -fdx, reset --hard, force checkout을 수행하지 않는다.
④ Unity .gitignore는 Library, Temp, Logs, UserSettings, 캐시·IDE 생성물을 기본 제외하되 Assets, Packages, ProjectSettings 및 .meta 파일은 추적한다. 체크아웃 전에 .git/info/exclude를 마련하고, 정상 전환 후 .gitignore를 보완한다.
⑤ Git 준비 실패 시 HIGH를 파일 수정 권한으로 실행하지 않는다. 충돌 아닌 Git 장애는 즉시 오류 보고한다. 이 강제 정책은 NEW 작업에만 적용하고 CONTINUE 및 진행 중 마일스톤에는 확대하지 않는다.

제15조 (명령 접수 시 Unity Editor 연결 확인)

① 대시보드의 '＋ 새 작업' 버튼은 이전 작업 정리 및 새 명령 입력 화면 전환만 담당하며 Unity Editor 실행·연결 점검을 시작하지 않는다.
② '▶ 실행' 또는 '＋ 작업 추가'로 실제 명령을 접수할 때 기존 Editor/Pipeline 연결 상태를 확인한다. 해당 점검은 새로운 GUI Editor를 자동 실행하거나 사용자 포커스를 전환하지 않는다.
③ NEW 명령의 Git 준비는 최초 Editor 연결 점검에 앞서 수행한다. CONTINUE 명령에는 NEW 전용 Git 충돌 파일 교체를 적용하지 않는다.
④ 연결된 Editor는 재사용하고 프로젝트 잠금이 있으면 중복 실행하지 않는다. Safe Mode·Pipeline 연결 실패·패키지 오류는 연결 실패로 기록하며 코드 전용 작업과 HQ 판단을 일괄 차단하지 않는다. Editor 의존 명령의 미확인 결과는 성공으로 판정하지 않는다.
⑤ 작업 중 연결 상태 감시는 읽기 전용이며 Editor 자동 재시작·재연결·강제 포커스 이동을 수행하지 않는다. 역할 AI가 실행할 명령을 결정하고 Worker는 지원 여부·실제 Editor 반환값을 기계적으로 확인한다.

제17조 (Worker 단일 EXE 실행 위치와 사용자 설정)

① Worker 프로그램 설정과 전역 실행 데이터는 Windows 사용자별 `%LOCALAPPDATA%\ProjectHub\Worker` 아래에 저장한다. 설정 파일은 `config` 디렉터리의 target-settings.json, worker-selection.json, window-placement.json으로 구분한다. EXE가 어디에서 실행되든 동일 사용자 PC의 동일 경로를 참조해야 하며 EXE 설치 경로에 쓰기 권한이 있다고 가정하지 않는다.
② 기존 배포 형식의 EXE 상대 `Worker\config`가 있으면 누락된 파일만 최초 이전한다. 사용자가 중앙 설정에 이미 저장한 파일을 덮어쓰지 않으며, 구버전 파일을 삭제하지 않는다. 배포 스크립트는 삭제하는 빌드 출력 폴더의 기존 설정을 사전 이전한다.
③ 기존 설정의 JSON 손상이나 접근 오류는 로그에 명시하고, 설정 저장은 임시 파일 작성 후 원자적 교체로 완료한다. 설정 마이그레이션에 실패하면 원래 파일이 보존되도록 한다.
④ 프로젝트 고유의 지속 체크포인트와 실행 이력은 여전히 작업 프로젝트 내 `.projecthub`에, 프로젝트 임시 파일은 프로젝트 내 `temp`에 두며 별도 Git 정책을 따른다.

제18조 (FULL_AUTONOMY 실행 및 위험도 기록)

① UnityWorker에서 Codex/OpenCode의 역할 요청은 FULL_AUTONOMY를 기본 적용한다. Codex 실행은 사용자 승인을 요구하지 않는 전체 접근 정책을 사용하고, OpenCode는 외부 디렉터리·셸 편집 권한을 허용한다. 이는 별도의 관리자 권한 상승이나 서비스 인증 우회와 다르다.
② FULL_AUTONOMY에서는 WORK·DESIGN·ASSET MANAGER·HIGH 셸 명령을 구형 BUILD 거부 Hook으로 일률 차단하지 않는다. 역할별 Editor 공유·Git 최종화 책임과 사용자 포커스 보호 규칙은 유지한다.
③ 관측된 명령 중 위험 신호에 대해 [주의], [경고], [위험] 태그, 실행 단계, 역할, Job ID, 명령 미리보기를 기존 명령 이력과 `%LOCALAPPDATA%\ProjectHub\Worker\logs\risk-audit.log`에 기록한다. 태그는 휴리스틱 진단 정보이며 모든 간접 작업의 위험을 보장하거나 통제하지 않는다. 로그가 실패해도 작업 명령을 소급 차단하지 않는다.
④ 실행 실패는 한정된 대체 방법으로 복구하되 부분 변경을 무조건 재실행하지 않으며 BUILD/TEST·HIGH 증거와 Git 최종화는 별도 검증한다.
⑤ FULL_AUTONOMY는 명령 실행 권한 정책이며 역할별 계획·작업 범위·검증 성공 조건 자체를 통과시킨다는 의미가 아니다. 이후 사용자의 종료·취소 및 실제 도구 오류는 계속 존중한다.

제19조 (실패·복구와 HQ 결정)

① 사용자 명시 취소, 잘못된 작업 대상, 사용자 소유 파일과의 복구 불가능한 충돌만 강제 중단한다. 검증되지 않은 완료는 성공 처리하지 않는다. 위험 태그는 차단 사유가 아니다.
② 다운로드·Editor/Pipeline·Import·BUILD/TEST·HIGH·Git 실패는 대체 경로를 한정해 시도하고 미해결 증거·체크포인트를 HQ에 넘겨 다음 판단을 받는다. 같은 실패를 무한 반복하지 않는다.
③ #1은 검증된 기존 에셋을 재사용하고 Import 미완료를 별도로 기록한다. NEW 작업의 origin/main Git 충돌 경로 강제 교체는 제14조에 한정한다. 에셋 재사용은 사용자 소유 씬을 덮어쓸 권한이 아니다.
