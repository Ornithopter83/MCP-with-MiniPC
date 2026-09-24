# GPT-Web-Feedback — 11-C-GOTO-CONTRACT 최신 점검 및 계약 분리

Updated: 2026-09-24

정책 원본은 `Master-Polish.md`다.

이 문서는 최신 `main` HEAD `ef673549a15561a9dd8a071dfd5660256be1ff24`의 실제 구현을 점검한 **현재 작업 피드백**만 담는다.

---

## 1. 현재 구현 상태

11-C-GOTO-CONTRACT의 핵심 코드는 이미 구현됐다.

확인된 구현:

- `WorkerGotoContract` 추가
- 상태: `HQ / WORK / JUDGE / HIGH / UNKNOWN`
- 전이:
  - `HQ -> WORK | HIGH`
  - `WORK -> JUDGE | HQ`
  - `JUDGE -> WORK`
  - `HIGH -> HQ`
  - `UNKNOWN -> HQ`
- ACTION은 HQ에서만 `CONTINUE / PAUSE / END`
- HIGH one-shot permit용 `JobHighLevelPermit` 구현
- 메인 화면의 `고수준 작업 허용` 체크박스 구현
- HIGH dispatch 전에 permit 소모
- WORK와 HIGH의 routing 규칙을 분리
- JEV adapter는 raw response를 반환
- JUDGE 결과는 기존 WORK session으로 돌아감
- Worker의 JEV PASS/PARTIAL threshold 판정과 자동 재시도 제거
- protocol/provider/session/transport 오류를 UNKNOWN으로 HQ에 전달
- Legacy Web의 `NEXT:WEB/JEV` 경로는 유지

현재 자동 검증 기록:

~~~text
dotnet test ProjectHub.sln --no-restore
Worker 56
Server 1
Agent 3
Core 1

dotnet build ProjectHub.sln -c Release --no-restore
경고 0 / 오류 0
~~~

즉 **핵심 상태 머신의 코드 구현과 자동 테스트는 완료 단계**다.

남은 실제 완료 조건은 Explorer E2E다.

~~~text
A. HQ -> WORK -> HQ -> END
B. HQ -> WORK -> JUDGE -> WORK -> HQ -> END
C. HIGH permit -> HQ -> HIGH -> HQ
D. 오류 -> UNKNOWN -> HQ
~~~

---

## 2. 현재 잘 된 부분

### 2.1 Worker 비판단 원칙이 실제 흐름에 반영됨

최신 `RunCoordinatorFirstJobAsync()`는 과거의:

- WorkCard 완료 gate
- AC 집합 비교
- required command match
- evidenceOk
- judgeOk
- JEV threshold PASS/PARTIAL
- 고정 재시도 횟수

를 현재 GOTO 흐름의 완료 판단에 사용하지 않는다.

HQ가 유효한 `ACTION=END`를 반환하면 Worker가 내용 검사를 추가하지 않고 종료한다.

이 방향이 Master 정책과 맞다.

### 2.2 HIGH one-shot permit이 올바른 위치로 이동함

HIGH 허가는 설정의 상시 enable이 아니라 실행 시점 UI snapshot으로 들어간다.

~~~text
unchecked + Run -> permit 0
checked   + Run -> permit 1
~~~

`JobHighLevelPermit.TryConsume()`이 실제 HIGH dispatch 직전에 한 번만 성공한다.

이 구조를 유지한다.

### 2.3 JUDGE 결과를 Worker가 판정하지 않음

`JevJudgeRunner.ReviewRawAsync()`는 provider raw body와 transport error를 반환한다.

현재 GOTO 경로에서 Worker가 결과 점수를 비교해 PASS/FAIL을 만들지 않는다.

이 원칙을 유지한다.

---

# 3. P0 — 역할별 계약을 완전히 분리할 것

현재 가장 먼저 보완할 부분이다.

정책은 역할별 계약이 분리되어 있지만 실제 구현은 아직 세 방식이 섞여 있다.

현재:

- HQ: `JEV-COORDINATOR-FOOTER-CONTRACT.md` 전체를 로드
- WORK: `MainWindow.xaml.cs` 안의 inline string
- HIGH: `MainWindow.xaml.cs` 안의 inline string
- JUDGE: `MainWindow.xaml.cs`에서 raw response 앞에 `GOTO:WORK`를 직접 조합
- UNKNOWN: local `RouteUnknown()`에서 envelope 생성

이 상태에서는 계약을 수정할 때 MainWindow 코드와 여러 문서를 같이 수정해야 하고, HQ에게 다른 역할의 규칙까지 불필요하게 노출된다.

## 3.1 최종 계약 파일 구조

