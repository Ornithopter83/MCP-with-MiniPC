# Worker-Polish — ProjectHub Worker 정책

이 문서는 Worker의 장기 책임과 마일스톤 실행 경계를 정의한다. 실제 AI 출력 문법은 전용 역할·라우팅 계약을 사용한다.

제1조 (역할)

① HQ는 사용자 목표 해석, 마일스톤 목표와 독립 WORKITEM 설계, WORK별 TEST ON/OFF, 다음 WORK·PAUSE·END 판단을 담당한다.
② #10+ GENERAL WORK는 배정된 WORKITEM과 WRITE_PATH 안에서 구현을 완결하는 Stateless 실행 단위다.
③ #0 RESOURCE는 GPTWEB을 사용하는 IMAGE 생성 전용 독립 sidecar다.
④ QA는 TEST가 필요한 마일스톤의 실제 동작을 확인하고 passed, issue, blocked 중 하나를 반환한다.
⑤ HIGH는 QA issue가 발생했을 때 실제 프로젝트를 조사하고 필요한 최종 보완을 한 번 수행한다.
⑥ Worker는 WORKITEM 배분, 역할 상태 라우팅, timeout/block 처리, Git finalization과 HQ 보고를 기계적으로 수행한다.
⑦ 활성 역할은 HQ, GENERAL WORK, RESOURCE, QA, HIGH다.

제2조 (HQ 주입과 하위 역할 전달)

① 모든 HQ 호출에는 HQ 역할 계약 전문을 직접 포함한다.
② HQ 설계 입력은 사용자 요청 또는 직전 마일스톤의 축약 보고와 최신 원격 기준으로 구성한다.
③ HQ의 WORK 설계 요소는 마일스톤 목표, WORKITEM, WRITE_PATH, WORK별 TEST ON/OFF다.
④ 같은 마일스톤의 GENERAL WORK는 서로의 파일·프로젝트·타입·API·분석·결과·산출물을 선행조건으로 삼지 않는 독립 작업으로 설계한다. 의존 관계가 있으면 하나의 WORKITEM으로 결합한다.
⑤ 쓰기 WORK의 WRITE_PATH는 서로 겹치지 않게 설계하며 Worker는 실제 겹침을 기계적으로 판정한다.
⑥ 이미지 생성은 RESOURCE #0으로 분리하고 새 RESOURCE 결과를 소비하는 작업은 다음 마일스톤에서 설계한다.
⑦ HQ 응답 파싱 실패는 전용 전체 응답 재요청 1회로 처리한다. Worker는 이전 HQ 응답과 파싱 오류를 참고 데이터로 전달하고 현재 HQ 역할 계약 전문을 다시 주입한다.
⑧ HQ 설계 결과를 하위 역할에 전달할 때 Worker는 해당 역할의 작업에 필요한 목표·지시·범위·결과만 추려 전달하고 Git·세션·임시경로·상태전이 정보는 Worker 내부 상태로 유지한다.

제3조 (프로젝트 루트와 작업영역)

① 사용자가 지정한 폴더가 유일한 실제 프로젝트 루트다. Git 저장소가 아니고 HQ가 GIT_INIT을 YES로 지정한 경우 Worker가 preflight에서 `git init -b main`을 수행한다.
② 모든 역할은 같은 실제 프로젝트 루트를 공유하고 GENERAL WORK는 자신의 WRITE_PATH를 쓰기 범위로 사용한다.
③ WRITE_PATH 밖 파일과 변경은 기존 상태 그대로 보존한다.
④ Worker는 마일스톤 시작과 종료의 파일 상태를 기계적으로 비교하여 이번 마일스톤의 실제 변경을 계산한다.

제4조 (마일스톤 실행)

① HQ WORK 응답을 파싱하면 실행 가능한 GENERAL WORK를 즉시 배분한다.
② 독립 GENERAL WORK는 같은 마일스톤 안에서 가능한 범위까지 병렬 실행한다.
③ RESOURCE #0은 독립 sidecar로 진행하고 다른 실행 단계는 RESOURCE 완료 여부와 독립적으로 진행한다. PENDING은 정상 진행 상태다.
④ GENERAL WORK가 blocked를 반환하면 Worker는 같은 WORKITEM에 이전 RESULT와 CLI 실패 진단을 참고 데이터로 전달하고 WORKER-SANDBOX.md를 첨부하여 1회만 재실행한다. 이 복구는 RESOURCE에는 적용하지 않는다.
⑤ 위 재실행 후에도 blocked이거나 역할 실행이 timeout이면 QA와 HIGH를 생략하고 Git finalize 단계로 진행한다.
⑥ 모든 GENERAL WORK가 completed이고 TEST=OFF만 있으면 Git finalize 단계로 진행한다.
⑦ 모든 GENERAL WORK가 completed이고 TEST=ON이 하나 이상 있으면 QA를 한 번 호출한다.
⑧ QA passed이면 Git finalize 단계로 진행한다.
⑨ QA issue이면 HIGH를 한 번 호출한 뒤 Git finalize 단계로 진행한다.
⑩ QA blocked 또는 timeout이면 HIGH를 생략하고 Git finalize 단계로 진행한다.
⑪ HIGH는 completed, blocked 또는 timeout 결과와 관계없이 한 번의 호출 뒤 Git finalize 단계로 진행한다.
⑫ READ_ONLY 정책을 제외한 모든 종료 경로는 Worker의 final commit·push로 수렴한다.

