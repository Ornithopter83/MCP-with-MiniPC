# Master-Polish — ProjectHub 현재 정책

## 문서 언어 절대 규칙

- 이 저장소의 정책 문서, 작업 문서, 인수인계 문서, 역할 계약, AI 역할 프롬프트의 설명 문장은 반드시 한글로 작성하고 저장한다.
- 설명 문장을 영문으로 작성하거나 영문 상태로 저장해서는 안 된다.
- ACTION, GOTO, QID, 상태 코드, 클래스명, 파일명, 명령어, API 필드명, 외부 제품명처럼 상호 운용이나 코드 식별에 필요한 고유 토큰만 원형을 유지할 수 있다.


갱신일: 2026-09-26 (KST)

이 문서는 ProjectHub의 현재 최상위 정책 원본이다.

ProjectHub의 목표는 AI가 설계·판단하고 Worker가 흐름·세션·전송·계측만 기계적으로 관리하는 역할 분리형 개발 도구다.

---

## 1. 최상위 불변식

Worker는 의미 판단 주체가 아니다.

Worker가 처리할 수 있는 것:
- 현재 역할 상태 저장 및 허용 상태 전이 검사
- ACTION/GOTO 제어행 문법 파싱
- 역할별 세션/전송/프로세스 실행
- WORK가 등록한 비동기 기계 작업의 실행, 시간 초과, 결과 경로 수집과 완료 상태 관리
- WORK_RESULT_REQUIRED 계측 완료까지 AI 호출 없이 대기하고 같은 WORK 세션에 결과 재주입
- 시간 초과/취소/인증/스키마/경로 안전성 오류 처리
- Web 대화 연결 및 생존 신호 생존 확인
- 기록/사용량/file 계측 기록
- Worker가 실제로 생성·전달한 HQ/RESOURCE Web 송신 내용과 RESOURCE 생명주기 기록
- JUDGE 전송 스키마와 RESOURCE 자연어 본문의 기계적 전달
- 미확인 원문 로그와 HQ용 한글 오류 요약
- 이미 알고 있는 실행 사실을 History UI에 표시
- 작업공간의 `.projecthub` 아래에 세션 상태, 관제 인수인계, 실시간 이벤트 로그, transcript를 기계적으로 저장하고 복구

Worker가 하지 않는 것:
- 요청 난이도·의도·우선순위 판단
- 다음 역할을 본문 의미로 추론
- 요구사항/AC/테스트/근거 충족 여부 판정
- JUDGE 결과 의미 해석 후 자동 PASS/FAIL 생성
- RESOURCE 생성 파일의 미적/기능적 품질 또는 용도 판정
- 생성 리소스가 어느 컴포넌트에 맞는지 판단
- 사용자의 후속 명령 없이 저장 리소스를 코드에 자동 연결
- 비동기 계측 결과의 의미·품질·요구사항 충족 여부 판단

---

## 2. 역할과 상태

| 상태 | UI 역할명 | 책임 | 실행 |
| --- | --- | --- | --- |
| HQ | 설계·관제 AI | 사용자 요청 해석, 구현 방향 설계, WORK 지시, JUDGE 질문 검토, CONTINUE/PAUSE/END | ChatGPT Web 또는 CLI 제공자 |
| WORK | 작업 AI | 코드 구현·수정·빌드·테스트·보고, RESOURCE/JUDGE 요청 | CLI 제공자 |
| RESOURCE | 리소스 AI | ChatGPT Web 생성 리소스 제작·생성 파일 수집·다운로드·지정 경로 저장 | 별도 ChatGPT Web 고정 |
| JUDGE | 작업 판단 AI | HQ 검토를 거친 WORK 질문 판정 | JEV |
| 미확인 | 오류 상태 | 기계적 오류 기록 및 HQ 요약 복귀 | Worker 내부 |

상태 전이:

~~~text
HQ       -> WORK
WORK     -> HQ | JUDGE | RESOURCE_QUEUE
WORK 실행 중 -> 기계 계측 요청 파일 등록 -> OBSERVATION sidecar
OBSERVATION WORK_RESULT_REQUIRED -> 현재 WORK 응답 보류 -> 완료 후 같은 WORK 세션 OBSERVATION_RESULT
OBSERVATION FINALIZE_ONLY -> 메인 의미 흐름 비차단 -> HQ END 시 기계적 대기 대상
JUDGE 요청 -> Worker가 JEV raw 결과를 요청한 같은 WORK 세션에 반환
RESOURCE_QUEUE 접수 -> HQ (RESOURCE_QUEUED)
RESOURCE_QUEUE 실행 -> RESOURCE Web (FIFO 1건) -> 완료 알림 queue
HQ ACTION=END -> 의미 작업 종료 고정 -> Worker가 기계적 대기 작업 확인
기계적 대기 작업 있음 -> 대기 -> 모두 종료 -> DONE / DONE_WITH_ERROR
PAUSED / CANCELED / DONE / DONE_WITH_ERROR + 사용자 작업 추가 -> USER_FOLLOWUP -> HQ (기존 HQ/WORK 세션 유지)
HQ END 전 RESOURCE 성공/실패 결과 -> 다음 WORK 입력에 기계적으로 전달
UNKNOWN  -> HQ 요약 복귀 (Job당 1회)
UNKNOWN 재발 -> 로그 기록 후 종료
~~~

HIGH 역할, HIGH GOTO, HIGH 일회성 허가, high_uses_remaining, 고수준 작업 허용 UI는 현재 정책에 존재하지 않는다.

---

## 3. HQ 실행 대상

HQ 대상은 두 종류다.

