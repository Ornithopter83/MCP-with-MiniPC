# Worker-Polish — ProjectHub Worker 정책

이 문서는 Worker의 장기 책임, 사용자 작업과 WORKITEM의 생명주기, 마일스톤 실행 경계를 정의한다. 역할의 출력 문법과 필드 제한은 전용 라우팅 계약, 기계적 판정의 세부 구현은 코드와 테스트를 따른다.

제1조 (역할과 책임)

① HQ는 사용자 목표 해석, 최초 계획, 마일스톤·WORKITEM 설계, QA 조사 지시와 다음 WORK·PAUSE·END 판단을 담당한다.
② #10+ GENERAL WORK는 지정된 WORKITEM과 WRITE_PATH 안에서 구현을 수행한다. WORKITEM은 마일스톤을 넘어 이어질 수 있고, 동일 WORKITEM의 실행 세션과 진행 상태는 Worker가 보존한다.
③ #0 RESOURCE는 GPTWEB 이미지 생성 전용의 독립 sidecar다. #1 ASSET MANAGER는 별개로 에셋 검색→획득·Import→씬 반영을 선행 실행한다.
④ QA는 예약된 마일스톤의 실제 동작과 실행 증거를 조사하고 passed, issue, blocked 중 하나를 반환한다.
⑤ HIGH는 마일스톤 WORK 실행 묶음이 종료된 뒤 고급 검토와 필요한 보완을 수행한다. QA의 issue 여부에만 종속되지 않는다.
⑥ Worker는 역할 배정과 병렬 실행, 세션·체크포인트, 기계적 BUILD·Git, 오류·중단 상태와 HQ 보고를 관리한다.
⑦ UnityWorker의 현재 실행 역할은 HQ, #1 ASSET MANAGER, #10+ WORK, #0 IMAGE RESOURCE, QA, HIGH다. 과거의 JUDGE·OBSERVATION 및 기존 #1 중간관리자 설계는 실행하지 않는다.

제2조 (HQ 설계와 역할 전달)

① HQ 호출에는 HQ 전용 역할 계약을 적용하고, 다음 판단에는 사용자 요청 또는 직전 마일스톤의 축약 결과와 현재 프로젝트·원격 상태를 제공한다.
② HQ는 첫 설계에서 목표와 완료 기준을 정리하고, 이후에는 필요한 마일스톤과 WORKITEM·쓰기 경로·검증 지시를 구성한다.
③ 현행 간결한 HQ 문법은 각 WORK의 테스트 필요 여부를 따로 선택하는 필드를 제공하지 않는다. 현재 파서는 일반 WORK를 testRequired=true로 구성하며 QA·HIGH 섹션을 요구한다.
④ 같은 마일스톤의 병렬 GENERAL WORK는 서로의 미완료 결과에 의존하지 않게 설계한다. 충돌하거나 선행 의존이 있는 작업은 범위를 합치거나 후속 마일스톤으로 나눈다.
⑤ 쓰기 WORK의 WRITE_PATH는 서로 겹치지 않게 구성하고 Worker가 경로 충돌을 기계적으로 판정한다.
⑥ RESOURCE 결과를 사용해야 하는 WORK는 해당 이미지가 실제 확보·검증된 다음 마일스톤에서 설계한다. RESOURCE 자체는 후속 단계를 기다리지 않는다.
⑦ HQ 응답 파싱 오류는 전체 응답 재요청과 정의된 형식 복구 경로로 처리한다. 형식 오류를 이유로 이미 수행한 실제 파일 작업을 다시 실행하지 않는다.
⑧ Worker는 각 역할에 해당 작업의 목표·지시·범위·필요한 결과를 전달하고, Git 처리와 기계적 상태 전이는 Worker 내부에서 관리한다.

제3조 (프로젝트 루트와 범위)

