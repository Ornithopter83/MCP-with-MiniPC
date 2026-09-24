# Master-Polish — ProjectHub 현재 정책

Updated: 2026-09-24 (KST)

이 문서는 ProjectHub의 현재 최상위 정책 원본이다. 다른 구현계획, CurrentWork, task, feedback 문서가 충돌하면 이 문서를 우선한다.

ProjectHub의 목표는 AI가 판단하고 Worker가 흐름만 제어하는 역할 분리형 CLI-to-CLI 개발 도구다.

---

## 1. 최상위 불변식

### 1.1 Worker는 판단하지 않는다

Worker는 흐름 제어 도구다. 판단 주체가 아니다.

판단 주체는 HQ, WORK, JUDGE, HIGH다. Worker는 역할 실행, 상태 전환, 세션, transport, telemetry를 기계적으로 관리한다.

Worker가 해도 되는 일:
- 현재 상태(HQ/WORK/JUDGE/HIGH/UNKNOWN) 저장
- ACTION/GOTO 제어행 문법 파싱과 허용 전이 확인
- 역할별 provider/model/reasoning/session 실행 및 resume
- process start/exit, timeout, cancel, authentication, transport/schema 오류 처리
- transcript, usage, session ID, 호출 시각, 파일 변경 telemetry 기록
- HIGH one-shot permit 저장/소모
- 제어행 뒤 opaque body를 원문 의미 그대로 전달
- protocol/provider/transport/session 오류를 UNKNOWN으로 기록하고 HQ에 한글 요약으로 1회 복귀
- 사용자 승인 경계와 sandbox 같은 기계적 안전장치 적용
- 역할 응답 완료 시 이미 알고 있는 role/state/usage/file telemetry로 UI 이력 카드 기록

Worker가 하면 안 되는 일:
- 요청 난이도, 의도, 우선순위 판단
- 적절한 역할을 Worker가 선택하거나 HIGH로 자동 승격
- AC 충족, 테스트 충분성, command/evidence/file/diff의 의미 판단
- JUDGE/JEV score/confidence/threshold를 비교해 PASS/PARTIAL/FAIL 생성
- JUDGE 결과를 근거로 자동 재작업 또는 다음 역할 결정
- 토큰/반복 횟수를 근거로 작업 성공·실패 판정
- HQ의 유효한 ACTION=END를 별도 semantic gate로 거부
- opaque body를 읽고 GOTO 추론
- AI 대신 작업 지시·수정 방향·검증 질문 생성
- UI나 routing을 위해 AI 본문에 semantic section tag를 강제하거나 검색

---

## 2. 역할과 상태

| 상태 | UI 역할명 | 책임 |
| --- | --- | --- |
| HQ | 설계·관제 AI | 전체 설계·관제·최종 판단 |
| WORK | 작업 AI | 일반 구현·수정·검증·보고 |
| JUDGE | 작업 판단 AI | WORK가 요청한 의미 판단 |
| HIGH | 고수준 작업 AI | 사용자 1회 허가 기반 고수준 작업 |
| UNKNOWN | 오류 상태 | 오류 원문은 한글 시스템 로그에 보관하고, HQ에는 한글 오류 요약만 전달 |

상태 전이:

~~~text
HQ      -> WORK | HIGH
WORK    -> JUDGE | HQ
JUDGE   -> WORK
HIGH    -> HQ
UNKNOWN -> 원문 로그 기록 -> HQ에 요약 전달 (Job당 1회) -> 일반 라우팅 재개
UNKNOWN 재발 -> 로그 기록 후 종료
~~~

역할별 session은 독립 유지한다.

---

## 3. 신규 CLI 출력 계약 — ACTION과 GOTO만 제어 토큰

신규 CLI-to-CLI에서 AI가 출력해야 하는 제어 토큰은 ACTION과 GOTO뿐이다. 제어행 뒤의 모든 내용은 opaque body다.

### HQ

~~~text
[ACTION=CONTINUE]
[GOTO : WORK]
<opaque body>
~~~

HIGH permit이 남아 있을 때만 GOTO:HIGH를 사용할 수 있다.

~~~text
[ACTION=PAUSE]
<opaque body>
~~~

~~~text
[ACTION=END]
<opaque body>
~~~

### WORK

~~~text
[GOTO : HQ]
<opaque body>
~~~

또는:

