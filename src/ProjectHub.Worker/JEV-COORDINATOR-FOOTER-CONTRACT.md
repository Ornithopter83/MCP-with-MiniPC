# CLI Role Routing Contract — ACTION + GOTO

Updated: 2026-09-24

정책 원본은 Master-Polish.md다.

이 문서는 신규 CLI-to-CLI 역할 계약을 정의한다.

## Core rule

**Worker는 판단하지 않는다.**

Worker는 첫 제어행과 상태 전이만 파싱하고 BODY를 opaque하게 전달한다.

## State graph

~~~text
HQ      -> WORK | HIGH
WORK    -> JUDGE | HQ
JUDGE   -> WORK
HIGH    -> HQ
UNKNOWN -> HQ
~~~

## HQ

HQ만 ACTION을 사용한다.

~~~text
[ACTION=CONTINUE]
[ACTION=PAUSE]
[ACTION=END]
~~~

CONTINUE:

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

PAUSE/END에는 GOTO가 없다.

Worker는 HQ END를 의미적으로 재판정하지 않는다.

## WORK

WORK는 ACTION을 사용하지 않는다.

허용:

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

WORK -> HIGH는 금지다.

## JUDGE

JUDGE 결과는 반드시 같은 WORK session으로 복귀한다.

~~~text
[GOTO : WORK]

[JUDGMENT]
<raw judge result>
~~~

JEV native API가 GOTO를 출력하지 않으면 adapter가 GOTO:WORK wrapper만 붙인다.

Worker는 score/confidence/threshold를 비교하지 않는다.

## HIGH

HIGH는 JUDGE를 사용하지 않는다.

~~~text
[GOTO : HQ]

[REPORT]
...
~~~

HIGH -> HQ만 허용한다.

## UNKNOWN

protocol/provider/transport/session/route 오류는:

~~~text
[GOTO : UNKNOWN]

[ERROR]
source_state: ...
code: ...
detail: ...
~~~

형태로 HQ에 전달한다.

Worker는 다른 정상 역할로 자동 대체하지 않는다.

## HIGH one-shot availability

HIGH route는 현재 Job의 high_uses_remaining == 1일 때만 HQ allowed route에 포함한다.

permit 생성자는 메인 UI의 사용자 체크 + 실행 클릭이다.

Worker는 사용자 텍스트에서 HIGH 허가를 추론하지 않는다.

## Worker responsibilities

허용:
- syntax/state transition
- role/session/provider execution
- timeout/cancel/auth/transport
- transcript/usage
- HIGH permit state
- raw body forwarding

금지:
- WorkCard/AC/evidence semantic validation
- command equivalence 판단
- test sufficiency 판단
- JUDGE/JEV PASS/FAIL 계산
- END 재검증
- 자동 재작업
- 자동 HIGH 승격
