# Worker-Polish — ProjectHub Worker 정책

갱신일: 2026-09-28 (KST)

이 문서는 `src/ProjectHub.Worker`와 Worker가 직접 포함·운영하는 HQ, WORK, RESOURCE, JUDGE, OBSERVATION, Web Bridge 실행 경계의 장기 정책을 정의한다.
ProjectHub 전체 공통 원칙과 문서 형식은 `Master-Polish.md`에 둔다.


제1조 (Worker 비판단 원칙)

① Worker는 의미 판단 주체가 아니다.
② Worker는 역할 상태 저장, 허용 상태 전이, 구조화 제어 토큰 파싱, 역할별 세션·전송·프로세스 실행과 기계 작업 생명주기를 관리한다.
③ Worker는 시간 초과, 취소, 인증, 스키마, 경로 안전성, Git 준비와 Web 연결 같은 기계적 실패를 기록하고 전달할 수 있다.
④ Worker는 작업공간의 ProjectHub 상태·이벤트·transcript를 진단과 이력 목적으로 기계적으로 저장할 수 있다.
⑤ Worker는 요청 난이도·의도·우선순위, 요구사항 충족 여부, JUDGE 판정 의미, RESOURCE 품질 또는 저장 리소스의 사용 위치를 스스로 결정하지 않는다.
⑥ Worker는 일반 본문 의미를 근거로 다음 역할을 추론하거나 사용자 후속 명령 없이 저장 리소스를 코드에 자동 연결하지 않는다.
⑦ 비동기 계측 결과의 프로세스 성공과 제품 요구 충족을 같은 의미로 취급하지 않는다.

---

제2조 (역할과 상태)

① HQ는 사용자 목표 해석, 설계·관제, WorkGraph 변경과 CONTINUE/PAUSE/END 판단을 담당한다.
② WORK는 현재 WorkItem 범위의 구현·수정·검증과 필요한 JUDGE/RESOURCE 요청을 담당한다.
③ RESOURCE는 생성 리소스의 Web 요청·수집 경로이며 의미 품질을 판정하지 않는다.
④ JUDGE는 HQ가 정리한 비기계적 판단 질문을 JEV에 전달하는 경로다.
⑤ OBSERVATION은 별도 AI 역할이 아니라 WORK가 요청하는 기계 계측 sidecar다.
⑥ RESOURCE, JUDGE, OBSERVATION의 완료 사실은 요청한 관제 문맥에 기계적으로 귀속한다.
⑦ PAUSE, CANCELED, DONE 또는 DONE_WITH_ERROR 뒤 사용자가 작업을 추가하면 기존 세션을 유지한 USER_FOLLOWUP으로 HQ에 전달할 수 있다.
⑧ HIGH 역할, HIGH GOTO 또는 일회성 고수준 허가 개념은 현재 정책에 두지 않는다.

---

제3조 (HQ 실행 대상)

① HQ는 ChatGPT Web 또는 연결된 CLI 실행기를 사용할 수 있다.
② HQ의 transport 선택은 실행 경로만 바꾸며 HQ 역할 계약과 WorkGraph 의미를 바꾸지 않는다.
③ WORK는 CLI 실행 계층으로 유지하고 일반 WORK를 ChatGPT Web 역할로 확장하지 않는다.
④ 제공자·모델·추론·세션 UI의 구체 항목과 지원 여부는 현재 runner 구현과 설정을 따른다.

---

제4조 (Web 연결)

① Worker는 HQ Web과 RESOURCE Web을 서로 다른 역할 슬롯과 conversation binding으로 관리한다.
② Worker는 관리형 runtime token을 발급하고 로컬 bridge 요청의 인증 경계를 검증한다.
③ heartbeat는 생존과 확장 동기화 확인에 사용하는 기계 신호이며 작업 목적지를 의미적으로 결정하지 않는다.
④ Worker는 일반 Chrome이나 현재 runtime token이 없는 페이지를 Web 작업 대상으로 사용하지 않는다.
⑤ content script의 연결, 전송 확인, DOM 관측과 결과 수집 책임은 `Web-Polish.md`의 확장 경계를 따른다.
⑥ Worker와 관리형 Web 확장 사이의 loopback HTTP wire는 `src/ProjectHub.Worker/Contracts/WEB-BRIDGE-CONTRACT.md`를 전용 원본으로 사용한다.