~~~text
HQ
├─ ChatGPT Web
└─ CLI
   ├─ OpenAI
   ├─ Claude
   └─ Muse
~~~

- ChatGPT Web 선택 시 제공자/모델/추론/CLI 세션 UI를 숨긴다.
- CLI 선택 시 제공자 → 모델 → 추론 → 세션 구조를 사용한다.
- OpenAI Codex CLI는 실제 실행이 연결되어 있다.
- Claude/Muse는 기존 제공자 abstraction을 유지하되 실제 runner가 연결되기 전에는 미연결 오류를 반환한다.
- 과거 transport=web을 CLI_TO_CLI에서 자동으로 codex_cli로 바꾸지 않는다.
- WORK에는 ChatGPT Web 대상을 추가하지 않는다.

---

## 4. Web 연결

HQ Web과 RESOURCE Web은 반드시 서로 다른 ChatGPT 대화를 사용한다.

Bridge는 다음 역할 연결을 명시적으로 저장한다.

~~~text
HQ       -> conversationId A
RESOURCE -> conversationId B
~~~

- 사용자가 각 ChatGPT 대화의 확장 패널에서 HQ 또는 RESOURCE 역할을 명시적으로 연결한다.
- 하나의 conversationId를 HQ와 RESOURCE에 동시에 연결하지 않는다.
- 생존 신호는 대화가 살아 있는지/확장 버전이 맞는지 확인하는 용도다.
- 마지막 생존 신호 대화을 작업 목적지로 사용하지 않는다.
- Worker는 역할 연결에서 얻은 conversationId로 작업를 명시적으로 생성한다.

---

## 5. 출력 계약

HQ만 ACTION을 사용한다.

~~~text
[ACTION=CONTINUE]
[GOTO : WORK]
<opaque body>
~~~

또는:

~~~text
[ACTION=PAUSE]
<opaque body>
~~~

~~~text
[ACTION=END]
<opaque body>
~~~

WORK:

~~~text
[GOTO : HQ]
<opaque body>
~~~

~~~text
[GOTO : JUDGE]
<JUDGE transport body>
~~~

~~~text
[GOTO : RESOURCE]
RESOURCE_TYPE: IMAGE
<자연어 리소스 생성 요청>
~~~

JUDGE는 ACTION/GOTO를 만들지 않는다. Worker가 JEV raw 결과를 요청한 같은 WORK 세션에 JUDGMENT 입력으로 반환한다.

RESOURCE는 메인 역할 상태와 분리된 사이드카 대기열로 실행한다. WORK가 RESOURCE를 요청하면 Worker는 명시된 종류와 자연어 요청을 FIFO 대기열에 넣고 접수 사실을 HQ에 전달한다. 이후 의미적 다음 단계는 HQ가 현재 사용자 목표와 관측된 실행 사실을 바탕으로 결정한다. HQ가 아직 END하지 않은 동안 RESOURCE 완료가 성공이든 실패든 Worker는 해당 requestId, 종류, 결과/오류를 다음 WORK 입력에 기계적으로 함께 전달한다. RESOURCE 실패는 UNKNOWN으로 승격해 HQ에 우회 전달하지 않는다. HQ가 END한 뒤에는 RESOURCE 완료 때문에 HQ나 WORK를 다시 호출하지 않는다. Worker는 HQ 종료 상태를 고정하고 남은 기계적 대기 작업만 추적한다.

비동기 계측은 새로운 GOTO 목적지나 AI 역할이 아니다. WORK는 현재 호출 중 헤더에 제공된 기계 작업 요청 폴더에 OBSERVATION JSON을 원자적으로 게시할 수 있다. Worker는 요청된 명령을 별도 프로세스로 실행하고 시간 초과·종료 코드·표준 출력/오류·명시된 결과 경로를 기계적으로 수집한다. WORK_RESULT_REQUIRED 요청이 있으면 현재 WORK 응답의 의미 라우팅을 보류하고 AI를 호출하지 않은 채 완료를 기다린 뒤 같은 WORK 세션에 OBSERVATION_RESULT를 전달한다. FINALIZE_ONLY 요청은 의미 흐름을 막지 않으며 HQ END 뒤 최종 DONE 전환 전에 Worker가 완료만 확인한다.

일반 본문는 불투명다. JUDGE 목적지의 스키마 검사와 RESOURCE 자연어 본문의 비어 있음 검사는 전송 계층의 기계적 유효성 검사이며 작업 의미 판단이 아니다.

---

## 6. HQ 설계와 ACTION 의미

새 사용자 요청 또는 목표가 크게 바뀐 요청에서 HQ는 단순 전달자가 아니다. 필요한 만큼 구현 방향을 설계해 WORK에 전달한다.

사용자의 요청에서 설계 기획에 관련된 부분은 반드시 HQ가 작업 수행한 뒤 구체화하여 WORK에 전달한다.

설계에 필요할 수 있는 항목:
- 목표
- 주요 구조
- 핵심 제약
- 검증 방향
- 필요한 리소스
- 사용자만 결정할 수 있는 부분

작은 후속 수정에는 전체 설계를 반복하지 않고 영향 범위만 갱신한다.

