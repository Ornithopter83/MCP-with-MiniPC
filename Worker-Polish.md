# Worker-Polish — ProjectHub Worker 정책

이 문서는 Worker의 장기 책임과 마일스톤 실행 경계를 정의한다. 실제 AI 출력 문법은 전용 역할·라우팅 계약을 사용한다.

제1조 (역할)

① HQ는 사용자 목표 해석, 원격 main 기준 확인, 마일스톤 목표와 독립 WORKITEM 설계, 각 WORKITEM의 테스트 필요 여부, 다음 마일스톤·PAUSE·END 판단만 담당한다.
② #10+ GENERAL WORK는 자신에게 배정된 WORKITEM과 WRITE_PATH만 수행하는 Stateless 구현 단위다.
③ QA는 테스트가 필요한 마일스톤에서 Worker가 준비한 최신 실행 대상을 실제 사용자 관점으로 동작 확인한다. 소스 구조를 검토하거나 수정하지 않는다.
④ HIGH는 QA가 문제를 보고한 경우에만 호출되는 최종 보완 역할이다. 실제 프로젝트 상태와 QA 문제를 조사하고 필요하면 직접 수정하며 수행 결과를 요약한다.
⑤ #0 RESOURCE는 GPTWEB을 사용하는 IMAGE 생성 전용 독립 sidecar다.
⑥ MANAGER 역할은 사용하지 않는다. WORK 배분, build, 복구 반복, QA/HIGH 분기, Git과 HQ 보고는 Worker가 기계적으로 수행한다.
⑦ Worker는 의미 판단 대신 역할 호출, 파싱, 파일 경계, 상태 전이, build, 프로세스와 Git을 관리한다.

제2조 (HQ 강제 주입)

① HQ 역할 계약 전문은 모든 HQ 호출에 직접 주입한다. 최초 호출 이후에도 생략하거나 경로 참조로 대체하지 않는다.
② HQ에는 마일스톤 목표, WORKITEM, WRITE_PATH, WORK별 TEST ON/OFF만 설계하도록 강제한다.
③ HQ는 QA 지시, HIGH 지시, 검토 기준, MANAGER 지시, build/run/publish 명령, 기계 실행 순서를 작성하지 않는다.
④ 같은 마일스톤의 GENERAL WORK는 서로의 파일·프로젝트·타입·API·분석·결과·산출물을 필요로 하지 않는 독립 작업이어야 한다. 그런 의존성이 하나라도 있으면 하나의 WORKITEM으로 결합한다.
⑤ WRITE_PATH가 겹치는 쓰기 WORK는 설계하지 않는다. Worker는 겹침을 발견하면 충돌한 WORK만 blocked 처리하고 나머지를 계속한다.
⑥ 이미지 생성이 필요하면 RESOURCE #0만 사용하고, 그 이미지를 소비하는 작업은 다음 마일스톤으로 분리한다.
⑦ HQ 응답 파싱 실패 시 부분 element 복구나 generic JSON repair를 사용하지 않는다. 같은 HQ 역할에 현재 계약 전문을 다시 주입하여 전체 응답을 1회만 재출력한다.
⑧ HQ 전체 복구에서도 JSON, order, QA 지시, MANAGER, mechanicalInstructions, highInstructions, validation 같은 구형 실행 구조를 다시 허용하지 않는다.

제3조 (프로젝트 루트와 작업영역)

① 사용자가 지정한 폴더가 유일한 실제 프로젝트 루트다. Git 저장소가 아니고 HQ가 GIT_INIT을 YES로 지정한 경우 Worker가 preflight에서 `git init -b main`을 수행한다.
② 실제 프로젝트 파일은 해당 루트에서 직접 생성·수정·삭제한다. WorkItem별 clone, worktree, shadow workspace 또는 별도 branch를 사용하지 않는다.
③ GENERAL WORK는 자신에게 허용된 WRITE_PATH 밖을 수정하지 않는다.
④ 현재 WORK의 WRITE_PATH 안에서는 현재 작업이 우선하며 기존 dirty 변경을 덮어쓸 수 있다.
⑤ WRITE_PATH 밖 파일과 변경은 수정·정리·reset·stage·commit하지 않는다.
⑥ Worker는 현재 마일스톤이 실제로 만든 변경목록을 기계 상태로 보존한다.

제4조 (마일스톤 실행)

