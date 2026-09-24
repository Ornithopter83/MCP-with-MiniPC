# Master-Polish — ProjectHub 현재 정책

Updated: 2026-09-24 (KST)

이 문서는 ProjectHub의 **현재 최상위 정책 원본**이다. 다른 구현계획, CurrentWork, task, feedback 문서가 이 문서와 충돌하면 이 문서를 우선한다.

ProjectHub의 목표는 **AI가 판단하고 Worker가 흐름만 제어하는 역할 분리형 CLI-to-CLI 개발 도구**다.

---

## 1. 최상위 불변식

### 1.1 Worker는 판단하지 않는다

**Worker는 흐름 제어 도구다. 판단 주체가 아니다.**

이 원칙은 검증 자동화, 비용 최적화, 안전장치, JUDGE/JEV 연동, Provider 확장, 복구 기능보다 우선한다.

Worker가 작업 본문을 읽고 “맞다/틀리다”, “충분하다/부족하다”, “끝내도 된다/다시 해야 한다”를 결정하는 로직은 **설계 위반**이다.

### 1.2 판단은 AI가 한다

- **HQ**: 사용자 목표 해석, 작업 지시, 결과 검토, 최종 CONTINUE/PAUSE/END 판단
- **WORK**: 실제 작업 수행, 자체 검증 수행, JUDGE 사용 여부 판단, HQ 보고
- **JUDGE**: WORK가 요청한 내용의 의미적 판단
- **HIGH**: 사용자가 명시적으로 허가한 1회성 고수준 작업 수행 후 HQ 보고
- **Worker**: 위 역할 사이의 계약된 메시지를 전달하고 상태를 전환

### 1.3 Worker가 해도 되는 일

Worker의 책임은 **기계적 orchestration**으로 한정한다.

1. 현재 상태(HQ/WORK/JUDGE/HIGH/UNKNOWN) 저장
2. 첫 제어행의 문법 파싱
3. 현재 상태에서 허용된 GOTO인지 상태표와 대조
4. 역할별 provider/model/reasoning/session 실행 및 resume
5. process start/exit, timeout, cancel, authentication, transport/schema 오류 처리
6. transcript, usage, session ID, 호출 시각, 원문 응답 기록
7. 사용자 UI 동작으로 생성된 HIGH one-shot permit 저장/소모
8. BODY/REPORT/INSTRUCTION/VALIDATION REQUEST/JUDGMENT/ERROR 원문 전달
9. protocol/provider/transport/session 오류를 UNKNOWN envelope로 HQ에 전달
10. Git commit/push, 배포 등 기존 사용자 승인 경계의 기계적 적용

Worker는 실행 사실을 **기록할 수는 있지만 해석하지 않는다**.

예:

```text
허용:
process exit_code = 1 기록
stdout/stderr 기록
해당 원문을 HQ/WORK에 전달

금지:
exit_code = 1 이므로 작업 실패라고 Worker가 판정
자동으로 WORK 재호출
HQ의 END 거부
HIGH로 자동 승격
```

### 1.4 Worker가 하면 안 되는 일

다음 로직은 신규 CLI-to-CLI 경로에서 금지한다.

- 사용자 요청의 난이도·의도·우선순위 해석
- WORK/HIGH/JUDGE 중 적절한 역할을 Worker가 선택
- WORK 대신 HIGH가 더 적절하다고 자동 판단
- 작업 결과가 요구사항이나 AC를 만족하는지 판단
- 테스트가 충분한지 판단
- 테스트 결과가 올바른지 판단
- planned command와 observed command를 비교해 PASS/FAIL 생성
- 파일, diff, hash, evidence를 읽고 구현 정답 여부 판단
- evidence가 충분/부족/낡았다고 판단
- JUDGE/JEV의 probability, confidence, SCORE, CHOICE, NOUL을 Worker가 threshold와 비교해 PASS/PARTIAL/FAIL 생성
- JUDGE 결과를 근거로 자동 재작업 또는 다음 역할 결정
- 반복 횟수를 근거로 구현 실패 판정
- 토큰 사용량을 근거로 작업 성공/실패 판정
- HQ의 유효한 `[ACTION=END]`를 별도 AC/evidence/test/JUDGE gate로 거부
- REPORT/JUDGMENT를 의미적으로 요약·변형해 원문 의미 변경
- BODY를 읽고 GOTO를 추론
- AI가 선택하지 않은 정상 역할로 자동 대체
- AI 대신 작업 지시, 수정 방향, 검증 질문 생성