ACTION 사용 예:
- CONTINUE: AI/Worker가 스스로 다음 의미 있는 진전을 만들 수 있음
- PAUSE: 화면 인상, 조작감, 음질, 취향, 외부 로그인/권한, 사용자 전용 선택 등 사람 개입 없이는 다음 판단이 의미 없음
- END: 현재 실행 구간의 의미 작업 목표가 충족됐고 사용자 확인을 기다릴 이유도 없음. Worker가 추적하는 기계적 대기 작업이 남아 있어도 END 판단을 미루지 않음
- PAUSE, 사용자 취소, END는 HQ/WORK 세션 폐기를 뜻하지 않는다. 사용자가 명시적으로 새 작업을 시작하기 전까지 현재 세션과 작업공간을 유지한다.
- HQ가 PAUSE를 반환하면 Worker는 새 READY WorkItem 시작을 중지하고 이미 RUNNING인 WORK를 취소하지 않은 채 완료 결과를 수확한다. RUNNING이 모두 정리된 뒤 PAUSED snapshot을 저장하며, 정상 PAUSE snapshot에는 RUNNING WorkItem이 남지 않는다.
- 사용자가 실행 중 취소하면 Worker는 현재 실행 프로세스와 해당 실행 구간의 대기 작업을 중단하고 상태를 CANCELED로 보존한다. 실행 중 `thread.started`에서 확보한 CLI session ID도 즉시 보존한다.
- 사용자가 작업 추가를 실행하면 Worker는 USER_FOLLOWUP으로 기존 HQ 세션부터 새 실행 구간을 시작한다. Worker가 후속 요청의 의미를 판단하거나 자동으로 재개하지 않는다.

HQ가 Web이든 CLI든 같은 역할 계약을 사용한다.

---

## 7. JUDGE 흐름

~~~text
WORK -> HQ      검증 질문 목록 + evidence + JUDGE용 Form 생성 요청
HQ   -> WORK    JUDGE용 Form
WORK -> JUDGE   Form 전송
Worker -> WORK  JEV raw 결과를 같은 세션에 반환
~~~

- JUDGE는 관측 가능한 사실 자체를 다시 확인하는 용도가 아니라, 현재 근거만으로 기계적으로 확정할 수 없는 판단에 사용한다.
- JUDGE에게 이미지·오디오·비디오 등 비텍스트 리소스 자체의 시각적·청각적·미적 품질이나 내용 적합성을 평가시키지 않는다.
- 그런 판단이 다음 작업이나 완료 결과에 영향을 주면 WORK는 질문 목록과 현재 근거를 정리해 HQ에 JUDGE용 Form 생성을 요청한다.
- 이미 판정한 판단의 근거가 의미 있게 바뀌면 WORK는 새 근거로 다시 요청한다.
- HQ는 WORK가 이미 관측 사실로 확정한 항목 자체를 JUDGE 문항으로 반복하지 않고, 그 사실로부터 추가 해석이 필요한 판단만 독립 판단 단위로 정리한다.
- WORK가 명시적으로 Form을 요청하지 않았더라도 WORK 보고에 다음 작업이나 완료 결과에 영향을 주는 비기계적 판단이 아직 남아 있으면 HQ는 그 판단만 JUDGE용 Form으로 작성해 WORK에 돌려준다.
- HQ가 만드는 Form은 필요한 범위, evidence, 응답 형태, 기준을 포함하고 Worker의 JUDGE 전송 파서가 읽을 수 있는 NOUL/SCORE/CHOICE 문법을 사용한다.
- 질문 식별자는 QID:<id> 형식을 사용한다. SCORE에는 정수=기준 항목이 하나 이상 필요하고, CHOICE에는 선택지=기준 항목이 하나 이상 필요하다.
- CHOICE 선택지 키는 영문자로 시작하고 영문자, 숫자, 밑줄, 하이픈만 사용한다. 한글 선택지 키는 전송 문법으로 인정하지 않는다.
- WORK는 받은 JUDGE용 Form을 JUDGE로 전송한다.
- Worker는 질문이나 Form의 의미적 적합성을 검사하지 않고 전송 문법만 기계적으로 확인한다.
- JEV 원본 응답은 Worker가 같은 WORK 세션으로 직접 반환하며 JUDGE는 별도 라우팅 출력을 만들지 않는다.
- JUDGE가 비활성인데 WORK가 JUDGE를 요청하면 Worker는 해당 WorkItem을 JUDGE_UNAVAILABLE로 BLOCKED 처리하고 현재 상태를 HQ에 전달한다.

---

## 8. RESOURCE 흐름

RESOURCE는 ChatGPT Web이 생성해 파일로 반환할 수 있는 모든 생성 리소스를 생성 → 수집/다운로드 → 저장 → 기록하는 사이드카 FIFO 대기열로 수행한다. 이미지·오디오·문서 등 구체 형식은 역할 의미가 아니라 반환 파일의 MIME 형식과 파일 정보로 구분한다.

~~~text
WORK -> GOTO:RESOURCE + 자연어 요청
  └─ Worker RESOURCE FIFO queue
       ├─ 현재 1건만 RESOURCE Web 실행
       ├─ 추가 요청은 QUEUED
       ├─ 생성 파일 전부 수집/다운로드
       ├─ assets/resources/<requestId>/ 아래에 안전한 파일명으로 저장
       └─ 완료 결과 queue -> HQ END 전 필요할 때 다음 WORK 호출에 전달

HQ ACTION=END
  -> Worker가 현재 실행 구간의 HQ 의미 작업 종료 상태를 고정
  -> 현재 실행 구간에서 이후 WORK 보고가 HQ로 향하면 "HQ의 작업은 종료되었습니다."로 차단
  -> Worker가 모든 기계적 대기 작업을 확인
  -> 남아 있으면 대기 상태에서 AI 호출 없이 완료만 기다림
  -> 모두 종료되면 Worker가 DONE / DONE_WITH_ERROR로 전환
  -> 사용자 작업 추가가 들어오면 기존 HQ/WORK 세션을 유지한 USER_FOLLOWUP 새 실행 구간 시작