---

제5조 (출력 계약)

① HQ와 WORK의 실제 ACTION, GOTO, WORK_GRAPH_PATCH, WORK_ITEM_STATUS 문법은 각각 `HQ-ROUTING-CONTRACT.md`와 `WORK-ROUTING-CONTRACT.md`를 단일 원본으로 사용한다.
② Worker는 계약에 정의된 제어 토큰과 구조만 기계적으로 해석하고 일반 본문의 의미를 라우팅 근거로 추론하지 않는다.
③ RESOURCE, JUDGE, OBSERVATION은 일반 WORK와 다른 사이드카 경로이며 각 전송 계약의 기계적 유효성만 검사한다.
④ 역할 계약과 Worker 정책에 같은 문법을 중복 정의하지 않는다.

---

제6조 (HQ 설계와 ACTION 의미)

① HQ는 새 사용자 목표나 의미 있게 바뀐 목표를 단순 전달하지 않고 WORK가 실행 가능한 수준으로 구체화한다.
② 작은 후속 수정에서는 전체 설계를 반복하지 않고 변경된 영향 범위만 갱신할 수 있다.
③ CONTINUE는 Worker와 AI가 사용자 입력 없이 다음 의미 있는 진전을 만들 수 있을 때 사용한다.
④ PAUSE는 사용자 전용 선택, 외부 권한, 취향 판단 등 사람 입력 없이는 다음 의미 판단을 진행할 수 없을 때 사용한다.
⑤ END는 현재 의미 목표가 완료됐다고 HQ가 판단할 때 사용하며 남은 기계적 outstanding 때문에 의미 종료 판단을 임의로 미루지 않는다.
⑥ HQ가 PAUSE를 반환하면 Worker는 새 READY WorkItem 시작을 중지하고 이미 RUNNING인 WorkItem은 완료 결과를 수확한 뒤 PAUSED 상태로 정리한다.
⑦ 사용자가 실행 중 취소하면 현재 실행 프로세스와 해당 실행 구간의 대기 작업을 중단하고 기계 상태를 CANCELED로 보존한다.
⑧ HQ가 Web이든 CLI든 같은 HQ 역할 계약을 사용한다.

---

제7조 (JUDGE 흐름)

① JUDGE는 관측 가능한 사실의 재확인이 아니라 현재 근거만으로 기계적으로 확정할 수 없는 판단에 사용한다.
② WORK는 판단 필요성과 근거를 HQ에 보고하고, HQ는 필요한 경우 JUDGE용 Form을 작성한다.
③ JUDGE에게 이미지·오디오·비디오 등 비텍스트 리소스 자체의 시각적·청각적·미적 품질이나 내용 적합성을 평가시키지 않는다.
④ Worker는 JUDGE 요청의 전송 문법만 검증하고 판정 의미를 해석하지 않는다.
⑤ JUDGE가 비활성인 상태에서 요청되면 해당 WorkItem을 기계적으로 BLOCKED 처리해 HQ에 알린다.
⑥ Form의 정확한 문법은 역할 계약을 단일 원본으로 사용한다.

---

제8조 (RESOURCE 흐름)

① WORKITEM #0은 생성 리소스 전용 예약 WorkItem이며 RESOURCE 요청과 완료 결과의 유일한 WorkItem 경로다.
② RESOURCE는 ChatGPT Web 기반 단일 FIFO 사이드카로 실행하며 Worker는 요청·상태·파일 수집·저장 사실만 기계적으로 관리한다.
③ 다른 일반 WorkItem은 RESOURCE를 직접 호출하거나 RESOURCE 완료 결과를 직접 수신하지 않는다.
④ 생성 결과의 의미적 품질, 사용 위치와 코드 연결 여부는 Worker나 RESOURCE가 판단하지 않는다.
⑤ RESOURCE 실패를 다른 생성 경로로 자동 우회하지 않는다.
⑥ RESOURCE의 실제 요청 문법은 WORK 역할 계약, Web 파일 수집 세부는 `Web-Polish.md`를 단일 원본으로 사용한다.

