# 13 JUDGE plan review flow

Updated: 2026-09-24

정책 원본은 `Master-Polish.md`다.

## A — HQ review before JUDGE

최근 WORK/JUDGE 로그에서 확인한 문제는 JUDGE transport 자체가 아니라 질문 설계였다. WORK가 넓은 기능 묶음을 하나의 NOUL로 보내거나, count/time/cause처럼 다른 정보 형태도 NOUL confidence 하나로 묻는 경우가 반복됐다.

사용자 결정:
- NOUL 사용량을 Worker 규칙으로 제한하지 않는다.
- SCORE/CHOICE 사용 비율을 강제하지 않는다.
- 특정 질문형을 금지하지 않는다.
- 대신 역할 contract에 다양한 좋은 사용 예를 제공한다.
- 판정이 필요하면 WORK가 먼저 HQ에 초안/evidence를 전달해 검토받는다.

### 흐름

~~~text
WORK -> HQ
  판정하려는 항목, draft questions, 현재 evidence 전달

HQ -> WORK
  질문 범위, evidence, 수치화 가능한 기준과 응답 형태를 검토해 반환

WORK -> JUDGE
  HQ 검토안을 인지한 뒤 실제 NOUL/SCORE/CHOICE 요청 작성

JUDGE -> WORK
  raw response 반환

WORK -> HQ
  결과 보고. 추가 판정이 필요하면 새 초안을 다시 HQ에 검토 요청
~~~

### 질문 예시 방향

NOUL:
- restart가 특정 transient state를 정리하는가

SCORE:
- 7개 bundled asset 중 몇 개가 검증 조건을 만족하는가
- 측정 가능한 count/range/level을 단계로 표현

CHOICE:
- line-clear / stage-card / input gate / spawn wait / 복합 원인 / evidence 부족 중 무엇이 현재 blocker인가

예시는 illustrative이며 quota나 강제 선택 규칙이 아니다.

### Worker 경계

변경하지 않음:
- WorkerGotoContract
- WorkerRoleState
- JEV transport parser
- opaque body 정책

Worker는 WORK body가 판정 초안인지, HQ가 검토했는지, 질문형이 적절한지 판단하지 않는다.

### 검증

- WORK contract가 JUDGE 전 HQ review flow를 설명
- HQ contract가 WORK의 판정 초안을 검토해 WORK로 반환하는 역할을 설명
- 두 contract 모두 NOUL/SCORE/CHOICE 예시를 포함
- 예시가 quota/mandatory distribution이 아님을 명시
- JUDGE unavailable contract는 기존처럼 HQ만 허용
- parser/state graph 회귀 없음
