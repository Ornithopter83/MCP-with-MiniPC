# CurrentWork

Updated: 2026-09-24

## Current policy

정책 원본: Master-Polish.md

**Worker는 판단하지 않는다. Worker는 흐름 제어 도구다.**

~~~text
HQ      -> WORK | HIGH
WORK    -> JUDGE | HQ
JUDGE   -> WORK
HIGH    -> HQ
UNKNOWN -> HQ
~~~

ACTION은 HQ만 CONTINUE/PAUSE/END를 사용한다.

신규 CLI-to-CLI는 GOTO를 사용한다.

## Current implementation gap

현재 활성 작업은 11-C-GOTO-CONTRACT다.

현재 코드에 최종 정책 이전 계약이 남아 있을 수 있으므로 아래를 정리한다.

- ACTION=HQ 제거
- 신규 CLI NEXT 제거 → GOTO
- HQ/WORK/JUDGE/HIGH/UNKNOWN 전이 적용
- Worker의 AC/test/evidence/JUDGE 의미 판단 제거
- Worker의 END 재판정 제거
- JUDGE raw result를 같은 WORK session으로 복귀
- HIGH는 HQ에서만 호출, HQ로만 복귀, JUDGE 미사용
- HIGH 설정창 상시 사용 체크 제거
- 메인 실행 버튼 왼쪽 고수준 작업 허용 one-shot checkbox 추가
- 사용자 체크+실행 때만 Job-local HIGH permit 1회 생성
- 오류 → UNKNOWN → HQ
- Legacy Web NEXT:WEB/JEV 회귀 보존

## Worker boundary

Worker는 다음을 기계적으로 처리한다.

- 제어행 문법
- 상태 전이
- session/provider 실행
- timeout/cancel/auth/transport
- transcript/usage
- HIGH permit

Worker는 다음을 판단하지 않는다.

- 요구사항 충족 여부
- AC PASS/FAIL
- 테스트 충분성
- evidence 충분성
- JUDGE/JEV 의미 결과
- 재작업 필요 여부
- HIGH 필요 여부
- 최종 완료 여부

## HIGH permit

~~~text
unchecked + Run -> high_uses_remaining = 0
checked   + Run -> high_uses_remaining = 1
~~~

HIGH dispatch 직전에 permit을 소모한다.

Worker는 사용자 텍스트에서 HIGH 허가를 추론하지 않는다.

## Verification required

단위:
- 상태 전이 parser/router
- HQ-only ACTION
- JUDGE→same WORK
- HIGH→HQ only
- HIGH one-shot permit
- UNKNOWN→HQ
- Worker semantic gate 없음

Explorer:
- HQ→WORK→HQ→END
- HQ→WORK→JUDGE→WORK→HQ→END
- HIGH 허가→HQ→HIGH→HQ
- invalid route/provider error→UNKNOWN→HQ

## Other residual

- 11-UI-B-EXPLORER-COLORS: 실제 Explorer 색상 확인 잔여

과거 구현 계약은 현재 판단 기준으로 사용하지 않는다.
