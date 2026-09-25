# Master-Polish — ProjectHub 현재 정책

## 문서 언어 절대 규칙

- 이 저장소의 정책 문서, 작업 문서, 인수인계 문서, 역할 계약, AI 역할 프롬프트의 설명 문장은 반드시 한글로 작성하고 저장한다.
- 설명 문장을 영문으로 작성하거나 영문 상태로 저장해서는 안 된다.
- ACTION, GOTO, QID, 상태 코드, 클래스명, 파일명, 명령어, API 필드명, 외부 제품명처럼 상호 운용이나 코드 식별에 필요한 고유 토큰만 원형을 유지할 수 있다.


갱신일: 2026-09-25 (KST)

이 문서는 ProjectHub의 현재 최상위 정책 원본이다.

ProjectHub의 목표는 AI가 설계·판단하고 Worker가 흐름·세션·전송·계측만 기계적으로 관리하는 역할 분리형 개발 도구다.

---

## 1. 최상위 불변식

Worker는 의미 판단 주체가 아니다.

Worker가 처리할 수 있는 것:
- 현재 역할 상태 저장 및 허용 상태 전이 검사
- ACTION/GOTO 제어행 문법 파싱
- 역할별 세션/전송/프로세스 실행
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
JUDGE    -> WORK
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

JUDGE:

~~~text
[GOTO : WORK]
<opaque body>
~~~

RESOURCE는 메인 역할 상태와 분리된 사이드카 대기열로 실행한다. WORK가 RESOURCE를 요청하면 Worker는 명시된 종류와 자연어 요청을 FIFO 대기열에 넣고 접수 사실을 HQ에 전달한다. 이후 의미적 다음 단계는 HQ가 현재 사용자 목표와 관측된 실행 사실을 바탕으로 결정한다. HQ가 아직 END하지 않은 동안 RESOURCE 완료가 성공이든 실패든 Worker는 해당 requestId, 종류, 결과/오류를 다음 WORK 입력에 기계적으로 함께 전달한다. RESOURCE 실패는 UNKNOWN으로 승격해 HQ에 우회 전달하지 않는다. HQ가 END한 뒤에는 RESOURCE 완료 때문에 HQ나 WORK를 다시 호출하지 않는다. Worker는 HQ 종료 상태를 고정하고 남은 기계적 대기 작업만 추적한다.

일반 본문는 불투명다. JUDGE 목적지의 스키마 검사와 RESOURCE 자연어 본문의 비어 있음 검사는 전송 계층의 기계적 유효성 검사이며 작업 의미 판단이 아니다.

---

## 6. HQ 설계와 ACTION 의미

새 사용자 요청 또는 목표가 크게 바뀐 요청에서 HQ는 단순 전달자가 아니다. 필요한 만큼 구현 방향을 설계해 WORK에 전달한다.

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
- 사용자가 실행 중 취소하면 Worker는 현재 실행 프로세스와 해당 실행 구간의 대기 작업을 중단하고 상태를 CANCELED로 보존한다. 실행 중 `thread.started`에서 확보한 CLI session ID도 즉시 보존한다.
- 사용자가 작업 추가를 실행하면 Worker는 USER_FOLLOWUP으로 기존 HQ 세션부터 새 실행 구간을 시작한다. Worker가 후속 요청의 의미를 판단하거나 자동으로 재개하지 않는다.

HQ가 Web이든 CLI든 같은 역할 계약을 사용한다.

---

## 7. JUDGE 흐름

~~~text
WORK -> HQ      검증 질문 목록 + evidence + JUDGE용 Form 생성 요청
HQ   -> WORK    JUDGE용 Form
WORK -> JUDGE   Form 전송
JUDGE -> WORK   raw 결과
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
- JEV 원본 응답은 같은 WORK 세션으로 반환한다.

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
- `.projecthub/events/<jobId>.jsonl`은 작업 종료 시 일괄 생성하지 않고 이벤트 발생 시마다 즉시 append한다.
- `.projecthub/transcripts/<jobId>.txt`에는 작업 transcript를 저장한다.
- Worker 재시작 후 작업공간에 재개 가능한 상태가 있으면 이를 기계적으로 복구해 `작업 추가`를 허용한다.
- 저장된 Codex 세션이 로컬에 없으면 해당 세션 ID를 사용하지 않고, 새 HQ 세션에 프로젝트 기억 파일과 이벤트 로그 경로를 함께 전달해 관제 문맥을 복구할 수 있게 한다.
- 사용자가 `새 작업`을 명시적으로 선택하면 활성 session-state만 제거하고 과거 handoff/event/transcript 파일은 기록으로 남긴다.
- Worker는 저장된 기억이나 로그의 의미를 해석해 자동 작업을 시작하지 않는다.

---

## 11. 현재 활성 작업

활성 작업은 tasks/14-resource-web-role.md다.

구현 코드 범위:
1. HIGH 제거 / RESOURCE 역할 + 사이드카 대기열
2. HQ Web 대상 복원
3. HQ/RESOURCE 명시적 대화 연결
4. HQ 설계 책임 + PAUSE 예시
5. WORK RESOURCE 위임 계약
6. RESOURCE 사이드카 FIFO 대기열 + 복수 생성 파일 결과 전송과 저장
7. 파이프라인/설정/이력 교체
8. PAUSE/END 후 동일 세션 작업 추가와 고정 크기 이력 입력 UI
9. 테스트/문서 갱신

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