신규 CLI 계약은 아래처럼 **역할별 독립 파일**로 분리한다.

~~~text
src/ProjectHub.Worker/Contracts/
  HQ-ROUTING-CONTRACT.md
  WORK-ROUTING-CONTRACT.md
  HIGH-ROUTING-CONTRACT.md
  JUDGE-ROUTING-CONTRACT.md
  UNKNOWN-ENVELOPE-CONTRACT.md

Legacy:
  LEGACY-WEB-JEV-FOOTER-CONTRACT.md
~~~

기존 `JEV-COORDINATOR-FOOTER-CONTRACT.md`라는 이름은 제거한다.

HQ 계약은 JEV 계약이 아니다.

역할 라우팅 계약과 JEV provider 계약을 파일명부터 분리한다.

## 3.2 계약 로더 분리

`JevContract.LoadCoordinatorFooter()`로 HQ contract를 읽지 않는다.

예:

~~~text
RoleContractLoader.LoadHq()
RoleContractLoader.LoadWork(judgeAvailable)
RoleContractLoader.LoadHigh()
RoleContractLoader.LoadJudge()
~~~

또는 동등한 작은 loader를 둔다.

각 계약은 embedded resource로 포함한다.

MainWindow에는 role-specific contract 본문을 하드코딩하지 않는다.

---

# 4. Header와 Footer의 책임도 분리

각 AI 호출은 아래 구조로 통일하는 것을 권장한다.

~~~text
ROLE INPUT HEADER
+
OPAQUE INBOUND BODY
+
ROLE OUTPUT FOOTER
~~~

Worker는 Header/Footer를 제공하지만 **본문을 해석하거나 새 작업 판단을 추가하지 않는다.**

---

## 4.1 HQ 계약

### HQ Input Header

Worker가 알려줄 것은 기계적 상태뿐이다.

~~~text
[ROLE : HQ]

[INBOUND TYPE : USER_REQUEST | WORK_REPORT | HIGH_REPORT | UNKNOWN]

[AVAILABLE GOTO]
WORK
HIGH   # permit이 있을 때만
~~~

HIGH permit이 없으면 HIGH 줄 자체를 넣지 않는다.

permit이 있으면 추가 메타데이터:

~~~text
[HIGH PERMIT : ONE_SHOT]
remaining=1
~~~

HQ Header에 포함하면 안 되는 것:

- “이 작업은 어려우므로 HIGH를 써라”
- “테스트가 부족하다”
- “JUDGE 결과가 낮다”
- “다시 수정해야 한다”

이런 판단은 HQ가 한다.

### HQ Output Footer

~~~text
You are HQ.
Only HQ may emit ACTION.

First control line:
[ACTION=CONTINUE]
[ACTION=PAUSE]
[ACTION=END]

CONTINUE requires exactly one GOTO from AVAILABLE GOTO.

Use [INSTRUCTION] after CONTINUE.
Use [REPORT] after PAUSE or END.

Do not emit GOTO:JUDGE.
Do not emit GOTO:HQ.
~~~

HQ 계약 파일에는 WORK/JUDGE/HIGH 역할의 출력 계약을 설명하지 않는다.

---

## 4.2 WORK 계약

### WORK Input Header

~~~text
[ROLE : WORK]

[INBOUND TYPE : HQ_INSTRUCTION | JUDGMENT]

[JUDGE AVAILABLE : true|false]
~~~

JUDGE가 OFF이면 Worker는 WORK에게 JUDGE를 정상 선택지로 보여주지 않는 편이 좋다.

이것은 의미 판단이 아니라 capability 전달이다.

### WORK Output Footer — JUDGE ON

~~~text
You are WORK.
Do not emit ACTION.

First control line must be exactly one of:

[GOTO : HQ]
[GOTO : JUDGE]

GOTO:HQ -> [REPORT]
GOTO:JUDGE -> [VALIDATION REQUEST]

Do not emit GOTO:HIGH.
~~~

### WORK Output Footer — JUDGE OFF

~~~text
You are WORK.
Do not emit ACTION.

Your only normal destination is:

[GOTO : HQ]

Follow with [REPORT].

JUDGE is unavailable for this Job.
Do not emit GOTO:HIGH.
~~~

현재 구현처럼 JUDGE가 OFF인데도 WORK Footer가 항상 JUDGE를 보여준 뒤 UNKNOWN으로 보내는 것보다 이 방식이 불필요한 오류를 줄인다.

---

## 4.3 HIGH 계약

### HIGH Input Header

~~~text
[ROLE : HIGH]

[INVOCATION : ONE_SHOT]
~~~

