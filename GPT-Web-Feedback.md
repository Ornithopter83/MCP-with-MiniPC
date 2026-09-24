# GPT-Web-Feedback — opaque body + 역할 응답 History 카드

Updated: 2026-09-24

정책 원본은 Master-Polish.md다.

최신 코드 baseline: d9dfc3d77209059dbc645dc108faeb0f3f44ace0 (Refactor role contracts and JEV transport).

이번 Explorer 기본 경로에서 HQ → WORK → HQ → END 동작은 확인됐지만 WORK의 실제 응답이 transcript에만 남고 메시지/작업 이력 카드에는 표시되지 않았다.

원인은 WORK가 REPORT tag를 만들지 못한 것이 아니다. 신규 GOTO flow와 History UI 사이에 불필요한 semantic tag/source 문자열 의존이 남아 있는 것이 문제다.

---

## 1. 최종 원칙

신규 CLI에서 Worker가 AI 출력에서 해석하는 제어 토큰은 ACTION과 GOTO뿐이다.

~~~text
HQ:
[ACTION=CONTINUE|PAUSE|END]
[GOTO : WORK|HIGH]   # CONTINUE일 때만
<opaque body>

WORK:
[GOTO : HQ|JUDGE]
<opaque body>

HIGH:
[GOTO : HQ]
<opaque body>
~~~

제어행 뒤의 모든 문자열은 opaque body다.

다음 semantic section tag를 신규 CLI 출력 계약에서 제거한다:
- INSTRUCTION
- REPORT
- VALIDATION REQUEST
- JUDGMENT

Worker는 위 태그를 요구하지 않고, 찾지 않고, 삽입하지 않고, History 카드 생성에 사용하지 않는다.

카드 때문에 AI 계약을 늘리지 않는다.

---

## 2. 현재 코드에서 실제 제거할 지점

### 2.1 역할 contract 파일

현재 다음 문구가 남아 있다.

- Contracts/HQ-ROUTING-CONTRACT.md: CONTINUE 뒤 INSTRUCTION, PAUSE/END 뒤 REPORT 강제
- Contracts/WORK-ROUTING-CONTRACT.md: GOTO:HQ 뒤 REPORT, GOTO:JUDGE 뒤 VALIDATION REQUEST 강제
- Contracts/HIGH-ROUTING-CONTRACT.md: GOTO:HQ 뒤 REPORT 강제
- Contracts/JUDGE-ROUTING-CONTRACT.md: GOTO:WORK 뒤 JUDGMENT 강제

수정 후 각 파일은 허용 ACTION/GOTO와 금지 route만 설명한다.

HQ 예:

~~~text
You are HQ. Only HQ may emit ACTION.
CONTINUE requires one GOTO from AVAILABLE GOTO.
PAUSE and END have no GOTO.
Everything after the control line(s) is opaque body.
~~~

WORK 예:

~~~text
You are WORK. Do not emit ACTION.
Allowed GOTO is HQ, or JUDGE when available.
Everything after GOTO is opaque body.
~~~

HIGH 예:

~~~text
You are HIGH. Do not emit ACTION.
Your only destination is HQ.
Everything after GOTO is opaque body.
~~~

JUDGE AI 예:

~~~text
You are JUDGE. Do not emit ACTION.
Your only destination is WORK.
Everything after GOTO is opaque body.
~~~

### 2.2 RoleContractLoader

ROLE / INBOUND TYPE / AVAILABLE GOTO / HIGH PERMIT / JUDGE AVAILABLE 같은 Worker 입력 metadata는 유지 가능하다.

이것은 AI 출력 요구가 아니라 현재 실행 문맥을 알려주는 input header다.

단:
- input metadata를 History 분류에 사용하지 않음
- body 앞에 semantic section marker를 삽입하지 않음
- body는 문자열 그대로 전달

