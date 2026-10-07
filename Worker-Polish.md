# Worker-Polish — ProjectHub Worker 정책

이 문서는 Worker의 장기 책임과 마일스톤 실행 경계를 정의한다. 실제 AI 출력 문법은 전용 역할·라우팅 계약을 사용한다.

제1조 (역할)

① HQ는 사용자 목표 해석, origin/main 확인, 마일스톤 목표와 독립 WORKITEM 설계, WORK별 TEST ON/OFF, 다음 WORK·PAUSE·END 판단을 담당한다.
② #10+ GENERAL WORK는 배정된 WORKITEM과 WRITE_PATH 안에서 구현을 완결하는 Stateless 실행 단위다.
③ #0 RESOURCE는 GPTWEB을 사용하는 IMAGE 생성 전용 독립 sidecar다.
④ QA는 Worker가 준비한 최신 실행 대상을 사용자 관점에서 동작 확인한다.
⑤ HIGH는 QA issue가 발생했을 때 실제 프로젝트를 조사하고 필요한 최종 보완을 직접 수행한다.
⑥ WORK 배분, build, 복구 반복, QA/HIGH 분기, Git과 HQ 보고는 Worker가 기계적으로 수행한다.
⑦ 활성 역할은 HQ, GENERAL WORK, RESOURCE, QA, HIGH다.

제2조 (HQ 주입과 복구)

① 모든 HQ 호출에는 HQ 역할 계약 전문을 직접 포함한다.
② HQ 설계 입력은 사용자 요청 또는 직전 마일스톤의 축약 보고와 최신 origin/main 기준으로 구성한다.
③ HQ의 WORK 설계 요소는 마일스톤 목표, WORKITEM, WRITE_PATH, WORK별 TEST ON/OFF다.
④ 같은 마일스톤의 GENERAL WORK는 서로의 파일·프로젝트·타입·API·분석·결과·산출물을 선행조건으로 삼지 않는 독립 작업으로 설계한다. 의존 관계가 있으면 하나의 WORKITEM으로 결합한다.
⑤ 쓰기 WORK의 WRITE_PATH는 서로 겹치지 않게 설계하며, Worker는 실제 겹침을 기계적으로 판정한다.
⑥ 이미지 생성은 RESOURCE #0으로 분리하고, 새 RESOURCE 결과를 소비하는 작업은 다음 마일스톤에서 설계한다.
⑦ HQ 응답 파싱 실패는 전용 전체 응답 재요청 1회로 처리한다. Worker는 이전 HQ 응답과 파싱 오류를 참고 데이터로 전달하고 현재 HQ 역할 계약 전문을 다시 주입한다.

제3조 (프로젝트 루트와 작업영역)

① 사용자가 지정한 폴더가 유일한 실제 프로젝트 루트다. Git 저장소가 아니고 HQ가 GIT_INIT을 YES로 지정한 경우 Worker가 preflight에서 `git init -b main`을 수행한다.
② 모든 역할은 같은 실제 프로젝트 루트를 공유하고 GENERAL WORK는 자신의 WRITE_PATH를 쓰기 범위로 사용한다.
③ 현재 WORK의 WRITE_PATH 안에서는 현재 작업이 우선하며 필요한 경우 기존 dirty 변경을 포함해 작업을 완결할 수 있다.
④ WRITE_PATH 밖 파일과 변경은 기존 상태 그대로 보존한다.
⑤ Worker는 현재 마일스톤이 만든 변경목록을 기계 상태로 보존한다.

제4조 (마일스톤 실행)

① HQ WORK 응답을 Worker가 파싱하면 실행 가능한 GENERAL WORK를 즉시 배분한다.
② 독립 GENERAL WORK는 같은 마일스톤 안에서 가능한 범위까지 병렬 실행한다.
③ RESOURCE #0은 독립 sidecar로 진행하고 다른 실행 단계는 RESOURCE 완료 여부와 독립적으로 진행한다. PENDING은 정상 진행 상태다.
④ TEST=ON인 WORK가 하나 이상 있으면 GENERAL WORK 종료 후 Worker가 마일스톤 entrypoint를 build한다.
⑤ build 성공 시 QA로 진행한다.
⑥ build 실패 시 Worker는 마일스톤 목표, 허용 WRITE_PATH, 마지막 build 로그와 `빌드 실패를 수정해주세요` 문구를 포함한 단일 복구 WORK를 생성한다.
⑦ 복구 WORK 종료 후 Worker가 같은 build를 다시 수행한다.
⑧ build 복구는 최대 2회이며 이후에도 실패하면 BUILD_FAILED_FINAL로 확정하고 Git finalize 단계로 진행한다.
⑨ TEST=OFF만 있는 마일스톤은 GENERAL WORK 종료 후 Git finalize 단계로 진행한다.
⑩ build 성공 후 QA passed이면 Git finalize로 진행한다.
⑪ QA issue이면 HIGH를 한 번 호출한 뒤 Git finalize로 진행한다.
⑫ READ_ONLY 정책을 제외한 모든 종료 경로는 Worker의 final commit·push로 수렴한다.

제5조 (Build와 복구 WORK)