① 사용자가 지정한 로컬 폴더가 유일한 프로젝트 루트다. 별도 clone·worktree를 실제 작업 루트의 대체물로 사용하지 않는다.
② 현재 간결한 HQ 문법은 GIT_INIT 필드를 제공하지 않는다. 초기화 여부는 현재 기계적 preflight·설정 계약에 따른다.
③ GENERAL WORK는 자신의 WRITE_PATH 안에서 수정한다. 지정되지 않은 사용자 파일과 변경은 보존한다.
④ Worker는 마일스톤 시작·종료 시점의 파일 상태를 비교해 기계적으로 이번 마일스톤의 변경을 계산한다.
⑤ QA는 소스와 Git을 수정하지 않는다. HIGH는 검토 과정에서 필요한 소스·설정 변경을 수행할 수 있으나 Git 최종화는 Worker가 담당한다.

제4조 (WORKITEM 식별·세션·재개)

① WORKITEM ID는 사용자 작업(Job) 안에서의 고유 식별자이며 실행 순서를 뜻하지 않는다. 신규 WORK는 미사용 ID(10 이상)를 NEW로 발급한다. ID의 단조 증가는 요구하지 않는다.
② RUNNING·IN_PROGRESS 상태의 기존 WORKITEM은 같은 ID, 동일한 WRITE_PATH·읽기 전용 범위·Provider·모델로 CONTINUE를 지정하여 다시 수행할 수 있다.
③ COMPLETED·BLOCKED·CANCELED WORKITEM은 동일 ID로 재배정하거나 CONTINUE하지 않는다. 후속 변경은 다른 신규 WORKITEM으로 설계한다.
④ Worker는 WORKITEM별로 실행 세션과 체크포인트를 격리하고 ID, 작업 범위, 모델, 세션, 실행 횟수, 최근 보고, 상태와 재해결 사용 횟수를 프로젝트의 .projecthub 안에 영구 기록한다.
⑤ WORK 결과의 in_progress는 진행 중인 작업의 상태다. Worker는 실제 작업 범위의 파일 변경으로 진전이 확인되는 경우 한정된 횟수만 동일 세션에서 추가 실행한다. 파일 진전이 없거나 호출 한도에 도달하면 HQ의 다음 판단에 맡긴다.
⑥ Worker는 WORK의 기존 연속 수행이 끝나는 시점에 마무리 보고서의 미해결 항목을 확인한다. 해결 가능한 blocked·in_progress 또는 completed의 ISSUES에 명시된 미완료 구현·미실행 검증에 한하여, 원래 임무와 해당 마무리 보고서를 동일 WORKITEM의 기존 세션에 재주입하여 재해결을 최대 1회 수행한다. 정상 완료, 실행 환경·권한 장애, 수동 승인 대기, 도구 실행 실패 및 유효하지 않은 보고서는 재해결하지 않는다.
⑦ 재해결은 새 WORKITEM 발급이나 완료된 ID의 재배정이 아니다. 최초 보고서는 재해결 입력에만 사용하고, 결과는 기존 @@REPORT 형식의 마무리 보고서로 갱신한다. Worker는 재해결이 끝나기 전에 해당 WORKITEM을 최종 종료 처리하지 않으며, 체크포인트에 사용 횟수를 기록하여 재시작·CONTINUE 후에도 추가 재해결을 허용하지 않는다. 재해결 후에도 미완료이면 실제 상태를 그대로 보고한다.
⑧ 마일스톤 종료는 IN_PROGRESS WORKITEM의 완료나 폐기를 뜻하지 않는다. HQ는 다음 마일스톤에서 명시적으로 CONTINUE를 지정할 수 있다.
⑨ 프로그램이 비정상 중단됐을 때 Worker는 저장된 사용자 작업과 WORK 체크포인트를 복원하고, 필수 역할 연결 조건이 갖춰지면 기존 작업 재개를 시도한다. 새 사용자 작업·명시적 취소·PAUSE를 임의 재시도로 바꾸지 않는다.
⑩ 사용자 작업의 END는 해당 작업에 RUNNING·IN_PROGRESS WORKITEM이 남아 있는 경우 거부한다.