---

제9조 (UI)

① Worker UI는 사용자 요청, 역할별 진행, WorkItem 상태와 실제 실행 결과를 서로 구분해 표시한다.
② History 카드는 내부 실행 순번보다 WorkItem ID를 우선 표시하고 예약 WorkItem은 용도를 함께 표시한다.
③ WorkGraph의 상세 상태는 내부 snapshot과 Full Message에 보존하며 UI 요약이 의미 판단 원본이 되지 않는다.
④ 사용자가 PAUSE, CANCELED, DONE 또는 DONE_WITH_ERROR 상태에서 작업을 추가하면 기존 관제 문맥을 유지한 USER_FOLLOWUP으로 시작한다. 새 작업은 기존 실행 문맥을 초기화한다.
⑤ `하네스 없음` 직통 작업은 HQ, WorkGraph, JUDGE, RESOURCE 역할 계약을 우회하되 사용자 첨부의 안전한 staging과 선택된 AI 실행은 유지한다.
⑥ UI의 픽셀, 줄 수, 게이지 칸 수, 기본 모델 문자열과 같은 표현 세부는 장기 정책으로 고정하지 않고 현재 UI 구현과 테스트를 따른다.
⑦ Coordinator-first에서 화면의 작업 폴더는 사용자에게 보이는 최종 target workspace의 원본이다. 기존 AI session의 ProjectPath나 Worker 실행 폴더가 이를 자동 대체하지 않는다.
⑧ target workspace가 지정되지 않았거나 존재하지 않으면 새 Coordinator-first 작업을 시작하지 않는다.
⑨ Coordinator-first의 Git bootstrap은 화면의 target workspace 자체를 저장소 루트로 사용한다. 상위 디렉터리에서 발견된 Git 저장소를 target workspace의 저장소로 승계하지 않으며, 필요한 경우 target workspace에 독립 저장소를 초기화한다.

---

제10조 (프로젝트 기억과 실시간 이벤트 로그)

① Worker의 저장 상태, handoff, event log와 transcript는 진단·이력 형식이며 장기 정책 원본이 아니다.
② 프로그램 시작 시 과거 session-state, HQ/WORK 세션, WorkGraph와 이벤트 이력을 자동 복구해 새 작업 문맥으로 사용하지 않는다.
③ 현재 프로그램 실행 안에서 사용자가 작업 추가로 같은 작업을 이어갈 때는 기존 HQ/WORK 세션과 WorkGraph를 유지할 수 있다.
④ HQ에는 전체 WorkGraph를 매번 반복하지 않고 직전 HQ 입력 이후 의미 있는 상태 변화만 기계 이벤트로 전달할 수 있다.
⑤ 역할 계약 전문은 같은 AI 세션의 첫 호출에 주입하고 후속 호출에는 현재 입력과 필요한 기계 사실만 전달할 수 있다.
⑥ 사용자가 새 작업을 시작하면 활성 continuation을 제거하고 과거 기록은 이력으로만 남긴다.
⑦ 저장된 기억·로그·계획 문서는 사용자가 명시적으로 조사·파악을 요청한 경우가 아니면 Worker가 의미를 해석해 자동 작업을 시작하는 근거로 사용하지 않는다.

---

제11조 (비동기 기계 작업과 계측)