① HQ WORK 응답을 받으면 Worker가 기계적으로 파싱하고 MANAGER 호출 없이 GENERAL WORK를 즉시 배분한다.
② 모든 실행 가능한 GENERAL WORK는 같은 마일스톤 안에서 가능한 범위까지 병렬 실행한다.
③ RESOURCE #0은 독립 sidecar로 실행하며 GENERAL WORK, build, QA, HIGH 또는 Git을 기다리게 하지 않는다. PENDING은 실패가 아니다.
④ TEST=ON인 WORK가 하나 이상 있으면 GENERAL WORK 종료 후 Worker가 마일스톤 entrypoint를 기계적으로 build한다.
⑤ build가 성공하면 QA를 호출한다.
⑥ build가 실패하면 Worker는 오류를 해석하지 않고 build 실패 로그와 정확한 문구 `빌드 실패를 수정해주세요`를 포함한 단일 복구 WORK를 생성한다.
⑦ 복구 WORK는 현재 마일스톤의 쓰기 가능한 WRITE_PATH 합집합 안에서만 작업한다. 복구 WORK 종료 후 Worker가 같은 build를 다시 수행한다.
⑧ build 복구는 최대 2회다. 두 번의 복구 뒤에도 build가 실패하면 BUILD_FAILED_FINAL로 확정하고 QA와 HIGH를 호출하지 않는다.
⑨ TEST=OFF만 있는 마일스톤은 build, QA, HIGH를 모두 생략한다.
⑩ build 성공 후 QA가 문제없음 결과를 반환하면 HIGH를 생략한다.
⑪ QA가 문제 결과를 반환한 경우에만 HIGH를 호출한다.
⑫ HIGH 종료 후에는 HIGH 수정 여부와 관계없이 추가 WORK, build, QA 또는 HIGH 반복을 만들지 않는다.
⑬ 모든 종료 경로에서 READ_ONLY 정책이 아닌 한 Worker가 현재 마일스톤 변경을 main에 강제 commit·push한다.

제5조 (Build와 복구 WORK)

① build 명령은 AI가 생성하지 않는다. Worker가 entrypoint와 지원되는 프로젝트 형식으로부터 기계적으로 결정한다.
② .NET entrypoint가 .csproj이면 해당 프로젝트를 build하고 Worker 런타임 결과는 프로젝트 루트 `bin`에 둔다.
③ build 실패 로그는 복구 WORK에 원문을 전달하되 Worker가 오류 원인, 수정 파일 또는 해결책을 판단하지 않는다.
④ 복구 WORK의 프롬프트에는 최소한 마일스톤 목표, 허용 WRITE_PATH, `빌드 실패를 수정해주세요`, 마지막 build 실패 로그를 포함한다.
⑤ WORK와 QA는 build·restore·test·run·publish를 임의로 수행하지 않는다. build는 Worker 기계 단계 소유다.
⑥ build 결과물, temp, .projecthub는 런타임 Git 제외영역으로 관리하고 tracked .gitignore를 Worker가 자동 수정하지 않는다.

제6조 (QA)

① QA는 build 성공 후 TEST가 필요한 경우에만 호출한다.
② QA는 마일스톤 목표와 완료된 WORK 결과를 기준으로 준비된 최신 실행 대상을 실제로 실행·조작하고 사용자 관점의 동작만 확인한다.
③ QA는 build, restore, publish, compile 또는 빌드를 유발하는 프로젝트 도구 명령을 수행하지 않는다.
④ QA는 소스·설계·정적 의존성을 판정하거나 프로젝트 파일을 수정하지 않는다.
⑤ QA 결과 status는 `passed` 또는 `issue`만 사용한다.
⑥ `passed`는 확인한 범위에서 문제를 발견하지 않았다는 뜻이며 즉시 HIGH를 생략한다.
⑦ 접근 실패, 실행 실패, 재현 오류 또는 동작 문제는 `issue`로 보고하고 관찰 사실을 issues에 기록한다.

제7조 (HIGH)

① HIGH는 QA가 `issue`를 반환한 경우에만 호출한다.
② HIGH는 일반 WORK보다 높은 파일·도구·실행 권한으로 QA 문제와 실제 프로젝트 상태를 조사한다.
③ HIGH는 필요하면 현재 마일스톤 범위의 일반 소스·설정 파일을 직접 생성·수정·삭제할 수 있다.
④ HIGH는 RESOURCE를 생성·편집·대체하지 않고 Git commit·push·branch 변경을 수행하지 않는다.
⑤ HIGH는 다음 마일스톤, QA 재호출, 추가 WORK 또는 재검증을 요청하지 않는다.
⑥ HIGH는 수행한 조사, 직접 수정, 해결하지 못한 문제와 최종 상태를 RESULT로 요약한다.
⑦ HIGH 종료 후 Worker는 추가 검증 없이 Git finalize로 이동한다.