~~~

WORK의 RESOURCE 요청은 JSON이나 전용 역할 프롬프트를 사용하지 않는다. [GOTO : RESOURCE] 뒤 첫 줄에는 `RESOURCE_TYPE: IMAGE|AUDIO|VIDEO|DOCUMENT|FILE` 중 하나를 명시하고, 그 아래에는 ChatGPT Web에 그대로 보낼 새로운 리소스 생성 요청 한 건의 자연어 지시만 둔다. 한 요청에는 한 종류만 포함하며 서로 다른 생성 종류는 별도 요청으로 분리한다. Worker는 이 분류를 추론하지 않고 명시된 토큰만 기계적으로 읽으며, RESOURCE Web에는 분류 헤더를 제거한 자연어 본문만 전달한다. 기존 요청의 상태 조회·취소·추적·확인·보고는 RESOURCE 라우팅으로 보내지 않는다.

~~~text
[GOTO : RESOURCE]
<자연어 리소스 생성 요청>
~~~

Worker는 자연어 본문을 해석하지 않고 명시된 RESOURCE_TYPE과 본문을 RESOURCE 대기열에 넣는다. RESOURCE Web에는 자연어 본문만 전달한다. RESOURCE Web은 생성 결과를 공통 `resultFiles[]`로 반환하며 각 항목은 파일 bytes, MIME 형식, 파일명을 포함한다. Worker는 작업공간 하위 `assets/resources/<requestId>/`에 저장한다. 반환 파일명이 안전하면 이를 정규화해 사용하고, 없거나 사용할 수 없으면 `resource-NN.<확장자>` 형식으로 기계적으로 이름을 만든다.

기계적 ResourceRequest 기록:
- Id
- Type: IMAGE / AUDIO / VIDEO / DOCUMENT / FILE
- 프롬프트
- TargetDirectory
- TargetFileName
- RequestedBy
- Status: REQUESTED / GENERATING / SAVED / FAILED
- SavedPath

RESOURCE 범위는 특정 파일 형식으로 제한하지 않는다. ChatGPT Web이 생성 결과를 실제 파일로 반환할 수 있고 확장이 이를 기계적으로 수집할 수 있으면 동일 RESOURCE 파이프라인을 사용한다. 형식별 차이는 RESOURCE 역할 분리가 아니라 확장의 파일 탐지·수집 어댑터 차이로 처리한다.
Web 확장의 사용자 작업형 제한시간은 5분 미만으로 두지 않는다. 메시지 전달 준비, composer 준비, send confirm, RESOURCE 다운로드 가능 결과 대기, 개별 첨부·리소스 다운로드, 결과 POST의 기본 제한시간은 5분으로 통일한다. heartbeat·상태 조회·진행 보고·응답 안정화처럼 짧은 기계 계측 지연은 이 규칙의 대상이 아니다. Worker의 RESOURCE transport 전체 제한은 30분을 유지한다.

RESOURCE가 하지 않는 것:
- 자동 코드 연결
- 자동 CSS/HTML 반영
- 생성 결과의 사용 컴포넌트 의미 판단
- 자동 빌드 반영
- RESOURCE Web 동시 병렬 실행(항상 1건씩 FIFO)
- 자동 품질 판정

사용자가 이후 별도 명령으로 "연결 대상인 리소스를 연결해줘"라고 요청하면 현재 세션의 USER_FOLLOWUP 또는 명시적으로 시작한 새 USER -> HQ -> WORK 흐름에서 저장된 리소스를 통합한다.

---

## 9. UI

상단 Pipeline:

~~~text
대기 / 설계·관제 / 작업 / 리소스 / 판정
~~~

표시:
- 설계·관제: ChatGPT Web 또는 선택된 CLI 모델
- 작업: 선택된 WORK 모델
- 리소스: ChatGPT Web
- 판정: JEV

대기 상태에서는 다섯 Pipeline 카드를 모두 역할 컬러로 표시하고 gold 활성 border/orbit은 사용하지 않는다. 실행 중에는 현재 메인 역할이 gold 활성 border/orbit으로 강조된다. RESOURCE 사이드카가 실행/대기 중이면 메인 역할과 별개로 RESOURCE 카드의 gold orbit도 독립 동작하며 상태와 대기 건수를 표시한다. RESOURCE는 기존 네 번째 카드 위치를 사용하지만 의미는 HIGH와 완전히 다르다.

메시지 및 작업 이력 그룹의 전체 크기는 고정한다. PAUSE, CANCELED 또는 DONE / DONE_WITH_ERROR 상태에서는 기존 이력을 위쪽에 유지하고 목록 아래에 이력 카드 약 두 개 높이의 후속 메시지 입력 영역을 표시한다. 하단에는 기존 실행/새 작업 버튼 왼쪽에 녹색 계열의 작업 추가 버튼을 표시한다. 작업 추가는 기존 이력과 HQ/WORK 세션을 유지한 채 USER_FOLLOWUP을 시작하며, 새 작업 버튼만 기존 세션과 이력을 명시적으로 초기화한다.
WorkGraph 상태를 별도의 `병렬 WORK` 패널로 표시하지 않는다. WORK 진행·응답 History 카드는 WorkItem 생성 순서를 기준으로 안정적인 `작업 (#N)` 표기를 사용하고, 내부 workItemId는 Full Message와 이벤트 로그의 참조 정보로 보존한다.
WORK 진행(`ROLE_PROGRESS`) History 카드는 제목 1줄과 본문 4줄, 총 5줄 높이로 고정한다. 본문은 18px line height의 4줄 영역을 사용하고 초과 내용은 잘라내며 전체 원문은 Full Message에서 확인한다. 짧은 본문도 같은 카드 높이를 유지한다.
현재 작업의 `3. 작업` 카드 하단에는 RUN/READY/BLOCKED/COMPLETED/FAILED 문자열 요약을 표시하지 않고, RUNNING WorkItem 수만 8칸 고정 녹색 게이지로 표시한다. 0건은 `□□□□□□□□`, 4건은 `■■■■□□□□`로 표시한다.

