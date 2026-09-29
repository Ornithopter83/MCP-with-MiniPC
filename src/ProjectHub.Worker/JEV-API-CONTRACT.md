# JEV API 전송 계약

갱신일: 2026-09-30 (KST)

상위 공통 정책은 `Master-Polish.md`이며 Worker 세부 정책은 `Worker-Polish.md`다.
이 문서는 Worker/Judge 어댑터와 JEV API 사이의 현재 전송 계약만 정의한다. JUDGE Form의 의미와 canonical 문법은 HQ/WORK 역할 계약을 원본으로 사용하며, 레거시 wire는 `Legacy/LEGACY-WEB-JEV-FOOTER-CONTRACT.md`에 둔다.

제1조 (역할 경계)

① Worker/Judge 어댑터는 요청 직렬화, API 호출, 응답 파싱, 전송·스키마 오류 구분과 원본 응답 기록·전달만 수행한다.
② Worker는 임계값 비교, PASS/PARTIAL/FAIL 생성, 근거 충분성 판정, 질문별 재작업 결정 또는 다음 AI 역할 결정을 수행하지 않는다.
③ JEV 결과의 의미 해석은 요청한 AI 역할이 담당한다.

제2조 (인증)

① 인증 정보는 환경 변수 또는 승인된 런타임 설정에서 읽는다.
② 비밀키와 인증 원문은 Git, AI 프롬프트 또는 로그에 기록하지 않는다.
③ 인증 정보가 없으면 제공자 호출을 시도하지 않고 기술 오류를 반환한다.

제3조 (요청 변환)

① WORK에서 JUDGE로 전달되는 본문은 HQ/WORK 역할 계약이 정의한 JUDGE Form을 사용한다.
② canonical QID 표기는 `QID:<id>`다.
③ 현재 parser가 이전 형식 `[QID:<id>]`을 호환 입력으로 수용할 수 있으나 새 Form의 canonical 출력에는 사용하지 않는다.
④ NOUL, SCORE, CHOICE의 질문 의미와 SCORE 기준 또는 CHOICE 선택지는 어댑터가 새로 작성하거나 보완하지 않는다.
⑤ 어댑터는 제공자 API에 필요한 구조 변환만 수행하고 필요한 제공자 필드가 없으면 스키마 또는 프로토콜 오류로 처리한다.

⑥ 현재 Form 예시는 다음과 같다.

```text
NOUL | QID:IMPLEMENTED <question>

SCORE | QID:QUALITY <question>
1=<score criterion>
2=<score criterion>

CHOICE | QID:FORMAT <question>
A=<choice criterion>
B=<choice criterion>
```

⑦ parser는 레거시 입력의 부가 instruction 행을 호환상 수용할 수 있으나 새 HQ Form의 canonical 출력은 제1항부터 제6항의 문법만 사용한다.

제4조 (제공자 요청)

① 제공자 요청은 JUDGE Form의 구조를 전송 가능한 provider schema로 변환한 값이다.
② 다음 JSON은 개념 예시이며 실제 제공자 필드명과 부가 메타데이터는 현재 어댑터 구현을 따른다.

```json
{
  "model": "jev-latest",
  "state": "<requester supplied state>",
  "questions": [
    {
      "id": "IMPLEMENTED",
      "type": "NOUL",
      "instructions": "..."
    }
  ]
}
```

제5조 (응답)

① Worker는 제공자 응답의 JSON과 필수 스키마를 기계적으로 읽을 수 있는지만 확인한다.
② score, confidence, choice와 evidence의 의미를 Worker가 판정하지 않는다.
③ 읽을 수 있는 원본 응답은 같은 WORK 세션으로 반환한다.
④ 다음 JSON은 개념 예시이며 실제 제공자 응답 구조는 현재 어댑터 구현을 따른다.

```json
{
  "model": "...",
  "answers": [
    {
      "id": "IMPLEMENTED",
      "value": 0.91
    }
  ],
  "usage": {}
}
```

제6조 (반환 경로)

① 현재 반환 경로는 다음과 같다.

```text
WORK -> JUDGE API -> raw result -> same WORK session
```

② 다음 WORK 입력에는 역할 헤더와 JEV 원본 응답 본문을 사용하고 GOTO 제어행이나 별도 의미 표식 `JUDGMENT`를 새로 추가하지 않는다.

제7조 (기술 오류)

① 인증 누락, 시간 초과, 네트워크·HTTP 오류, 잘못된 JSON, 필수 제공자 필드 누락과 지원하지 않는 스키마·형식은 기술 오류다.
② 기술 오류를 구현 FAIL이나 판정 FAIL로 변환하지 않는다.
③ 오류 원문과 기술 상세는 비밀값을 제거한 로컬 로그에 기록하고 관제 경로에는 발생 역할, 오류 코드와 필요한 설명만 전달한다.
④ 같은 Job에서 동일 전송 오류가 반복될 때의 중복 억제와 종료 세부는 현재 Worker 구현과 테스트를 따른다.

제8조 (사용량)

① 제공자가 제공하는 경우 provider, model/revision, request ID, latency와 usage를 기록할 수 있다.
② 사용량 값이 없으면 0으로 추정하지 않고 UNKNOWN으로 기록한다.

제9조 (캐시와 재시도)

① Worker는 evidence digest나 model revision을 근거로 기존 JUDGE 결과의 의미적 유효성을 판정하지 않는다.
② 의미 기반 캐시 무효화가 필요하면 raw 메타데이터를 남겨 AI가 판단할 수 있게 한다.
③ 전송 재시도는 명시된 인프라 정책과 구현 범위 안에서만 수행하며 낮은 score 또는 confidence를 이유로 Worker가 재질문하지 않는다.