① build 명령은 Worker가 entrypoint와 지원되는 프로젝트 형식으로부터 기계적으로 결정한다.
② .NET entrypoint가 .csproj이면 해당 프로젝트를 build하고 Worker 런타임 결과는 프로젝트 루트 `bin`에 둔다.
③ build 실패 로그는 원문으로 복구 WORK에 전달하고 수정 판단은 복구 WORK가 수행한다.
④ 복구 WORK의 쓰기 범위는 현재 마일스톤의 쓰기 가능한 WRITE_PATH 합집합이다.
⑤ build·restore·test·run·publish 같은 기계 실행은 Worker가 소유하고 WORK와 QA에는 준비된 상태를 전달한다.
⑥ build 결과물, temp, .projecthub는 런타임 Git 제외영역으로 관리한다.

제6조 (QA)

① QA는 build 성공 후 TEST가 필요한 경우에 호출한다.
② QA 입력은 마일스톤 목표, 완료된 WORK 결과와 Worker가 준비한 최신 실행 대상이다.
③ QA의 판정 근거는 실제 화면, 입력, 출력, 오류, runtime 상태와 재현 사실이다.
④ 프로젝트 소스 변경 책임은 WORK와 HIGH에 있고, build 준비 책임은 Worker에 있다.
⑤ QA 결과 status는 `passed` 또는 `issue`다.
⑥ `passed`는 확인 범위에서 문제를 발견하지 않은 상태다.
⑦ `issue`는 접근 실패, 실행 실패, 재현 오류 또는 목표와 다른 동작을 관찰한 상태이며 issues에 관찰 사실을 기록한다.

제7조 (HIGH)

① HIGH는 QA가 `issue`를 반환한 경우에 호출한다.
② HIGH는 일반 WORK보다 높은 파일·도구·실행 권한으로 QA 문제와 실제 프로젝트 상태를 조사한다.
③ HIGH는 현재 마일스톤 범위의 일반 소스·설정 파일을 직접 생성·수정·삭제할 수 있다.
④ 이미지 생성 책임은 RESOURCE #0에, Git finalization 책임은 Worker에 있다.
⑤ HIGH의 출력은 수행한 조사, 직접 수정, 남은 문제와 최종 상태를 RESULT로 요약한다.
⑥ HIGH 종료 후 Worker는 추가 검증 단계 없이 Git finalize로 이동한다.

제8조 (Git)

① ProjectHub의 유일한 작업 branch는 `main`이고 원격 기준은 `origin/main`이다.
② Git commit·push는 Worker가 소유한다.
③ GENERAL WORK 종료 직후에는 중간 commit을 만들지 않고 build·복구·QA·HIGH 분기가 끝난 뒤 마일스톤 변경을 한 번 final commit·push한다.
④ HIGH 수정 파일도 같은 final commit 대상에 포함한다.
⑤ READ_ONLY_NO_FILE_CHANGES는 SKIPPED_READ_ONLY로 기록한다.
⑥ push 문제는 main에서 처리한다.
⑦ Git 결과와 관계없이 Worker는 HQ 최종 보고를 생성한다.

제9조 (HQ 최종 보고)

① Worker는 AI 요약자 없이 기계적으로 HQ 보고를 작성한다.
② 보고에는 마일스톤 outcome, HIGH 수행결과 또는 HIGH 미실행 사유, final commit/push 결과와 현재 판단에 필요한 실패 사실을 포함한다.
③ BUILD_FAILED_FINAL은 HIGH_RESULT=SKIPPED_BUILD_FAILED와 마지막 build 실패 요약으로 전달한다.
④ QA passed는 HIGH_RESULT=SKIPPED_QA_PASSED로 전달한다.
⑤ TEST가 없으면 HIGH_RESULT=SKIPPED_TEST_OFF로 전달한다.
⑥ QA issue로 HIGH를 수행한 경우 HIGH RESULT 원문 요약을 전달한다.
⑦ 다음 HQ 입력은 현재 판단에 필요한 축약 상태만 사용한다.

제10조 (PAUSE와 사용자 개입)

① main으로 수렴하지 못했거나 현재 checkout이 main이 아니면 PAUSE한다.
② 지정 작업영역 안의 기존 dirty나 동시 변경은 자체로는 실행 중단 조건이 아니다.
③ PAUSE 상태에서는 지정되지 않은 경로를 기존 상태 그대로 보존한다.
④ 사용자가 필요한 수동 조치를 수행한 뒤 재개할 수 있어야 한다.

제11조 (UI와 설정)

① UI는 HQ, GENERAL WORK, RESOURCE, QA, HIGH와 Worker 기계 상태를 표시한다.
② HQ, GENERAL WORK, QA, HIGH는 각각 독립 Provider·모델 설정을 사용할 수 있다.
③ RESOURCE는 GPTWEB 고정이다.
④ 메인 창의 X 버튼은 트레이 숨김으로 동작하며 명시적 Exit가 종료를 요청한다.

제12조 (문자 인코딩)

① Worker 역할 입력, 전송, JSON, 보고, transcript, 상태 파일과 로그는 UTF-8을 기본으로 한다.
② 새 텍스트 파일은 대상 형식이 별도로 요구하지 않는 한 UTF-8 BOM 없이 기록한다.
③ JSON은 한글과 일반 비ASCII 문자를 실제 Unicode 문자로 기록하고 JSON 문법에 필요한 escaping만 적용한다.
④ Windows PowerShell 5.1에서 텍스트를 읽거나 출력할 때 UTF-8을 명시한다.
⑤ 문자 디코딩 실패는 원문을 보존하고 오류로 보고한다.
