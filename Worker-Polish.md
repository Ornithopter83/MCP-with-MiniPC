# Worker-Polish — ProjectHub Worker 정책

이 문서는 Worker의 장기 책임만 정의한다. 실제 출력 문법은 전용 라우팅 계약을 사용한다.

제1조 (역할)

① HQ는 사용자 목표 해석, WorkItem 작업 목록 구성, WorkGraph 관제와 CONTINUE·PAUSE·END 판단을 담당한다.
② WORK는 현재 WorkItem의 목표와 작업 목록을 수행하고 목록별 결과를 HQ에 보고한다. WORK는 작업 분할이나 새 WorkItem 필요 여부를 판단하지 않는다.
③ RESOURCE는 생성 리소스를 Web으로 요청하고 수집한다.
④ Worker는 의미 판단 대신 상태, 전송, 프로세스, Git과 기계 작업을 관리한다.

제2조 (보고)

① WORK 보고는 Worker가 의미적으로 요약하거나 다시 작성하지 않는다.
② Worker는 workItemId, state, resultRef 등 필요한 최소 기계 메타데이터와 WORK 보고 본문을 HQ에 전달한다.
③ 코드 내용은 HQ 프롬프트에 자동 주입하지 않는다.
④ 오류가 발생해도 역할 계약에 해당 오류 전용 규칙을 추가하지 않는다.

제3조 (WorkGraph)

① WorkGraph는 Worker가 기계 상태로 보존한다.
② HQ가 반환한 WORK_GRAPH_PATCH만 WorkGraph 의미 변경에 사용한다.
③ Worker는 revision, JSON 구조, dependency와 상태 전이처럼 기계적으로 확인 가능한 조건만 검사한다.
④ 서로 독립적인 READY WorkItem은 설정된 동시성 범위에서 병렬 실행할 수 있다.

제4조 (WORK 실행)

① WORK에는 현재 WorkItem 목표, HQ가 배정한 작업 목록과 필요한 현재 사실만 제공한다.
② WORK는 배정 목록 밖으로 의미 범위를 확장하지 않고 발견 사실만 결과에 기록한다.
③ 역할 계약 전문은 새 AI 세션의 첫 호출에만 주입한다.
④ 후속 호출에는 현재 입력과 필요한 기계 사실만 전달한다.
⑤ 선행 결과는 필요한 최소 메타데이터와 보고를 전달하고 대용량 manifest 전문을 자동 주입하지 않는다.
⑥ WORK 실행의 명령 처리 실패, 비정상 종료, timeout 또는 계약 불일치는 Worker가 의미 복구를 선택하지 않고 기계 사실과 함께 BLOCKED로 HQ에 전달하며, 이후 RELEASE·CANCEL·후속 WorkItem 판단은 HQ가 담당한다.

제5조 (RESOURCE)

① RESOURCE Web 전송·수집 실패는 의미 작업 실패와 구분한다.
② RESOURCE 실패를 일반 WORK의 다른 생성 경로로 자동 우회하지 않는다.

제6조 (Web)

① HQ Web과 RESOURCE Web은 별도 역할 슬롯으로 관리한다.
② KEY는 요청과 응답을 연결하는 기계 표식으로 사용한다.
③ Web 확장은 전송, DOM 관측과 결과 수집만 담당하고 작업 의미를 해석하지 않는다.

제7조 (Git과 결과)

