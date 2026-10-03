# ProjectHub 구현 로드맵

갱신일: 2026-10-03

상위 공통 정책은 `Master-Polish.md`이며 프로젝트별 장기 정책과 전용 계약이 이 문서보다 우선한다. 날짜별 변경 이력과 과거 구현 경로는 `tasks/*.md`, 전용 기록 문서와 Git 이력에 둔다.

제1조 (현재 정책 기준 구조)

① 사용자 목표의 의미 해석과 작업 분해는 HQ가 담당하고 Worker는 승인된 WorkGraph를 기계적으로 실행한다.

```text
                         ┌─ NORMAL WORK ───────┐
USER -> HQ -> WorkGraph ─┼─ NORMAL WORK ───────┼─> INTEGRATION WORK -> HQ
                         └─ NORMAL WORK ───────┘
                               │
                               ├─ RESOURCE sidecar
                               ├─ JUDGE sidecar
                               └─ OBSERVATION sidecar
```

② 일반 WorkItem은 #10부터 사용하고 #0은 RESOURCE 전용으로 사용하며 #1~#9는 예약 영역으로 둔다.
③ maxConcurrentWork 1~8은 같은 Scheduler 실행 경로를 사용하며 차이는 동시 슬롯 수뿐이다.
④ RESOURCE, JUDGE, OBSERVATION은 일반 WorkItem으로 자동 승격하지 않고 현재 요청 문맥에 귀속되는 sidecar로 유지한다.
⑤ HQ와 WORK의 정확한 ACTION, GOTO, WORK_GRAPH_PATCH, WORK_ITEM_STATUS 및 RESOURCE_TYPE 문법은 전용 역할 계약을 원본으로 사용한다.

제2조 (WORK 실행과 결과)

① NORMAL WORK는 `origin` 원격에서 만든 독립 `projecthub/*` branch와 Worker 소유 disposable clone의 일반 파일을 대상으로 실행한다.
② WORK AI는 Git metadata와 Git 원격을 작업 수단으로 사용하지 않는다.
③ Worker가 checkpoint commit, 원격 `projecthub/*` 게시 확인, CODE_CHANGE `resultRef`와 후속 전달 메타데이터를 기계적으로 관리한다. 별도 Commit Manifest나 materialization ledger를 두지 않는다.
④ WorkItem 생애 동안 checkpoint commit이 생성됐거나 이전 BLOCKED 단계의 CODE_CHANGE provenance가 보존된 경우 최종 재개 실행에서 새 commit이 없어도 CODE_CHANGE를 ANALYSIS로 낮추지 않는다.
⑤ 여러 CODE_CHANGE 결과를 함께 반영해야 하면 HQ가 INTEGRATION WorkItem을 추가한다.
⑥ INTEGRATION은 주 저장소와 분리된 독립 clone에서 일반 파일 기준으로 의미적 통합과 검증을 수행하고, Worker가 완료 결과를 새 `projecthub/*` 원격 CODE_CHANGE로 게시한다.
⑦ 하네스 없음 Direct Work는 위 NORMAL WORK/INTEGRATION 경로와 별도다. WorkGraph나 Git 준비·clone·checkpoint를 거치지 않고 사용자가 지정한 작업 폴더에서 선택한 Provider를 직접 실행한다.

제3조 (continuation과 기록)

① PAUSE 또는 CANCELED 뒤 작업 폴더가 그대로이면 `작업 추가`는 기존 HQ/WORK 세션과 WorkGraph를 USER_FOLLOWUP 문맥으로 이어갈 수 있다.
② PAUSE 또는 CANCELED 뒤 작업 폴더가 외부에서 변경됐거나 Git 상태를 정상 확인할 수 없으면 기존 WorkGraph를 이어 붙이지 않고 현재 파일을 기준으로 새 Job과 새 WorkGraph를 시작한다.
③ DONE 또는 DONE_WITH_ERROR 뒤의 `작업 추가`는 이전 WorkGraph continuation이 아니라 현재 작업 폴더를 기준으로 한 새 Job이다.
④ 프로그램 시작 시 과거 `session-state`, HQ/WORK 세션, WorkGraph, event log를 자동 복구해 새 작업의 의미 문맥으로 사용하지 않는다.
⑤ 사용자가 `새 작업`을 시작하면 활성 continuation을 제거하고 과거 상태와 transcript는 진단·이력으로만 남긴다.
⑥ event log, transcript, handoff와 저장 상태는 정책 원본이 아니다.
⑦ WorkGraph, event log, transcript와 continuation 상태는 작업 루트의 `.projecthub`에 저장한다. disposable clone·tool cache·임시 실행 파일은 `.projecthub/runtime`에 둔다. `.projecthub`는 Git status·baseline·checkpoint·CODE_CHANGE 결과에서 기계적으로 제외하며, 종료·DONE·PAUSE 시 runtime cleanup을 최선 노력으로 수행하되 runtime cache·임시 파일 완전 삭제를 의미 작업 완료의 선행조건으로 삼지 않는다.

제4조 (RESOURCE, JUDGE, OBSERVATION)