HQ가 작성한 INSTRUCTION 본문을 그대로 넣는다.

### HIGH Output Footer

~~~text
You are HIGH.

Do not emit ACTION.
Do not use JUDGE.
Do not route to WORK.
Do not route to HIGH.

Your only valid destination is:

[GOTO : HQ]

Follow with [REPORT].
~~~

HIGH 계약에는 JUDGE 사용법 자체를 넣지 않는다.

---

## 4.4 JUDGE 계약

JUDGE는 두 종류를 구분한다.

### Native JEV provider

JEV는 일반 CLI 역할처럼 Footer를 요구하지 않는다.

Worker adapter는 WORK의 `VALIDATION REQUEST`를 provider request로 변환하고 raw response를 받는다.

Worker가 할 일:

~~~text
serialize request
call provider
read response
return raw response to WORK
~~~

Worker가 하지 않을 일:

~~~text
threshold compare
PASS/PARTIAL/FAIL
evidence sufficiency
retry decision
next role decision
~~~

### 향후 AI Judge provider

AI Judge를 붙이는 경우에만 독립 `JUDGE-ROUTING-CONTRACT.md`를 사용한다.

~~~text
You are JUDGE.
Do not emit ACTION.

Your only destination is:

[GOTO : WORK]

Follow with [JUDGMENT].
~~~

---

# 5. P0 — JUDGE → WORK 입력에서 GOTO를 제거할 것

현재 JUDGE 처리 후 Worker는 WORK에 넘길 `inbound` 자체를:

~~~text
[GOTO : WORK]

[JUDGMENT]
<raw response>
~~~

형태로 만든 뒤 state를 WORK로 변경한다.

여기에는 **출력 제어행과 입력 메시지의 역할이 섞여 있다.**

GOTO는 Worker가 이미 소비한 routing command다.

다음 WORK 호출의 입력에는 GOTO를 다시 넣지 않는 편이 명확하다.

권장:

~~~text
[ROLE : WORK]
[INBOUND TYPE : JUDGMENT]

[JUDGMENT]
<raw response>
~~~

그 뒤 WORK 전용 Output Footer를 붙인다.

즉:

~~~text
JUDGE raw result
   ↓ Worker가 state=WORK 설정
WORK input header + raw judgment + WORK footer
~~~

로 한다.

AI 입력에 과거 GOTO 제어행을 다시 넣지 않는다.

---

# 6. P0 — Legacy Web에 남은 구형 HQ 계약 제거

최신 CLI GOTO 구현은 정리됐지만 `MainWindow.xaml.cs`의 Legacy Web 영역에는 아직 다음 구형 계약이 남아 있다.

~~~text
[ACTION=HQ]
[NEXT : IMPLEMENTER|HIGH_LEVEL|JUDGE|COORDINATOR]
~~~

구체적으로:

- `WebActionKind.Hq`
- `ParseWebAction()`의 HQ
- `RunWebResponseThroughCodexAsync()`의 ACTION HQ 분기
- `BuildWebPrompt()`의 ACTION=HQ 안내
- Web HQ 전달 prompt의 old role NEXT

이 문구들은 현재 Master의 신규 역할 계약과 충돌한다.

Legacy Web에서 보존해야 할 것은 원래 공개 wire인:

~~~text
[ACTION=CONTINUE]
[ACTION=PAUSE]
[ACTION=END]

[NEXT : WEB]
[NEXT : JEV]
~~~

뿐이다.

### 권장

Legacy Web에는 `ACTION=HQ`를 제거한다.

Web이 새 CLI HQ에 메시지를 넘겨야 할 기능이 정말 필요하면 old NEXT role protocol을 Web에게 가르치지 말고 별도 adapter command를 사용한다.

예:

~~~text
SYSTEM HANDOFF -> HQ
message_type = WEB_HANDOFF
body = <raw web body>
~~~

그 뒤부터는 새 HQ ACTION+GOTO contract가 처리한다.

**Legacy Web protocol과 신규 role protocol을 한 응답 안에 섞지 않는다.**

---

# 7. P0 — 사용하지 않는 구형 semantic contract 코드 제거

최신 GOTO loop는 `WorkerGotoContract`를 사용한다.

하지만 저장소에는 아직 `CoordinatorFirstContracts.cs`의 구형 구조가 남아 있다.

예:

- `CoordinatorWorkCard`
- `WorkAcceptanceCriterion`
- `ImplementerResult`
- `CoordinatorReview`
- `TryParseWorkCard()`
- `TryParseReview()`
- `TryParseReviewAction()`
- `HasRequiredValidationEvidence()`
- `CommandMatches()`

