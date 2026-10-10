# Worker-Polish — ProjectHub Worker 정책

이 문서는 Worker의 장기 책임, 사용자 작업과 WORKITEM의 생명주기, 마일스톤 실행 경계를 정의한다. 역할의 출력 문법과 필드 제한은 전용 라우팅 계약, 기계적 판정의 세부 구현은 코드와 테스트를 따른다.

제1조 (역할과 책임)

① HQ는 사용자 목표 해석, 최초 계획, 마일스톤·WORKITEM 설계, QA 조사 지시와 다음 WORK·PAUSE·END 판단을 담당한다.
② #10+ GENERAL WORK는 지정된 WORKITEM과 WRITE_PATH 안에서 구현을 수행한다. WORKITEM은 마일스톤을 넘어 이어질 수 있고, 동일 WORKITEM의 실행 세션과 진행 상태는 Worker가 보존한다.
③ #0 RESOURCE는 GPTWEB 이미지 생성 전용의 독립 sidecar다.
④ QA는 예약된 마일스톤의 실제 동작과 실행 증거를 조사하고 passed, issue, blocked 중 하나를 반환한다.
⑤ HIGH는 마일스톤 WORK 실행 묶음이 종료된 뒤 고급 검토와 필요한 보완을 수행한다. QA의 issue 여부에만 종속되지 않는다.
⑥ Worker는 역할 배정과 병렬 실행, 세션·체크포인트, 기계적 BUILD·Git, 오류·중단 상태와 HQ 보고를 관리한다.
⑦ 현재 실행 역할은 HQ, GENERAL WORK, RESOURCE, QA, HIGH다. 과거의 JUDGE·OBSERVATION·#1 중간관리자 설계는 현재 실행 필수 단계가 아니다.

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