① 비동기 기계 작업은 Worker의 공통 registry에서 생명주기와 outstanding 상태를 관리한다.
② OBSERVATION 요청은 실행 명령, 작업 폴더, 시간 제한, 결과 경로와 completion mode를 기계적으로 명시해야 한다.
③ 실행 폴더와 결과 경로는 승인된 ProjectHub 작업 경계 안에서만 허용한다.
④ WORK_RESULT_REQUIRED는 현재 WORK 의미 라우팅을 보류하고 계측 완료 뒤 같은 WORK 세션에 결과를 재주입하는 안전 게이트다.
⑤ FINALIZE_ONLY는 의미 흐름을 막지 않으며 HQ END 뒤에도 남아 있으면 Worker가 최종 DONE/DONE_WITH_ERROR 전에 완료 여부를 확인한다.
⑥ RESOURCE도 전체 outstanding 집계와 최종 대기 게이트에 포함할 수 있다.
⑦ 계측 성공은 종료 코드, 시간 초과, 결과 경로 존재 같은 기계 사실만 의미하며 제품 요구 충족 여부는 WORK 또는 HQ가 판단한다.
⑧ 요청 파일명, 저장 디렉터리와 원자 게시 방식 같은 구현 세부는 현재 코드와 테스트를 원본으로 사용한다.

---

제12조 (동적 병렬 WORK Graph)

① WorkItem #0~#9는 시스템 예약 영역이며 일반 WorkItem은 #10부터 사용한다. #0은 RESOURCE, #1은 기존 이미지 가공 전용이고 #2~#9는 예약 상태로 둔다.
② HQ는 WorkItem의 생성·목표·dependency·취소와 의미적 재시도를 결정한다. WORK와 Worker는 승인되지 않은 새 의미 작업을 직접 생성하지 않는다.
③ Worker는 최대 동시 실행 수 안에서 dependency가 충족된 READY WorkItem을 기계적으로 슬롯에 배정한다. maxConcurrentWork=1도 같은 Scheduler의 단일 슬롯 동작이다.
④ NORMAL WorkItem은 독립 Git branch와 linked worktree를 Worker가 준비하며, AI 실행 중 Git 접근 경계는 제19조를 따른다.
⑤ WorkItem 시작 전 Git 준비 실패는 의미적 실행 실패와 구분해 BLOCKED로 보존할 수 있으며, Worker는 충돌의 의미를 자동 해결하지 않는다.
⑥ COMPLETED, FAILED, CANCELED은 종료 기록이다. 의미 작업 재시도는 새 WorkItem ID를 사용한다.
⑦ COMPLETED 결과에는 Worker가 기계적으로 측정한 resultType을 기록한다. WorkItem 생애 동안 checkpoint commit이 하나라도 생성됐거나 이전 BLOCKED 단계에서 CODE_CHANGE provenance가 보존된 경우 CODE_CHANGE이고, 그렇지 않으면 ANALYSIS다. 마지막 재개 실행에서 새 commit이 없다는 이유만으로 기존 CODE_CHANGE를 ANALYSIS로 낮추지 않는다. ANALYSIS의 resultRef는 코드 통합 대상이라는 의미가 아니다.
⑧ 여러 CODE_CHANGE 결과를 결합해야 하면 HQ는 kind=INTEGRATION WorkItem을 추가한다. 통합을 위한 linked-worktree 권한 probe용 NORMAL WorkItem을 선행하지 않는다.
⑨ INTEGRATION의 작업공간과 Git 경계는 제24조와 제19조를 따른다.
⑩ Integration WORK는 일반 파일 내용 기준으로 의미적 통합·충돌 해결·검증을 수행한다.
⑪ RESOURCE, JUDGE, OBSERVATION은 WorkGraph의 별도 일반 WorkItem으로 자동 변환하지 않고 기존 사이드카 귀속 규칙을 유지한다.
⑫ HQ END 전에 의미 WorkItem의 완료 상태를 HQ가 판단하며, 최종 DONE/DONE_WITH_ERROR 전에는 Worker가 추적하는 기계적 outstanding이 모두 종료되어야 한다.
⑬ CODE_CHANGE가 생성되면 Worker는 Commit Manifest를 기계적으로 생성해 WorkGraph 결과와 선행 결과 문맥에 연결한다. 선행 WORK 프롬프트에는 manifest 전체 본문을 인라인하지 않고 manifest 경로와 변경 파일 요약처럼 크기가 제한된 기계 메타데이터만 전달하며, 상세 파일 내용은 manifest 파일 또는 Integration snapshot을 통해 접근한다. 세부 생성·전달 경계는 제19조를 따른다.
⑭ HQ END 시점에 완료된 CODE_CHANGE가 사용자 target workspace에 아직 반영되지 않았다면 Worker는 WorkGraph가 유휴 상태인 종료 게이트에서만 최종 반영을 시도한다.
⑮ 완료된 INTEGRATION이 dependency로 소비한 NORMAL CODE_CHANGE는 별도 반영 대상으로 다시 취급하지 않는다.
⑯ 미반영 NORMAL CODE_CHANGE가 하나이면 Worker는 target branch가 예상 상태이고 clean이며 fast-forward 가능한 경우에만 해당 commit을 target workspace에 기계적으로 반영한다.
⑰ 미반영 NORMAL CODE_CHANGE가 둘 이상이면 Worker는 임의 병합하지 않고 END를 보류해 HQ가 INTEGRATION WorkItem을 추가할 수 있게 한다.
⑱ target branch 변경, dirty 상태, non-fast-forward 또는 검증 실패가 있으면 force/reset으로 해결하지 않고 기계 사실을 HQ에 반환한다.
⑲ 최종 반영이 완료된 뒤 같은 프로그램 실행에서 USER_FOLLOWUP이 이어지면 현재 target workspace HEAD를 새 실행 구간의 기본 기준 ref로 사용한다.
⑳ NORMAL WorkItem을 시작할 때 Worker는 선언된 기준 ref와 완료된 CODE_CHANGE dependency의 resultRef를 Git ancestry로 기계적으로 축약해 실제 코드 기준점을 정한다. 하나의 tip으로 축약되지 않는 독립 CODE_CHANGE 계보는 NORMAL에 자동 결합하지 않고 INTEGRATION 필요 상태로 보존한다.
㉑ WORK가 의미 결과를 보고한 뒤 checkpoint에 실패하면 Worker는 의미 작업을 곧바로 실패로 바꾸지 않고 worktree와 보고를 보존한 채 checkpoint 재시도 상태로 BLOCKED 처리할 수 있다. 해당 재개에서는 WORK AI를 다시 실행하지 않고 checkpoint 이후 기계 단계를 재개한다.
㉒ 빌드 로그, self-test 보고서, 임시 내보내기 파일과 분석 결과처럼 최종 납품물이 아닌 검증 산출물은 WorkItem별 runtime temp에 기록하며 NORMAL worktree의 코드 변경 provenance에 포함하지 않는다.
㉓ 실행 가능한 사용자 UI 또는 주요 사용자 흐름을 변경한 최종 CODE_CHANGE는 종료 판단 전에 해당 코드 결과를 기준으로 하는 독립 검증 WorkItem에서 다시 확인한다. 검증 WorkItem은 발견한 결함을 직접 수정하지 않고 사실을 보고하며 필요한 수정은 별도 후속 WorkItem으로 분리한다.