그리고 현재 테스트에도 이 구형 contract 테스트가 남아 있다.

이 코드는 **Worker 비판단 정책을 다시 끌어들일 수 있는 가장 큰 혼동 요소**다.

### 작업

1. 전체 참조 검색
2. 신규 GOTO/Legacy Web 어느 runtime에서도 사용하지 않으면 파일과 해당 테스트 제거
3. 정말 Legacy에 필요한 타입이 있으면 의미 gate가 없는 최소 transport 타입만 별도 legacy namespace/file로 이동

단위 테스트 숫자를 유지하기 위해 사용하지 않는 semantic gate 코드를 보존하지 않는다.

---

# 8. P1 — JUDGE request parser의 책임을 더 좁힐 것

현재 신규 JUDGE 경로도:

~~~text
JevContract.TryParseValidation(...)
~~~

을 호출한다.

이 parser는 현재 `PASS:` threshold까지 읽고 range를 검사한다.

최신 `ReviewRawAsync()`는 이 threshold를 provider 결과 판단에는 사용하지 않는다.

실제 provider request 생성에 필요한 것은 주로:

- question type
- instructions
- score criteria
- choice criteria
- evidence IDs/state

이다.

따라서 신규 GOTO JUDGE path에서는 **provider 호출에 필요한 구조만 parse**하는 별도 parser를 두는 것을 권장한다.

예:

~~~text
JudgeTransportContract.ParseRequest(...)
~~~

이 parser는:
- request 구조가 API 호출 가능한지 확인
- 질문 type/criteria를 provider schema로 변환

만 한다.

WORK가 자신의 판단 기준으로 적은 PASS threshold는:
- opaque instructions로 보존하거나
- WORK session 문맥에 맡기고
- Worker가 range/meaning을 검증하지 않는다.

Legacy Web/JEV가 기존 PASS 문법 호환을 꼭 필요로 한다면 legacy parser에만 남긴다.

---

# 9. P1 — 파일명에서도 역할과 Provider를 분리

현재 이름:

~~~text
JEV-COORDINATOR-FOOTER-CONTRACT.md
~~~

은 두 책임을 섞는다.

최종적으로:

~~~text
Contracts/HQ-ROUTING-CONTRACT.md
Contracts/WORK-ROUTING-CONTRACT.md
Contracts/HIGH-ROUTING-CONTRACT.md
Contracts/JUDGE-ROUTING-CONTRACT.md
Contracts/UNKNOWN-ENVELOPE-CONTRACT.md

Legacy/LEGACY-WEB-JEV-FOOTER-CONTRACT.md
JEV-API-CONTRACT.md
~~~

처럼 구분하는 편이 좋다.

- Role contract = AI 출력 제어
- JEV API contract = provider transport
- Legacy Web contract = 과거 Web wire

세 영역을 파일 구조에서도 섞지 않는다.

---

# 10. P1 — MainWindow에서 계약 문자열 제거

현재 WORK/HIGH footer가 `MainWindow.xaml.cs` inline string이다.

이를 모두 contract loader로 이동한다.

MainWindow의 역할은:

~~~text
1. 현재 state 확인
2. 입력 Header 생성
3. 해당 role contract 로드
4. prompt 조립
5. AI 실행
6. control parse
7. 다음 state 전환
~~~

까지만 둔다.

MainWindow에 다음과 같은 긴 자연어 규약을 직접 넣지 않는다.

~~~text
"Control contract for WORK..."
"Control contract for HIGH..."
~~~

계약 수정 시 UI orchestration 코드를 수정하지 않아도 되게 한다.

---

# 11. 계약 조립 예시

## HQ

~~~text
[ROLE : HQ]
[INBOUND TYPE : WORK_REPORT]

[AVAILABLE GOTO]
WORK
HIGH

[HIGH PERMIT]
remaining=1

[INBOUND BODY]
<WORK report>

--- OUTPUT CONTRACT ---
<HQ-ROUTING-CONTRACT.md>
~~~

## WORK — 최초 작업

~~~text
[ROLE : WORK]
[INBOUND TYPE : HQ_INSTRUCTION]
[JUDGE AVAILABLE : true]

[INBOUND BODY]
<HQ instruction>

--- OUTPUT CONTRACT ---
<WORK-ROUTING-CONTRACT.md>
~~~

## WORK — Judge 복귀

~~~text
[ROLE : WORK]
[INBOUND TYPE : JUDGMENT]
[JUDGE AVAILABLE : true]

[JUDGMENT]
<raw JEV response>

--- OUTPUT CONTRACT ---
<WORK-ROUTING-CONTRACT.md>
~~~

## HIGH