---

## 2. 역할과 상태

신규 CLI-to-CLI의 상태 이름은 아래 다섯 개로 고정한다.

| 상태 | UI 역할명 | 책임 |
| --- | --- | --- |
| **HQ** | 설계·관제 AI | 전체 설계·관제·최종 판단 |
| **WORK** | 작업 AI | 일반 구현·수정·검증·보고 |
| **JUDGE** | 작업 판단 AI | WORK가 요청한 의미 판단 |
| **HIGH** | 고수준 작업 AI | 사용자 1회 허가 기반 고수준 작업 |
| **UNKNOWN** | 오류 상태 | 정상 역할이 아닌 protocol/infrastructure 오류 전달 |

초기 모델 예시는 다음과 같다.

- HQ: OpenAI GPT-6 Sol CLI
- WORK: OpenAI GPT-6 Luna CLI / Medium
- JUDGE: 선택적 JEV 또는 향후 AI Judge adapter
- HIGH: 사용자가 설정한 고수준 모델

역할과 모델은 분리한다. 같은 모델을 여러 역할에 지정할 수 있지만 세션과 책임은 분리한다.

---

## 3. ACTION 규약

ACTION은 **HQ만** 사용할 수 있다.

허용 값:

```text
[ACTION=CONTINUE]
[ACTION=PAUSE]
[ACTION=END]
```

### CONTINUE

HQ가 다른 작업 역할을 호출할 때 사용한다.

```text
[ACTION=CONTINUE]
[GOTO : WORK]

[INSTRUCTION]
...
```

HIGH one-shot permit이 남아 있을 때만:

```text
[ACTION=CONTINUE]
[GOTO : HIGH]

[INSTRUCTION]
...
```

### PAUSE

```text
[ACTION=PAUSE]

[REPORT]
...
```

추가 AI 호출 없이 사용자 대기 상태로 간다.

### END

```text
[ACTION=END]

[REPORT]
...
```

유효한 HQ ACTION이면 Worker는 종료한다.

**Worker가 AC/evidence/test/JUDGE 결과를 다시 검사해 END를 거부하지 않는다.**

`ACTION=HQ`는 신규 CLI-to-CLI에서 사용하지 않는다.

---

## 4. GOTO 규약

신규 CLI-to-CLI의 행선지 키워드는 **GOTO**다.

```text
[GOTO : HQ]
[GOTO : WORK]
[GOTO : HIGH]
[GOTO : JUDGE]
[GOTO : UNKNOWN]
```

GOTO는 성공/실패 판단이 아니라 **다음 상태**만 뜻한다.

최종 상태표:

```text
HQ      -> WORK | HIGH
WORK    -> JUDGE | HQ
JUDGE   -> WORK
HIGH    -> HQ
UNKNOWN -> HQ
```

### HQ

- 정상 GOTO: WORK
- HIGH one-shot permit이 있을 때만 HIGH
- HQ → JUDGE 금지
- HQ → HQ 금지

### WORK

- 정상 GOTO: JUDGE 또는 HQ
- WORK → HIGH 금지
- ACTION 사용 금지

HQ 보고:

```text
[GOTO : HQ]

[REPORT]
...
```

JUDGE 요청:

```text
[GOTO : JUDGE]

[VALIDATION REQUEST]
...
```

### JUDGE

JUDGE는 WORK만 보조한다.

```text
WORK -> JUDGE -> 같은 WORK session
```

JUDGE 결과:

```text
[GOTO : WORK]

[JUDGMENT]
...
```

JEV처럼 native API가 GOTO를 출력하지 않는 경우 adapter가 `GOTO:WORK` wrapper만 기계적으로 붙일 수 있다. Worker는 JUDGMENT의 의미를 판정하지 않는다.

### HIGH

HIGH는 JUDGE를 사용하지 않는다.

```text
HQ -> HIGH -> HQ
```

정상 응답:

```text
[GOTO : HQ]

[REPORT]
...
```

