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

권장 형식은 각 질문을 **하나의 반증 가능한 주장**으로 제한하는 다중 행 형식이다. 기존 inline PASS 형식도 호환성을 위해 허용하지만 새 질문에는 아래 형식을 사용한다.

```text
- NOUL | [<importance>] <단일 검증 주장>
  EVIDENCE: <제공된 근거의 짧은 발췌 또는 파일/테스트 참조>
  SCOPE: <이번 판정의 범위>
  COUNTEREXAMPLE: <이 주장을 반증하는 관찰>
  PASS: YES >= <threshold>
```

`EVIDENCE`, `SCOPE`, `COUNTEREXAMPLE`는 선택 항목이다. 작성할 때 존재하지 않는 파일·테스트·실행 결과를 만들어내지 않는다. Worker는 Codex가 반환한 작은 텍스트 artifact와 작업 폴더 안에서 `EVIDENCE:`에 명시한 텍스트 파일을 제한적으로 읽어 provenance/digest와 함께 전달할 수 있다. 파일 경로나 참조 이름만 적혀도 실제 전달 여부는 보장되지 않으므로, JEV 결과가 근거 부족으로 남거나 Worker가 `SUMMARY_ONLY`만 전달한 질문은 PASS로 완료하지 않는다. Worker가 파일을 읽었다는 것은 소스 발췌이지 테스트 실행 증거가 아니다. 테스트 실행을 주장할 때는 실제 runner 결과를 별도 evidence로 전달해야 한다.

importance는 `[LOW]`, `[MEDIUM]`, `[HIGH]`, `[CRITICAL]` 중 하나이며 수치 의미는 다음과 같다.

| 중요도 | 기본 PASS |
| --- | --- |
| LOW | `YES >= 0.60` |
| MEDIUM | `YES >= 0.70` |
| HIGH | `YES >= 0.80` |
| CRITICAL | `YES >= 0.90` |

질문과 threshold는 검증 호출 전에 고정한다. JEV 결과가 낮다는 이유로 threshold를 낮추지 않는다. 관련 원자 질문은 같은 요청에 batch할 수 있으나 서로 다른 주장을 한 질문으로 합치지 않는다.

예:

```text
- NOUL | [HIGH] ResetRound()은 생명 손실 뒤 현재 Brick 배치를 유지하는가?
  EVIDENCE: GameWorld.cs — ResetRound() 경로의 소스 발췌를 확인할 것.
  SCOPE: 생명 손실 후 같은 Stage를 재시작하는 경로.
  COUNTEREXAMPLE: Brick 목록을 비우거나 다시 만들면 NO.
  PASS: YES >= 0.80
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

- NOUL | [<importance>] <원자적 주장>
  EVIDENCE: <짧은 근거 또는 참조>
  SCOPE: <판정 범위>
  COUNTEREXAMPLE: <반증 조건>
  PASS: YES >= <threshold>
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
- 각 NOUL은 하나의 검증 주장만 포함한다. 필요한 경우 EVIDENCE/SCOPE/COUNTEREXAMPLE를 질문의 continuation line으로 명시한다.
- Worker는 질문 continuation line을 JEV `instructions`에 보존하고, 내부 state의 evidence envelope에 실제 전달한 발췌·digest·provenance와 QID 매핑을 함께 기록한다. 수집할 수 없는 경로 문자열은 SUMMARY_ONLY fallback으로 남긴다.
- 각 검증에는 명시적인 PASS 문턱값 또는 허용값을 둔다.
- Worker는 AI의 의미적 판단을 대신 수행하지 않는다.
- 유효한 JEV 응답이 고정 threshold에 미달하면 이를 곧바로 구현 FAIL로 보지 않고 `PARTIAL`로 처리한다. 같은 Codex session은 먼저 실제 모순·증거 부족·confidence 미달·사용자 확인 필요를 구분한다. threshold를 낮추거나 confidence만 맞추려고 코드를 바꾸지 않는다.
- evidence가 바뀐 경우 영향을 받는 원자 질문만 다시 요청한다. retry 질문은 `[QID:C1]`처럼 기존 질문 ID를 유지하며, 영향받지 않은 PASS 질문은 재전송하지 않는다. QID가 없는 기존 footer는 등장 순서대로 C1, C2…를 부여한다.
- PARTIAL 재검증은 같은 작업에서 최대 3회다. 세 번째에도 해결되지 않거나 추가 검증이 불가능하면 `[NEXT : WEB]` 보고로 검토를 요청하고 구현 완료로 표시하지 않는다.
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