~~~text
[ROLE : HIGH]
[INVOCATION : ONE_SHOT]

[INBOUND BODY]
<HQ instruction>

--- OUTPUT CONTRACT ---
<HIGH-ROUTING-CONTRACT.md>
~~~

---

# 12. 회귀 테스트 추가

## 계약 파일 격리

### CONTRACT-HQ-01
HQ contract에 다음이 없어야 한다.

~~~text
GOTO : JUDGE
GOTO : HQ
WORK output rules
HIGH output rules
~~~

### CONTRACT-WORK-01
Judge ON:
- HQ/JUDGE만 존재

Judge OFF:
- HQ만 존재

### CONTRACT-HIGH-01
HIGH contract에는 정상 GOTO가 HQ 하나뿐이다.

### CONTRACT-JUDGE-01
native JEV adapter는 raw response를 변경하지 않는다.

### CONTRACT-JUDGE-02
JUDGE 결과를 WORK 입력으로 줄 때 `[GOTO : WORK]`를 prompt body에 다시 넣지 않는다.

## Legacy 분리

### CONTRACT-LEGACY-01
Legacy Web에는:

~~~text
ACTION CONTINUE/PAUSE/END
NEXT WEB/JEV
~~~

만 존재.

### CONTRACT-LEGACY-02
Legacy Web prompt에 아래 문자열이 없어야 한다.

~~~text
ACTION=HQ
NEXT : IMPLEMENTER
NEXT : HIGH_LEVEL
NEXT : COORDINATOR
~~~

## dead code

### CONTRACT-DEAD-01
신규 GOTO runtime에서 `CoordinatorFirstContracts` semantic gate 참조 0.

### CONTRACT-DEAD-02
구형 `HasRequiredValidationEvidence/CommandMatches`가 runtime completion decision에 사용되지 않음.

---

# 13. Explorer E2E에서 추가로 볼 것

기존 네 경로에 **실제 prompt 계약 표시/로그**를 같이 확인한다.

### A. HQ → WORK → HQ

확인:
- HQ prompt에는 HQ contract만
- WORK prompt에는 WORK contract만
- HIGH/JUDGE 출력 규칙이 불필요하게 노출되지 않음

### B. WORK → JUDGE → WORK

확인:
- JUDGE raw response가 같은 WORK session으로 복귀
- WORK 입력에 이전 `GOTO:WORK` control tag가 다시 섞이지 않음
- Worker가 PASS/FAIL을 생성하지 않음

### C. HIGH

확인:
- permit이 없을 때 HQ available GOTO에 HIGH 없음
- permit이 있을 때만 HIGH 노출
- HIGH prompt에는 JUDGE 선택지 없음
- HIGH 결과는 HQ로만 복귀

### D. UNKNOWN

확인:
- 원본 오류가 HQ에 전달
- Worker가 WORK/HIGH/JUDGE 중 하나로 자동 대체하지 않음

---

# 14. 권장 작업 순서

현재 코드 구현은 이미 상당 부분 완료됐으므로 기능을 다시 쓰지 말고 **계약 경계 정리**에 집중한다.

1. 역할별 contract 파일 생성
2. `RoleContractLoader` 추가
3. HQ의 combined coordinator footer 제거
4. WORK/HIGH inline footer 제거
5. JUDGE→WORK input에서 GOTO control tag 제거
6. JUDGE ON/OFF에 따라 WORK allowed route contract 생성
7. Legacy Web의 ACTION=HQ / old role NEXT 제거
8. `CoordinatorFirstContracts` 구형 semantic code 참조 검색 후 제거/격리
9. 신규 Judge transport parser와 legacy parser 책임 분리
10. 단위 회귀 테스트
11. Release build
12. Explorer 네 경로 E2E

---

# 15. 이번 점검의 결론

현재 구현은 **정책 방향 자체는 맞게 넘어왔다.**

가장 중요한 상태 머신과 Worker 비판단 원칙은 코드에 반영됐다.

다음 단계에서 새 기능을 더 붙이기보다:

> **역할별 계약을 파일 단위로 분리하고, MainWindow는 계약을 로드해서 전달만 하며, legacy Web/JEV와 신규 GOTO 계약을 완전히 격리하는 것**

이 우선이다.

특히 다음 세 항목은 P0로 본다.

1. **HQ/WORK/HIGH/JUDGE 계약 파일 분리**
2. **Legacy Web의 ACTION=HQ + old role NEXT 제거**
3. **구형 CoordinatorFirstContracts semantic gate 코드 제거/격리**

이 세 가지가 끝나면 Worker가 “흐름제어 툴”이라는 구조가 코드 레벨에서도 훨씬 명확해진다.
