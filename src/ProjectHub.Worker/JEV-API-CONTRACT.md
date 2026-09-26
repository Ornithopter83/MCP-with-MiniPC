# JEV API 전송 계약

갱신일: 2026-09-24

상위 공통 정책은 Master-Polish.md이며 Worker 세부 정책은 Worker-Polish.md다.

이 문서는 Worker/Judge 어댑터가 JEV API와 통신할 때의 **전송 계약**만 정의한다.

제1조 (원칙)

**Worker는 JEV 결과를 판단하지 않는다.**

Worker는:
- 요청 직렬화
- API 호출
- 응답 파싱
- 전송/스키마 오류 구분
- 원본 응답 기록/전달

만 수행한다.

Worker는:
- 임계값 비교
- PASS/PARTIAL/FAIL 생성
- 근거 충분성 판정
- 질문별 재작업 결정
- 다음 AI 역할 결정

을 하지 않는다.

제2조 (인증)

인증은 환경변수에서 읽는다.

비밀키는:
- Git에 기록하지 않음
- 프롬프트에 넣지 않음
- 기록에 기록하지 않음
- HTTP Authorization 헤더 원문을 로그에 남기지 않음

인증이 없으면 제공자 호출을 시도하지 않고 기술 오류를 반환한다.

제3조 (요청)

신규 CLI에서 JUDGE 요청 본문은 WORK가 `[GOTO : JUDGE]` 뒤에 작성한 opaque body다. 별도 `VALIDATION REQUEST` marker를 붙이지 않는다. Legacy Codex는 legacy 계약에 따른 요청 본문을 사용한다.

신규 WORK body는 하나 이상의 원자적 질문을 다음 형식으로 표현한다:

~~~text
NOUL | [QID:IMPLEMENTED] <question and response instructions>
PASS: YES >= 0.90
EVIDENCE: src/implementation.cs
SCOPE: the requested behavior only
COUNTEREXAMPLE: one concrete failure condition
SCORE | [QID:QUALITY] <question and response instructions>
<integer>=<score criterion>
CHOICE | [QID:FORMAT] <question and response instructions>
<CHOICE_KEY>=<choice criterion>
EVIDENCE: <workspace-relative file path>
~~~

QID는 질문별로 고유하게 지정한다. SCORE/CHOICE는 각각 최소 하나의 criterion이 필요하다. 근거, SCOPE, COUNTEREXAMPLE, PASS는 선택적 질문 지침이며, 근거 경로는 workspace 상대 경로로 지정한다. 질문은 각각 독립된 하나의 판단 대상이어야 한다. 일부 질문만 다시 판정할 때는 해당 QID의 질문만 다시 보낸다. Worker는 이 필드를 제공자 전송 구조로만 변환하며 판정의 의미를 결정하지 않는다.

Worker 어댑터는 제공자 API에 필요한 구조 변환만 수행한다.

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

필요한 제공자 필드가 없으면 스키마/프로토콜 오류로 처리한다.

제4조 (응답)

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

Worker는 JSON/스키마가 읽을 수 있는지만 확인한다.

값의 의미는 JUDGE를 요청한 AI가 판단한다.

예:

~~~text
0.91 >= 0.90 인가?
score가 허용 범위인가?
choice가 기대값인가?
evidence가 충분한가?
~~~

이 질문들에 Worker가 답하지 않는다.

제5조 (반환 경로)

신규 CLI-to-CLI:

~~~text
WORK -> JUDGE API -> raw result -> same WORK session
~~~

어댑터 return 봉투 구조:

Worker는 WORK에서 JUDGE로 향하는 경로를 처리한다. 다음 WORK 입력에는 역할 헤더와 JEV 원본 응답 본문을 사용하며, GOTO 제어행을 다시 넣거나 의미 표식 `JUDGMENT`를 추가하지 않는다.

레거시 모드:

~~~text
Codex -> JEV API -> raw result -> same Codex session
~~~

그 후 Codex가 legacy `NEXT:WEB/JEV` 중 다음 경로를 선택한다.

제6조 (오류)

다음은 기술 오류다.

- 인증 missing
- 시간 초과
- 네트워크/HTTP 오류
- 잘못된 JSON
- 필수 제공자 필드 누락
- 지원하지 않는 스키마/형식

신규 CLI-to-CLI에서는 오류 원문과 기술 상세를 로컬 한글 로그에 기록하고, 발생 역할·오류 코드·한글 설명만 HQ에 Job당 한 번 전달해 정상 관제를 재개한다. 요약 전달 후 오류가 재발하면 추가 AI 호출 없이 로그 기록 후 종료한다.

레거시 모드에서는 오류 원문을 요청 Codex/관제 경로에 전달한다.

Worker는 오류를 구현 FAIL로 바꾸지 않는다.

제7조 (사용량)

가능하면 다음을 기록한다.

- 제공자
- 모델/개정
- 요청 ID
- 지연 시간
- 사용량
- 사용량 알려짐/UNKNOWN

사용량 값이 없으면 0으로 추정하지 않고 UNKNOWN으로 기록한다.

제8조 (Worker에서 의미 기반 캐시 무효화 금지)

Worker는 근거 digest나 model revision을 보고 기존 JUDGE 결과가 의미적으로 유효/무효인지 결정하지 않는다.

필요하면 raw 메타데이터를 기록해 AI가 판단할 수 있게 전달한다.

제9조 (판정 결과 기반 재시도 정책 금지)

전송 재시도는 일반 인프라 정책이 명시된 경우에만 수행할 수 있다.

낮은 score/confidence를 이유로 Worker가 재질문하거나 WORK를 재호출하지 않는다.

제10조 (요약)

~~~text
Worker/Judge adapter = transport
AI/JUDGE              = judgment
HQ                     = final action
~~~

이 경계를 넘지 않는다.
