# JEV Footer Contract v1

이 파일은 ProjectHub Worker가 Codex CLI 작업 지시 뒤에 붙일 JEV 분기용 footer 계약의 기준안이다.

핵심 역할은 다음과 같다.

- GPT Web: 작업 목표와 상위 지침을 제공하는 관리자
- Worker: 모든 메시지를 수신하고 다음 목적지로 전달하는 라우터
- Codex CLI: 실제 작업 수행자
- JEV: Codex가 요청한 검증 항목을 평가하는 선택형 검증자

모든 흐름은 반드시 Worker를 경유한다.

```text
GPT Web
  ↓
Worker
  ↓
Codex CLI
  ↓
Worker
  ├─ [NEXT : WEB] → GPT Web
  └─ [NEXT : JEV] → JEV
                        ↓
                      Worker
                        ↓
                  후속 목적지 처리
```

JEV와 Codex CLI가 서로 직접 통신하거나 직접 반복 작업을 수행한다고 가정하지 않는다.

---

## VARIABLE CONTRACT

Codex CLI는 현재 결과를 GPT Web에 보고하기 전에 의미적 검증이 필요하다고 판단하는 경우 아래 세 타입 중 필요한 항목을 사용해 JEV 검증을 요청한다.

변동 계약의 질문, 척도, 선택지, 문턱값은 작업마다 달라질 수 있다.

### 1. NOUL

예/아니오로 판단 가능한 조건을 검증한다.

형식:

```text
NOUL | <검증 질문> | PASS: YES >= <threshold>
```

예:

```text
NOUL | 현재 구현이 사용자의 원래 요구사항을 충분히 충족하는가? | PASS: YES >= 0.90
```

사용 예:
- 요구사항을 충족했는가
- 특정 기능을 변경하지 않았다고 판단할 수 있는가
- 결과가 주어진 조건과 일치하는가

### 2. SCORE

품질, 위험도, 범위 이탈 정도처럼 단계적 평가가 필요한 조건을 검증한다.

형식:

```text
SCORE | <검증 질문>
<점수/단계 정의>
PASS: <문턱 조건>
```

예:

```text
SCORE | 요구사항 대비 범위 이탈 정도는 어느 수준인가?
1 = 정확히 요구 범위 안
2 = 경미한 주변 변경
3 = 불필요한 변경 포함
4 = 상당한 범위 이탈
5 = 다른 작업 수준
PASS: SCORE <= 2.0
```

사용 예:
- 요구사항 일치도
- 범위 이탈 정도
- 변경 위험도
- 결과 완성도

### 3. CHOICE

여러 의미적 상태 중 하나를 분류해야 할 때 사용한다.

형식:

```text
CHOICE | <검증 질문>
<선택지 이름> = <의미>
...
PASS: <허용 선택지>
```

예:

```text
CHOICE | 현재 변경의 성격을 분류하라.
EXPECTED = 요청한 변경 범위
MINOR = 관련 주변 코드의 경미한 변경
OUT_OF_SCOPE = 요구하지 않은 변경
UNKNOWN = 판단 정보 부족
PASS: EXPECTED 또는 MINOR
```

사용 예:
- 변경 성격 분류
- 산출물 상태 분류
- 요구사항 충족 형태 분류
- 위험 유형 분류

---

## FIXED CONTRACT

Codex CLI의 최종 응답 첫 유효행은 반드시 다음 둘 중 하나여야 한다.

```text
[NEXT : WEB]
```

또는

```text
[NEXT : JEV]
```

두 태그를 동시에 사용하지 않는다.

### [NEXT : WEB]

현재 결과를 GPT Web에 보고할 준비가 되었다고 판단할 때 사용한다.

태그 다음에는 GPT Web 관리자가 현재 작업을 판단할 수 있도록 간결한 보고서를 작성한다.

권장 형식:

```text
[NEXT : WEB]

[REPORT]

수행 내용:
- ...

변경 사항:
- ...

검증 결과:
- ...

남은 사항:
- 없음 / ...
```

Worker 동작:

```text
Codex CLI
→ Worker
→ GPT Web
```

### [NEXT : JEV]

현재 결과를 GPT Web에 보고하기 전에 VARIABLE CONTRACT에 따른 JEV 검증이 필요할 때 사용한다.

태그 다음에는 보고서나 장황한 자체평가를 작성하지 않는다.

JEV가 검증해야 할 항목만 작성한다.

형식:

```text
[NEXT : JEV]

[VALIDATION REQUEST]

- NOUL | <질문> | PASS: YES >= <threshold>
- SCORE | <질문/척도> | PASS: <조건>
- CHOICE | <질문/선택지> | PASS: <허용값>
```

예:

```text
[NEXT : JEV]

[VALIDATION REQUEST]

- NOUL | 현재 구현이 원래 요구사항을 충족하는가? | PASS: YES >= 0.90

- SCORE | 요구사항 대비 범위 이탈 정도는 어느 수준인가?
  1 = 정확히 요구 범위 안
  2 = 경미한 주변 변경
  3 = 불필요한 변경 포함
  4 = 상당한 범위 이탈
  5 = 다른 작업 수준
  PASS: SCORE <= 2.0

- CHOICE | 현재 변경의 성격을 분류하라.
  EXPECTED = 요청한 변경 범위
  MINOR = 경미한 주변 변경
  OUT_OF_SCOPE = 요구하지 않은 변경
  UNKNOWN = 판단 정보 부족
  PASS: EXPECTED 또는 MINOR
```

Worker 동작:

```text
Codex CLI
→ Worker
→ JEV
```

JEV 응답 역시 Worker가 먼저 수신한다. Worker가 다음 목적지를 결정하고 전달한다.

---

## RESPONSE RULE

- 첫 유효행은 반드시 `[NEXT : WEB]` 또는 `[NEXT : JEV]` 중 하나다.
- 두 NEXT 태그를 동시에 사용하지 않는다.
- `[NEXT : WEB]` 뒤에는 `[REPORT]`를 작성한다.
- `[NEXT : JEV]` 뒤에는 `[VALIDATION REQUEST]`만 작성한다.
- JEV 요청에는 구현 보고서, 장문의 설명, 자유형 자체평가를 붙이지 않는다.
- JEV 검증 요청은 NOUL / SCORE / CHOICE 중 필요한 타입만 사용한다.
- 각 검증에는 명시적인 PASS 문턱값 또는 허용값을 둔다.
- Worker는 AI의 의미적 판단을 대신 수행하지 않는다.
- Worker는 모든 응답을 먼저 수신한 뒤 태그와 계약에 따라 목적지로 전달한다.
- JEV는 선택 기능이다. JEV가 비활성화된 상태에서는 기존 Worker ↔ Codex ↔ GPT Web 흐름을 유지한다.

---

## 범용성 원칙

JEV Contract는 특정 작업 종류를 정의하지 않는다.

대신 현재 결과에 대해 무엇을 검증할지를 정의한다.

따라서 동일한 계약 구조를 다음에 재사용할 수 있다.

```text
코드 구현
문서 작성
설계 검토
데이터 분석
파일 생성
리팩터링
테스트 결과 검토
```

작업이 달라져도 Worker의 전달 구조와 고정 NEXT 계약은 유지하고, VARIABLE CONTRACT의 질문·척도·선택지·문턱값만 바꾼다.