제5조 (마일스톤 실행)

① HQ의 유효한 WORK 설계를 받으면 실행 가능한 독립 WORK를 병렬 슬롯 안에서 배정한다. 동시 실행 수는 사용자 설정을 따르며, Worker는 고정 상한이나 자원량 기반 자동 축소를 적용하지 않는다.
② RESOURCE는 사용자 작업 단위의 독립 sidecar로 실행된다. GENERAL WORK, QA, HIGH, Git, HQ 다음 판단, PAUSE, 후속 마일스톤은 RESOURCE의 완료를 기다리지 않는다. 같은 사용자 작업 안의 복수 RESOURCE 요청은 RESOURCE 실행 수명 간 충돌 없이 직렬화한다.
③ RESOURCE가 성공하면 이미지 검사 후 지정 TARGET_PATH에 반영하며, 다음 Git 최종화에서 그 결과를 수집한다. RESOURCE의 PENDING은 정상 상태다.
④ 새 사용자 작업이 시작될 때만 이전 작업의 미완료 RESOURCE를 cancel/abandon하고 종료를 확인한 뒤 해당 임시영역을 정리한다.
⑤ Worker는 모든 병렬 GENERAL WORK의 이번 실행 및 필요한 1회 재해결이 종료되기를 기다린 다음 기계 BUILD를 실행한다. 실제 코드 빌드 오류에 한해 WORK #8 복구를 한 번 시도하고, 환경 오류를 소스 수정으로 우회하지 않는다.
⑥ 모든 WORK 실행을 기다린 뒤에도 in_progress WORKITEM이 남아 있는 마일스톤에서는 QA와 일반 HIGH 검토를 수행하지 않고 진행 상태를 HQ에 보고한다.
⑦ 미완료 WORKITEM이 없는 마일스톤에서 QA가 예약되어 있으면 QA를 실행하고, 그 결과와 무관하게 일반 HIGH 검토를 한 번 수행한다. QA를 예약하지 않은 경우에도 HIGH 검토는 수행한다.
⑧ 일반 HIGH 결과가 완료·차단되거나 BUILD·QA에 문제가 있어도 READ_ONLY 정책 이외의 마일스톤은 Git 최종화 단계로 진행한다. Git preflight·최종화 실패는 별도 HIGH 진단 및 한정 재시도 경로를 사용할 수 있다.
⑨ Worker는 마일스톤 결과와 Git 상태를 기계적으로 축약해 HQ에 돌려주고, 다음 마일스톤·PAUSE·END는 HQ가 결정한다.

제6조 (역할 결과와 오류)

① WORK, QA, HIGH는 @@REPORT 및 고정 중분류의 평문 계약으로 결과를 반환한다. 대괄호 제어 표식과 종료 표식은 역할 전용 계약을 따른다.
② WORK status는 completed, in_progress, blocked다. QA status는 passed, issue, blocked이며 HIGH status는 completed, blocked다.
③ 실행 timeout·도구·전송 실패는 실제 기계 상태로 기록한다. 역할의 자연어 SUMMARY·ISSUES를 기계적 성공 증거로 오인하지 않는다.
④ 명백한 문법 오류는 원문의 STATUS·증거를 바꾸지 않는 기계적 복구를 우선 적용하고, 필요한 경우 현재 역할 계약에 맞춘 형식 재출력을 요청한다.
⑤ WORK 형식 재출력은 동일 WORKITEM의 구현을 다시 실행시키지 않는다. 복구 뒤에도 유효한 결과가 없으면 해당 WORK는 blocked로 보고한다.
⑥ QA·HIGH는 확인된 사실과 미검증을 구분한다. HIGH의 completed만으로 QA가 확인하지 못한 사항을 passed로 변경하지 않는다.

제7조 (Git)