제8조 (Git)

① ProjectHub의 유일한 작업 branch는 `main`이다. 다른 branch를 작업 대상으로 인정하지 않는다.
② 원격 기준은 오직 `origin/main`이다.
③ WORK, RESOURCE, QA와 HIGH는 Git commit·push를 수행하지 않는다.
④ GENERAL WORK 종료 직후 중간 commit/push barrier를 만들지 않는다.
⑤ 마일스톤의 build/복구/QA/HIGH 분기가 모두 종료된 뒤 Worker가 현재 마일스톤 변경을 main에 한 번 강제 commit·push한다.
⑥ HIGH가 수정한 파일도 현재 마일스톤 변경목록에 포함해 같은 final commit 대상으로 처리한다.
⑦ READ_ONLY_NO_FILE_CHANGES이면 commit·push를 생략하고 SKIPPED_READ_ONLY로 기록한다.
⑧ push 문제는 main에 한해서 처리하며 다른 branch로 우회하지 않는다.
⑨ Git 실패 여부와 관계없이 HQ 최종 보고는 생략하지 않는다.

제9조 (HQ 최종 보고)

① Worker는 AI 요약자 없이 기계적으로 HQ 보고를 작성한다.
② HQ에는 개별 WORK 로그, 전체 build 로그, QA 성공 세부사항을 반복 주입하지 않는다.
③ 보고에는 마일스톤 outcome, HIGH 수행결과 또는 HIGH 미실행 사유, final commit/push 결과와 현재 판단에 필요한 실패 사실만 포함한다.
④ BUILD_FAILED_FINAL이면 HIGH_RESULT를 SKIPPED_BUILD_FAILED로 표시하고 마지막 build 실패 사실을 축약해서 전달한다.
⑤ QA passed이면 HIGH_RESULT를 SKIPPED_QA_PASSED로 표시한다.
⑥ TEST가 없으면 HIGH_RESULT를 SKIPPED_TEST_OFF로 표시한다.
⑦ QA issue로 HIGH를 수행한 경우 HIGH RESULT 원문 요약을 전달한다.
⑧ 이전 마일스톤에서 종결된 세부 로그는 다음 HQ 입력에 누적하지 않는다.

제10조 (PAUSE와 사용자 개입)

① main으로 수렴하지 못했거나 현재 checkout이 main이 아니면 PAUSE한다.
② 지정 작업영역 안의 기존 dirty나 동시 변경만으로 PAUSE하지 않는다.
③ PAUSE 상태에서 지정되지 않은 경로를 reset·강제 checkout·삭제하지 않는다.
④ 사용자가 필요한 수동 조치를 수행한 뒤 재개할 수 있어야 한다.

제11조 (UI와 설정)

① UI는 HQ, GENERAL WORK, RESOURCE, QA, HIGH와 Worker 기계 상태를 표시한다.
② MANAGER 역할 설정과 실행 단계를 사용자에게 노출하지 않는다.
③ HQ, GENERAL WORK, QA, HIGH는 각각 독립 Provider·모델 설정을 사용할 수 있다.
④ RESOURCE는 GPTWEB 고정이다.
⑤ 메인 창의 X 버튼은 Worker 종료가 아니라 트레이 숨김으로 동작하며 명시적 Exit만 종료를 요청한다.

제12조 (문자 인코딩)

① Worker 역할 입력, 전송, JSON, 보고, transcript, 상태 파일과 로그는 UTF-8을 기본으로 한다.
② 새 텍스트 파일은 대상 형식이 별도로 요구하지 않는 한 UTF-8 BOM 없이 기록한다.
③ JSON의 한글과 일반 비ASCII 문자는 실제 Unicode 문자로 직렬화하고 불필요하게 `\\uXXXX`로 변환하지 않는다.
④ Windows PowerShell 5.1에서 텍스트를 읽거나 출력할 때 UTF-8을 명시한다.
⑤ 문자 디코딩 실패를 다른 코드페이지로 임의 재해석하여 정상 결과처럼 처리하지 않는다.
