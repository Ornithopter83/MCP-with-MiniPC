# ProjectHub 구현 로드맵

Updated: 2026-09-24

정책 원본은 Master-Polish.md다.

## Current architecture

~~~text
HQ      -> WORK | HIGH
WORK    -> JUDGE | HQ
JUDGE   -> WORK
HIGH    -> HQ
UNKNOWN -> HQ
~~~

- ACTION은 HQ만 CONTINUE/PAUSE/END를 사용한다.
- 신규 CLI 행선지는 GOTO를 사용한다.
- Worker는 흐름 제어 도구이며 의미 판단을 하지 않는다.
- HIGH는 사용자 실행 시점 one-shot permit으로만 사용 가능하다.
- 기존 GPT Web NEXT:WEB/JEV는 legacy mode에서 보존한다.

## Worker boundary

Worker가 담당:
- 제어행 문법
- 상태 전이
- 역할별 session/provider 실행
- timeout/cancel/auth/transport 오류
- transcript/usage
- HIGH permit 상태

Worker가 담당하지 않음:
- AC 충족 판단
- 테스트 충분성 판단
- evidence 의미 판단
- JUDGE/JEV 결과 재판정
- WORK 재작업 필요 여부 판단
- HIGH 자동 승격
- HQ END 재검증

판단과 다음 행동 결정은 AI가 수행한다.

## Current active work

### 11-C-GOTO-CONTRACT

목표:
- 기존 CLI-to-CLI 라우터를 최종 ACTION+GOTO 계약으로 정리
- Worker semantic judgment 제거
- JUDGE 결과를 같은 WORK session으로 복귀
- HIGH를 HQ→HIGH→HQ one-shot 경로로 제한
- UNKNOWN 오류 복귀 경로 구현
- HIGH 실행 권한을 메인 UI의 사용자 체크+실행 이벤트로 변경

상세 구현 계약은 tasks/11-cli-coordinator-first.md를 따른다.

완료 조건:
- 단위 라우팅 테스트
- Debug/Release 빌드
- Explorer 실제 왕복
- Legacy Web 회귀 없음

## Deferred

현재 활성 작업이 끝나기 전에는 다음을 동시에 구현하지 않는다.

- JobRunner crash/restart 복구
- 추가 Provider 실연동
- 비용 기반 자동 정책
- 고급 예산/호출 제한 UX
- 기타 대규모 리팩터링

## UI residual

11-UI-B-EXPLORER-COLORS는 실제 화면 확인이 남아 있다. GOTO 계약 구현과 섞어 기능 범위를 확장하지 않는다.

## Policy guard

향후 기능을 추가할 때 다음 질문을 먼저 적용한다.

> 이 기능이 Worker가 작업 결과를 판단하게 만드는가?

YES면 Worker 기능으로 구현하지 않는다.

판단은 HQ/WORK/JUDGE/HIGH가 수행하고 Worker는 계약된 흐름만 실행한다.