---

제13조 (관리형 Web 런타임)

① Worker는 HQ와 RESOURCE 관리형 Chromium 슬롯의 시작·종료·생존과 역할별 profile 선택을 관리한다.
② Worker는 역할별 로그인 profile을 보존하되 브라우저 자격정보를 Git, 로그 또는 AI 프롬프트에 복사하지 않는다.
③ 확장의 페이지 동작, DOM 관측과 Web UI 처리 규칙은 `Web-Polish.md` 책임으로 두고 Worker 정책에 중복 정의하지 않는다.
④ runtime 실행 파일, 창 배치, throttling 대응과 같은 기계 세부는 현재 런타임 구현과 테스트를 따른다.
⑤ Worker는 Web 응답의 의미적 정확성이나 RESOURCE 결과의 미적·기능적 품질을 판단하지 않는다.

---

제14조 (본문 구조 마커)

① ACTION, GOTO 등 라우팅 제어 토큰의 정확한 위치와 형식은 역할 계약을 따른다.
② WORK_GRAPH_PATCH, WORK_ITEM_STATUS, RESOURCE_TYPE 등 본문 구조 마커는 계약이 허용한 범위에서 기계적으로 탐색한다.
③ 동일 의미의 구조 마커가 중복되면 전송 계층은 기계 오류로 처리할 수 있다.
④ Worker는 구조 마커의 위치·형식만 검사하고 마커 주변 일반 설명의 의미를 추론해 라우팅하지 않는다.
⑤ JSON 객체 추출과 RESOURCE 본문 절단 등 구체 파싱 규칙은 해당 역할 계약과 parser 테스트를 단일 원본으로 사용한다.