~~~text
[GOTO : JUDGE]
<opaque body>
~~~

### HIGH

~~~text
[GOTO : HQ]
<opaque body>
~~~

### 자유형 AI JUDGE

~~~text
[GOTO : WORK]
<opaque body>
~~~

UNKNOWN은 Worker 내부 오류 상태이며 정상 AI가 선택하는 목적지가 아니다.

### semantic body tag 금지

신규 CLI 역할 계약은 INSTRUCTION, REPORT, VALIDATION REQUEST, JUDGMENT 같은 본문 태그를 요구하지 않는다.

Worker는 이런 태그를 찾거나, 붙이거나, 지우거나, History 카드 분류에 사용하지 않는다.

제어행을 소비한 뒤 남은 전체 문자열이 그대로 body다.

예를 들어 WORK가 GOTO:JUDGE 뒤에 쓴 전체 body가 곧 JUDGE 요청 원문이다.

### Worker 입력 metadata

Worker는 ROLE, INBOUND TYPE, AVAILABLE GOTO, HIGH PERMIT, JUDGE AVAILABLE 같은 입력 전용 metadata를 호출 대상 AI에 제공할 수 있다.

이 metadata는 AI가 반환해야 하는 출력 계약이 아니며 History 카드 생성이나 의미 판단에 사용하지 않는다.

---

## 4. 역할별 정책

- HQ만 ACTION=CONTINUE/PAUSE/END를 사용한다.
- HQ CONTINUE의 정상 목적지는 WORK와, one-shot permit이 있을 때의 HIGH뿐이다.
- WORK는 HQ 또는 JUDGE로만 이동한다. JUDGE 사용 필요성은 WORK가 판단한다.
- WORK가 JUDGE 판정이 유용하다고 판단하면 먼저 HQ에 판정 초안과 현재 evidence를 전달한다.
- HQ는 판정 초안의 질문 범위, evidence, 응답 형태와 수치화 가능한 기준을 검토해 같은 WORK 흐름으로 돌려준다.
- WORK는 HQ의 검토안을 인지한 뒤 실제 JUDGE 요청을 구성해 `[GOTO : JUDGE]`로 보낸다.
- JUDGE 결과는 반드시 같은 WORK session으로 복귀한다.
- native JEV provider의 raw response는 별도 JUDGMENT tag 없이 같은 WORK session에 opaque body로 전달한다.
- 추가 판정이 필요하면 WORK는 새 판정 초안을 다시 HQ에 검토 요청한 뒤 JUDGE로 보낼 수 있다.
- HIGH는 JUDGE를 사용하지 않고 HQ로만 복귀한다.
- UNKNOWN은 오류 원문과 기술 상세를 한글 시스템 로그에 보관하고, 발생 역할·오류 코드·한국어 설명만 HQ에 최대 한 번 전달해 정상 관제를 재개한다. 동일 Job에서 오류 요약 전달 후 UNKNOWN이 다시 발생하면 추가 AI 호출 없이 로그에 기록하고 종료한다.

판정 흐름은 기존 상태 전이만 사용한다.

~~~text
WORK -> HQ (판정 초안 검토)
HQ   -> WORK (검토안 반환)
WORK -> JUDGE
JUDGE -> WORK
WORK -> HQ (결과 보고 또는 다음 판정 초안)
~~~

Worker는 body를 읽어 이 검토가 실제로 수행됐는지 판정하거나 강제하지 않는다. 새 제어 토큰이나 semantic marker도 추가하지 않는다. 이 흐름은 AI 역할 계약으로 유지한다.

WORK는 JUDGE가 활성화된 경우 `[GOTO : JUDGE]` 본문에 NOUL/SCORE/CHOICE 형식의 원자적 질문을 작성할 수 있다. 질문마다 고유 QID를 사용하고 SCORE/CHOICE 기준을 포함한다. PASS, SCOPE, COUNTEREXAMPLE, workspace 상대 EVIDENCE는 선택적으로 질문에 붙일 수 있다. 일부 질문의 재판정이 필요하면 해당 QID 질문만 다시 요청한다.

