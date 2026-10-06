# Worker-Polish — ProjectHub Worker 정책

이 문서는 Worker의 장기 책임과 새 마일스톤 실행 경계를 정의한다. 실제 AI 출력 문법은 전용 역할·라우팅 계약을 사용한다.

제1조 (역할)

① HQ는 사용자 목표 해석, 프로젝트 전체 관제, 마일스톤 설계와 세부설계, WorkItem 작업영역과 최소 실행 순서, QA 수행 여부, 완료 기준과 다음 마일스톤 판단을 담당한다.
② #1 중간관리자는 HQ의 세부설계를 실제 실행으로 연결하고 WorkItem 배정, 결과 취합, build·run·publish, HIGH 결과에 따른 후속 작업, 마일스톤 Git 처리와 HQ 최종 보고를 담당한다.
③ QA는 HQ가 예약한 경우에만 실행하며 HQ가 지정한 프로그램·웹과 조사 범위를 실제로 확인하여 사실 기반 결과를 반환한다. QA는 코드 수정, 설계 변경, 다음 작업 결정이나 HIGH 호출을 하지 않는다.
④ HIGH는 WorkItem 실행 묶음과 예약된 QA가 끝난 뒤 호출되는 높은 권한의 마일스톤 검증·보완 역할이다. 실제 프로젝트 상태를 조사하고 필요한 경우 직접 수정할 수 있으나 HQ 설계 자체나 다음 마일스톤을 결정하지 않는다.
⑤ #0 RESOURCE는 GPTWEB을 사용하는 RESOURCE MAKE 전용 역할이다. RESOURCE 결과는 마일스톤 안의 작업 결과로 취급하고 QA와 HIGH가 확인할 수 있어야 한다.
⑥ #10+ GENERAL WORK는 자신에게 배정된 작업과 작업영역만 수행하는 Stateless 실행 단위다.
⑦ Worker는 의미 판단 대신 역할 호출, 세션, 상태, 프로세스, 파일 경계, Git과 기계 작업을 관리한다.

제2조 (역할 설정)

① HQ, #1 중간관리자, QA, HIGH와 GENERAL WORK는 설정창에서 각각 독립적으로 Provider·모델을 선택할 수 있어야 한다.
② RESOURCE는 GPTWEB 고정이며 별도 Provider·모델 선택을 제공하지 않는다.
③ 실행 중 역할의 Provider·모델을 Worker가 임의로 다른 모델로 교체하지 않는다.
④ 실행 중 설정은 잠그고 다음 새 작업 상태에서 변경할 수 있게 하는 것을 기본으로 한다.
⑤ 역할 계약 전문은 동일 AI 세션의 최초 호출에만 직접 주입한다. 같은 세션의 후속 호출은 ProjectHub Git 저장소의 해당 계약 파일 경로·파일명을 명시적으로 참조하고 전문을 반복 주입하지 않는다. 새 세션이거나 계약이 갱신되어 다시 bootstrap해야 할 때만 전문을 다시 주입한다.

제3조 (프로젝트 루트와 작업영역)