① 현재 마일스톤 최종화의 작업 브랜치는 main, 원격 기준은 origin/main이다.
② Git metadata와 원격 동기화, commit·push는 Worker의 기계 책임이다. WORK·QA·HIGH는 Git 최종화를 수행하지 않는다.
③ Worker는 시작·종료의 실제 변경을 기준으로 작업 범위와 RESOURCE 결과를 수집하고, 지정 범위 밖의 사용자 변경은 보존한다. 역할 보고의 CHANGED_PATH는 commit 대상의 단독 근거가 아니다.
④ 최초 설계 문서는 필요한 경우 별도 초기 커밋으로 저장하며, 마일스톤 최종 결과는 Worker가 commit·push한다. 실패는 HQ에 실제 상태와 함께 보고한다.
⑤ READ_ONLY_NO_FILE_CHANGES는 SKIPPED_READ_ONLY로 처리한다.

제8조 (HQ 결과와 완료)

① Worker는 추가 AI 요약자 없이 WORK·BUILD·QA·HIGH·RESOURCE·Git의 실제 상태를 기계적으로 정리한다.
② blocked·timeout·in_progress의 식별자와 다음 판단에 필요한 오류·잔여 사항을 HQ에 전달한다. QA issue와 HIGH의 수정·미해결 내용도 손실 없이 축약한다.
③ HQ의 END는 사용자 목표의 전체 완료 판단이며, Worker는 진행 중인 WORKITEM 체크포인트가 남아 있으면 이를 수락하지 않는다.

제9조 (PAUSE와 사용자 개입)

① main으로 수렴하지 못했거나 checkout이 main이 아니면 사용자 개입이 필요한 상태를 보고한다.
② 기존 dirty 파일이나 동시 변경은 그 사실만으로 실행 중단 사유가 아니다. 안전한 진행이 불가능한 실제 충돌은 사용자 판단에 맡긴다.
③ PAUSE는 사용자 follow-up을 기다리는 상태다. 환경 오류를 숨기거나 사용자 의사 없이 무한 자동 복구하지 않는다.
④ 새로운 사용자 작업이 아닌 한 기존 작업의 저장 상태를 보존해 재개할 수 있도록 한다.

제10조 (UI와 설정)

① UI는 HQ, GENERAL WORK, RESOURCE, QA, HIGH 및 Worker 기계 상태를 구분해 표시한다.
② 실제 작업 단위의 이력 카드는 배정·진행·응답을 동일 카드에서 갱신하고, 기계 이벤트는 transcript·로그에 기록한다.
③ HQ, GENERAL WORK, QA, HIGH는 역할별 독립 Provider·모델 설정을 지원한다. RESOURCE는 GPTWEB 고정이다.
④ 메인 창의 X 버튼은 트레이 숨김이며, 명시적 Exit가 종료를 요청한다.

제11조 (문자 인코딩)

① Worker 입력, 전송, 보고, transcript, 상태 파일과 로그는 UTF-8을 기본으로 한다.
② 대상 형식에 별도 요구가 없으면 새 텍스트 파일은 UTF-8 BOM 없이 기록한다.
③ JSON은 Worker 내부 직렬화에 사용할 수 있고 비ASCII 문자를 실제 Unicode 문자로 기록한다. AI 역할 응답에 JSON 출력을 요구하는 것과 구분한다.
④ Windows PowerShell 5.1의 텍스트 입출력은 UTF-8을 명시한다.
⑤ 디코딩 실패는 원문을 보존하고 오류로 보고한다.

제12조 (UnityWorker 전용 1차 실행 정책)