① 병렬 WorkGraph는 선택한 작업 폴더 자체가 Git 저장소 root이고, `origin` 원격 저장소가 존재하며 현재 branch와 로컬 HEAD가 원격 branch HEAD와 일치하는 clean 상태에서만 시작한다. 작업 폴더의 상위 디렉터리에 있는 다른 Git 저장소를 자동 채택하지 않는다.
② CODE_CHANGE의 `resultRef`는 Worker가 `projecthub/*` branch에 push하고 원격에서 같은 commit을 확인한 Git commit SHA다.
③ WORK는 Git 저장소를 생성·복구·stage·commit·push하지 않으며, checkpoint와 원격 게시를 Worker가 기계적으로 수행한다.
④ checkpoint 대상은 프로젝트의 `.gitignore`가 결정한다. Worker는 build·cache·runtime 경로에 대한 별도 exclude pathspec이나 재생성 파일 목록을 유지하지 않는다.
⑤ ProjectHub runtime, WorkGraph, transcript와 tool cache는 프로젝트 checkout 밖의 Worker 소유 경로에 둔다.
⑥ 병렬 결과의 Integration은 Worker 소유 격리 공간에서 수행하고, 충돌 없이 확정된 결과만 새 remote CODE_CHANGE로 게시한다.
⑦ 완료된 CODE_CHANGE는 파일 단위 MATERIALIZE/COPY나 별도 materialization ledger 없이 commit 계보로 추적한다. 최종 result가 현재 원격 동기화 branch에 포함되지 않았으면 clean 사용자 checkout을 해당 `projecthub/*` 원격 result branch로 전환하며, 기본·보호 branch에 자동 merge·push하지 않는다.
⑧ 사용자 작업 폴더가 dirty이거나 원격과 어긋나면 Worker가 임의 merge·reset·재초기화하지 않고 기계 오류로 차단한다.
⑨ PAUSE·CANCELED continuation은 보존된 WorkGraph와 원격 resultRef를 기준으로 하며, DONE 뒤 새 작업은 현재 원격 branch HEAD에서 새 Job을 시작한다.
⑩ #9 게시 산출물은 프로젝트 checkout과 disposable runtime 밖의 Worker 소유 영구 artifact 경로에 저장한다.
⑪ 하네스 없음 Direct Work는 WorkGraph, HQ/WORK 역할 계약, Git baseline, disposable clone, checkpoint, resultRef와 landing 정책의 적용 대상이 아니다. 사용자가 선택한 작업 폴더를 직접 working directory로 사용하고 project instruction 주입 없이 선택한 Provider를 실행한다. 다만 Master-Polish.md 제3조의 변경·안전 경계와 Worker의 child-process 수명 책임은 그대로 적용한다.

제8조 (프로세스)

① ProjectHub/Worker 프로세스 자신은 child Job Object에 넣지 않는다.
② Worker가 관리하는 외부 프로세스는 suspended 상태로 생성하고, 해당 작업·역할의 KILL_ON_JOB_CLOSE Job Object에 연결한 뒤에만 실행을 재개한다.
③ WORK 취소, 역할 재시작 또는 Worker 종료 신호는 해당 Job 종료에 직접 연결하며 그 Job에서 파생된 전체 프로세스 tree를 즉시 종료한다.
④ 명시적 Worker Exit에서는 등록된 모든 활성 child Job을 강제 종료한 뒤 애플리케이션 종료를 진행한다.
⑤ bridge와 background listener 종료는 WPF Dispatcher continuation에 의존하지 않는다.
⑥ 프로세스 수명 문제는 AI 계약이나 PID 후손 추적 규칙을 추가하지 않고 실행 계층에서 해결한다.
⑦ ProjectHub가 시작하는 Codex 역할은 computer-use capability를 실행 계층에서 비활성화한다.

제9조 (UI와 설정)

① UI는 HQ, WORK, RESOURCE와 실제 실행 상태를 표시한다.
② 설정은 실제 사용 중인 역할과 실행 옵션만 노출한다.
③ 사용하지 않는 역할이나 transport의 설정 UI를 유지하지 않는다.
④ 메인 창의 X 버튼은 Worker 종료가 아니라 트레이 숨김으로 동작하며, 명시적 Exit만 Worker 종료를 요청한다.
⑤ 새 작업 시작 전 이전 runtime 정리는 최선 노력으로 수행하며, 정리 실패만으로 HQ 시작을 차단하지 않는다.
⑥ 새 작업 입력은 Git 기준점 준비가 성공하기 전까지 유지하며, 성공한 뒤에만 작업 이력 화면으로 전환한다.