① 사용자가 지정한 폴더가 유일한 실제 프로젝트 루트다. Git 저장소가 아니고 HQ가 `initializeGitIfMissing`을 true로 지정하면 Worker가 마일스톤 preflight에서 기계적으로 `git init -b main`을 수행하여 처음부터 main으로 초기화한다. 그 외에는 Git 저장소가 준비되어 있어야 한다.
② 프로젝트의 실제 폴더·파일 구조는 해당 루트에서 직접 생성·수정·삭제한다. WorkItem별 clone, worktree, shadow workspace 또는 별도 작업 branch를 실제 프로젝트 작업공간으로 사용하지 않는다.
③ HQ는 마일스톤 설계 단계에서 동시에 실행될 WorkItem의 생성·수정·삭제 영역이 겹치지 않도록 설계한다.
④ 같은 마일스톤 안의 WorkItem dependency는 최대한 배제하고, 한 결과가 다른 작업의 전제가 되는 경우 가능한 한 다음 마일스톤과의 선후 관계로 분리한다. 같은 마일스톤에서 불가피한 경우만 최소 실행 순서를 둔다.
⑤ Worker는 HQ가 명시한 작업영역과 현재 마일스톤이 실제로 만든 생성·수정·삭제 변경목록을 기계 상태로 보존한다.
⑥ 현재 ProjectHub 작업이 우선이다. HQ가 지정한 WRITE_PATH, RESOURCE targetPath 또는 HIGH가 명시한 CHANGED_PATH 안에 기존 dirty 변경이나 실행 중 새 변경이 있어도 그 이유로 PAUSE하지 않으며 현재 작업이 해당 내용을 덮어쓸 수 있다.
⑦ 지정된 작업영역 밖의 파일과 변경은 수정·정리·reset·stage·commit하지 않는다. 작업 도중 외부에서 지정된 작업영역 안에 끼워 넣은 변경은 보존을 보장하지 않는다.
⑧ Worker는 파일 변경의 작성자를 지속적으로 판정하거나 의미 검증하지 않는다. 프로젝트 전체가 dirty라는 이유만으로 작업을 차단하지 않는다.

제4조 (마일스톤 실행)

① HQ는 전용 계약의 `[ACTION=...]` 다음 단일 JSON 객체로 마일스톤 설계, WorkItem, 작업영역, QA 예약, RESOURCE와 필요한 entrypoint 정보를 전달한다.
② Worker는 ACTION과 JSON envelope를 기계적으로 파싱한다. HQ의 추가 설명과 세부 지시는 JSON 내부 필드에 포함하며 JSON 밖의 별도 BODY/END_ACTION 형식은 사용하지 않는다.
③ 중간관리자는 HQ 설계를 받아 GENERAL WORK와 RESOURCE를 실행시킨다.
④ 모든 계획된 작업은 성공·실패와 관계없이 terminal 상태가 되어야 현재 실행 묶음이 끝난 것으로 본다.
⑤ HQ가 QA를 예약했다면 Worker는 HQ 지시를 기계적으로 파싱하여 HIGH 호출 직전에 QA를 삽입한다. QA 필요 여부를 Worker, 중간관리자 또는 HIGH가 새로 판단하지 않는다.
⑥ 예약된 QA가 없거나 QA 실행이 끝나면 HIGH를 호출한다.
⑦ HIGH 결과를 받은 중간관리자는 HQ 설계 범위에서 후속 작업을 수행하거나 현재 마일스톤 실행을 종료할 수 있다. 후속 실행 묶음도 같은 QA 예약과 HIGH 검증 흐름을 따른다.
⑧ 마일스톤의 모든 작업이 성패와 관계없이 끝나고 중간관리자가 Git 처리를 마치면 HQ에 반드시 최종 보고한다.
⑨ HQ는 이전 마일스톤의 성공·실패 여부와 관계없이 보고된 실제 변경, QA/HIGH 결과와 Git 상태를 기준으로 다음 지시를 만든다.

제5조 (RESOURCE)

① RESOURCE Web transport는 현재 IMAGE 생성 경로를 사용하며 GPTWEB에 고정한다.
② RESOURCE 결과는 먼저 `<project-root>/temp/Resource`에 저장하고 작업 이력을 남긴다.
③ RESOURCE가 완료되어 프로젝트에 반영할 때는 복사본을 남기지 않고 HQ가 지정한 최종 프로젝트 경로로 move한다.
④ 최종 목적지가 HQ가 지정한 targetPath이면 현재 RESOURCE 작업이 우선하며 기존 파일이 있어도 최종 결과로 덮어쓴다.
⑤ RESOURCE 결과와 보고는 일반 WorkItem 결과와 함께 현재 마일스톤 결과로 취급한다.
⑥ RESOURCE 실패를 다른 임의 생성 경로로 자동 우회하지 않는다.

제6조 (Build, Run, Publish와 프로세스)