① 생성 리소스의 요청과 완료 결과는 WorkItem #0 경로에서만 처리하고 다른 일반 WorkItem이 RESOURCE를 직접 호출하거나 완료 결과를 직접 수신하지 않는다.
② RESOURCE의 현재 요청 문법은 `WORK-ROUTING-CONTRACT.md`를 따르며 Worker는 생성 결과의 품질이나 코드 연결 위치를 판단하지 않는다.
③ JUDGE는 HQ가 정리한 비기계적 판단 질문을 JEV에 전달하는 경로이고 Worker는 전송·스키마 수준만 처리한다.
④ OBSERVATION은 별도 AI 역할이 아니라 WORK가 요청하는 기계 계측 sidecar다.
⑤ FINALIZE_ONLY 등 남은 기계 작업은 HQ의 의미 종료 이후에도 최종 DONE/DONE_WITH_ERROR 전에 완료 여부를 확인할 수 있다.

제5조 (Git과 안전 경계)

① Worker는 WORK 실행 전에 Git metadata를 AI 실행 경계 밖으로 격리하고 원격 프로토콜 접근을 차단한다.
② 실행 중 사용자 작업 폴더가 바뀌면 그 변경을 자동 merge하지 않고 사용자 파일을 보존한 채 기존 결과 반영을 중단한다.
③ NORMAL WORK의 Git 준비는 선택한 작업 폴더 자체가 Git 저장소 root이고 `origin`이 존재해야 한다. 기존 HEAD가 있으면 현재 branch와 로컬 HEAD가 대응 원격 branch HEAD와 일치하는 clean 상태를 요구한다. 최초 commit이 없는 unborn 저장소는 `.gitignore` 기준 작업 폴더 파일을 Worker 소유 격리 clone에 옮겨 `projecthub/*` 초기 baseline commit으로 게시하고 그 SHA를 이후 WORK의 공통 기준점으로 사용한다. 게시 확인 뒤 사용자 작업 폴더의 현재 branch도 파일 내용 변경 없이 같은 baseline commit을 로컬 HEAD로 사용하게 하며 기본 branch 원격 push는 하지 않는다. 상위 디렉터리의 다른 Git 저장소를 자동 채택하지 않으며 기존 HEAD가 있는 저장소의 불일치·dirty 상태를 `.git` 재초기화·자동 merge·reset으로 우회하지 않는다. 이 항목은 하네스 없음 Direct Work에 적용하지 않는다.
④ 연결 프로젝트의 `projecthub/*` 작업 branch checkpoint push는 Worker가 수행할 수 있지만, 기본·보호 branch push, force push, 파괴적 reset과 배포는 별도 명시적 승인 없이 수행하지 않는다.
⑤ 정확한 disposable clone 경로, 환경 변수와 Git 명령행 옵션은 현재 구현과 테스트를 원본으로 사용한다.

제6조 (Web 런타임)

① HQ와 RESOURCE는 서로 다른 persistent profile, conversationId와 runtime token을 사용하는 관리형 app window 슬롯으로 운영한다.
② 일반 Chrome이나 runtime token이 없는 페이지는 Worker Web 작업 대상으로 사용하지 않는다.
③ Web 응답 회수, 첨부 준비, 파일 수집, UI 이상 관측과 숨김 실행의 세부 규칙은 `Web-Polish.md`와 현재 구현·테스트를 원본으로 사용한다.
④ HQ Web correlation KEY는 SEND_CONFIRM과 WAIT_RESPONSE에서 DOM mutation과 독립된 주기 감시를 함께 사용하며, 현재 KEY 응답이 확인되면 role·turn selector가 실패해도 결과 회수 경로를 계속 진행한다.
⑤ HQ Web 성공 응답은 현재 KEY 뒤의 별도 줄 `[RESPONSE=OK]`까지 확인한 경우에만 완료로 취급하며, 제한시간 초과 시 직전 답변 완료 여부를 한 번만 다시 요청한 뒤 실패하면 transport 오류로 종료한다.
⑥ RESOURCE Web 완료는 `[RESPONSE=OK]` 같은 텍스트 표식을 요구하지 않고 현재 요청의 생성 파일 준비와 capture 상태를 기준으로 처리한다.

제7조 (검증 기준)

① 코드 변경은 변경 범위에 맞는 자동 테스트와 빌드를 수행하고 실제 결과만 기록한다.
② Windows Worker의 runtime 완료 판정에는 solution build/test만으로 충분하지 않으며 필요한 경우 publish 후 실제 Worker 시작과 관리형 Web 왕복을 별도로 확인한다.
③ GitHub Actions의 Windows CI는 clean runner에서 restore, build, test의 기본 회귀를 잡는 독립 검증으로 사용하되 로컬 Chromium/profile/UI 동작을 대체하지 않는다.
④ 테스트가 취소, timeout 또는 미실행이면 성공으로 기록하지 않는다.
⑤ 대용량 publish·export·clean-environment·end-to-end 검증은 같은 입력으로 중복 수행하지 않고 최종 통합 상태에서 필요한 경우 한 번으로 집중한다.

제8조 (후속 개선)

① 외부 제공자 연결은 실제 인증·모델·세션 규격이 준비된 경우에만 현재 runner 경계 안에서 추가한다.
② RESOURCE 병렬화, 자동 대화방 교체·동일 요청 재전송 같은 적극적 Web 복구, 프로그램 재시작 후 의미 continuation 복원처럼 현재 정책을 바꾸는 기능은 기존 기록을 근거로 자동 활성화하지 않고 별도 정책 변경으로 다룬다.
③ 반복되는 실제 장애는 재현 근거와 회귀 테스트를 확보한 뒤 현재 정책 또는 구현 원본에 최소 범위로 반영한다.
