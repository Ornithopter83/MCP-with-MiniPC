# JEV API Contract v1

이 문서는 ProjectHub Worker가 `[NEXT : JEV]` 요청을 실제 TypeSafe Jev API 호출로 변환하고, 응답을 다시 Worker 라우팅 판단에 사용할 때의 호출/응답 규약을 정의한다.

기준 footer 계약은 다음 파일을 사용한다.

```text
src/ProjectHub.Worker/JEV-FOOTER-CONTRACT.md
```

이 문서는 footer 문법을 다시 정의하지 않고, footer의 `[VALIDATION REQUEST]`를 실제 API 요청/응답으로 변환하는 방법만 정의한다.

---

## 1. 기본 호출 규약

공식 TypeSafe Jev endpoint:

```text
POST https://api.typesafe.ai/v1/systemone
```

인증:

```text
Authorization: Bearer <TYPESAFE_API_KEY>
Content-Type: application/json
```

API Key는 반드시 환경변수에서만 읽는다.

```text
TYPESAFE_API_KEY
```

금지:

```text
- API Key를 소스에 하드코딩
- target-settings.json에 저장
- MESSAGE LOG에 출력
- transcript에 출력
- 예외 메시지에 원문 Key 포함
- Git에 기록
```

Worker는 환경변수가 없거나 비어 있으면 JEV 호출을 시도하지 않는다.

---

## 2. 요청 Body 공통 형식

Jev 요청은 다음 세 필드를 사용한다.

```json
{
  "model": "jev-latest",
  "state": {},
  "questions": {}
}
```

의미:

```text
model
- 초기값: jev-latest
- 추후 운영 안정화 후 특정 버전 pin 가능

state
- 이번 검증에 필요한 실제 상태/문맥
- Worker가 의미를 요약하거나 재해석하지 않는다
- 원래 Task와 Codex CLI 결과를 그대로 구조화하여 넣는다

questions
- [VALIDATION REQUEST]에서 추출한 NOUL / SCORE / CHOICE 검증 항목
- 각 항목은 고유 ID(C1, C2, C3...)를 key로 사용
- 같은 state에 대한 여러 질문은 한 번의 API 호출에 묶는다
```

권장 state:

```json
{
  "task": "<원래 Task 지시 원문>",
  "codex_result": "<Codex CLI 최종 응답 원문>"
}
```

필요한 경우 Worker가 이미 보유한 비해석 메타데이터를 추가할 수 있다.

예:

```json
{
  "task": "...",
  "codex_result": "...",
  "round": 2,
  "working_directory": "C:\\GameProject\\TETRIS"
}
```

Worker는 state 내용을 새 문장으로 요약하거나 결론을 추가하지 않는다.

---

## 3. NOUL 요청 규약

Footer 예:

```text
NOUL | 현재 구현이 사용자의 원래 요구사항을 충분히 충족하는가? | PASS: YES >= 0.90
```

Jev question 변환:

```json
{
  "C1": {
    "type": "noul",
    "instructions": "현재 구현이 사용자의 원래 요구사항을 충분히 충족하는가?"
  }
}
```

선택적으로 true/false 의미를 명시할 수 있다.

```json
{
  "C1": {
    "type": "noul",
    "instructions": "현재 구현이 사용자의 원래 요구사항을 충분히 충족하는가?",
    "criteria": {
      "true": "원래 요구사항을 충분히 충족함",
      "false": "원래 요구사항을 충분히 충족하지 못함"
    }
  }
}
```

예상 응답:

```json
{
  "type": "noul",
  "noul": 0.94
}
```

해석:

```text
noul
- YES일 확률
- 범위: 0.0 ~ 1.0
```

Footer가 다음이라면:

```text
PASS: YES >= 0.90
```

Worker의 기계적 비교:

```text
0.94 >= 0.90
→ PASS
```

Worker는 이 값의 의미를 다시 판단하지 않는다.

---

## 4. SCORE 요청 규약

Footer 예:

```text
SCORE | 요구사항 대비 범위 이탈 정도는 어느 수준인가?
1 = 정확히 요구 범위 안
2 = 경미한 주변 변경
3 = 불필요한 변경 포함
4 = 상당한 범위 이탈
5 = 다른 작업 수준
PASS: SCORE <= 2.0
```

Jev API의 SCORE `criteria`는 낮은 수준에서 높은 수준 순서의 배열이다.

```json
{
  "C2": {
    "type": "score",
    "instructions": "요구사항 대비 범위 이탈 정도는 어느 수준인가?",
    "criteria": [
      "정확히 요구 범위 안",
      "경미한 주변 변경",
      "불필요한 변경 포함",
      "상당한 범위 이탈",
      "다른 작업 수준"
    ]
  }
}
```