① build·run·publish의 의미적 수행과 완료 판단은 중간관리자의 책임이다. Worker는 중간관리자가 요청한 기계 실행과 프로세스 제어를 수행한다.
② HQ는 QA가 확인해야 할 실행파일 경로나 프로젝트 entrypoint를 마일스톤 설계에 미리 지정할 수 있고, 중간관리자는 QA 전에 해당 실행 대상을 준비한다.
③ build·publish 최종 결과는 `<project-root>/bin`에 두고 가장 최근 상태만 유지한다. 실행별·시점별 결과를 별도 영구 경로에 누적하지 않는다.
④ `.projecthub/artifacts` 같은 별도 게시 artifact 저장소를 사용하지 않는다.
⑤ `bin/`, `temp/`, `.projecthub/`는 ProjectHub 런타임 Git 제외영역으로 사용한다. Worker는 이를 위해 tracked `.gitignore`를 자동 수정하지 않고 저장소 로컬 `.git/info/exclude`를 사용한다.
⑥ 결과 파일의 최신 변경 시각 같은 기계 사실은 필요 시 참고할 수 있으나 Worker가 제품 품질을 의미적으로 판정하지 않는다.
⑦ 역할 또는 build·run·QA가 시작한 외부 실행 프로세스는 해당 보고 시점에 종료되어 있어야 한다. 남아 있으면 Worker가 관리하는 프로세스 tree를 강제 종료하는 것을 원칙으로 한다.
⑧ ProjectHub/Worker 프로세스 자신은 child Job Object에 넣지 않고, Worker가 관리하는 외부 프로세스는 KILL_ON_JOB_CLOSE 경계에 연결한다.

제7조 (HIGH)

① HIGH는 일반 WORK보다 높은 파일·도구·실행 권한으로 현재 마일스톤을 조사할 수 있다.
② HIGH는 필요하면 프로젝트 파일을 직접 수정하여 마일스톤 문제를 보완할 수 있다.
③ HIGH는 HQ 세부설계, WORK/RESOURCE 결과, QA 결과가 있는 경우 그 결과와 실제 프로젝트 상태를 함께 검토한다.
④ HIGH는 HQ의 설계를 다른 목표로 바꾸거나 다음 마일스톤을 결정하지 않는다.
⑤ HIGH는 QA를 호출·재호출하거나 QA 필요 여부를 판단하지 않는다.
⑥ HIGH는 수행한 조사, 직접 수정, 남은 문제와 현재 검증 결과를 중간관리자에게 보고한다.

제8조 (Git)

