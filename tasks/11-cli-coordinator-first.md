# 11 Coordinator-first CLI-to-CLI

Updated: 2026-09-24

## Current active scope — 11-C-GOTO-CONTRACT

Master-Polish.md가 최상위 정책 원본이다.

현재 목표는 기존 CLI-to-CLI 경로를 **HQ / WORK / JUDGE / HIGH / UNKNOWN + ACTION/GOTO** 계약으로 맞추는 것이다.

## Core invariant

**Worker는 판단하지 않는다. Worker는 흐름 제어 도구다.**

Worker가 수행하는 일:
- 제어행 문법 파싱
- 상태 전이 확인
- 역할별 session/provider 실행
- GOTO 라우팅
- timeout/cancel/auth/transport 오류 처리
- transcript/usage 기록
- HIGH one-shot permit 저장/소모

Worker가 하지 않는 일:
- 작업 결과 정답 여부 판단
- AC 충족 판단
- 테스트 충분성 판단
- evidence 충분성 판단
- JUDGE/JEV PASS/FAIL 의미 판정
- 재작업 필요 여부 판단
- 최종 완료 여부 판단
- 역할 자동 승격/대체

최종 판단은 AI가 한다.

## State contract

~~~text
HQ      -> WORK | HIGH
WORK    -> JUDGE | HQ
JUDGE   -> WORK
HIGH    -> HQ
UNKNOWN -> HQ
~~~

## ACTION

ACTION은 HQ만 사용한다.

~~~text
[ACTION=CONTINUE]
[ACTION=PAUSE]
[ACTION=END]
~~~

ACTION=HQ는 사용하지 않는다.

HQ의 CONTINUE:
- 기본: [GOTO : WORK]
- 현재 Job에 HIGH permit이 남아 있을 때만: [GOTO : HIGH]

PAUSE/END에는 GOTO가 없다.

HQ의 유효한 END를 Worker가 별도 semantic gate로 거부하지 않는다.

## Role contracts

### HQ

~~~text
[ACTION=CONTINUE]
[GOTO : WORK]

[INSTRUCTION]
...
~~~

또는 HIGH permit이 있을 때:

~~~text
[ACTION=CONTINUE]
[GOTO : HIGH]

[INSTRUCTION]
...
~~~

### WORK

~~~text
[GOTO : HQ]

[REPORT]
...
~~~

또는:

~~~text
[GOTO : JUDGE]

[VALIDATION REQUEST]
...
~~~

WORK는 HIGH를 호출하지 않는다.

### JUDGE

JUDGE는 반드시 같은 WORK session으로 복귀한다.

~~~text
[GOTO : WORK]

[JUDGMENT]
...
~~~

JEV native 응답에는 adapter가 GOTO:WORK wrapper만 기계적으로 붙일 수 있다. Worker가 threshold를 비교해 의미적 PASS/FAIL을 만들지 않는다.

### HIGH

HIGH는 JUDGE를 사용하지 않는다.

~~~text
[GOTO : HQ]

[REPORT]
...
~~~

## HIGH one-shot permit

HIGH는 설정창의 상시 ON/OFF 기능이 아니다.

설정창에는 HIGH의 provider/model/reasoning/thread 설정만 둔다.

메인 화면 실행 버튼 왼쪽:

~~~text
[ ] 고수준 작업 허용    [ ▶ 실행 ]
~~~

사용자가 체크한 상태로 실행을 누르면:

~~~text
high_uses_remaining = 1
~~~

미체크 실행:

~~~text
high_uses_remaining = 0
~~~

규칙:
- Worker는 텍스트에서 허가를 추론하지 않는다.
- 실제 HIGH dispatch 직전에 1→0.
- HIGH 실패 시 permit 자동 복구 없음.
- Job 종료 시 남은 permit 폐기.
- 다음 Job에 이월 금지.
- 실행 직후 checkbox는 unchecked.
- 진행 중 Job은 실행 시작 snapshot만 사용.

## UNKNOWN

정상 AI 역할이 아니다.

기계적 오류 예:
- 제어행 누락/문법 오류
- 금지된 GOTO
- HIGH permit 없음
- JUDGE 비활성/연결 실패
- provider timeout/auth 오류
- process/session/transport 오류

Worker는 자동 대체하지 않는다.

~~~text
[GOTO : UNKNOWN]

[ERROR]
source_state: ...
code: ...
detail: ...
~~~

를 HQ에 전달한다.

HQ가 다음 ACTION을 판단한다.

## Required code changes

1. 신규 CLI 경로의 NEXT 제거, GOTO 도입
2. ACTION=HQ 제거
3. 역할 enum/state를 HQ/WORK/JUDGE/HIGH/UNKNOWN으로 정리
4. ACTION parser를 HQ 응답에만 적용
5. HQ allowed GOTO = WORK 또는 permit이 남은 HIGH
6. WORK allowed GOTO = HQ/JUDGE
7. JUDGE return = 같은 WORK session
8. HIGH return = HQ only
9. WORK/HIGH Footer 분리
10. Worker semantic gates 제거
11. JEV threshold/evidence 의미 판정을 Worker flow에서 제거
12. protocol/infrastructure error → UNKNOWN → HQ
13. 설정창 HIGH 사용 체크박스 제거
14. 메인 실행 버튼 왼쪽 one-shot HIGH checkbox 추가
15. Job-local high_uses_remaining 구현
16. 기존 GPT Web NEXT:WEB/JEV legacy 회귀 보존

## Regression tests

- HQ CONTINUE + GOTO WORK → WORK 1회
- HQ END → 추가 AI 호출 0, Worker semantic gate 0
- WORK GOTO HQ → 같은 HQ session
- WORK GOTO JUDGE → JUDGE → 같은 WORK session
- WORK GOTO HIGH → UNKNOWN → HQ
- JUDGE ACTION 출력 → UNKNOWN → HQ
- HIGH GOTO HQ → 같은 HQ session
- HIGH GOTO JUDGE → UNKNOWN → HQ
- HIGH permit 0 → HQ allowed list에 HIGH 없음
- HIGH permit 1 → HIGH 최대 1회
- HIGH 사용 후 재요청 → UNKNOWN → HQ
- REPORT/JUDGMENT 내용이 잘못돼 보여도 Worker가 의미 판정하지 않고 전달
- command quoting/exit code가 달라도 Worker가 작업 성공/실패를 판단하지 않음
- Legacy Web NEXT:WEB/JEV 정상 회귀

## Explorer E2E

최소 네 경로를 실제 UI에서 확인한다.

~~~text
A. HQ -> WORK -> HQ -> END
B. HQ -> WORK -> JUDGE -> WORK -> HQ -> END
C. HIGH 허가 -> HQ -> HIGH -> HQ -> END 또는 WORK
D. invalid route/provider error -> UNKNOWN -> HQ
~~~

## Completion condition

11-C-GOTO-CONTRACT 완료는 **Worker가 의미 판단 없이 계약된 흐름만 제어하는 실제 Explorer 왕복**이 확인됐을 때 기록한다.

빌드/단위테스트만으로 Explorer E2E 완료를 선언하지 않는다.