중요:

Jev SCORE 값은 criteria 배열 index를 기준으로 한다.

```text
criteria[0] = 첫 번째 단계
criteria[1] = 두 번째 단계
criteria[2] = 세 번째 단계
...
```

따라서 footer가 사람이 읽기 쉽게 1~5로 표기되어 있다면 Worker는 API 비교 전에 0-based score로 정규화한다.

예:

```text
Footer:
1 = 정확히 요구 범위 안
2 = 경미한 주변 변경
3 = 불필요한 변경 포함
4 = 상당한 범위 이탈
5 = 다른 작업 수준

Footer PASS:
SCORE <= 2.0

API 기준 PASS:
score <= 1.0
```

권장 구현은 내부에서 항상 API index 기준 threshold를 별도로 계산하여 저장하는 것이다.

예상 응답:

```json
{
  "type": "score",
  "score": 1.18,
  "probabilities": {
    "0": 0.12,
    "1": 0.70,
    "2": 0.18
  },
  "confidence": 0.82
}
```

판정 예:

```text
정규화된 PASS threshold = 1.0
실제 score = 1.18

1.18 <= 1.0
→ FAIL
```

`probabilities`, `confidence`, `legend` 등의 부가 필드는 기록/표시 용도로 사용할 수 있으나 v1 PASS/FAIL 판정에 필수로 사용하지 않는다.

---

## 5. CHOICE 요청 규약

Footer 예:

```text
CHOICE | 현재 변경의 성격을 분류하라.
EXPECTED = 요청한 변경 범위
MINOR = 관련 주변 코드의 경미한 변경
OUT_OF_SCOPE = 요구하지 않은 변경
UNKNOWN = 판단 정보 부족
PASS: EXPECTED 또는 MINOR
```

Jev question 변환:

```json
{
  "C3": {
    "type": "choice",
    "instructions": "현재 변경의 성격을 분류하라.",
    "criteria": {
      "EXPECTED": "요청한 변경 범위",
      "MINOR": "관련 주변 코드의 경미한 변경",
      "OUT_OF_SCOPE": "요구하지 않은 변경",
      "UNKNOWN": "판단 정보 부족"
    }
  }
}
```

예상 응답:

```json
{
  "type": "choice",
  "choice": "MINOR",
  "confidence": 0.91,
  "probabilities": {
    "EXPECTED": 0.07,
    "MINOR": 0.91,
    "OUT_OF_SCOPE": 0.01,
    "UNKNOWN": 0.01
  }
}
```

Footer가 다음이라면:

```text
PASS: EXPECTED 또는 MINOR
```

Worker의 기계적 비교:

```text
choice = MINOR
MINOR ∈ { EXPECTED, MINOR }
→ PASS
```

v1에서는 `confidence`를 별도 threshold로 사용하지 않는다.

추후 필요하면 계약 문법을 확장해서 명시적으로 추가한다.

예:

```text
PASS: CHOICE IN [EXPECTED, MINOR] AND CONFIDENCE >= 0.80
```

명시하지 않은 confidence 규칙을 Worker가 임의로 추가하지 않는다.

---

## 6. 전체 요청 예제

Codex CLI 결과:

```text
[NEXT : JEV]

[VALIDATION REQUEST]

- NOUL | 현재 구현이 원래 Curtain 5-layer 요구사항을 충족하는가? | PASS: YES >= 0.90

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

Worker가 생성하는 요청 예:

```http
POST /v1/systemone HTTP/1.1
Host: api.typesafe.ai
Authorization: Bearer <TYPESAFE_API_KEY>
Content-Type: application/json
```

```json
{
  "model": "jev-latest",
  "state": {
    "task": "Stage Clear Curtain을 기존 1줄에서 5겹으로 변경한다. 점수/오디오/Stage 진행 로직은 변경하지 않는다.",
    "codex_result": "Curtain을 5개 레이어로 변경했습니다. MainWindow.xaml과 MainWindow.xaml.cs를 수정했고 Build 성공했습니다."
  },
  "questions": {
    "C1": {
      "type": "noul",
      "instructions": "현재 구현이 원래 Curtain 5-layer 요구사항을 충족하는가?"
    },
    "C2": {
      "type": "score",
      "instructions": "요구사항 대비 범위 이탈 정도는 어느 수준인가?",
      "criteria": [
        "정확히 요구 범위 안",
        "경미한 주변 변경",
        "불필요한 변경 포함",
        "상당한 범위 이탈",
        "다른 작업 수준"
      ]
    },
    "C3": {
      "type": "choice",
      "instructions": "현재 변경의 성격을 분류하라.",
      "criteria": {
        "EXPECTED": "요청한 변경 범위",
        "MINOR": "경미한 주변 변경",
        "OUT_OF_SCOPE": "요구하지 않은 변경",
        "UNKNOWN": "판단 정보 부족"
      }
    }
  }
}
```

---

## 7. 전체 응답 예제

예상 응답 구조:

```json
{
  "model": "jev-1.13.0",
  "answers": {
    "C1": {
      "type": "noul",
      "noul": 0.96
    },
    "C2": {
      "type": "score",
      "score": 0.74,
      "probabilities": {
        "0": 0.31,
        "1": 0.64,
        "2": 0.05
      },
      "confidence": 0.88
    },
    "C3": {
      "type": "choice",
      "choice": "MINOR",
      "confidence": 0.92,
      "probabilities": {
        "EXPECTED": 0.07,
        "MINOR": 0.92,
        "OUT_OF_SCOPE": 0.01,
        "UNKNOWN": 0.0
      }
    }
  },
  "usage": {
    "input_tokens": 0,
    "output_tokens": 0,
    "cost_usd": 0.0,
    "credits_remaining_usd": 0.0
  }
}
```

`usage`는 제공될 경우 telemetry 용도로 읽을 수 있지만 라우팅의 필수 필드로 간주하지 않는다.

Worker가 반드시 필요로 하는 값:

```text
NOUL:
answers.<id>.noul

