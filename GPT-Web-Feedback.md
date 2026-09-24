# GPT-Web-Feedback — Current implementation guidance

Updated: 2026-09-24

정책 원본은 Master-Polish.md다.

이 문서는 현재 활성 작업 11-C-GOTO-CONTRACT의 구현 지침만 담는다.

---

## 1. 절대 원칙 — Worker는 판단하지 않는다

**Worker는 흐름 제어 도구다.**

판단은 AI가 한다.

- HQ: 설계·관제·최종 ACTION 판단
- WORK: 실제 작업·검증·JUDGE 사용 여부 판단
- JUDGE: WORK가 요청한 의미 판단
- HIGH: 사용자 1회 허가 기반 고수준 작업
- Worker: 계약 파싱·상태 전이·실행·전달

Worker가 직접 판단하면 안 되는 항목:

- 요구사항 충족 여부
- AC PASS/FAIL
- 테스트 충분성
- evidence 충분성/신선도
- planned command와 observed command의 의미적 동일성
- JUDGE/JEV threshold 의미
- JUDGE 결과의 PASS/PARTIAL/FAIL 재분류
- 재작업 필요 여부
- HIGH가 필요한지 여부
- 최종 완료 여부
- HQ END 거부

Worker는 process exit code, stdout/stderr, session, usage 같은 사실을 기록할 수 있지만 그 사실의 의미를 결정하지 않는다.

---

## 2. 최종 상태 계약

~~~text
HQ      -> WORK | HIGH
WORK    -> JUDGE | HQ
JUDGE   -> WORK
HIGH    -> HQ
UNKNOWN -> HQ
~~~

신규 CLI-to-CLI의 route keyword는 GOTO다.

~~~text
[GOTO : HQ]
[GOTO : WORK]
[GOTO : HIGH]
[GOTO : JUDGE]
[GOTO : UNKNOWN]
~~~

신규 CLI 경로에서 NEXT는 사용하지 않는다.

기존 GPT Web의 NEXT:WEB / NEXT:JEV는 legacy mode에서만 보존한다.

---

## 3. ACTION 계약

ACTION은 HQ만 사용한다.

~~~text
[ACTION=CONTINUE]
[ACTION=PAUSE]
[ACTION=END]
~~~

ACTION=HQ는 제거한다.

### HQ CONTINUE

기본:

~~~text
[ACTION=CONTINUE]
[GOTO : WORK]

[INSTRUCTION]
...
~~~

HIGH one-shot permit이 남아 있을 때만:

~~~text
[ACTION=CONTINUE]
[GOTO : HIGH]

[INSTRUCTION]
...
~~~

### HQ PAUSE

~~~text
[ACTION=PAUSE]

[REPORT]
...
~~~

### HQ END

~~~text
[ACTION=END]

[REPORT]
...
~~~

Worker는 유효한 END를 AC/test/evidence/JUDGE 결과로 재심사하지 않는다.

---

## 4. WORK 계약

WORK는 ACTION을 출력하지 않는다.

허용 GOTO는 HQ 또는 JUDGE뿐이다.

### HQ 보고

~~~text
[GOTO : HQ]

[REPORT]

수행 내용:
- ...

변경:
- ...

검증:
- ...

남은 사항:
- ...
~~~

Worker는 REPORT 본문을 의미적으로 검사하지 않는다.

### JUDGE 요청

~~~text
[GOTO : JUDGE]

[VALIDATION REQUEST]

...
~~~

Worker는 요청을 JUDGE에 전달한다.

WORK → HIGH는 금지다.

---

## 5. JUDGE 계약

JUDGE는 WORK 전용 보조 판단자다.

~~~text
WORK -> JUDGE -> 같은 WORK session
~~~

자유형 AI Judge:

~~~text
[GOTO : WORK]

[JUDGMENT]
...
~~~

JEV native API처럼 GOTO를 출력하지 않는 Provider는 adapter가 다음 wrapper만 기계적으로 붙일 수 있다.

~~~text
[GOTO : WORK]

[JUDGMENT]
<raw provider response>
~~~

Worker/Judge adapter가 해도 되는 일:

- request serialization
- provider 호출
- response parsing
- timeout/auth/HTTP/schema 오류
- raw response 보존
- same WORK session 복귀

Worker/Judge adapter가 하면 안 되는 일:

- confidence/score threshold 비교
- PASS/PARTIAL/FAIL 의미 생성
- evidence 충분성 판단
- 자동 재작업
- 자동 HQ 복귀
- HIGH 승격