CLI 역할 실행 중 Codex의 주 응답 채널에서 `item.completed` / `agent_message`가 발생하면 Worker는 본문 의미를 해석하지 않고 `작업 진행` 이력 카드로 그대로 추가한다. 진행 카드는 제목과 다중 줄 본문만 표시하고 토큰/파일 행은 표시하지 않는다. 진행 카드의 발생 횟수나 이력 개수에 별도 제한을 두지 않으며, 최종 역할 응답 카드는 기존 작업 요청/수행 결과/리소스 요청 형식을 유지한다.

모든 Worker 관측 메시지는 작업 중 즉시 `.projecthub/events/<jobId>.jsonl`에 한 이벤트 한 줄로 append한다. 이벤트에는 시각, 출처, 상태, 참조 정보와 실제 Full Message를 저장한다. History 카드는 요약 표시를 유지하되 사용자가 항목을 두 번 클릭하면 해당 Full Message를 별도 창에서 확인할 수 있다.

설정:
- HQ: 실행 대상 Web/CLI + CLI일 때 제공자/모델/추론/세션
- WORK: 제공자/모델/추론/세션. 기본값은 OpenAI / GPT-6 Luna / Medium이며 저장 모델을 임의 변환하는 마이그레이션은 하지 않는다.
- RESOURCE: ChatGPT Web 고정
- JUDGE: JEV 설정
- HQ/RESOURCE Web 카드는 각각 명시적 역할 연결의 연결/생존 신호/확장 동기화 상태와 연결된 대화 정보를 보여준다.
- 설정 본문은 작은 화면에서도 세로 스크롤되며 하단 닫기/적용 버튼은 항상 별도 footer에 남는다.
- 역할 표기는 설계·관제 / 작업 / 리소스 / 판정으로 통일한다.

---

## 10. 프로젝트 기억과 실시간 이벤트 로그

- 작업공간 루트의 `.projecthub/session-state.json`에 JobId, HQ/WORK 설정과 세션 ID, 마지막 상태, 마지막 HQ 메시지, 이벤트 로그 경로를 저장한다.
- `.projecthub/last-handoff.md`에는 사람이 읽을 수 있는 마지막 관제 인수인계를 저장한다.
- `.projecthub/events/<jobId>.jsonl`은 Job 전체의 실시간 원시 이벤트 스트림으로 유지하며 작업 종료 시 일괄 생성하지 않고 이벤트 발생 시마다 즉시 append한다.
- `.projecthub/transcripts/`는 통합 Job 로그가 아니라 사용자 명령 실행 구간별 transcript 보관소다. 최초 `실행`과 각 `작업 추가`는 서로 다른 transcript 파일을 만든다.
- 명령 실행 구간이 DONE, DONE_WITH_ERROR, PAUSED, CANCELED 또는 실행 오류로 끝나면 Worker는 해당 구간에서 발생한 메시지만 새 transcript 파일에 기록한다. 이전 명령의 transcript를 덮어쓰거나 뒤에 합치지 않는다.
- transcript 파일명은 시작 시각 기반의 짧은 `yyMMdd-HHmmss.txt` 형식을 사용한다. 같은 초에 이름이 겹치면 `-02`, `-03` 순번을 붙인다.
- Worker 재시작 후 작업공간에 재개 가능한 상태가 있으면 이를 기계적으로 복구해 `작업 추가`를 허용한다.
- 병렬 WorkGraph를 복구해 USER_FOLLOWUP을 시작할 때는 저장 파일 경로만 전달하지 않고 현재 revision과 WorkItem 상태를 HQ 입력 본문에도 기계적으로 포함한다. 따라서 HQ가 Web이든 CLI든 복구 상태를 직접 확인할 수 있다.
- 저장된 Codex 세션이 로컬에 없으면 해당 세션 ID를 사용하지 않고, 새 HQ 세션에 프로젝트 기억 파일과 이벤트 로그 경로를 함께 전달해 관제 문맥을 복구할 수 있게 한다.
- 사용자가 `새 작업`을 명시적으로 선택하면 활성 session-state만 제거하고 과거 handoff/event/transcript 파일은 기록으로 남긴다.
- Worker는 저장된 기억이나 로그의 의미를 해석해 자동 작업을 시작하지 않는다.

---

## 11. 현재 활성 작업

활성 작업은 tasks/16-parallel-work-graph.md다.

목표는 단일 WORK 직렬 실행을 동적 DAG 기반 병렬 WORK 실행으로 확장하는 것이다.