---

제15조 (관리형 Web app window)

① Worker는 HQ와 RESOURCE에 각각 하나의 관리형 app window 역할 슬롯을 제공한다.
② Worker는 슬롯의 시작 URL과 conversation binding을 관리한다.
③ 확장의 tab 권한, 페이지 초기화와 표시 상태에서의 동작은 `Web-Polish.md`를 따르며, 창 프로세스의 표시·숨김·재시작 세부는 현재 런타임 구현과 테스트를 따른다.

---

제16조 (사용자 첨부 입력)

① 사용자 첨부는 자연어 본문과 분리된 기계 객체로 관리하고 파일명, MIME, 크기, hash와 안전한 staging 경로를 보존한다.
② Worker는 CLI 및 Web 역할로 첨부 bytes를 전달할 때 경로 안전성과 가능한 경우 SHA-256 일치를 검증한다.
③ 첨부 내용의 의미를 Worker가 판정해 라우팅하지 않는다.
④ 첨부 개수·크기·금지 형식·캐시 경로 같은 구체 제한은 현재 구현과 테스트를 원본으로 사용한다.

---

제17조 (Web 응답 회수와 일반 결과 파일)

① Worker는 Web 요청 송신 성공과 응답 회수 성공을 서로 다른 기계 단계로 기록한다.
② Worker는 correlation KEY가 있는 HQ 응답에서 현재 KEY로 검증된 결과만 현재 task 결과로 수락한다.
③ Worker는 확장이 반환한 일반 결과 파일의 bytes·hash·경로 안전성을 검증하고 별도 결과 경로에 저장한다.
④ assistant DOM 탐지, 다운로드 후보 판정, fallback과 응답 안정화는 `Web-Polish.md`의 확장 책임이며 구체 selector와 시간값은 구현·테스트를 따른다.

---

제18조 (Web 첨부 전송 준비)

① Worker는 attachment bytes 검증과 확장이 보고하는 ChatGPT UI 준비 상태를 서로 다른 기계 단계로 취급한다.
② Worker는 확장이 보고한 준비·전송·실패 상태를 기록하되 UI 의미를 추론하지 않는다.
③ 첨부 UI 탐지와 Send 가능 상태 판정은 `Web-Polish.md`의 확장 책임이며 구체 timeout과 DOM 규칙은 구현·테스트를 따른다.

---

제19조 (WORK Git 실행 경계)

① NORMAL과 INTEGRATION WORK의 AI 실행은 작업공간의 일반 파일을 대상으로 하며 Git metadata와 Git 원격 연결을 작업 수단으로 사용하지 않는다.
② Worker는 AI 실행 전에 해당 작업공간의 Git metadata를 실행 경계 밖으로 격리하고 Git 원격 프로토콜을 차단하며, AI 실행이 끝나면 checkpoint 전에 Git metadata를 기계적으로 복원한다.
③ checkpoint commit 생성, Commit Manifest 생성, Integration 선행 commit snapshot 준비, 완료 commit import와 target branch fast-forward 및 종료 게이트의 단일 NORMAL CODE_CHANGE fast-forward 같은 Git metadata 작업은 Worker가 수행한다.
④ CODE_CHANGE의 Commit Manifest에는 commit, parent, tree, 변경·삭제 경로, 파일 hash와 인라인 가능한 텍스트 최종 내용을 담고 HQ와 후속 WORK가 commit 내용을 별도 ANALYSIS 작업으로 다시 수집하지 않게 한다.
⑤ Git metadata 임시 격리 경로, 원격 프로토콜 차단 환경 변수, 인라인 크기 제한과 Git 명령행 옵션은 장기 정책으로 고정하지 않고 현재 구현과 테스트를 원본으로 사용한다.