JUDGE 오류는 UNKNOWN으로 HQ에 전달한다.

---

## 6. HIGH 계약

HIGH는 사용자 1회 허가가 있을 때만 HQ가 호출할 수 있다.

HIGH는 ACTION을 사용하지 않는다.

HIGH는 JUDGE를 사용하지 않는다.

정상 응답:

~~~text
[GOTO : HQ]

[REPORT]
...
~~~

허용:

~~~text
HQ -> HIGH -> HQ
~~~

금지:

~~~text
HIGH -> JUDGE
HIGH -> WORK
HIGH -> HIGH
~~~

---

## 7. HIGH one-shot UI permit

설정창의 기존 HIGH “사용” 체크박스는 제거한다.

HIGH 설정 카드에는 다음만 남긴다.

- provider
- model
- reasoning
- thread/session

메인 화면 실행 버튼 바로 왼쪽에:

~~~text
[ ] 고수준 작업 허용    [ ▶ 실행 ]
~~~

체크박스를 둔다.

### Run click

unchecked:

~~~text
high_uses_remaining = 0
~~~

checked:

~~~text
high_uses_remaining = 1
~~~

그 후 UI checkbox는 즉시 unchecked로 복원한다.

진행 중 Job은 launch snapshot만 사용한다.

Worker는 자유형 사용자 텍스트에서 HIGH 허가를 추론하지 않는다.

### HIGH dispatch

HQ가 GOTO:HIGH를 출력하고 permit이 1이면:

~~~text
high_uses_remaining: 1 -> 0
invoke HIGH
~~~

dispatch 실패라도 permit을 자동 복구하지 않는다.

Job 종료 시 미사용 permit은 폐기한다.

다음 Job으로 이월하지 않는다.

---

## 8. HQ Footer의 allowed route

HIGH permit 없음:

~~~text
Allowed GOTO:
- WORK

HIGH is not available for this Job.
~~~

HIGH permit 있음:

~~~text
Allowed GOTO:
- WORK
- HIGH

HIGH may be used once.
HIGH returns directly to HQ and does not use JUDGE.
~~~

HIGH 사용 후:

~~~text
Allowed GOTO:
- WORK

HIGH has already been consumed for this Job.
~~~

Worker는 HIGH가 필요한지 판단하지 않는다.

Worker가 하는 일은 현재 permit 상태를 계약에 표시하는 것뿐이다.

---

## 9. UNKNOWN

UNKNOWN은 AI 역할이 아니다.

기계적 오류를 HQ에 돌려보내는 상태다.

예:

- ACTION 누락
- 금지 GOTO
- HIGH permit 없음
- JUDGE 비활성/연결 불가
- provider timeout
- authentication 오류
- process 시작 실패
- session resume 실패
- transport/schema 오류

예제:

~~~text
[GOTO : UNKNOWN]

[ERROR]
source_state: WORK
code: GOTO_NOT_ALLOWED
requested_goto: HIGH
detail: <original error/response>
~~~

Worker는 오류를 보고 WORK/HIGH/JUDGE로 자동 대체하지 않는다.

UNKNOWN은 HQ에 전달된다.

HQ가 다음 ACTION을 판단한다.

---

## 10. Parser 책임

### HQ

- 첫 유효행 ACTION 필수
- ACTION 허용: CONTINUE/PAUSE/END
- CONTINUE이면 GOTO 필수
- GOTO 허용: WORK, 또는 permit이 남은 HIGH
- PAUSE/END에는 GOTO 없음

### WORK

- ACTION 금지
- 첫 제어행 GOTO
- 허용: HQ/JUDGE

### JUDGE

- ACTION 금지
- 결과는 WORK로 복귀

### HIGH

- ACTION 금지
- GOTO:HQ만 허용

### UNKNOWN

- 다음 상태 HQ 고정

Parser는 BODY 내용을 해석하지 않는다.

---

## 11. 코드에서 제거할 구형 로직

신규 CLI-to-CLI control path에서 다음 로직은 제거하거나 사용하지 않는다.

- ACTION=HQ
- NEXT : IMPLEMENTER
- NEXT : HIGH_LEVEL
- NEXT : COORDINATOR
- NEXT : JUDGE
- WorkCard schema를 완료 gate로 사용
- ImplementerResult schema를 완료 gate로 사용
- Review AC 집합 비교
- HasRequiredValidationEvidence
- CommandMatches를 작업 완료 gate로 사용
- evidenceOk
- judgeOk
- Worker accepted 계산
- Worker END 재판정
- JEV threshold를 Worker가 비교
- evidence digest 변경을 Worker가 의미적 재판정 근거로 사용
- 고정 재작업 3회 후 구현 실패 판정
- IMPLEMENT 후 별도 routing-only AI 호출
- JUDGE 결과에 따라 Worker가 자동 WORK 재호출
- Worker가 자동 HIGH fallback 선택
- HIGH 설정창 영구 enable 값으로 route 허용