NOUL/SCORE/CHOICE는 사용 비율이나 우선순위를 강제하지 않는다. 역할 contract에는 다양한 사용 예를 제공한다.
- NOUL 예: 재시작 후 특정 transient state가 정리되는지 같은 단일 yes/no 주장
- SCORE 예: 7개 asset 중 몇 개가 조건을 만족하는지, 측정 가능한 단계/범위를 표현하는 질문
- CHOICE 예: 여러 후보 중 어떤 상태가 지연 원인인지 구분하는 질문
- 수치 기준은 사용자 요청, 현재 작업 결과 또는 workspace evidence에서 유도하고, 근거가 없는 숫자를 새로 만들지 않는다.

---

## 5. HIGH one-shot 사용자 허가

설정창에는 HIGH의 provider/model/reasoning/thread-session 설정만 둔다.

메인 화면 실행 버튼 왼쪽:

~~~text
[ ] 고수준 작업 허용    [ ▶ 실행 ]
~~~

체크 + 실행이면 현재 Job의 high_uses_remaining=1, 미체크 실행이면 0이다.

- permit 생성자는 사용자 체크 + 실행 클릭뿐
- Worker는 자연어에서 HIGH 허가를 추론하지 않음
- HIGH dispatch 직전에 1 -> 0
- 실패 시 자동 복구 없음
- Job 종료 시 남은 permit 폐기
- 다음 Job으로 이월 금지
- 실행 직후 checkbox unchecked
- permit이 있어도 HIGH 사용 여부는 HQ가 판단

---

## 6. JUDGE transport 정책

Worker/Judge adapter는 provider 호출, 최소 schema 변환, timeout/auth/HTTP/schema 오류, raw response 보존과 전달만 수행한다.

WORK가 HQ 검토안을 인지한 뒤 GOTO:JUDGE를 출력하면 GOTO 뒤 body 전체를 요청 원문으로 취급한다. 신규 CLI에서는 VALIDATION REQUEST marker를 찾지 않는다.

HQ 검토 여부는 Worker가 body를 해석해 검사하지 않는다. Worker는 유효한 상태 전이와 transport만 처리한다.

Worker는 confidence/score threshold 비교, PASS/PARTIAL/FAIL 생성, evidence 충분성 판단, 자동 재작업을 하지 않는다.

---

## 7. 메시지 및 작업 이력 카드 정책

History UI는 관찰/표시 계층이며 routing protocol이 아니다. 카드를 만들기 위해 AI에게 별도 태그를 출력시키지 않는다.

카드 생성 근거는 Worker가 이미 알고 있는 호출 role, 응답 완료 시점, ACTION/GOTO 결과, provider usage, file telemetry, infrastructure error다.

신규 CLI에서는 LUNA RESULT, JEV RESULT 같은 source 문자열이나 본문 태그를 역으로 해석해 role/card 종류를 추론하지 않는다.

### 카드 본문 3줄 규격

1줄 — 표시용 요약:
- opaque body의 첫 유효 텍스트를 whitespace normalize
- 약 100~140자 범위의 UI 상수로 자르고 뒤 내용이 있으면 … 표시
- 추가 AI 호출로 요약하지 않음
- Worker가 의미를 재작성하거나 성공/실패를 추론하지 않음

2줄 — 토큰:

~~~text
토큰 · 총 1,284 · 입력 920 · 캐시 210 · 출력 164
~~~

reasoning usage가 별도 제공되면 같은 줄에 추가한다. usage가 없으면 토큰 · 미제공으로 표시하고 0으로 추정하지 않는다.

3줄 — 파일:

~~~text
파일 · 생성 1 · 수정 0 · 삭제 0 · projecthub-smoke.txt
~~~

파일이 많으면 대표 파일명 + 외 N개 형식으로 줄인다. 변경이 없으면 파일 · 변경 없음으로 표시한다.

현재 telemetry가 path/name/mime/size만 제공해 생성·수정·삭제를 구분할 수 없으면 Worker가 추정하지 않는다. 우선 파일 · N개 감지로 표시하거나 별도 기계적 FileChangeTelemetry를 추가한다.

카드는 짧게 보여주되 전체 AI 원문, stdout/stderr, 상세 usage/file 목록은 transcript/detail에 보존할 수 있다.

---

## 8. 인프라·세션·Provider 경계

Worker는 working directory, provider/model 실행 가능 여부, auth, timeout/cancel, sandbox, role session, secret redaction, 승인 없는 Git/배포 차단, transcript/usage/file telemetry를 기계적으로 관리할 수 있다.

HQ는 기본 read-only, WORK/HIGH는 승인된 작업 폴더에서 workspace-write를 사용할 수 있다.