구현 코드 범위:
1. WorkItem / WorkGraph / GraphPatch 도메인과 상태 전이
2. 설정 가능한 maxConcurrentWork와 ParallelWorkScheduler
3. WorkItem별 Codex 세션, Git branch, worktree 격리
4. HQ가 의미적으로 WorkItem 생성·변경·취소·의존성을 결정하는 계약
5. WORK의 SPLIT_REQUEST와 HQ GraphPatch 반영
6. Integration WorkItem을 통한 병렬 결과 통합·충돌 해결·전체 검증
7. RESOURCE / JUDGE / OBSERVATION의 workItemId 귀속
8. WorkGraph와 WorkItem 세션/branch/worktree/result 상태 영속화 및 재시작 복구
9. History 카드의 WorkItem 번호 표시와 실행 상태 귀속
10. 병렬 실행 진입 전 로컬 Git 자동 초기화와 사용자 승인 기반 baseline commit
11. 단일 WORK 대비 병렬 WORK 실제 E2E 비교 검증

실제 Windows 빌드/테스트/Explorer E2E는 실행 가능한 .NET/Explorer 환경에서 검증해야 한다.


## 12. 역할 계약 일반화 규칙

역할 계약은 일회성 작업 메모가 아니라 장기간 유지되는 프로토콜 경계다.

계약에 포함할 수 있는 내용:
- 지속 가능한 역할 책임
- 허용된 ACTION/GOTO 문법과 상태 전이 제약
- 상호 운용에 필요한 전송 문법 또는 기계적 불변식
- 의미 판단을 하는 AI와 기계적 Worker 사이의 일반 경계

계약에 포함하지 않는 내용:
- 특정 사용자 요청, 테스트 실행, 제품 도메인, 파일명, 자산, 게임, 오디오 사례, 장애에서 복사한 예시
- 특정 시나리오에만 맞는 횟수, 목록, 재시도, 남은 작업 계산
- 관측된 실패 문장을 그대로 붙이는 임시 대응 문구
- 임시 구현 이력, 디버깅 지시, 인수 테스트 스크립트

시나리오별 내용은 테스트, 픽스처, 작업 이력, 검증 기록에 둔다. 계약 규칙을 추가하기 전에 무관한 미래 작업에도 그대로 맞는지 확인한다. 아니라면 계약에 넣지 않는다.

## 13. 문서와 작업 언어

- 역할 계약, 정책 문서, 작업 계획, 작업 기록, AI 역할 프롬프트의 설명 문장은 한글로 작성한다.
- ACTION, GOTO, QID, 상태 코드, 클래스명, 파일명처럼 상호 운용이나 코드 식별에 필요한 토큰은 원형을 유지할 수 있다.
- 효율보다 해석 일관성과 한글 문맥 유지를 우선한다.


---

## 14. 비동기 기계 작업과 계측

- 비동기 기계 작업의 공통 생명주기는 Worker의 `MechanicalWorkRegistry`가 관리한다.
- 현재 종류는 RESOURCE와 OBSERVATION이며 새 종류가 추가돼도 Worker가 작업 의미를 추론하지 않는다.
- OBSERVATION 요청은 작업별 `.projecthub/mechanical/<jobId>/requests` 폴더를 사용하고, active/result 기록도 같은 jobId 아래에 보존한다.
- WORK는 완성된 요청 JSON을 임시 파일에 쓴 뒤 `.json`으로 원자적으로 게시한다.
- OBSERVATION은 직접 실행할 command/arguments, 실행 폴더, 시간 제한, 결과 경로, 환경 변수와 completionMode를 기계적으로 명시한다.
- 실행 폴더와 결과 경로는 현재 작업공간 또는 ProjectHub 실행 디렉터리 하위만 허용한다.
- WORK_RESULT_REQUIRED는 현재 WORK 응답을 보류하는 안전 게이트다. Worker는 완료까지 AI 호출 없이 대기하고 결과를 같은 WORK 세션에 `OBSERVATION_RESULT`로 재주입한 뒤 새 WORK 응답을 받아야 의미 라우팅을 계속한다.
- FINALIZE_ONLY는 결과 의미 해석이 필요 없는 작업에만 사용한다. 의미 흐름은 계속되며 HQ가 END한 뒤에도 남아 있으면 Worker가 완료까지 기다린 후 DONE 또는 DONE_WITH_ERROR를 기록한다.
- RESOURCE도 같은 공통 기계 작업 레지스트리에 FINALIZE_ONLY로 등록해 전체 outstanding 집계와 최종 대기 게이트에 포함한다.
- 계측 결과의 성공 여부는 프로세스 종료 코드, 시간 초과, 결과 경로 존재 같은 기계 사실만 뜻한다. 결과가 제품 요구를 만족하는지는 WORK/HQ가 판단한다.


---

## 15. 동적 병렬 WORK Graph

병렬 WORK는 고정된 WORK-1, WORK-2 같은 새 역할을 만들지 않는다. WORK 역할은 동일하며 Worker가 설정된 최대 동시 실행 수 안에서 HQ가 승인한 WorkItem을 독립 실행 슬롯에 배정한다.

의미 판단 경계:
- HQ는 사용자 목표를 WorkItem으로 분해하고 각 WorkItem의 목표, 의존성, 추가·변경·취소를 결정한다.
- WORK는 자신에게 배정된 WorkItem 범위 안에서 구현·검증하고, 새 독립 작업이 필요하다고 판단하면 직접 새 WORK를 시작하지 않고 SPLIT_REQUEST를 HQ에 보고한다.
- Worker는 WorkItem의 의미를 판단하지 않는다. 이미 HQ가 승인한 WorkGraph에서 상태와 의존성을 기계적으로 계산하고 READY WorkItem을 빈 슬롯에 배정한다.
- 여러 READY WorkItem 중 별도 의미 우선순위가 없으면 Worker는 HQ가 제공한 명시적 순서 또는 안정적인 생성 순서를 기계적으로 사용한다.
- 새 Job과 USER_FOLLOWUP은 maxConcurrentWork 값과 기존 WorkGraph 유무와 관계없이 동일한 WorkGraph/Scheduler 경로를 사용한다.
- maxConcurrentWork=1은 별도 직렬 엔진이 아니라 실행 슬롯이 1개인 WorkGraph다.
- 과거 continuation에 저장 WorkGraph가 없으면 Worker가 빈 WorkGraph를 생성해 같은 HQ 관제 문맥에서 후속 요청을 이어가며 레거시 직렬 실행 경로로 돌아가지 않는다.

