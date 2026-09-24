# Legacy Web JEV Footer Contract

Updated: 2026-09-24

이 문서는 기존 GPT Web ↔ Codex ↔ JEV legacy mode의 공개 NEXT wire만 보존한다.

신규 CLI-to-CLI 정책은 Master-Polish.md의 ACTION + GOTO 계약을 따른다.

## Core rule

**Legacy mode에서도 Worker는 판단하지 않는다.**

Worker는 NEXT를 읽어 전달하고 JEV provider 응답을 같은 Codex session에 돌려줄 뿐, threshold나 evidence를 보고 PASS/FAIL을 만들지 않는다.

## Codex route

첫 유효행:

~~~text
[NEXT : WEB]
~~~

또는:

~~~text
[NEXT : JEV]
~~~

### WEB

~~~text
[NEXT : WEB]

[REPORT]
...
~~~

Worker는 REPORT를 GPT Web에 전달한다.

REPORT의 사실 여부를 Worker가 판단하지 않는다.

### JEV

~~~text
[NEXT : JEV]

[VALIDATION REQUEST]
...
~~~

Worker는 VALIDATION REQUEST를 JEV adapter에 전달한다.

질문 안의 PASS/threshold/criteria는 **JUDGE가 해석할 요청 내용**이며 Worker 완료 gate가 아니다.

## JEV response

JEV 응답은 Worker가 의미적으로 평가하지 않는다.

Worker는 transport/schema 수준에서 응답을 읽을 수 있으면 원문을 같은 Codex session으로 전달한다.

권장 envelope:

~~~text
[JUDGMENT]

<raw JEV response>
~~~

Codex가 결과를 해석하고 다시 NEXT:WEB 또는 NEXT:JEV를 선택한다.

## Technical error

JEV timeout/auth/HTTP/schema 오류는 PASS/FAIL로 추측하지 않는다.

Worker는 오류 원문을 같은 Codex session 또는 legacy 관제 경로에 전달한다.

Worker가 자동 재시도 횟수나 구현 실패를 결정하지 않는다.

## Worker responsibilities

허용:
- NEXT syntax parse
- provider call
- request/response serialization
- timeout/auth/HTTP/schema error
- same-session return
- transcript/usage
- secret redaction

금지:
- threshold comparison
- PASS/PARTIAL/FAIL 생성
- evidence 충분성 판단
- evidence freshness 판단
- 자동 Codex 재작업
- 자동 Web 완료 판단
- 결과 본문 의미 변형

## Compatibility

이 문서의 NEXT:WEB/JEV는 legacy mode에만 해당한다.

신규 CLI-to-CLI에서는 NEXT를 사용하지 않고 GOTO를 사용한다.