현재 OPAQUE INBOUND BODY 같은 Worker 입력 라벨은 기술적으로 유지 가능하지만 routing에 필요하지 않다면 더 단순화해도 된다. 중요한 것은 AI가 그것을 반환하도록 요구하지 않는 것이다.

### 2.3 JudgeTransportContract

현재 ExtractRequest()가 VALIDATION REQUEST marker를 찾는다.

신규 GOTO flow에서는 이미 WORK가 GOTO:JUDGE를 선택했으므로 route.Body 전체가 JUDGE 요청이다.

권장:

~~~text
ExtractRequest(body) -> body.Trim()
~~~

또는 ExtractRequest 자체를 제거하고 route.Body를 TryParse/serialize에 직접 전달한다.

provider가 요구하는 NOUL/SCORE/CHOICE 구조 parsing은 transport schema 목적에 한해 유지할 수 있다.

PASS threshold의 의미 판단은 Worker가 하지 않는다.

### 2.4 JUDGE raw return

현재 MainWindow.xaml.cs에서:

~~~text
inbound = [JUDGMENT] + raw response
~~~

형태로 marker를 다시 붙인다.

제거한다.

native JEV 결과는:

~~~text
inboundType = JUDGMENT
inbound = transport.RawResponse
state = WORK
~~~

처럼 같은 WORK session에 raw body를 직접 전달한다.

inboundType은 Worker 내부 metadata이고 AI output tag가 아니다.

---

## 3. History 카드 — 새 이벤트 프로토콜을 만들지 말 것

역할별 WORK REPORT, HIGH REPORT 같은 새 wire tag나 AI event contract를 만들 필요가 없다.

각 역할 호출이 끝나는 순간 Worker는 이미 다음 정보를 안다.

- 현재 WorkerRoleState
- 실행한 role
- response completion
- parsed ACTION/GOTO
- Codex/JEV usage
- Codex result files
- process/transport error

이 실행 사실을 History builder에 직접 전달한다.

즉:

~~~text
WORK call 완료
  -> WorkerGotoContract.Parse(WORK, response)
  -> role = WORK를 이미 알고 있음
  -> body = route.Body
  -> usage = result.Usage
  -> files = result.Files / file change telemetry
  -> WORK History card 생성
  -> 다음 state로 이동
~~~

이다.

AI 본문에 REPORT가 있는지 확인하는 단계는 없다.

---

## 4. 현재 History 코드 문제

MainWindow.xaml.cs의 CreateHistoryEvent()는 source 문자열을 검사해 역할을 추정한다.

예:
- LUNA / IMPLEMENT / WORKER -> Implementer
- JEV / JUDGE -> Judge
- SOL / CODEX / GPT WEB -> Coordinator

그리고 LUNA RESULT, JEV RESULT, SOL REVIEW 같은 과거 source 이름을 특정 카드로 매핑한다.

신규 GOTO flow에서 WORK → HQ 반환은:

~~~text
inboundType = WORK_REPORT
inbound = route.Body
state = HQ
...
AddTaskMessage(AI HANDOFF, inbound, status: WORK_REPORT)
~~~

형태라 CreateHistoryEvent()가 AI HANDOFF를 신규 WORK 결과 카드로 인식하지 못해 null을 반환한다.

이것이 이번 WORK 카드 누락의 직접 원인이다.

### 수정 방향

Legacy Web 쪽 source-string mapper는 legacy용으로 남길 수 있다.

신규 CLI에는 별도 typed helper를 둔다.

개념 예:

~~~text
AddRoleResponseHistory(
    role: WorkerRoleState.Work,
    body: route.Body,
    action: null,
    target: WorkerRoleState.Hq,
    usage: result.Usage,
    files: result.Files,
    fileChanges: ...);
~~~

이 helper는 새로운 wire/event protocol이 아니다. Worker 내부 UI 기록 함수일 뿐이다.

role/card title은 body 내용이 아니라 이미 아는 state/control에서 정한다.