HIGH → JUDGE, WORK, HIGH는 금지한다.

### UNKNOWN

UNKNOWN은 AI 역할이 아니라 **기계적 오류 상태**다.

예:

- 제어행 누락/잘못된 형식
- 현재 상태에서 금지된 GOTO
- HIGH permit 없음
- JUDGE 비활성/연결 불가
- provider timeout
- authentication 실패
- process 실행 실패
- session resume 실패
- transport/schema 오류

Worker는 오류를 의미적으로 해결하지 않는다.

```text
[GOTO : UNKNOWN]

[ERROR]
source_state: WORK
code: ...
detail: <원문 오류>
```

UNKNOWN은 기계적으로 HQ에 전달한다.

```text
UNKNOWN -> HQ
```

HQ가 다음 행동을 판단한다.

---

## 5. HIGH one-shot 사용자 허가

HIGH는 상시 활성 기능이 아니다.

설정창에는 HIGH의 다음 설정만 둔다.

- provider
- model
- reasoning
- thread/session 관련 설정

설정창의 영구 `사용` 체크박스는 사용하지 않는다.

메인 화면의 실행 버튼 왼쪽에:

```text
[ ] 고수준 작업 허용    [ ▶ 실행 ]
```

체크박스를 둔다.

### permit 생성

사용자가 직접:

```text
고수준 작업 허용 체크
+
실행 클릭
```

했을 때만 현재 Job에:

```text
high_uses_remaining = 1
```

을 생성한다.

체크하지 않으면:

```text
high_uses_remaining = 0
```

이다.

Worker는 자연어에서 HIGH 허가를 추론하지 않는다.

### permit 수명

- Job-local
- 최대 1회
- HIGH dispatch 직전에 1 → 0
- HIGH process가 실패해도 자동 복구하지 않음
- Job 종료 시 남은 permit 폐기
- 다음 Job으로 이월 금지
- 실행 직후 메인 체크박스는 unchecked로 복원
- 진행 중 Job은 실행 시작 시 snapshot만 사용

### permit의 의미

체크는 “반드시 HIGH를 사용”이 아니다.

정확한 의미:

> 이번 Job에서 HQ가 필요하다고 판단하면 HIGH를 최대 한 번 호출할 수 있다.

HIGH를 쓸지 말지는 **HQ가 판단**한다.

---

## 6. JUDGE 정책

JUDGE는 선택 기능이다.

- JUDGE를 요청할 권한은 WORK만 가진다.
- JUDGE 결과는 반드시 같은 WORK session으로 복귀한다.
- HQ는 JUDGE를 직접 호출하지 않는다.
- HIGH는 JUDGE를 호출하지 않는다.
- JUDGE의 의미 판단 결과는 WORK가 해석한다.
- 최종 CONTINUE/PAUSE/END는 HQ가 판단한다.

Worker/Judge adapter가 허용되는 작업:

- provider 호출
- request/response serialization
- timeout/authentication/HTTP/schema 오류 확인
- raw response 보존
- raw response를 같은 WORK session으로 전달

Worker/Judge adapter가 하면 안 되는 작업:

- confidence/score threshold 비교
- PASS/PARTIAL/FAIL 의미 생성
- evidence 충분성 판단
- 수정 필요 여부 판단
- 자동 WORK 재호출
- 자동 HQ 종료/계속 판단

JUDGE가 연결되지 않았는데 WORK가 JUDGE를 요청하면 Worker는 다른 역할로 대체하지 않고 UNKNOWN으로 HQ에 알린다.

---

## 7. Worker의 보안·인프라 경계

“판단하지 않는다”는 “보안 경계가 없다”는 뜻이 아니다.

Worker는 다음 기계적 경계를 유지할 수 있다.

- working directory 존재/접근 가능 여부
- provider/model 실행 가능 여부
- authentication 여부
- process timeout/cancel
- role별 session 분리
- workspace sandbox 적용
- secret/credential 로그 마스킹
- 허용되지 않은 파일 첨부 차단
- 사용자 승인 없는 Git commit/push/배포 금지
- 동시에 같은 workspace를 수정하는 writer 수 제한
- transcript/usage 저장

단, 인프라 오류를 **작업 품질 판단과 섞지 않는다.**