① Git 저장소는 ProjectHub 작업의 필수 전제다. 저장소가 없고 HQ가 초기화를 명시한 경우 Worker가 준비한다. origin이 없거나 push가 불가능한 것은 마일스톤 실행을 막지 않고 Git 결과에 사실대로 보고한다.
② ProjectHub가 사용하는 유일한 작업 branch는 `main`이다. AUTO, master, feature/*, projecthub/*, checkpoint, integration 또는 그 밖의 branch를 작업 대상으로 인정하지 않는다.
③ 새 저장소는 `git init -b main`으로 초기화한다. 호환성 fallback이 필요하더라도 최종 HEAD는 반드시 `refs/heads/main`이어야 하며 다른 branch 이름을 허용하지 않는다.
④ 기존 저장소에서 local main이 있으면 main으로 전환하고, local main은 없지만 origin/main이 있으면 origin/main을 추적하는 local main만 준비한다. local/remote main이 모두 없으면 현재 branch 이름을 main으로 변경하여 수렴시킨다. main으로 수렴하지 못하면 PAUSE하며 다른 branch에서 작업하지 않는다.
⑤ HQ, 사용자 입력 또는 다른 역할이 main 이외의 branch를 지정해도 실행하지 않는다. 마일스톤 계약 단계에서 오류로 처리한다.
⑥ 일반 WORK, RESOURCE, QA와 HIGH는 마일스톤 단위 Git 결과 확정을 대신하지 않는다. 최종 commit·push는 Worker가 main에 대해서만 기계적으로 수행한다.
⑦ WorkItem별 별도 branch, checkpoint branch, Integration branch와 별도 원격 result branch를 생성하거나 사용하지 않는다.
⑧ 마일스톤 종료 시 Worker는 HQ가 지정한 WRITE_PATH와 RESOURCE targetPath, HIGH가 명시한 CHANGED_PATH 안에서 현재 마일스톤 변경목록에 기록된 생성·수정·삭제만 stage하여 main에 하나의 마일스톤 commit을 만든다. 지정되지 않은 경로는 dirty여도 stage·commit하지 않는다. 프로젝트 전체를 무조건 stage하지 않는다.
⑨ commit 또는 push 문제가 있으면 main에 한해서 fetch/rebase와 재시도를 수행할 수 있다. 다른 branch로 우회하거나 다른 branch에 push하지 않는다.
⑩ 원격 서비스 정책, 권한, 네트워크 또는 실제 Git 제약 때문에 main push가 불가능하거나 반복 해결에 실패한 경우 현재 commit SHA, 로컬/원격 상태와 실패 사실을 HQ에 보고한다.
⑪ push 성공 여부와 무관하게 마일스톤의 모든 작업이 끝났다면 HQ 최종 보고는 생략하지 않는다.

제9조 (보고과 상태)

① WORK, RESOURCE, QA와 HIGH의 보고는 Worker가 의미적으로 다시 작성하지 않고 필요한 기계 메타데이터와 함께 보존한다.
② 현재 마일스톤 임시정보에는 최소한 HQ 지시, HQ 세부설계, 현재 마일스톤 변경목록, WorkItem/RESOURCE 결과, 최신 QA 결과와 최신 HIGH 결과를 둘 수 있다.
③ QA와 HIGH의 과거 검증 cycle 전체를 영구 이력으로 누적하는 것을 기본으로 하지 않는다. 장기적으로 필요한 사실은 프로젝트 문서나 HQ 관제 맥락에서 유지한다.
④ 진행 중 마일스톤의 복잡한 snapshot·rollback을 필수로 두지 않는다. 장애 후 상태가 애매하면 HQ가 현재 로컬 상태를 보고 재수행 여부를 판단한다.
⑤ 중간관리자의 HQ 최종 보고에는 현재 로컬 변경 상태, WORK/RESOURCE 결과, QA 결과가 있으면 그 결과, HIGH 결과, 후속 작업, commit·push 결과와 참조 가능한 commit SHA를 포함한다.

제10조 (PAUSE와 사용자 개입)

① 지정된 작업영역 안의 dirty 변경이나 동시 변경은 현재 작업보다 우선하지 않으며 그 이유로 PAUSE하지 않는다.
② main으로 수렴하지 못했거나 현재 checkout이 main이 아니면 PAUSE한다. 다른 branch에서 작업을 계속하지 않는다.
③ PAUSE 상태에서 Worker가 지정되지 않은 경로를 reset·강제 checkout·삭제하여 해결하지 않는다.
④ 사용자가 하네스 없이 필요한 수동 조치를 수행한 뒤 재개할 수 있어야 한다.
⑤ 사용자 파일 관리 실수나 프로젝트 자체의 잘못된 Git 설정을 모두 예측해 선제 차단하는 것을 목표로 하지 않는다.

제11조 (UI와 설정)

① UI는 HQ, #1 중간관리자, GENERAL WORK, RESOURCE, QA, HIGH와 실제 실행 상태를 표시한다.
② HQ, #1 중간관리자, QA, HIGH와 GENERAL WORK의 Provider·모델 설정을 각각 노출한다.
③ RESOURCE는 GPTWEB 고정으로 표시하고 Provider·모델 선택 UI를 두지 않는다.
④ 사용하지 않는 역할이나 transport 설정 UI를 유지하지 않는다.
⑤ 메인 창의 X 버튼은 Worker 종료가 아니라 트레이 숨김으로 동작하며 명시적 Exit만 Worker 종료를 요청한다.