권장 title:
- HQ + CONTINUE: 작업 요청
- HQ + PAUSE/END: 수행 결과
- WORK: 수행 결과
- JUDGE: 판정 결과
- HIGH: 수행 결과
- UNKNOWN: 오류 전달

---

## 5. 카드 본문 포맷 — 정확히 3줄

사용자가 원하는 카드는 상세 보고서가 아니라 짧은 이력이다.

역할명/아이콘/시각 영역을 제외하고 본문은 기본 3줄로 고정한다.

### 1줄 — 응답 미리보기

body를 의미적으로 요약하지 않는다.

처리:
1. route.Body 또는 raw provider body 사용
2. newline/tab/연속 공백을 한 칸으로 normalize
3. TextBlock은 TextWrapping=NoWrap
4. TextTrimming=CharacterEllipsis
5. 한 줄만 표시

가능하면 문자열을 임의 재작성하기보다 WPF ellipsis로 잘라 표시한다.

예:

~~~text
projecthub-smoke.txt를 생성하고 다시 읽어 PROJECTHUB_WORK_OK 한 줄을 확인했습니다. …
~~~

AI를 추가 호출해 요약하지 않는다.

### 2줄 — 토큰 telemetry

예:

~~~text
토큰 · 총 1,284 · 입력 920 · 캐시 210 · 출력 164
~~~

reasoning token이 별도 제공되면:

~~~text
토큰 · 총 1,284 · 입력 920 · 캐시 210 · 출력 164 · 추론 80
~~~

provider usage 미제공:

~~~text
토큰 · 미제공
~~~

unknown을 0으로 표시하지 않는다.

### 3줄 — 파일 telemetry

목표:

~~~text
파일 · 생성 1 · 수정 0 · 삭제 0 · projecthub-smoke.txt
~~~

여러 파일:

~~~text
파일 · 생성 1 · 수정 3 · 삭제 0 · projecthub-smoke.txt 외 3개
~~~

변경 없음:

~~~text
파일 · 변경 없음
~~~

### 현재 telemetry 한계

현재 CodexCliFile은 Path / FileName / MimeType / Size만 가진다.

ExtractFiles()도 JSONL에서 후보 path를 수집한 뒤 현재 존재하는 파일만 반환한다.

따라서 현재 정보만으로는:
- 생성인지 수정인지 구분 불가
- 삭제 파일 표현 불가

이다.

Worker가 AI 응답 문장을 읽어 생성/수정/삭제를 추정하면 안 된다.

권장:
1. Codex CLI JSONL의 기계적 file/tool event에서 change type을 얻을 수 있으면 그것을 사용
2. 부족하면 별도 FileChangeTelemetry 모델/collector 추가
3. 정확한 change type이 없을 때는 파일 · N개 감지 · 대표파일 외 N개로 표시

파일 change telemetry도 작업 품질 판단이 아니라 실행 사실 기록이어야 한다.

---

## 6. 카드 생성 위치

### HQ

RunCoordinatorRoleAsync 결과를 받고 ACTION/GOTO parse가 끝난 직후 카드 기록.

CONTINUE이면 body는 HQ가 다음 역할에 보낸 지시 내용의 preview.

PAUSE/END이면 body는 HQ의 사용자-facing 결과 preview.

### WORK

WorkerGotoContract.Parse(WORK, result.FinalMessage) 직후 카드 기록.

GOTO:HQ든 GOTO:JUDGE든 WORK 응답 자체는 한 번 완료됐으므로 WORK 카드 1개를 남긴다.

### JUDGE

provider raw response 수신 직후 Judge 카드 기록.

그 뒤 같은 WORK session으로 raw body 전달.

### HIGH

WorkerGotoContract.Parse(HIGH, result.FinalMessage) 직후 HighLevel 카드 기록.

그 뒤 HQ로 이동.

### UNKNOWN