---

제20조 (WorkGraph ID 입력 정규화)

① WorkGraph 전송 경계는 계약에서 허용한 숫자 또는 문자열 ID 입력을 내부 문자열 ID로 정규화할 수 있다.
② 정규화 뒤 ID 안전성, dependency 존재, self dependency와 cycle 등 기계적 유효성 검사를 동일하게 적용한다.
③ 구체 JSON 스키마와 허용 operation은 HQ 역할 계약을 단일 원본으로 사용한다.

---

제21조 (구조화 AI 출력 Helper)

① Worker가 AI 구조화 문자열을 소비하는 경우 지원 payload는 공용 Structured Helper를 먼저 통과시킨다.
② Helper는 결정론 파서를 먼저 적용하고, 성공하면 AI 복구를 호출하지 않는다.
③ 최초 파싱 실패 시에만 현재 WORK 실행 설정을 사용한 격리된 일회 복구를 허용하고, 복구 결과도 같은 결정론 파서로 다시 검증한다.
④ 호출자는 Helper의 최종 성공·실패만 소비하고 별도의 중복 복구 분기를 만들지 않는다.
⑤ Helper의 복구는 구조 복원만 수행하며 새 의미 값의 생성 근거로 사용하지 않는다.

---

제22조 (Web HQ 병렬 완료 점검)

① HQ transport가 Web인 경우 개별 WorkItem COMPLETED는 다른 작업이 계속 실행 중일 때도 다음 HQ 관제 기회가 될 수 있다.
② HQ 호출이 이미 진행 중이면 새 동시 HQ 호출을 만들지 않고 상태 변경을 기존 Scheduler 이벤트 흐름에 합친다.
③ FAILED 또는 비외부 BLOCKED와 QUIESCENT 같은 더 강한 상태 이벤트가 동시에 있으면 별도 완료 점검을 중복 생성하지 않는다.
④ 완료 점검에서 HQ는 추가 작업이 없으면 no-op CONTINUE를 사용할 수 있다.

---

제23조 (관리형 Web UI 이상 관측)

① Worker는 확장이 보고한 WEB_UI_ANOMALY_OBSERVED를 현재 task의 진단 사실로 기록할 수 있다.
② 해당 관측은 task 상태, conversationId, role binding, lease, correlation KEY 또는 전송 흐름을 직접 변경하지 않는다.
③ 확장이 어떤 Web UI를 어떻게 관측하는지는 `Web-Polish.md`의 책임이며 Worker는 동일 task의 동일 관측을 기계적으로 중복 억제할 수 있다.
④ 자동 대화방 이동이나 동일 요청 재전송은 실제 증거와 별도 정책 변경 없이 수행하지 않는다.

---

제24조 (Integration 독립 Git clone)

① 일반 WorkItem은 linked worktree 격리를 유지하고 INTEGRATION WorkItem의 파일 기준점은 주 저장소와 분리된 독립 clone으로 Worker가 준비한다.
② Integration AI의 Git metadata·원격 접근 경계는 제19조를 동일하게 적용한다.
③ Worker는 dependency의 CODE_CHANGE commit을 Integration 작업공간의 무시된 일반 파일 snapshot으로 기계적으로 펼치며, Integration WORK는 해당 snapshot·Commit Manifest·보고를 입력으로 사용해 일반 파일 기준으로 의미적 통합과 검증을 수행한다.
④ clone 준비, 선행 snapshot 준비, checkpoint commit과 완료 commit 검증은 Worker가 기계적으로 관리한다.
⑤ 완료 commit을 주 저장소로 가져오고 target branch에 fast-forward하는 작업은 Worker만 수행한다.
⑥ clone root, Git metadata, source branch 또는 target branch 상태가 예상과 다르면 자동 force/reset으로 해결하지 않고 기계 오류로 HQ에 보고한다.
⑦ 정확한 clone 경로와 Git 명령행 옵션은 장기 정책으로 고정하지 않고 현재 구현과 테스트를 원본으로 사용한다.