WorkItem 기본 상태:
- PLANNED: HQ가 정의했지만 아직 실행 조건을 평가하지 않은 상태
- READY: 모든 명시적 선행 의존성이 완료되어 실행 가능한 상태
- RUNNING: Worker가 실행 슬롯, 세션, 작업공간을 배정해 실행 중인 상태
- COMPLETED: 해당 WorkItem 실행이 정상적으로 끝나 결과 참조가 기록된 상태
- FAILED: WORK 프로세스가 실제로 시작된 뒤 복구할 수 없는 실행 실패가 발생했거나 WORK가 실패 결과로 종료한 상태
- BLOCKED: 미완료·실패 의존성, HQ 판단 대기, 또는 WORK 시작 전의 기계적 실행 준비 실패 때문에 현재 실행할 수 없는 상태
- CANCELED: HQ 또는 사용자의 명시적 취소가 적용된 상태

동시 쓰기 격리:
- 동시 실행 WorkItem은 각각 독립 Git branch와 worktree를 사용한다.
- worktree와 branch 생성·삭제·경로 검증은 Worker가 기계적으로 수행한다.
- worktree 파일시스템 경로는 Windows와 외부 도구의 경로 길이 위험을 줄이기 위해 repository/job/workItem 식별자를 짧은 안정 해시가 포함된 segment로 축약한다. Git branch 이름은 기존의 식별 가능한 형식을 유지한다.
- 같은 Git 저장소의 worktree 준비처럼 공유 Git metadata를 변경하는 짧은 구간은 Worker가 저장소 단위로 직렬화하고, 준비가 끝난 WORK 실행은 설정된 슬롯 수대로 병렬 수행한다.
- Git 준비 명령이 실패하면 Worker는 오류 코드뿐 아니라 실제 exit code와 stderr를 WorkItem 기계 보고에 보존한다.
- WORK 세션이 시작되기 전의 WORKTREE_* 준비 실패는 의미적 FAILED로 확정하지 않고 BLOCKED로 보존한다. USER_FOLLOWUP의 현재 Git 사전 검사가 성공하면 같은 WorkItem을 다시 실행 가능한 상태로 되돌린다.
- 구버전 snapshot에 WORKTREE_*가 FAILED로 저장되어 있으면, 현재 열린 WorkItem의 dependency가 직접 참조하고 sessionId/resultRef가 없는 항목만 준비 실패로 마이그레이션해 재활성화한다. 참조되지 않는 과거 FAILED 항목은 기록으로 유지한다.
- 실패한 worktree add가 동일 WorkItem branch만 남겼다면 그 branch가 다른 worktree에서 사용 중이지 않고 정확히 원래 base commit을 가리킬 때만 새 worktree에 안전하게 재사용한다.
- WorkItem의 시작 기준 ref는 WorkGraph에 명시적으로 기록한다.
- Worker는 충돌의 의미를 자동 해결하지 않는다.
- 새 병렬 실행을 시작할 때 작업 폴더가 Git 저장소가 아니면 Worker는 AI를 호출하기 전에 해당 작업 폴더에서 `git init`을 기계적으로 수행할 수 있다.
- Git HEAD가 없거나 현재 저장소에 commit되지 않은 변경이 있으면 Worker는 실제 repository root와 현재 branch를 사용자에게 보여주고 기준점 생성 승인을 요청한다.
- 사용자가 승인한 경우에만 Worker가 `git add --all`과 로컬 ProjectHub identity를 사용한 baseline commit을 생성한다. 사용자가 취소하면 AI 의미 작업을 시작하지 않는다.
- Git 준비 단계는 사용자 승인 전에는 source/index를 변경하지 않고, baseline 승인을 받은 뒤에만 ProjectHub 관리 `.gitignore` 블록을 생성·갱신하고 ProjectHub 런타임/검증 캐시의 index 추적을 해제한다.
- ProjectHub 관리 ignore 기본값은 `.projecthub/`, `.verification-appdata/`, `.projecthub-worktrees/`와 명백한 OS·편집기 임시 파일만 포함한다. 기존 사용자 규칙은 보존한다.
- Worker가 새로 `git init`한 저장소 또는 아직 HEAD가 없는 초기 저장소에는 파일/폴더 존재만으로 기계적으로 식별 가능한 안전 preset을 추가할 수 있다. Godot은 `.godot/`, Unity는 `Library/`, `Temp/`, `Logs/`, `Obj/`, `UserSettings/`, .NET은 `bin/`, `obj/`, Node는 `node_modules/`만 자동 제외한다.
- 이미 존재하던 Git 저장소에는 프로젝트별 preset을 새로 주입하지 않고 ProjectHub 관리 블록만 보장한다.
- Git 준비 시 repository local `core.longpaths=true`를 기계적으로 설정하며 전역 Git 설정은 변경하지 않는다.
- 이미 추적 중인 `.projecthub/`, `.verification-appdata/`, `.projecthub-worktrees/`는 사용자가 baseline 생성을 승인한 경우에만 `git rm --cached`로 index에서 제거하고 로컬 파일은 보존한다.
- 원격 저장소는 기존처럼 `origin` URL이 있으면 기계적으로 확인만 하며 자동 remote 생성, pull, push는 수행하지 않는다.