RouteUnknown이 만든 source/code/detail로 System/오류 카드 기록 가능.

오류 내용을 의미적으로 요약하지 않고 code + 짧은 detail preview만 표시한다.

---

## 7. transcript와 카드 역할 분리

Transcript:
- AI 응답 원문
- full stdout/stderr
- 상세 usage
- 파일 상세 목록
- session/provider 정보

History 카드:
- 한 줄 preview
- token line
- file line

카드가 짧다는 이유로 AI 응답 형식을 짧게 강제하지 않는다.

---

## 8. 단위 테스트

### Control contract
- HQ 응답이 ACTION/GOTO + plain body만으로 통과
- WORK 응답이 GOTO:HQ + plain body만으로 통과
- WORK 응답이 GOTO:JUDGE + plain body만으로 통과
- HIGH 응답이 GOTO:HQ + plain body만으로 통과
- semantic body tag가 없어도 정상
- body 안에 REPORT/JUDGMENT 문자열이 있어도 routing 결과에 영향 없음

### Judge transport
- GOTO:JUDGE 뒤 plain body가 그대로 transport parser 입력
- VALIDATION REQUEST marker 없이 정상 request 가능
- native raw response에 JUDGMENT marker를 삽입하지 않음
- same WORK session 복귀

### History
- WORK response completion -> Implementer 카드 정확히 1개
- HIGH response completion -> HighLevel 카드 정확히 1개
- JUDGE response -> Judge 카드 정확히 1개
- HQ CONTINUE/END -> Coordinator 카드
- AI HANDOFF source 문자열에 의존하지 않음
- preview는 한 줄 ellipsis
- usage unknown -> 미제공
- change type unknown -> 생성/수정/삭제 추정 없음

### Legacy
- NEXT:WEB/JEV 기존 흐름 회귀 없음
- legacy marker parsing은 legacy namespace에만 존재

---

## 9. Explorer 재검증

### A. 기본

~~~text
HQ -> WORK -> HQ -> END
~~~

화면 기대 순서:

~~~text
설계 관제 · 작업 요청
작업 · 수행 결과
설계 관제 · 수행 결과
~~~

사용자 최초 요청 카드를 별도 표시한다면 그 카드가 맨 앞에 추가될 수 있다.

WORK 카드가 transcript에만 있고 UI에 빠지면 실패.

### B. Judge

~~~text
HQ -> WORK -> JUDGE -> WORK -> HQ -> END
~~~

WORK/JUDGE/복귀 WORK 응답 카드가 각각 누락 없이 표시되는지 확인.

### C. HIGH

~~~text
permit -> HQ -> HIGH -> HQ
~~~

HIGH 카드가 실제 HIGH 호출 완료 시 생성되는지 확인.

### D. UNKNOWN

~~~text
invalid route/provider error -> UNKNOWN -> HQ
~~~

Worker가 fallback 역할을 고르지 않고 오류 사실만 표시하는지 확인.

---

## 10. 구현 순서

1. 네 role output contract에서 semantic body tag 요구 제거
2. JudgeTransportContract marker 검색 제거
3. JUDGE raw response marker 삽입 제거
4. 신규 CLI typed History helper 추가
5. HQ/WORK/JUDGE/HIGH 응답 완료 지점에 직접 카드 기록 연결
6. 신규 CLI에서 source-string History 추론 우회/제거
7. 카드 3줄 UI binding 구성
8. token telemetry 연결
9. file change telemetry 정확도에 맞는 표시 구현
10. 필요하면 FileChangeTelemetry 추가
11. 단위 테스트
12. Release build/publish
13. Explorer A~D 재검증

---

## 11. 완료 기준

다음이 모두 충족돼야 11-C-GOTO-CONTRACT를 완료 처리한다.