Worker는 지원되지 않는 모델을 임의 대체하지 않는다.

### 8.1 Provider 확장 정책

AI 역할 Provider의 코드 식별자는 `AiServiceProvider` enum으로 관리한다.

현재 예약 Provider:

~~~text
OpenAI
Claude
Muse
~~~

영속 설정의 wire 값은 호환성을 위해 소문자 문자열(`openai`, `claude`, `muse`)을 유지하고, 실행 코드에서는 이를 enum으로 해석한다. 알 수 없는 Provider를 OpenAI로 자동 대체하지 않는다.

Provider 이름은 안정된 식별자이고 모델명과 reasoning은 교체 가능한 catalog 데이터다. HQ / WORK / HIGH는 각각 독립적으로 Provider → Model → Reasoning을 선택한다.

실행은 `IAiRoleRunner` 경계로 분리한다. Worker의 역할 라우팅은 Provider별 CLI 세부 명령을 판단하지 않고, 선택된 Provider runner의 preflight/run 결과만 기계적으로 처리한다.

현재 실제 실행이 연결된 Provider는 OpenAI Codex CLI뿐이다. Claude/Muse는 UI/catalog/visual/runner skeleton까지 준비하되 인증·실행 transport가 연결되기 전에는 `CLAUDE_NOT_CONFIGURED` / `MUSE_NOT_CONFIGURED`로 차단한다. 다른 Provider로 자동 fallback하지 않는다.

Provider가 session/resume을 지원하는지는 capability로 관리한다. 지원하지 않거나 아직 연결되지 않은 Provider의 session 선택 UI는 비활성화한다.

Provider icon은 단일 visual resolver에서 결정한다. 공식 자산이 준비되지 않은 Provider는 중립 console symbol을 fallback으로 사용하며, 기능 코드에 특정 브랜드 asset 경로를 하드코딩하지 않는다.

JEV는 AI 역할 Provider 목록에 포함하지 않고 JUDGE 전용 판단 transport로 유지한다.

---

## 9. 비용·토큰 정책

Worker는 role/provider/model/reasoning, input/cached/output/reasoning usage, latency, session/call ID 같은 측정값만 기록한다.

비용이나 토큰량을 작업 품질 판단에 사용하지 않는다. History 카드의 토큰 줄도 telemetry 표시일 뿐이다.

---

## 10. Legacy Web 호환

Legacy Web의 ACTION=CONTINUE/PAUSE/END와 NEXT:WEB/JEV는 별도 legacy mode에서만 보존한다.

Legacy의 기존 본문 marker가 필요하면 legacy namespace/contract 내부에만 한정하고 신규 CLI로 가져오지 않는다.

---

## 11. 현재 구현 우선순위

현재 활성 작업은 **13-A JUDGE plan review flow**다.

1. WORK가 판정이 필요하다고 판단하면 판정 초안과 evidence를 HQ에 먼저 전달
2. HQ가 질문 범위, evidence, 수치화 가능한 기준과 응답 형태를 검토해 WORK에 반환
3. WORK가 검토안을 인지한 뒤 JUDGE 요청 구성
4. JUDGE raw response는 같은 WORK session으로 복귀
5. 추가 판정이 필요하면 새 초안을 다시 HQ 검토 흐름에 태울 수 있음
6. NOUL/SCORE/CHOICE 사용 예를 WORK/HQ contract에 다양하게 제공
7. 질문형 비율, 우선순위, 금지 조건은 Worker 규칙으로 만들지 않음
8. Worker parser/state/JEV transport와 opaque-body 원칙은 유지
9. contract 회귀 테스트
10. Windows build/test 및 Explorer 실제 흐름 확인

12-B Provider-ready 구조는 완료 기록으로 유지한다. Claude/Muse 실제 CLI/인증/model discovery/session 연결은 12-D deferred다.

---

## 12. 최종 체크

이 로직이 작업 내용의 옳고 그름이나 다음 행동을 Worker 스스로 판단하면 Worker에 두지 않는다.

UI/전달 기능 때문에 AI에게 ACTION/GOTO 외 별도 semantic tag를 출력시키고 있다면 태그 의존성을 제거하고 Worker가 이미 가진 state/telemetry를 사용한다.

판단은 AI가 하고 Worker는 계약된 흐름과 관찰 가능한 실행 사실만 관리한다.