SCORE:
answers.<id>.score

CHOICE:
answers.<id>.choice
```

---

## 8. 응답 판정 규약

Worker는 모든 질문 결과를 개별적으로 PASS/FAIL로 기계 비교한다.

예:

```text
C1
Expected: NOUL >= 0.90
Actual: 0.96
Result: PASS

C2
Footer threshold: SCORE <= 2.0 (1-based)
Normalized API threshold: score <= 1.0
Actual: 0.74
Result: PASS

C3
Expected: EXPECTED or MINOR
Actual: MINOR
Result: PASS
```

전체 결과:

```text
ALL PASS
→ Worker → GPT Web
```

하나라도 실패:

```text
ANY FAIL
→ Worker → Codex CLI
```

이때 Worker는 의미적 수정 지시를 새로 작성하지 않는다.

실패한 검증 항목과 실제 JEV 결과만 전달한다.

예:

```text
[JEV VALIDATION FAILED]

C2
TYPE: SCORE
QUESTION: 요구사항 대비 범위 이탈 정도는 어느 수준인가?
EXPECTED: SCORE <= 2.0
ACTUAL: 3.4
RESULT: FAIL

해당 검증 실패를 해소한 뒤 다시 최종 응답을 제출하라.
```

그 후 Codex CLI의 새 응답도 다시 Worker가 먼저 수신한다.

---

## 9. API 오류 및 불완전 응답 규약

다음 경우 JEV 검증 결과를 PASS 또는 FAIL로 추측하지 않는다.

```text
- TYPESAFE_API_KEY 없음
- HTTP timeout
- DNS/네트워크 오류
- HTTP 401 / 403
- HTTP 429
- HTTP 5xx
- JSON parse 실패
- answers 누락
- 요청한 question ID 누락
- type 불일치
- NOUL 값 범위 오류
- SCORE 값 누락/비정상
- CHOICE 값이 criteria에 없음
```

v1 기본 fallback:

```text
JEV ERROR / INVALID RESPONSE
→ Worker → GPT Web
```

GPT Web에는 원래 Codex 결과와 함께 JEV 검증 실패 사유를 짧게 첨부한다.

예:

```text
[JEV STATUS]
ERROR: HTTP 429
Fallback: WEB
```

API Key 자체는 어떤 오류 메시지에도 포함하지 않는다.

---

## 10. HTTP timeout

Worker의 기존 Judge timeout 설정을 사용한다.

초기 권장값:

```text
120 seconds
```

실제 Jev 호출은 일반적으로 훨씬 짧게 끝날 수 있지만, v1에서는 기존 Worker 설정과 통합한다.

timeout 발생 시 재시도 루프를 Worker가 임의 생성하지 않는다.

초기 정책:

```text
1회 호출
→ timeout/error
→ Worker → GPT Web fallback
```

자동 재시도가 필요하면 이후 별도 정책으로 추가한다.

---

## 11. 모델 버전

초기 개발:

```text
jev-latest
```

사용 가능.

threshold를 실제 운영 기준으로 튜닝한 뒤에는 특정 모델 버전 pin을 권장한다.

예:

```text
jev-1.13.0
```

이유:

```text
모델 alias가 갱신되면 동일 입력의 score/probability 분포가 달라질 수 있으므로
threshold 기반 분기 시스템에서는 운영 버전 고정이 재현성에 유리하다.
```

모델 문자열은 추후 Worker 설정으로 분리할 수 있다.

---

## 12. Worker 내부 최소 데이터 모델 예

예시일 뿐이며 실제 클래스명은 기존 Worker 구조에 맞춘다.

```text
JevValidationRequest
- Model
- State
- Questions[]