- 신규 CLI AI 출력 제어 계약이 ACTION/GOTO only
- semantic body tag 의존성 0
- body는 opaque 전달
- Worker 의미 판단 0
- History 카드가 AI tag/source 추론 없이 role/state/telemetry에서 생성
- 카드가 1줄 preview + 2줄 token + 3줄 file 형식
- WORK/JUDGE/HIGH 카드 누락 없음
- Legacy Web 회귀 없음
- Explorer A~D 확인
---

# 2026-09-24 UI 피드백 — 상단 단계 화살표 제거 + 황금빛 활성 테두리

> **범위 고정:** 이 절은 최신 main HEAD `ce5c5fedfad4bf48c47d1de6243c69b8301d6b39` 이후의 **상단 “현재 작업” 파이프라인 UI만** 다룬다.
> 최근 반영된 ACTION/GOTO parser, UNKNOWN→HQ 요약 처리, WORK→JUDGE/JEV 계약, opaque body, History 카드 로직은 이번 UI 작업에서 수정하지 않는다.
> **이번 단계에서는 제품 코드를 아직 수정하지 말고 설계 기준으로 사용한다.**

## 1. 변경 목표

현재 상단 파이프라인은:

~~~text
[1. 대기]  »»  [2. 설계 관제]  »»  [3. 작업]  »»  [4. 고수준 작업]  »»  [5. 판정]
~~~

형태이고, 실제 움직임은 카드가 아니라 `PipelineArrow1~4` / `PipelineArrowText1~4`를 `UpdatePipelineArrowAnimation()`에서 펄스시키는 방식이다.

이를 다음처럼 바꾼다.

~~~text
[1. 대기]   [2. 설계 관제]   [3. 작업]   [4. 고수준 작업]   [5. 판정]
                ↑
       현재 단계 카드 외곽만
       황금빛이 둘레를 도는 효과
~~~

- 화살표는 제거
- 카드 사이에는 좁은 여백만 남김
- 현재 실행 중인 한 단계의 카드 외곽선만 황금빛 애니메이션
- 카드 배경/아이콘/역할 고유색은 기존 정책 유지
- 비활성 단계는 기존 그레이스케일 유지
- Idle 단계도 현재 단계이면 동일한 황금 테두리 애니메이션
- HIGH/JUDGE가 단순히 사용 가능하다는 이유만으로 애니메이션하지 않음
- 실제 `_currentTaskStage`인 카드만 애니메이션

## 2. 현재 구현 기준

상단 Grid는 현재 `* | 36 | * | 36 | * | 36 | * | 36 | *` 구조다. 36px 열에는 PipelineArrow 1~4가 들어 있다.

현재 활성 카드 자체는 `SetPipelineCard()`에서 정적 BorderBrush, 2px BorderThickness, `CreateCurrentStageShadow()`만 적용하고 있으며 카드 테두리 애니메이션은 없다.

## 3. 화살표 제거와 간격 축소

권장 Grid:

~~~text
* | 12 | * | 12 | * | 12 | * | 12 | *
~~~

초기값은 12px를 권장한다. 카드에는 이미 `Margin=4`가 있으므로 12px spacer면 충분하다.

상단 XAML에서 제거 대상:

~~~text
PipelineArrow1~4
PipelineArrowText1~4
~~~

화살표 열 자체는 없애지 말고 12px spacer로 유지한다.

상단 전용 `UpdatePipelineArrowAnimation()`은 제거 대상으로 본다. `UpdatePipelineVisuals()`와 `UpdateArrowAnimation()` 안의 해당 호출도 제거한다.

단, `_flowTimer`와 `UpdateArrowAnimation()` 자체는 삭제하지 않는다. 현재 하단/legacy Flow animation과 Judge pulse에도 사용된다.

`_nextTaskStage` / `explicitNextStage`는 상단 화살표 제거 후 전체 참조 검색에서 실제 사용처가 0인지 확인한 뒤에만 cleanup한다.

## 4. 황금 테두리 구현 방식