command execution 추출 자체는 transcript/관제 전달용 기록으로 남길 수 있다.

기록과 판단을 분리한다.

---

## 12. 유지할 Worker 기능

다음은 의미 판단이 아닌 실행 인프라이므로 유지한다.

- working directory 확인
- role/provider/model 실행 가능 여부
- auth 상태
- session ID/resume
- process start/exit
- timeout/cancel
- sandbox
- transcript
- usage
- credential redaction
- 외부 Git/배포 승인
- route grammar
- state transition
- HIGH one-shot permit

인프라 오류는 작업 실패로 Worker가 판정하지 않고 UNKNOWN으로 HQ에 전달한다.

---

## 13. UI 변경

### Settings

제거:
- HighLevelEnabledCheckBox
- HIGH “사용 여부 / 사용” UI

유지:
- HIGH provider
- HIGH model
- HIGH reasoning
- HIGH thread/session

### Main

실행 영역:

~~~text
[ preflight/status ... ] [ ] 고수준 작업 허용  [ ▶ 실행 ]
~~~

HIGH card는 permit을 가졌다는 이유만으로 활성색이 되지 않는다.

HIGH 실제 dispatch 중일 때만 HIGH stage를 활성 표시한다.

---

## 14. 핵심 회귀 테스트

### ACTION/GOTO

- HQ CONTINUE + GOTO WORK → WORK 1회
- HQ CONTINUE + permit 1 + GOTO HIGH → HIGH 1회
- HQ PAUSE → 추가 AI 0
- HQ END → 추가 AI 0
- WORK GOTO HQ → same HQ session
- WORK GOTO JUDGE → JUDGE → same WORK session
- WORK GOTO HIGH → UNKNOWN → HQ
- JUDGE ACTION → UNKNOWN → HQ
- HIGH GOTO HQ → HQ
- HIGH GOTO JUDGE → UNKNOWN → HQ

### Worker non-judgment

- REPORT가 잘못된 PASS 주장을 포함해도 Worker는 그대로 HQ에 전달
- JUDGMENT confidence가 낮아도 Worker는 의미적 FAIL 생성 금지
- exit_code 1이 있어도 Worker가 자동 WORK 재호출 금지
- required/observed command 문자열이 달라도 Worker가 작업 PASS/FAIL 생성 금지
- evidence가 누락돼 보여도 Worker가 HQ END를 거부하지 않음
- HQ END가 문법적으로 유효하면 종료

### HIGH

- unchecked Run → permit 0
- checked Run → permit 1
- launch 직후 checkbox unchecked
- permit 1이어도 HQ가 WORK만 사용 가능
- HIGH dispatch 직전 permit 0
- HIGH 재호출 불가
- HIGH failure 후 permit 복구 금지
- 다음 Job permit 이월 금지

### Legacy

- GPT Web NEXT:WEB/JEV 기존 경로 회귀 없음

---

## 15. Explorer E2E

실제 UI에서 최소 네 경로를 확인한다.

~~~text
A. HQ -> WORK -> HQ -> END

B. HQ -> WORK -> JUDGE -> WORK -> HQ -> END

C. 사용자 “고수준 작업 허용” 체크
   -> Run
   -> HQ -> HIGH -> HQ -> END 또는 WORK

D. invalid GOTO/provider error
   -> UNKNOWN
   -> HQ
~~~

C에서 확인:
- HIGH 호출은 최대 1회
- HIGH 뒤 JUDGE 없음
- HIGH 사용 후 HQ allowed route에서 HIGH 없음

---

## 16. 구현 완료 판단

11-C-GOTO-CONTRACT가 완료되려면:

- Worker가 의미 판단을 하지 않는 코드 경로
- ACTION/GOTO 상태 머신
- HIGH one-shot UI permit
- JUDGE same-WORK return
- UNKNOWN→HQ
- Legacy Web 회귀
- Explorer 실제 왕복

이 모두 확인되어야 한다.

**최종 리뷰 질문:**

> Worker가 작업 내용의 옳고 그름 또는 다음 행동을 스스로 판단하는 코드가 남아 있는가?

남아 있다면 제거 대상이다.
