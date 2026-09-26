# 레거시 Web JEV 하단 계약

갱신일: 2026-09-24

이 문서는 기존 GPT Web ↔ Codex ↔ JEV 레거시 모드의 공개 ACTION/NEXT wire만 보존한다.

신규 CLI-to-CLI 정책은 Worker-Polish.md와 현재 HQ/WORK 라우팅 계약을 따른다.

제1조 (핵심 규칙)

**레거시 모드에서도 Worker는 판단하지 않는다.**

Worker는 NEXT를 읽어 전달하고 JEV 제공자 응답을 같은 Codex 세션에 돌려줄 뿐, 임계값나 근거를 보고 PASS/FAIL을 만들지 않는다.

Legacy Web action은 다음 세 값만 사용한다.

~~~text
[ACTION=CONTINUE]
[ACTION=PAUSE]
[ACTION=END]
~~~

CONTINUE는 본문이 있어야 한다. Legacy Web은 HQ 역할 라우팅을 수행하지 않는다.

제2조 (Codex 경로)

첫 유효행:

~~~text
[NEXT : WEB]
~~~

또는:

~~~text
[NEXT : JEV]
~~~

① WEB

~~~text
[NEXT : WEB]

[REPORT]
...
~~~

Worker는 NEXT 뒤 본문에서 [REPORT] 행을 기계적으로 찾고 REPORT를 GPT Web에 전달한다. 설명이 먼저 와도 되지만 [REPORT]는 정확히 하나만 둔다.

REPORT의 사실 여부를 Worker가 판단하지 않는다.

② JEV

~~~text
[NEXT : JEV]

[VALIDATION REQUEST]
...
~~~

Worker는 NEXT 뒤 본문에서 [VALIDATION REQUEST] 행을 기계적으로 찾고 JEV 어댑터에 전달한다. 설명이 먼저 와도 되지만 해당 마커는 정확히 하나만 둔다.

질문 안의 PASS/임계값/criteria는 **JUDGE가 해석할 요청 내용**이며 Worker 완료 gate가 아니다.

제3조 (JEV 응답)

JEV 응답은 Worker가 의미적으로 평가하지 않는다.

Worker는 전송/스키마 수준에서 응답을 읽을 수 있으면 원문을 같은 Codex 세션으로 전달한다.

권장 봉투 구조:

~~~text
[JUDGMENT]

<raw JEV response>
~~~

Worker는 `GOTO:WORK` 제어행을 다시 입력에 넣지 않는다. Codex가 결과를 해석하고 `NEXT:WEB` 또는 `NEXT:JEV`를 선택한다.

제4조 (기술 오류)

JEV 시간 초과/auth/HTTP/스키마 오류는 PASS/FAIL로 추측하지 않는다.

Worker는 오류 원문을 같은 Codex 세션 또는 legacy 관제 경로에 전달한다.

Worker가 자동 재시도 횟수나 구현 실패를 결정하지 않는다.

제5조 (Worker 책임)

허용:
- NEXT syntax 파싱
- 제공자 call
- 요청/응답 직렬화
- 시간 초과/auth/HTTP/스키마 오류
- same-세션 return
- 기록/사용량
- 비밀값 가림 처리

금지:
- 임계값 comparison
- PASS/PARTIAL/FAIL 생성
- 근거 충분성 판단
- 근거 freshness 판단
- 자동 Codex 재작업
- 자동 Web 완료 판단
- 결과 본문 의미 변형

제6조 (호환성)

이 문서의 NEXT:WEB/JEV는 레거시 모드에만 해당한다.

신규 CLI-to-CLI에서는 NEXT를 사용하지 않고 GOTO를 사용한다.