예:

```text
AUTH_REQUIRED
PROVIDER_TIMEOUT
SESSION_RESUME_FAILED
ROUTE_NOT_ALLOWED
HIGH_NOT_AUTHORIZED
```

같은 기계적 오류를 UNKNOWN으로 HQ에 전달한다.

---

## 8. 세션 정책

HQ, WORK, HIGH는 각각 독립 세션을 가진다.

- HQ: 기본 read-only
- WORK: 승인된 working folder에 workspace-write
- HIGH: 승인된 working folder에 workspace-write
- JUDGE: 별도 API/AI adapter 또는 독립 판정 세션

같은 역할의 같은 Job 후속은 가능하면 같은 session을 resume한다.

JUDGE 호출 후에는 **같은 WORK session**으로 반드시 복귀한다.

설정창의 모델 변경은 진행 중 Job의 session snapshot에 소급 적용하지 않는다.

---

## 9. 모델·Provider 설정

현재 UI에 노출하는 AI Provider는 우선 OpenAI 계열로 제한한다.

각 역할은 독립적으로:

- provider
- model
- reasoning
- session/thread

를 설정할 수 있다.

향후 타사 Provider를 추가할 수 있도록 내부 adapter 경계는 역할과 분리한다.

Worker는 지원되지 않는 모델을 임의로 다른 모델로 대체하지 않는다.

자동 유료 fallback이나 자동 모델 상향도 하지 않는다.

---

## 10. 비용·토큰 정책

비용 절약의 책임도 Worker의 의미 판단으로 해결하지 않는다.

Worker는 다음 **측정값**만 기록한다.

- role/provider/model/reasoning
- input/cached input/output/reasoning usage
- usage known/unknown
- latency
- retry count/reason
- payload/footer bytes
- session/call ID

Worker가 비용을 보고 “이 작업은 실패”, “HIGH로 바꿔야 한다”, “JUDGE를 생략해야 한다”고 판단하지 않는다.

비용/품질 전략은 HQ 또는 사용자 정책이 결정한다.

---

## 11. Legacy Web 호환

기존 GPT Web/Extension 경로는 별도 legacy mode로 유지할 수 있다.

Legacy Web의 공개 wire:

```text
[ACTION=CONTINUE|PAUSE|END]
[NEXT : WEB]
[NEXT : JEV]
```

는 기존 호환을 위해 보존한다.

신규 CLI-to-CLI에서는 `NEXT`를 사용하지 않고 `GOTO`만 사용한다.

Legacy Web 계약을 신규 GOTO 계약으로 몰래 재해석하지 않는다.

---

## 12. 현재 구현 우선순위

현재 최우선 후속은 **11-C-GOTO-CONTRACT**다.

구현 목표:

1. 신규 CLI 경로의 `NEXT` 제거 → `GOTO`
2. `ACTION=HQ` 제거
3. HQ/WORK/JUDGE/HIGH/UNKNOWN 상태표 구현
4. ACTION parser를 HQ에만 적용
5. WORK와 HIGH Footer 분리
6. JUDGE 결과를 같은 WORK session으로 복귀
7. Worker의 모든 semantic gate 제거
8. HIGH 설정창 영구 사용 체크 제거
9. 메인 실행 버튼 왼쪽 `고수준 작업 허용` one-shot checkbox 추가
10. HIGH permit Job-local snapshot/소모 구현
11. protocol/infrastructure 오류 → UNKNOWN → HQ
12. 기존 Web legacy ACTION/NEXT 회귀 보존

최소 실제 E2E:

```text
A. HQ -> WORK -> HQ -> END
B. HQ -> WORK -> JUDGE -> WORK -> HQ -> END
C. 사용자 HIGH 허가 -> HQ -> HIGH -> HQ -> END 또는 WORK
D. invalid route/provider error -> UNKNOWN -> HQ
```

---

## 13. 최종 체크 문장

향후 설계와 코드 리뷰에서는 아래 질문 하나를 먼저 확인한다.

> **이 로직이 작업 내용의 옳고 그름이나 다음 행동을 Worker 스스로 판단하는가?**

YES라면 Worker에 두지 않는다.

판단은 AI가 하고, Worker는 계약된 흐름만 제어한다.
