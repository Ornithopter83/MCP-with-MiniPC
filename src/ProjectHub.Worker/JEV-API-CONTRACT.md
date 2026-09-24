# JEV API Transport Contract

Updated: 2026-09-24

정책 원본은 Master-Polish.md다.

이 문서는 Worker/Judge adapter가 JEV API와 통신할 때의 **transport 계약**만 정의한다.

## 1. Principle

**Worker는 JEV 결과를 판단하지 않는다.**

Worker는:
- 요청 serialize
- API 호출
- 응답 parse
- transport/schema 오류 구분
- raw response 기록/전달

만 수행한다.

Worker는:
- threshold 비교
- PASS/PARTIAL/FAIL 생성
- evidence 충분성 판정
- 질문별 재작업 결정
- 다음 AI 역할 결정

을 하지 않는다.

## 2. Authentication

인증은 환경변수에서 읽는다.

비밀키는:
- Git에 기록하지 않음
- prompt에 넣지 않음
- transcript에 기록하지 않음
- HTTP Authorization 헤더 원문을 로그에 남기지 않음

인증이 없으면 provider 호출을 시도하지 않고 기술 오류를 반환한다.

## 3. Request

JUDGE 요청 본문은 WORK 또는 legacy Codex가 만든 VALIDATION REQUEST를 기반으로 한다.

Worker adapter는 provider API에 필요한 구조 변환만 수행한다.

예시 개념:

~~~json
{
  "model": "jev-latest",
  "state": "<requester supplied state>",
  "questions": [
    {
      "id": "C1",
      "type": "NOUL",
      "instructions": "..."
    }
  ]
}
~~~

Worker가 질문의 의미를 새로 작성하거나 보완하지 않는다.

필요한 provider 필드가 없으면 schema/protocol 오류로 처리한다.

## 4. Response

응답 예시 개념:

~~~json
{
  "model": "...",
  "answers": [
    {
      "id": "C1",
      "value": 0.91
    }
  ],
  "usage": {}
}
~~~

Worker는 JSON/schema가 읽을 수 있는지만 확인한다.

값의 의미는 JUDGE를 요청한 AI가 판단한다.

예:

~~~text
0.91 >= 0.90 인가?
score가 허용 범위인가?
choice가 기대값인가?
evidence가 충분한가?
~~~

이 질문들에 Worker가 답하지 않는다.

## 5. Return path

신규 CLI-to-CLI:

~~~text
WORK -> JUDGE API -> raw result -> same WORK session
~~~

adapter return envelope:

~~~text
[GOTO : WORK]

[JUDGMENT]
<raw provider response>
~~~

legacy mode:

~~~text
Codex -> JEV API -> raw result -> same Codex session
~~~

그 후 Codex가 다음 NEXT를 선택한다.

## 6. Error

다음은 기술 오류다.

- authentication missing
- timeout
- network/HTTP error
- invalid JSON
- required provider field missing
- unsupported schema/type

신규 CLI-to-CLI에서는 오류를 UNKNOWN으로 HQ에 전달한다.

legacy mode에서는 오류 원문을 요청 Codex/관제 경로에 전달한다.

Worker는 오류를 구현 FAIL로 바꾸지 않는다.

## 7. Usage

가능하면 다음을 기록한다.

- provider
- model/revision
- request ID
- latency
- usage
- usage known/unknown

usage 값이 없으면 0으로 추정하지 않고 unknown으로 기록한다.

## 8. No semantic cache invalidation in Worker

Worker는 evidence digest나 model revision을 보고 기존 JUDGE 결과가 의미적으로 유효/무효인지 결정하지 않는다.

필요하면 raw metadata를 기록해 AI가 판단할 수 있게 전달한다.

## 9. No retry policy by judgment

transport 재시도는 일반 인프라 정책이 명시된 경우에만 수행할 수 있다.

낮은 score/confidence를 이유로 Worker가 재질문하거나 WORK를 재호출하지 않는다.

## 10. Summary

~~~text
Worker/Judge adapter = transport
AI/JUDGE              = judgment
HQ                     = final action
~~~

이 경계를 넘지 않는다.