① 이 브랜치는 Unity 프로젝트 자동 개발 특화 버전이다. Unity CLI/MCP는 Editor와 연결하는 외부 도구이며 Unity Editor는 씬·프리팹 변경과 실제 실행 환경이다.
② Unity HQ는 Gameplay 소스 작성과 Scene/Prefab 변경을 별도 WORK 또는 마일스톤으로 설계한다. Toolchain·Editor 연결 확인은 장기적으로 Worker의 기계 관제 책임이며 독립 상시 AI 역할을 새로 만들지 않는다.
③ Editor 공유 씬·Asset Import·Domain Reload 충돌을 방지하기 위해 Editor 전용 독점 스케줄러 완성 전에는 이 브랜치의 WORK를 하나씩 실행한다. 동시 WORK 설정값은 보존하되 1차 구현에서는 병렬 실행하지 않는다.
④ Unity 컴파일/Console/PlayMode의 실제 증거가 없으면 BUILD_SKIPPED 또는 WORK 완료 보고를 검증 성공으로 선언하지 않는다. Unity 전용 BUILD/QA 어댑터 도입 전에는 QA가 검증 가능 범위와 환경 제약을 구분한다.
⑤ Assets와 대응 .meta, Packages, ProjectSettings는 허용된 변경 범위를 따른다. Library, Temp, Logs, UserSettings 등의 생성물은 Git 대상에서 제외한다.
⑥ Unity CLI/MCP 연결 오류를 코드 결함으로 오인해 자동 소스 복구를 반복하지 않는다.
⑦ UnityWorker 제품 코드 브랜치와 사용자의 게임 프로젝트 작업 브랜치는 별개다. 게임 프로젝트의 기존 main/origin/main Git 계약을 임의 변경하지 않는다.
⑧ 1차 구현은 역할 카드·계약·WORK 직렬화, Unity CLI 재컴파일 BUILD, HQ 시작 전 프로젝트/Pipeline/Editor 읽기 연결 점검을 제공한다. 이 점검은 Codex 샌드박스 내부 MCP 호출 성공까지 입증하지 않는다. Editor 접근 분리, 실제 Windows 통합 검증과 Unity PlayMode QA 기계 판정은 후속 과제다.

