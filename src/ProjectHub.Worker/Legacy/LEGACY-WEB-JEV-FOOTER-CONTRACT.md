# 레거시 Web JEV 하단 계약

갱신일: 2026-09-30 (KST)

이 문서는 기존 GPT Web ↔ Codex ↔ JEV 레거시 모드의 ACTION/NEXT wire만 보존한다. 신규 Coordinator-first 흐름은 `Worker-Polish.md`와 현재 HQ/WORK 라우팅 계약을 따른다.

제1조 (핵심 규칙)

① 레거시 모드에서도 Worker는 의미 판단을 수행하지 않는다.
② Worker는 NEXT를 읽어 전달하고 JEV 제공자 응답을 같은 Codex 세션에 반환하며, 임계값이나 근거를 해석해 PASS/FAIL을 만들지 않는다.
③ Legacy Web action은 다음 세 값만 사용한다.

~~~text
[ACTION=CONTINUE]
[ACTION=PAUSE]
[ACTION=END]
~~~

④ CONTINUE는 본문을 포함한다.
⑤ Legacy Web은 현재 HQ 역할 라우팅을 수행하지 않는다.

제2조 (Codex 경로)

① Codex 응답의 첫 유효 제어행은 다음 중 하나를 사용한다.

~~~text
[NEXT : WEB]
~~~

~~~text
[NEXT : JEV]
~~~

② WEB 경로는 다음 형식을 사용한다.

~~~text
[NEXT : WEB]

[REPORT]
...
~~~

③ Worker는 NEXT 뒤 본문에서 `[REPORT]` 행을 기계적으로 찾고 그 뒤의 REPORT를 GPT Web에 전달한다.
④ `[REPORT]` 앞에 설명이 있을 수 있으나 같은 응답에는 해당 마커를 정확히 하나만 둔다.
⑤ Worker는 REPORT의 사실 여부를 판단하지 않는다.
⑥ JEV 경로는 다음 형식을 사용한다.

~~~text
[NEXT : JEV]

[VALIDATION REQUEST]
...
~~~

⑦ Worker는 NEXT 뒤 본문에서 `[VALIDATION REQUEST]` 행을 기계적으로 찾고 그 뒤의 요청을 JEV 어댑터에 전달한다.
⑧ `[VALIDATION REQUEST]` 앞에 설명이 있을 수 있으나 같은 응답에는 해당 마커를 정확히 하나만 둔다.
⑨ 질문 안의 임계값과 기준은 JEV에 전달할 요청 내용이며 Worker 완료 gate가 아니다.

제3조 (JEV 응답)

① JEV 응답은 Worker가 의미적으로 평가하지 않는다.
② Worker는 전송·스키마 수준에서 응답을 읽을 수 있으면 원문을 같은 Codex 세션으로 전달한다.
③ 레거시 호환 봉투는 다음 형식을 사용할 수 있다.

~~~text
[JUDGMENT]

<raw JEV response>
~~~

④ Worker는 `GOTO : WORK` 제어행을 레거시 Codex 입력에 새로 추가하지 않는다.
⑤ Codex는 반환된 원문을 해석하고 `NEXT : WEB` 또는 `NEXT : JEV`를 선택한다.

제4조 (기술 오류)

① JEV 시간 초과, 인증, HTTP 및 스키마 오류를 PASS/FAIL로 추측하지 않는다.
② Worker는 비밀값을 제외한 기술 오류를 같은 Codex 세션 또는 레거시 관제 경로에 전달한다.
③ Worker는 전송 오류만으로 의미 작업의 자동 재시도 횟수나 구현 실패를 결정하지 않는다.

제5조 (Worker 책임)

① Worker는 다음 기계 책임을 수행할 수 있다.
1. NEXT 문법 파싱
2. 제공자 호출
3. 요청·응답 직렬화
4. 시간 초과·인증·HTTP·스키마 오류 처리
5. 같은 세션으로 결과 반환
6. 기록·사용량 계측
7. 비밀값 가림 처리

② Worker는 다음 의미 판단을 수행하지 않는다.
1. 임계값 비교
2. PASS/PARTIAL/FAIL 생성
3. 근거 충분성 또는 freshness 판단
4. 자동 Codex 재작업 결정
5. 자동 Web 완료 판단
6. 결과 본문 의미 변형

제6조 (호환성)

① 이 문서의 NEXT:WEB/JEV는 레거시 모드에만 사용한다.
② 신규 Coordinator-first 흐름에서는 NEXT를 사용하지 않고 현재 HQ/WORK 계약의 GOTO를 사용한다.