동적 확장:
- HQ는 실행 중에도 GraphPatch로 WorkItem을 추가·변경·취소하거나 의존성을 변경할 수 있다.
- COMPLETED, FAILED, CANCELED WorkItem은 종료 기록으로 유지한다. WORK가 실제로 시작된 뒤의 FAILED 재시도는 기존 종료 항목을 재작성하지 않고 새 ID WorkItem을 추가한 뒤 필요한 비종료 후속 항목의 dependency를 새 작업으로 바꾼다.
- WORK 시작 전 준비 실패의 재실행은 의미 작업 재시도가 아니라 Worker 실행 준비 재개이므로 같은 WorkItem을 사용할 수 있다.
- 현재 Graph를 변경하지 않고 이미 READY인 WorkItem을 계속 실행할 때 HQ의 operations=[] GraphPatch는 revision을 바꾸지 않는 no-op CONTINUE로 처리한다.
- 이미 종료된 WorkItem에 대한 CANCEL은 상태를 바꾸지 않는 멱등 요청으로 기계적으로 수용한다. 목표·dependency·baseRef처럼 종료 기록을 변경하는 수정은 계속 금지한다.
- WORK가 SPLIT_REQUEST를 보고해도 새 WorkItem 생성 여부와 의존성은 HQ가 결정한다.
- Worker는 승인되지 않은 작업을 의미적으로 생성하지 않는다.

Integration:
- 병렬 결과의 통합도 별도 새 AI 역할이 아니라 WORK 역할의 Integration WorkItem으로 표현한다.
- Integration WorkItem은 통합 대상 WorkItem을 명시적 dependency로 가진다.
- 서로 다른 완료 WorkItem의 resultRef를 최종 코드 상태에 함께 반영해야 하는지는 HQ가 판단하며, 필요하면 END 전에 kind=INTEGRATION WorkItem을 추가한다.
- Integration WORK는 각 결과 ref/branch를 바탕으로 병합, 충돌 해결, 전체 빌드·테스트를 수행하고 통합 결과를 HQ에 보고한다.
- 새 INTEGRATION WorkItem의 첫 실행은 Graph에 저장된 과거 baseRef보다 실행 시점 주 작업공간의 현재 branch/HEAD를 Worker가 기계적으로 우선해 해당 HEAD에서 integration worktree를 만든다. 실제 사용한 baseRef는 WorkItem 실행 문맥에 다시 기록한다.
- INTEGRATION WORK는 현재 integration worktree 안에서만 통합·검증하며 주 작업공간이나 target branch를 직접 수정하지 않는다.
- Integration WORK가 COMPLETED를 보고하면 Worker는 해당 checkpoint commit을 주 작업공간의 현재 branch에 fast-forward만 허용하는 방식으로 기계적으로 반영한다.
- 주 작업공간이 dirty 상태이거나 detached HEAD이거나 integration commit이 현재 HEAD의 fast-forward 대상이 아니면 Worker는 force/reset/push로 해결하지 않고 INTEGRATION_LANDING_FAILED로 해당 WorkItem을 BLOCKED 처리한다.
- Integration landing 실패는 blockCode와 별도로 기계적 세부 코드(blockDetailCode)를 WorkGraph snapshot과 HQ 상태 이벤트에 보존한다. 과거 snapshot의 Worker 생성 INTEGRATION_LANDING 블록에 errorCode가 있으면 복구 시 세부 코드로 승격한다.
- Integration worktree 첫 준비와 주 작업공간 ff-only landing은 같은 repository의 primary mutation gate로 직렬화해 primary HEAD 기준과 landing 사이의 경쟁을 막는다.
- Integration landing 실패의 의미적 해결 방법과 사용자 개입 필요 여부는 HQ가 판단한다.
- Worker는 merge 충돌의 의미적 해결책을 선택하지 않는다.
- 성공한 Integration resultRef를 이후 새 WorkItem의 기본 baseRef로 기계적으로 사용할 수 있다. dependency가 있다는 사실만으로 Worker가 임의의 dependency resultRef를 baseRef로 선택하지는 않는다.
- HQ가 특정 선행 결과에서 직접 이어서 구현해야 한다고 판단하면 해당 WorkItem의 baseRef를 GraphPatch에 명시한다. baseRef가 생략된 새 WorkItem은 현재 주 작업공간 HEAD 또는 가장 최근 성공 Integration resultRef를 기계적 기본값으로 사용한다.

기존 사이드카:
- RESOURCE, JUDGE, OBSERVATION은 기존 역할과 책임을 유지한다.
- 병렬 실행에서는 모든 요청·완료·결과에 workItemId를 연결해 원래 WORK 세션으로 기계적으로 귀속한다.
- WORK_RESULT_REQUIRED OBSERVATION은 해당 WorkItem만 대기시키며 다른 READY WorkItem의 실행을 막지 않는다.

종료:
- HQ ACTION=END는 더 이상 실행 중인 의미 WorkItem을 암묵적으로 폐기하지 않는다. END를 수용하려면 현재 WorkGraph가 HQ가 완료로 판단한 상태여야 하며, Worker는 그 판단 자체를 검증하지 않고 명시된 제어와 기계적 outstanding만 처리한다.
- 최종 DONE / DONE_WITH_ERROR 전에는 실행 중 WorkItem, Integration WorkItem, RESOURCE, OBSERVATION 등 Worker가 추적하는 기계적 outstanding이 모두 종료되어야 한다.