제13조 (#1 ASSET MANAGER 실행 정책)

① HQ는 게임에 에셋이 필요한 경우 첫 마일스톤부터 @@ASSET_MANAGER=1을 우선 구성한다. 에셋 불필요 시 생략한다. #1만 있는 마일스톤을 지원하며 #1은 #10+ WORK 전에 실행한다.
② #1은 DISCOVER, ACQUIRE, INTEGRATE 3단계를 동일 Request ID·AI 세션에서 순서대로 수행한다. DISCOVER는 읽기 전용 에셋 탐색, ACQUIRE는 PROJECT_ONLY 재사용 또는 CC0_AUTO의 승인 가능한 획득·Import, INTEGRATE는 실제 씬 저장·참조 확인이다.
③ #1 단계별 쓰기 범위, 모델, 세션 및 실행 상태를 .projecthub 체크포인트에 기록한다. 중단된 변경 단계를 무조건 다시 실행하지 않는다. 요청 범위 밖의 변경은 BLOCKED로 보고한다.
④ #1은 WORK의 명령 실행 보호 Hook을 적용받는다. Import·씬 변경은 Editor 공유 상태 보호를 위한 직렬 실행이다. Git 최종화는 Worker가 담당한다.
⑤ 공식 공급원 검색/다운로드 및 Editor 실기 성공은 Windows에서 별도 검증한다. CI 성공만으로 실제 에셋 설치·씬 배치 완료를 주장하지 않는다.

제14조 (Unity 새 작업의 충돌 파일 강제 교체)

① NEW 작업은 첫 HQ 전달 전에 Git main 준비와 Unity .gitignore 규칙 설치를 완료한다. 이전 로컬 브랜치 master 등에서 origin/main 전환이 미추적 또는 변경 파일의 경로 충돌로 실패하면, Worker가 충돌 파일만 원격 main 내용으로 강제 교체하고 재시도한다.
② 충돌 파일에 대한 백업·stash·임시 복제본은 만들지 않는다. 이미 다른 위치에 별도 백업이 있다고 가정한다. 원격 대상 파일과 겹치는 오래된 로컬 파일 및 디렉터리 충돌 잔여물을 삭제한다.
③ origin/main과 충돌하지 않는 미스테이징/스테이징 파일은 변경하지 않는다. Git 메타데이터(.git), ProjectHub 지속 상태(.projecthub)와 임의의 프로젝트 외부 경로는 삭제하지 않는다. 전체 저장소에 git clean -fdx, reset --hard, force checkout을 수행하지 않는다.
④ Unity .gitignore는 Library, Temp, Logs, UserSettings, 캐시·IDE 생성물을 기본 제외하되 Assets, Packages, ProjectSettings 및 .meta 파일은 추적한다. 체크아웃 전에 .git/info/exclude를 마련하고, 정상 전환 후 .gitignore를 보완한다.
⑤ Git 준비 실패 시 HIGH를 파일 수정 권한으로 실행하지 않는다. 충돌 아닌 Git 장애는 즉시 오류 보고한다. 이 강제 정책은 NEW 작업에만 적용하고 CONTINUE 및 진행 중 마일스톤에는 확대하지 않는다.

제15조 (명령 접수 시 Unity Editor 자동 기동)

① 대시보드의 '＋ 새 작업' 버튼은 이전 작업 정리 및 새 명령 입력 화면 전환만 담당한다. 이 버튼, 빈 입력 필드의 포커스, 창 시작 및 설정 변경은 Unity Editor를 실행하지 않는다.
② 사용자가 새 명령을 '▶ 실행'으로 제출하거나 '＋ 작업 추가'로 명령을 접수한 때만 대상 프로젝트의 Unity Editor 연결 상태를 확인하고, 필요할 경우 `unity open <프로젝트 루트>`를 실행한다. 사용자 입력 없이 뒤따르는 자동 마일스톤·WORK/QA 단계에서는 Editor를 새로 띄우지 않는다.
③ NEW 명령은 Git main 강제 충돌 경로 교체가 완료된 다음에만 Editor 자동 기동을 수행하여 씬 파일과 에디터 메모리의 불일치를 방지한다. CONTINUE 명령은 destructive NEW Git 정리를 하지 않는다.
④ 대상 프로젝트 Editor/Pipeline이 이미 연결되어 있으면 재사용한다. 프로젝트 잠금이 확인되면 Editor를 중복 실행하지 않고 Pipeline 초기화를 대기한다. Safe Mode·CLI 실행 실패·패키지 문제는 중복 실행으로 우회하지 않고 오류를 보고한다.
⑤ 자동 기동 후에는 준비 여부를 시간제한 내에서 확인한다. 자동 시작 또는 초기화에 실패하면 명령 실행을 차단하거나 추가 명령의 오류를 기록한다. 사용자가 직접 종료한 Editor를 작업자가 백그라운드 반복 실행하지 않는다.

제17조 (Worker 단일 EXE 실행 위치와 사용자 설정)

① Worker 프로그램 설정과 전역 실행 데이터는 Windows 사용자별 `%LOCALAPPDATA%\ProjectHub\Worker` 아래에 저장한다. 설정 파일은 `config` 디렉터리의 target-settings.json, worker-selection.json, window-placement.json으로 구분한다. EXE가 어디에서 실행되든 동일 사용자 PC의 동일 경로를 참조해야 하며 EXE 설치 경로에 쓰기 권한이 있다고 가정하지 않는다.
② 기존 배포 형식의 EXE 상대 `Worker\config`가 있으면 누락된 파일만 최초 이전한다. 사용자가 중앙 설정에 이미 저장한 파일을 덮어쓰지 않으며, 구버전 파일을 삭제하지 않는다. 배포 스크립트는 삭제하는 빌드 출력 폴더의 기존 설정을 사전 이전한다.
③ 기존 설정의 JSON 손상이나 접근 오류는 로그에 명시하고, 설정 저장은 임시 파일 작성 후 원자적 교체로 완료한다. 설정 마이그레이션에 실패하면 원래 파일이 보존되도록 한다.
④ 프로젝트 고유의 지속 체크포인트와 실행 이력은 여전히 작업 프로젝트 내 `.projecthub`에, 프로젝트 임시 파일은 프로젝트 내 `temp`에 두며 별도 Git 정책을 따른다.