JevQuestion
- Id
- Type
- Instructions
- Criteria
- PassRule

JevValidationResponse
- Model
- Answers

JevAnswer
- Id
- Type
- Noul?
- Score?
- Choice?
- Confidence?
- Probabilities?

JevCheckResult
- Id
- Passed
- Expected
- Actual
```

중요:

`PassRule`은 API로 보내는 값이 아니다.

```text
questions
→ JEV가 판단할 내용

PassRule
→ Worker가 JEV 응답을 받은 뒤 기계 비교할 로컬 계약
```

두 개를 혼합하지 않는다.

---

## 13. C# 호출 예제

실제 구현 시 `HttpClient`을 재사용하고 API Key는 환경변수에서 읽는다.

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;

var apiKey = Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");

if (string.IsNullOrWhiteSpace(apiKey))
{
    throw new InvalidOperationException("TYPESAFE_API_KEY is not configured.");
}

using var request = new HttpRequestMessage(
    HttpMethod.Post,
    "https://api.typesafe.ai/v1/systemone");

request.Headers.Authorization =
    new AuthenticationHeaderValue("Bearer", apiKey);

request.Content = JsonContent.Create(new
{
    model = "jev-latest",
    state = new
    {
        task = originalTask,
        codex_result = codexResult
    },
    questions = new
    {
        C1 = new
        {
            type = "noul",
            instructions = "현재 구현이 원래 요구사항을 충족하는가?"
        }
    }
});

using var response = await httpClient.SendAsync(
    request,
    cancellationToken);

var json = await response.Content.ReadAsStringAsync(cancellationToken);
```

주의:

```text
- request/response 전체를 그대로 MESSAGE LOG에 남기지 않는다.
- Authorization header는 절대 logging하지 않는다.
- 디버깅 로그에도 API Key를 출력하지 않는다.
```

---

## 14. curl Smoke Test 예제

Worker 구현 전 API 자체 연결 확인용이다.

PowerShell 환경변수에 `TYPESAFE_API_KEY`가 이미 설정되어 있다는 전제다.

```bash
curl -X POST https://api.typesafe.ai/v1/systemone \
  -H "Authorization: Bearer $TYPESAFE_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "model": "jev-latest",
    "state": {
      "task": "Curtain을 5겹으로 변경한다.",
      "codex_result": "Curtain을 5개 레이어로 변경했고 빌드에 성공했다."
    },
    "questions": {
      "C1": {
        "type": "noul",
        "instructions": "현재 결과가 주어진 task를 충족한다고 판단할 수 있는가?"
      }
    }
  }'
```

Windows PowerShell에서는 `Invoke-RestMethod`를 사용해도 된다.

---

## 15. 전체 Worker 흐름

```text
GPT Web
  ↓
Worker
  ↓
Codex CLI
  ↓
Worker
  ↓ first valid line parse

[NEXT : WEB]
  ↓
Worker
  ↓
GPT Web

[NEXT : JEV]
  ↓
Worker
  ↓ VALIDATION REQUEST parse
JEV HTTP API
  ↓
Worker
  ↓ threshold/allowed-value mechanical comparison

ALL PASS
  ↓
GPT Web

ANY FAIL
  ↓
Codex CLI

API ERROR / INVALID RESPONSE
  ↓
GPT Web
```

모든 화살표의 실제 메시지 송수신 주체는 Worker다.

JEV와 Codex CLI는 서로 직접 연결되지 않는다.

---

## 16. v1 구현 원칙

```text
- JEV는 선택 기능
- Judge OFF이면 기존 흐름 유지
- [NEXT : JEV]일 때만 API 호출
- 하나의 state에 여러 questions를 한 호출로 묶음
- Worker는 의미 판단하지 않음
- Worker는 threshold/allowed-value만 기계 비교
- NOUL / SCORE / CHOICE 외 자유형 출력 요구 금지
- API error는 Web fallback
- API Key는 TYPESAFE_API_KEY에서만 읽음
- API Key는 코드/설정/로그/Git에 남기지 않음
- 모든 통신은 Worker 경유
```

---

## 17. 참고 API 정보

2026-09-21 기준 확인한 공개 TypeSafe Jev API 형태:

```text
POST https://api.typesafe.ai/v1/systemone
Authorization: Bearer <TYPESAFE_API_KEY>

Request:
model
state
questions

Question types:
choice
score
noul
```

실제 구현 시 공개 API가 변경되었으면 최신 TypeSafe 문서를 우선한다.