제5조 (역할 응답과 복구)

① WORK, QA, HIGH는 고정 필드와 @@SECTION 기반 평문 annotation으로 Worker와 통신한다.
② WORK status는 completed 또는 blocked다.
③ QA status는 passed, issue 또는 blocked다.
④ HIGH status는 completed 또는 blocked다.
⑤ 역할 실행 timeout은 Worker가 timeout 상태로 기록한다.
⑥ 역할 응답을 파싱할 수 없으면 같은 역할에 현재 역할 계약 전문과 이전 응답을 다시 주입하여 전체 응답을 1회 재요청한다.
⑦ 재요청 후에도 파싱할 수 없으면 blocked로 처리한다.
⑧ Worker는 역할의 SUMMARY나 ISSUES 내용을 품질 판단에 사용하지 않고 status만 상태전이에 사용한다.

제6조 (QA와 HIGH)

① QA는 TEST가 필요한 마일스톤에서 실제 프로그램·웹을 실행하고 필요하면 build, test, run 절차를 수행하여 동작을 확인한다.
② QA가 프로그램 문제를 확인하면 issue를 반환하고 실행환경 또는 도구 문제로 확인 자체가 불가능하면 blocked를 반환한다.
③ HIGH는 QA issue에 대해서만 호출한다.
④ HIGH는 실제 프로젝트를 조사하고 필요한 소스·설정 변경을 직접 수행할 수 있다.
⑤ HIGH 종료 후 Worker는 추가 QA나 별도 품질 판정 없이 Git finalize로 이동한다.

제7조 (Git)

① ProjectHub의 유일한 작업 branch는 `main`이고 원격 기준은 `origin/main`이다.
② Git commit·push는 Worker가 소유한다.
③ Worker는 마일스톤 시작과 종료의 실제 파일 차이를 기준으로 commit 대상을 계산하고 지정된 작업 범위 밖 변경은 보존한다.
④ AI 역할이 보고한 CHANGED_PATHS는 설명용이며 Git commit 귀속의 단독 근거로 사용하지 않는다.
⑤ READ_ONLY_NO_FILE_CHANGES는 SKIPPED_READ_ONLY로 기록한다.
⑥ Git 결과와 관계없이 Worker는 HQ 최종 보고를 생성한다.

제8조 (HQ 최종 보고)

① Worker는 AI 요약자 없이 기계적으로 HQ 보고를 작성한다.
② 정상 완료 시에는 역할 상태와 Git 결과를 축약해서 전달한다.
③ WORK, QA, HIGH가 blocked 또는 timeout이면 해당 역할 id, stage, SUMMARY와 ISSUES를 다음 판단에 필요한 범위로 전달한다.
④ QA issue 후 HIGH가 수행되면 HIGH 결과의 SUMMARY와 ISSUES를 전달한다.
⑤ 다음 HQ 입력은 현재 판단에 필요한 축약 상태만 사용한다.

제9조 (PAUSE와 사용자 개입)

① main으로 수렴하지 못했거나 현재 checkout이 main이 아니면 PAUSE한다.
② 지정 작업영역 안의 기존 dirty나 동시 변경은 자체로는 실행 중단 조건이 아니다.
③ PAUSE 상태에서는 지정되지 않은 경로를 기존 상태 그대로 보존한다.
④ 사용자가 필요한 수동 조치를 수행한 뒤 재개할 수 있어야 한다.

제10조 (UI와 설정)

① UI는 HQ, GENERAL WORK, RESOURCE, QA, HIGH와 Worker 기계 상태를 표시한다.
② 작업 이력 카드는 실제 작업 단위마다 하나를 유지하고 분배·진행·응답은 같은 카드의 상태와 내용으로 갱신한다.
③ Worker 내부 기계 이벤트는 transcript와 로그에 기록하고 별도 작업 카드로 확장하지 않는다.
④ HQ, GENERAL WORK, QA, HIGH는 각각 독립 Provider·모델 설정을 사용할 수 있다.
⑤ RESOURCE는 GPTWEB 고정이다.
⑥ 메인 창의 X 버튼은 트레이 숨김으로 동작하며 명시적 Exit가 종료를 요청한다.

제11조 (문자 인코딩)

① Worker 역할 입력, 전송, 보고, transcript, 상태 파일과 로그는 UTF-8을 기본으로 한다.
② 새 텍스트 파일은 대상 형식이 별도로 요구하지 않는 한 UTF-8 BOM 없이 기록한다.
③ JSON은 Worker 내부 직렬화에 사용할 수 있으며 한글과 일반 비ASCII 문자를 실제 Unicode 문자로 기록한다.
④ Windows PowerShell 5.1에서 텍스트를 읽거나 출력할 때 UTF-8을 명시한다.
⑤ 문자 디코딩 실패는 원문을 보존하고 오류로 보고한다.