기존 카드 Border 자체는 유지하고 각 카드와 같은 Grid.Column 위에 투명한 Rectangle overlay를 하나 추가하는 방식을 권장한다.

개념:

~~~xml
<Border x:Name="PipelineCoordinatorCard" Grid.Column="2" Margin="4" CornerRadius="10" ... />
<Rectangle x:Name="PipelineCoordinatorActiveBorder" Grid.Column="2" Margin="4"
           RadiusX="10" RadiusY="10" Fill="Transparent"
           StrokeThickness="2.5" IsHitTestVisible="False" Panel.ZIndex="5" />
~~~

동일하게 다음 overlay를 둔다.

~~~text
PipelineIdleActiveBorder
PipelineCoordinatorActiveBorder
PipelineImplementerActiveBorder
PipelineHighLevelActiveBorder
PipelineJudgeActiveBorder
~~~

이 방식은 기존 Background/Icon/Text layout을 건드리지 않고 애니메이션만 독립 start/stop할 수 있다.

## 5. 둘레를 도는 효과

단순 gradient 회전보다 `rounded Rectangle + StrokeDashArray + StrokeDashOffset animation`을 추천한다. 실제 밝은 금빛 구간이 카드 둘레를 따라 이동하는 느낌이 더 분명하다.

예:

~~~text
Stroke          #FFFFD76A
StrokeThickness 2.5
StrokeDashArray 2 10
StrokeDashCap   Round
StrokeDashOffset 0 -> -24
Duration        1.8~2.2초
RepeatBehavior  Forever
~~~

시각 조합은 아래 정도를 권장한다.

~~~text
base gold   #806F5315
moving gold #FFFFD76A
highlight   #FFFFF0AE
glow        #CCFFC84A
~~~

한 개의 dashed stroke만 쓰지 말고, 약한 정적 gold outline + moving bright dash + ShadowDepth=0의 약한 gold glow 조합이 적절하다.

과하게 번쩍이는 게임 UI보다는 관리 UI에 맞는 낮은 강도의 glow로 유지한다.

## 6. 애니메이션 구동 방식

황금 테두리를 기존 150ms `_flowTimer`로 프레임 계산하지 않는다.

WPF `DoubleAnimation` 또는 Storyboard로 `StrokeDashOffset`을 직접 반복시킨다.

권장 helper 개념:

~~~text
SetPipelineStageAnimation(overlay, isActive)
~~~

활성 시 animation start, 비활성 시 `BeginAnimation(..., null)` 후 overlay Collapsed 처리한다.

이렇게 하면 기존 `_flowFrame`과 legacy Flow animation에 새 책임을 추가하지 않는다.

## 7. SetPipelineCard 연결

`SetPipelineCard()`가 이미 계산하는 `current = _currentTaskStage == stage` 값을 gold overlay 활성의 유일한 조건으로 사용한다.

즉:

~~~text
기존 Worker TaskStage -> current bool -> gold border 표시
~~~

일 뿐이며 animation이 작업 흐름이나 다음 단계를 판단하지 않는다.

현재 카드의 정적 role-colored BorderBrush는 gold overlay와 겹치므로 current card 자체 BorderBrush는 Transparent 또는 약한 neutral로 두고, 진행 표시를 overlay가 전담하도록 한다.

현재 `CreateCurrentStageShadow()`의 파란 shadow도 gold와 충돌하므로 제거하고 gold glow로 대체하는 것을 권장한다. 역할별 고유색은 Background/Icon/Text로 유지한다.

## 8. Idle / HIGH / Judge 규칙

Idle은 현재 teal 활성색 정책을 그대로 유지하고, current일 때 `teal card + gold moving border`로 표시한다.

HIGH permit이 있다는 이유만으로 HIGH를 컬러/애니메이션 활성화하지 않는다. 실제 `TaskStage.HighLevel`에 진입했을 때만 gold border를 켠다.

Judge가 비활성이면 카드 gray/disabled 상태를 유지하고 gold overlay는 항상 OFF다.

## 9. 단계 전환 예

~~~text
HQ 실행 중      -> Coordinator gold orbit
HQ -> WORK       -> Coordinator stop / Implementer start
WORK -> JUDGE    -> Implementer stop / Judge start
JUDGE -> WORK    -> Judge stop / Implementer start
HQ -> HIGH       -> Coordinator stop / HighLevel start
HIGH -> HQ       -> HighLevel stop / Coordinator start
~~~

화살표를 통해 이동 중을 표현하지 않고 현재 실행 주체 하나만 명확하게 표시한다.

## 10. 최신 변경과 충돌 금지

최신 HEAD에는 이번 UI 조사 이전에 다음 변경이 이미 들어왔다.

- GOTO parser contained-keyword handling
- UNKNOWN 오류의 HQ 요약 전달 정책
- WORK의 JEV/JUDGE 요청 지침
- opaque body / History
- HIGH one-shot permit

이번 UI 작업에서 아래 영역은 건드리지 않는다.

~~~text
WorkerGotoContract.cs
JudgeTransportContract.cs
Contracts/HQ-ROUTING-CONTRACT.md
Contracts/WORK-ROUTING-CONTRACT.md
Contracts/HIGH-ROUTING-CONTRACT.md
Contracts/JUDGE-ROUTING-CONTRACT.md
UNKNOWN routing
HIGH permit logic
History routing logic
~~~

수정 범위는 원칙적으로 `MainWindow.xaml`, `MainWindow.xaml.cs`의 pipeline visual 전용 코드, pipeline UI 관련 테스트에 한정한다.

같은 MainWindow.xaml.cs가 최근 계속 바뀌고 있으므로 실제 구현 직전에 반드시 최신 HEAD를 다시 fetch한다.

## 11. 권장 구현 순서

1. 최신 HEAD 재확인
2. 상단 pipeline arrow XAML 제거
3. arrow column 36 -> 12
4. 카드별 active border overlay 추가
5. gold stroke/glow style 추가
6. `SetPipelineStageAnimation()` helper 추가
7. `SetPipelineCard()` current 값으로 start/stop
8. Idle에도 동일 helper 적용
9. 상단 `UpdatePipelineArrowAnimation()` 호출 제거
10. `UpdatePipelineArrowAnimation()` 본체 제거
11. PipelineArrow name reference 0 확인
12. `_nextTaskStage/explicitNextStage`는 참조 0일 때만 cleanup
13. `_flowTimer`와 legacy Flow/Judge pulse 유지 확인
14. Debug/Release build
15. Explorer 실제 단계 전환 확인

## 12. Explorer 확인

기본 `Idle -> HQ -> WORK -> HQ -> Idle/END`, Judge `WORK -> JUDGE -> WORK`, HIGH `HQ -> HIGH -> HQ`를 확인한다.

각 시점에 gold orbit은 정확히 한 카드에만 있어야 하며, 빠른 전환이나 UNKNOWN/HQ 복귀에서도 이전 카드에 animation이 남으면 안 된다.

## 13. 완료 기준

- 상단 pipeline arrow 4개 완전 제거
- 카드 사이 spacer 약 12px
- 현재 단계 카드 1개에만 gold moving border
- role background/icon 색상 유지
- inactive gray 유지
- HIGH permit만으로 HIGH 활성 표시하지 않음
- Judge OFF 카드 animation 없음
- 단계 변경 시 이전 animation 완전 정지
- `_flowTimer` / legacy Flow / Judge pulse 회귀 없음
- routing/JEV/UNKNOWN/HIGH permit/History 기능 변경 없음

이 UI 변경은 라우팅 의미를 해석하는 기능이 아니라 **Worker가 이미 알고 있는 현재 TaskStage를 표시하는 presentation layer**로만 구현한다.