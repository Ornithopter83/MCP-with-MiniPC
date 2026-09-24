# GPT Web Feedback

Updated: 2026-09-23


## 2026-09-23 메인 화면 시각 정합 후속 — 실제 구현 `476541c` vs 승인 최종 이미지

기준:

```text
현재 Git HEAD
476541c783572ad3dbeebbeff166632bee5ea93b
Redesign ProjectHub worker dashboard
```

이번 후속의 source of truth는 사용자가 제공한 두 화면 중 **두 번째 승인 최종 이미지**다. 첫 번째 화면은 `476541c` 구현 결과이며, 기능 구조는 맞지만 시각 비율과 상태 표현이 아직 승인 이미지와 다르다.

기존 11-UI-B의 기능 방향은 유지하되 아래 지시는 **시각 정합과 입력/이력 전환에 대해 우선 적용**한다. 특히 이전 지시 중 "메인 이력에는 내용 요약을 표시하지 않는다"는 부분은 이번 사용자 결정으로 수정한다. **raw transcript 전문은 표시하지 않지만, 각 단계의 요청 요약과 수행결과 요약은 표시한다.**

### 11-UI-B-K1 — 전체 레이아웃 비율부터 승인 이미지에 맞춘다

현재 구현은 상단이 너무 낮고, 5단계 카드가 지나치게 좁으며, 이력 영역이 지나치게 큰 빈 면적으로 보인다.

승인 이미지의 세로 비율을 기준으로 메인 Grid를 다음 감각으로 조정한다.

```text
상단 프로젝트/서버 요약       약 10~11%
현재 작업                    약 23~25%
메시지 및 작업 이력          약 52~55%
하단 실행 영역               약 11~13%
```

1400×900 기준 권장 시작값:

```xml
<RowDefinition Height="96"/>
<RowDefinition Height="220"/>
<RowDefinition Height="*"/>
<RowDefinition Height="100"/>
```

현재의 `78 / 220 / * / 82`는 상단과 하단이 너무 압축되어 보인다.

외곽 margin은 승인 이미지처럼 화면 가장자리에서 충분히 떨어뜨린다.

```text
좌우: 22~24px
상단: 16~18px
하단: 14~18px
섹션 간 gap: 10~12px
```

각 SectionBorder는:

```text
Background   #FFFFFF
Border       #D7E5F4 계열 1px
CornerRadius 10~12
```

으로 통일한다.

### 11-UI-B-K2 — 상단 프로젝트 카드의 글자와 여백을 키운다

현재 첫 화면은 프로젝트/작업 폴더/서버 주소가 작고 상단에 눌려 있다. 승인 이미지에서는 상단 정보 자체가 하나의 안정적인 header 카드처럼 보인다.

왼쪽 정보 카드:

```text
label column width: 90~105px
label font: 15~16px, SemiBold
value font: 15~16px
행 간격: 6~8px
좌우 padding: 18~22px
상하 padding: 12~14px
```

현재처럼 행 간격 3px은 너무 촘촘하므로 늘린다.

프로젝트 이름은 실제 repository 표시값을 유지한다. 승인 이미지의 `Ornithopter 개발 프로젝트`는 시각 예시이며 고정 문자열로 넣지 않는다.

### 11-UI-B-K3 — 서버 카드를 승인 이미지 수준으로 키운다

현재 서버 카드는 세로 높이가 작고 아이콘/텍스트가 압축되어 있다.

권장:

```text
server card width: 330~350px
icon container: 64~70px
server title: 16~18px Bold
machine name: 14~15px
online/offline: 14px Bold
status dot: 11~12px
```

온라인은:

```text
dot  #00AA35
text #00A33A
```

오프라인은 현재 실제 상태를 그대로 표시하되:

```text
dot  #FF4D0A
text #FF3B00
```

정도로 선명하게 한다.

승인 이미지가 온라인이라고 해서 실제 offline 상태를 online으로 위장하면 안 된다.

### 11-UI-B-K4 — 우측 설정 버튼을 "아이콘 + 설정" 카드로 복원

현재 구현의 우측 상단 설정은 떠 있는 큰 gear 하나라 승인 이미지와 가장 눈에 띄게 다르다.

승인 이미지처럼 별도 작은 카드로 만든다.

구조:

```text
┌─────────┐
│   ⚙     │
│  설정   │
└─────────┘
```

권장:

```text
width: 72~82px
height: header full height
background: #F7FBFF 또는 White
border: #D7E5F4 1px
corner radius: 9~10
gear: 27~30px
label "설정": 13~14px
```

hover 시 배경만 `#EAF4FF` 정도로 바꾸고 과한 animation은 넣지 않는다.

기존 `Settings_Click`과 설정 popup 동작은 그대로 연결한다.

### 11-UI-B-K5 — "현재 작업" 제목 앞의 장식 아이콘을 제거

현재 구현:

```text
▤ 현재 작업
```

승인 이미지:

```text
현재 작업
```

따라서 `▤`를 제거한다.

동일하게 `메시지 및 작업 이력` 앞의 `▤`도 제거한다.

섹션 제목 권장:

```text
FontSize   19~21
FontWeight SemiBold 또는 Bold
Margin     left 4~6
```

### 11-UI-B-K6 — 5개 카드를 UniformGrid 9열로 만들지 않는다

현재 가장 큰 레이아웃 문제다.

`UniformGrid Columns="9"` 때문에 카드 5개와 화살표 4개가 **모두 동일 폭**을 가져 카드가 지나치게 좁고 카드 사이가 벌어진다.

반드시 일반 `Grid`로 바꾼다.

예:

```xml
<Grid.ColumnDefinitions>
  <ColumnDefinition Width="*"/>
  <ColumnDefinition Width="34"/>
  <ColumnDefinition Width="*"/>
  <ColumnDefinition Width="34"/>
  <ColumnDefinition Width="*"/>
  <ColumnDefinition Width="34"/>
  <ColumnDefinition Width="*"/>
  <ColumnDefinition Width="34"/>
  <ColumnDefinition Width="*"/>
</Grid.ColumnDefinitions>
```

핵심:

- 카드 5개는 같은 넓은 폭.
- 화살표 영역은 30~38px 정도만 사용.
- 카드 간 시각적 gap은 8~12px 수준.
- 카드가 현재 화면처럼 좁고 세로로 긴 형태가 되지 않게 한다.

승인 이미지에서는 5개 카드가 현재 작업 영역의 대부분을 차지한다.

### 11-UI-B-K7 — 카드 크기와 내부 배치를 승인 이미지에 맞춘다

각 카드:

```text
height: Current Task content 영역 대부분
corner radius: 8~10
internal padding: 14~18
```

내부는 정확히 세 요소만 둔다.

```text
1. "N. 단계명"
2. 역할 아이콘
3. 모델명
```

권장 타이포:

```text
단계명 22~24px Bold
아이콘 74~86px 영역
모델명 18~20px SemiBold/Bold
```

현재 구현의 20px / 72px / 17px보다 한 단계 키운다.

대기 카드의 시계 아이콘도 단순 문자 `◷`보다 승인 이미지처럼 두께가 있는 원형 clock asset/vector를 사용한다. 외곽 원은 #7A8797, 내부 clock은 white.

### 11-UI-B-K8 — 카드 컬러는 카드 전체 상태에 적용한다

현재 구현은 비활성 카드의 background만 회색으로 바꾸고 내부 title/model/icon circle은 원래 컬러를 유지한다. 그래서 실제 화면에서 2~5가 모두 색이 살아 있어 "현재 1~2개만 컬러" 원칙이 깨진다.

비활성화 시 아래 **모든 요소를 함께 회색화**한다.

```text
card background
title foreground
model foreground
icon circle/background
AI icon image
border
```

가능하면 이미지 asset도:

```text
current-openai.png
current-openai-gray.png
current-jev.png
current-jev-gray.png
```

처럼 이미 존재하는 gray asset을 재사용한다.

상태별 표시:

#### 현재 단계

- 원래 역할색 100%
- 2px 역할색 border
- 약한 외곽 glow/drop shadow
- opacity 1

#### 다음 단계

- 원래 역할색을 사용하되 current보다 10~20% 약하게
- border는 1px 또는 없음
- shadow 없음

#### 나머지 단계

- background #ECEFF3 ~ #F1F3F6
- foreground #8A95A5
- gray icon
- opacity를 전체 카드에 무작정 0.48로 낮추지 않는다. 글자가 너무 흐려지지 않게 명시적인 gray palette를 사용한다.

#### 사용 안 함인 optional 단계

- 같은 gray palette
- 더 약한 foreground #A6AFBB
- 필요하면 opacity 0.7 정도
- 카드 위치는 유지

현재 구현의 `role.Card.Opacity = 0.48` 방식은 텍스트 가독성을 지나치게 떨어뜨리므로 피한다.

### 11-UI-B-K9 — 역할별 기본색을 승인 이미지에 더 가깝게 조정

권장 palette:

```text
대기
background #E9EDF2
accent     #788594
text       #4F5D70

설계 관제
background #EAF4FF
accent     #1477E8
text       #0765D7

작업
background #FDE2E6
accent     #D92B50
text       #A91636

고수준 작업
background #E6CDD9
accent     #8B164D
text       #70103B

판정
background #D9F2E2
accent     #07864A
text       #056B37
```

카드에 그라데이션을 과하게 넣지 말고 승인 이미지처럼 매우 약한 tonal variation만 허용한다.

### 11-UI-B-K10 — 활성 카드에만 승인 이미지의 blue focus 느낌을 준다

설계 관제가 current일 때 승인 이미지에는 단순 2px border보다 약간의 빛/그림자가 있다.

WPF에서:

- current card border 2px
- `DropShadowEffect` BlurRadius 약 10~14
- ShadowDepth 0
- Opacity 0.18~0.25
- color는 해당 역할 accent

정도로 제한한다.

다음 단계는 shadow를 받지 않는다.

### 11-UI-B-K11 — 화살표를 작은 gray 문자가 아니라 파란 connector로 만든다

현재 `❯` 한 글자는 승인 이미지와 다르다.

승인 이미지의 화살표는 카드 사이 중앙을 연결하는 **청색 이중 chevron / capsule connector**처럼 보인다.

권장 구조:

```text
[카드]  ≫  [카드]
```

- width 30~36px
- foreground #1477E8
- background는 투명 또는 아주 옅은 #EAF4FF
- 필요하면 원형/rounded capsule 36×48 안에 chevron 배치

비활성 구간:

- #C8D4E2
- opacity 0.45~0.55
- animation 없음

활성 구간:

- #1477E8
- opacity pulse
- 약한 horizontal translate 3~5px

현재 `FlowArrowStyle`처럼 모든 arrow가 Loaded부터 영구 애니메이션 되는 방식은 사용하지 않는다.

### 11-UI-B-K12 — stage skip을 "인접 index만"으로 판정하지 않는다

현재 `UpdatePipelineVisuals()`는 화살표 활성 조건이 사실상 `from == index+1 && to == index+2`라서:

```text
작업 → 판정
```

처럼 HighLevel을 건너뛰면 실제 진행 표현이 끊길 수 있다.

skip 경로는 두 방법 중 하나로 명확히 처리한다.

권장 A:

- `작업 → 고수준` 화살표와 `고수준 → 판정` 화살표를 연속 활성화하되
- disabled high-level card는 회색
- 화살표가 "통과"함을 보여준다.

또는 B:

- 카드 overlay 위에 실제 destination까지 하나의 connector animation을 별도로 그린다.

첫 구현은 A가 단순하다.

Judge OFF이고 HighLevel OFF이면 작업 완료 후 더 이상 다음 화살표를 활성화하지 않는다.

### 11-UI-B-K13 — idle 상태에서 "메시지 및 작업 이력" 본문을 입력창으로 사용

현재 구현은 이력 섹션이 비어 있고, 입력창은 별도의 하단 bar에 있다. 사용자가 원하는 구조는 반대다.

**작업이 아직 시작되지 않은 idle/new-task 상태에서는 "메시지 및 작업 이력"의 큰 본문 자체가 작업 입력창으로 변해야 한다.**

즉:

```text
┌ 메시지 및 작업 이력 ──────────────────────────────┐
│                                                   │
│ 작업 내용을 입력하세요...                         │
│                                                   │
│                                                   │
└───────────────────────────────────────────────────┘
                                      [ ▶ 실행 ]
```

현재 하단의 별도 `DashboardTaskInput` bar는 제거한다.

입력창 권장:

- section 내부의 남는 공간 전체 사용
- BorderThickness 0 또는 매우 약한 1px
- Background #FBFDFF
- Padding 20~24px
- FontSize 17~18px
- TextWrapping Wrap
- AcceptsReturn true
- vertical scrollbar Auto
- placeholder `작업 내용을 입력하세요...`
- placeholder color #7C8DA8

입력 중에도 section title은 `메시지 및 작업 이력`을 유지한다. title을 COMMAND로 바꾸지 않는다.

### 11-UI-B-K14 — 실행 즉시 같은 영역이 작업 이력으로 전환

사용자가 입력 후 `실행`을 누르면:

```text
DashboardInputView  → Collapsed
DashboardHistoryView → Visible
```

로 같은 자리에서 즉시 전환한다.

첫 번째 이력은 반드시 사용자 요청을 남긴다.

예:

```text
[설계 관제 아이콘]  설계 관제       23:47:03
                    작업 요청
                    "메인 화면의 카드 간격을 승인 이미지 기준으로 조정..."
                    요청 1건 · 184 B
```

그 뒤 단계별 결과가 도착할 때 같은 리스트에 누적한다.

### 11-UI-B-K15 — 이전 "요약 금지" 지시는 수정한다

이번 사용자 결정에 따라 메인 이력은 단순히:

```text
결과 수신 · 18.6 KB
```

만 표시하지 않는다.

**요청 내용과 수행결과의 짧은 요약을 함께 표시한다.**

단, raw stdout, 전체 prompt, 전체 GPT Web 응답, JSON 원문을 그대로 덤프하지 않는다.

한 행의 본문은 최대 2줄 정도를 권장한다.

예:

```text
설계 관제
요청: Current Task를 5단계 고정 카드로 변경하고 활성 단계만 강조

작업
결과: MainWindow XAML을 변경하고 카드 상태 바인딩 및 단계 전환 로직을 연결
4 files · 18.6 KB · PASS

판정
결과: 단계 전환과 optional skip 검증 완료
2.1 KB · PASS
```

즉:

```text
화면 = 사람이 읽을 수 있는 요약
내부 transcript/export = 원본
```

의 경계를 유지한다.

### 11-UI-B-K16 — HistoryEvent 모델에 summary를 명시적으로 추가

현재 `WorkerHistoryEvent`가 title/size/count/status 중심이라면 사용자용 요약 필드를 분리한다.

권장:

```csharp
record WorkerHistoryEvent(
    DateTimeOffset Timestamp,
    TaskStage SourceStage,
    string EventType,
    string Title,
    string? Summary,
    long? SizeBytes,
    int? ItemCount,
    int? FileCount,
    string? Status,
    string? ReferenceId);
```

Summary 생성 원칙:

- 요청: 사용자가 입력한 작업 지시를 1~2줄로 잘라 표시.
- 설계 관제 결과: work card의 핵심 목적/작업 수를 구조화 데이터에서 요약.
- 작업 결과: changed paths 수, 구현 report summary/status를 우선 사용.
- 고수준 작업: high-level 결과의 실제 summary가 있을 때만 표시.
- 판정: decision + 짧은 message.
- 단순 시스템 telemetry는 summary를 만들지 말고 status/metadata만 표시.

AI를 추가 호출해서 화면용 summary를 별도 생성하지 않는다. 이미 존재하는 구조화 결과/최종 메시지의 짧은 안전한 부분을 사용한다.

### 11-UI-B-K17 — 이력 행 디자인을 승인 이미지와 동일한 정보 밀도로 구성

승인 이미지의 행은 왼쪽 역할 영역 + 오른쪽 넓은 본문 영역이다.

권장 Grid:

```text
[64px 아이콘] [190~230px 역할/시간] [* 요약/메타]
```

행 높이:

```text
약 58~66px
```

왼쪽:

- 역할별 원형 배경
- icon 36~40px
- 설계 관제 blue
- 작업 coral
- 고수준 wine
- 판정 green
- 시스템 gray

중앙:

- 역할명 15~16px Bold
- timestamp 12~13px muted

오른쪽:

- 아주 옅은 역할별 배경 또는 alternate row background
- summary 14~15px
- metadata 12~13px muted
- corner radius 7~9
- padding 14~16

최신 항목이 위로 오도록 한다.

### 11-UI-B-K18 — 작업 완료 후 이력을 즉시 없애지 않는다

중요하다.

`CurrentStage == Idle` 하나만 보고 무조건 입력창으로 되돌리면 작업 완료 직후 사용자가 결과 요약을 볼 수 없다.

따라서 View mode를 stage와 분리한다.

예:

```csharp
enum DashboardBodyMode
{
    NewTaskInput,
    TaskHistory
}
```

전환:

```text
프로그램 시작 / 새 작업 준비
→ NewTaskInput

실행 버튼 누름
→ TaskHistory

작업 수행 중
→ TaskHistory

작업 완료/실패/취소
→ TaskHistory 유지
```

완료 후 자동으로 입력창으로 돌아가지 않는다.

다음 새 작업을 시작할 때만 `NewTaskInput`으로 돌아가야 한다.

이번 범위에서 별도 `새 작업` 버튼을 추가하고 싶지 않다면, 완료 상태에서 하단 버튼을:

```text
[ 새 작업 ]
```

으로 바꾸고 클릭 시 입력 모드로 복귀시키는 방식이 가장 단순하다.

입력 모드에서는 다시:

```text
[ ▶ 실행 ]
```

이다.

실행 중:

```text
[ ■ 취소 ]
```

이렇게 하나의 우하단 버튼을 3상태로 쓰면 승인 이미지의 단순한 UI를 유지할 수 있다.

### 11-UI-B-K19 — 하단에는 입력 bar를 남기지 않는다

현재 첫 화면의 하단 왼쪽 별도 텍스트 입력 bar는 승인 이미지에 없다.

하단 row는 버튼 정렬을 위한 공간으로만 쓴다.

```text
왼쪽: 빈 공간
오른쪽: 실행/취소/새 작업 버튼
```

필요하면 하단 왼쪽에 오류/preflight 문구를 1줄 표시할 수 있으나 기본 상태에서는 비워 둔다.

### 11-UI-B-K20 — 실행 버튼을 더 크게, 더 아래/오른쪽으로 정렬

승인 이미지 기준:

```text
width  240~270px
height 64~72px
font   26~28px Bold
corner 10~12px
right margin 18~24px
bottom margin 14~18px
```

blue:

```text
#1477E8 ~ #087BFF
```

약한 DropShadow:

```text
blur 14~18
opacity 0.18~0.25
depth 3~4
```

disabled 상태도 현재처럼 거의 흰색이 되어 글자가 사라지지 않게 한다.

권장 disabled:

```text
background #B8C8DA
foreground White
opacity 0.85
```

### 11-UI-B-K21 — 실행 가능 조건과 시각 상태를 분리해서 보여준다

버튼이 비활성일 때 이유가 화면에서 전혀 보이지 않으면 사용자가 고장으로 오해한다.

다만 승인 이미지의 단순함을 해치지 않도록 별도 큰 status panel은 만들지 않는다.

입력 모드에서 실행 불가 사유가 있다면 입력창 아래 또는 하단 좌측에 한 줄만 표시한다.

예:

```text
Codex 로그인이 필요합니다.
설계 관제 모델 설정을 확인하세요.
작업 내용을 입력하세요.
```

Server offline은 현재 실행 mode가 Server/Web을 실제로 요구할 때만 block 사유가 되어야 한다. coordinator-first local CLI가 Server와 무관하게 실행 가능하다면 단순 Server offline만으로 실행 버튼을 막지 않는다.

### 11-UI-B-K22 — 현재 단계 산정에서 TaskDirection 문자열 파싱 의존을 줄인다

현재 `SetFlowState()`는:

```csharp
TaskDirection.Text.Contains("SOL")
TaskDirection.Text.Contains("JEV")
...
```

로 `TaskStage`를 추론한다.

이 방식은 UI 문구가 바뀌면 stage가 깨지고, 향후 high-level 연결도 불안정하다.

이번 후속에서 실행 엔진의 각 경계가 명시적으로:

```csharp
SetTaskStage(TaskStage.Coordinator, next)
SetTaskStage(TaskStage.Implementer, next)
SetTaskStage(TaskStage.HighLevel, next)
SetTaskStage(TaskStage.Judge, null)
```

를 호출하도록 바꾼다.

`TaskDirection.Text`는 내부 상태의 결과로 표시할 수는 있어도 source of truth가 되어서는 안 된다.

### 11-UI-B-K23 — 고수준 작업은 실제 실행 연결 전까지 가짜 active로 표시하지 않는다

현재 task 문서상 high-level/JEV 실제 실행 통합은 별도 잔여가 존재한다.

따라서 UI는 설정에서 enable되어 있다는 이유만으로 "현재 고수준 작업 중"처럼 보이면 안 된다.

- enable = 단계 사용 가능
- active = 실제 runner가 해당 단계 실행 중

을 분리한다.

실제 high-level runner 연결 전에는 카드 자체는 존재하지만 현재 단계로 active 전환하지 않는다.

### 11-UI-B-K24 — 판정 카드의 JEV 아이콘 크기/배경을 승인 이미지에 맞춘다

현재 JEV 이미지가 검은 사각 배경/원형 green background 안에서 다소 작거나 겹쳐 보일 수 있다.

승인 이미지처럼:

- green circular/soft background
- JEV asset은 중앙 58~64px
- clipping이 있다면 Uniform stretch
- 검은 사각 경계가 원 밖으로 튀어나오지 않게 한다.

필요하면 원형 clip을 적용한다.

### 11-UI-B-K25 — 제목/모델의 정렬을 정확히 중앙에 맞춘다

모든 pipeline 카드에서:

```text
title center
icon center
model center
```

를 강제한다.

모델 이름 길이가 달라도 card 내부 높이가 흔들리지 않도록:

- title row 고정 높이
- icon row 고정 높이
- model row 고정 높이

로 Grid를 쓰는 편이 좋다.

### 11-UI-B-K26 — 시각 검증은 "기능 실행 화면"까지 캡처해서 비교한다

이번에는 빌드 성공만으로 UI 완료 처리하지 않는다.

반드시 최소 3상태를 실제 Explorer 실행본에서 확인한다.

```text
A. NewTaskInput
- 1. 대기 current
- 나머지 gray
- 이력 영역 = 큰 입력창
- 실행 버튼

B. Coordinator/Implementer 진행
- current + next 최대 2개 color
- 나머지 완전 gray
- 해당 arrow만 animation
- 입력창이 사라지고 history list
- 요청 row + 중간 결과 row 생성

C. 완료/판정
- history가 유지됨
- 요청/수행결과/판정 요약 확인
- 버튼이 새 작업 상태로 전환
```

각 상태에서 승인 이미지와 아래 항목을 눈으로 대조한다.

```text
header height
settings card
card width
arrow width
font size
card saturation
inactive grayscale
history row spacing
run button size/location
overall empty-space balance
```

### 11-UI-B-K27 — JEV 검증 요청

위 시각 구조와 상태 연결이 구현된 다음 Codex 작업은 JEV 검증을 우선 요청한다.

검증 항목:

```text
1. idle/input ↔ history 전환 상태가 실행 lifecycle과 일치하는가
2. 완료 후 history가 사라지지 않는가
3. current/next 외 카드가 실제로 완전 grayscale인가
4. optional high-level/Judge skip 시 arrow route가 끊기지 않는가
5. summary가 raw transcript 전문을 노출하지 않는가
6. 같은 이벤트가 HistoryEvent에 중복 삽입되지 않는가
7. 실행/취소/새 작업 버튼 상태가 서로 충돌하지 않는가
8. settings 변경 후 카드 모델/아이콘이 즉시 갱신되는가
```



## 2026-09-23 메인 화면 재구성 지시 — 승인 이미지 기준

최신 기준 커밋은 `0c71d91e6d6a5019e1a6b9ff5097010a7da013cd`이다. 현재 `src/ProjectHub.Worker/MainWindow.xaml`에는 기존 2-node CURRENT TASK, Codex/GPT Web MESSAGE 탭, 2개 입력창 COMMAND 영역이 남아 있고, `RunTask_Click`은 `CommandInput`과 `WebInstructionInput`에 직접 의존한다. 이번 작업은 이 실행 경로를 한 번에 뜯어고치지 말고 **승인 이미지와 동일한 메인 구도를 먼저 만든 뒤 기존 기능을 단계적으로 연결**한다.

### 11-UI-B-A — 메인 화면 시각 구조를 먼저 승인 이미지와 동일하게 고정

첫 단계에서는 기능 확장보다 `MainWindow.xaml`의 메인 화면 구조를 교체한다. 설정 팝업, 서버 상태 확인, 역할별 모델/추론 설정, Codex thread 연결, JEV endpoint 테스트는 보존한다.

메인 화면의 최종 순서는 아래로 고정한다.

```text
Window title / ProjectHub
┌──────────────────────────────────────────────┐
│ 프로젝트 / 작업 폴더 / 서버 주소     서버 │ ⚙ │
├──────────────────────────────────────────────┤
│ 현재 작업                                    │
│ [1 대기] → [2 설계 관제] → [3 작업] → [4 고수준 작업] → [5 판정] │
├──────────────────────────────────────────────┤
│ 메시지 및 작업 이력                          │
│ 최근 이벤트 목록                             │
├──────────────────────────────────────────────┤
│                                      [▶ 실행]│
└──────────────────────────────────────────────┘
```

삭제/이동 대상:

- 기존 좌측 메뉴는 두지 않는다.
- 기존 `CURRENT TASK`의 좌우 2-node 원형 흐름은 제거한다.
- 기존 `MESSAGE`의 `Codex` / `GPT Web` 선택 탭과 `MESSAGE LOG · ...` 헤더를 제거한다.
- 기존 `COMMAND` 시각 영역 전체를 메인 화면에서 제거한다.
- 하단 usage/model/reasoning/Clear 조작부도 메인 화면에서 제거한다. 역할별 모델/추론은 설정창이 source of truth다.
- 우측 상단의 설정 아이콘은 유지하고 현재 설정 팝업을 그대로 연다.
- 하단 우측에는 승인 이미지처럼 큰 `▶ 실행` 버튼만 둔다.
- 불필요한 하단 상태 문구는 두지 않는다.

이미지 재현 단계에서는 기존 비즈니스 로직 삭제를 최소화한다. 제거되는 컨트롤에 직접 묶인 로직은 다음 단계에서 adapter/state로 옮길 수 있게 분리하되, 먼저 빌드 가능한 화면을 만든다.

### 11-UI-B-B — 현재 작업을 5개 고정 카드로 변경

현재 작업 영역은 런타임에 카드가 재배치되지 않는다. 항상 가로 한 줄에 아래 5개 위치를 유지한다.

```text
1. 대기
2. 설계 관제
3. 작업
4. 고수준 작업
5. 판정
```

카드 표시 규칙:

- `대기`: 회색 카드 + 시계 아이콘.
- `설계 관제`: 청색 계열.
- `작업`: 연적색/코랄 계열.
- `고수준 작업`: 와인/버건디 계열.
- `판정`: 녹색 계열.
- 각 AI 단계는 설정창에서 선택된 역할의 실제 아이콘과 모델명을 표시한다.
- 설정 변경 후 메인 카드의 모델명/아이콘이 즉시 동기화돼야 한다.
- 고수준 작업 또는 판정이 `사용 안 함`이면 위치는 유지하고 비활성 그레이스케일로 표시한다.

활성화 규칙은 **현재 단계 1개 + 필요하면 다음 단계 1개까지만 컬러**다.

예:

```text
설계 관제 실행 중:
1 회색
2 컬러 + 강한 테두리
3 약한 컬러(다음 단계)
4 회색
5 회색

판정 실행 중:
1~4 회색
5 컬러 + 강한 테두리
```

완료된 이전 단계라고 해서 계속 컬러로 남기지 않는다. 메인 화면의 목적은 이력 누적이 아니라 **현재 위치 식별**이다.

카드 내부 텍스트는 승인 이미지 수준으로 제한한다.

```text
단계 번호 + 단계명
AI 아이콘
모델명
```

기존의 `진행 중`, `대기 중`, 설명 문장, JUDGE 상태 문구 등은 카드 내부에서 제거한다. 필요 상태는 테두리/배경/애니메이션으로 전달한다.

### 11-UI-B-C — 카드 사이 방향 애니메이션

기존 `FlowArrow1~3` 방식의 애니메이션 아이디어는 재사용할 수 있으나, 2-node 흐름이 아니라 **5개 고정 카드 사이의 4개 구간**으로 옮긴다.

원칙:

- 카드 사이에 방향 화살표를 고정 배치한다.
- 전체 화살표가 항상 움직이지 않는다.
- 현재 단계에서 실제 다음 단계로 진행하는 구간만 pulse/opacity 애니메이션을 준다.
- 대기 상태에서는 애니메이션을 멈춘다.
- 고수준 작업을 사용하지 않는 실행에서는 `작업 → 판정`처럼 실제 라우팅 구간만 활성화한다.
- 애니메이션은 의미를 표현해야 하므로 장식용 무한 흐름으로 사용하지 않는다.

기존 `_flowTimer`, `UpdateArrowAnimation()`은 보존 가능하지만 `_pairArrowActive`/좌우 node 전용 구조 대신 `currentStage`, `nextStage` 기반으로 재구성한다.

권장 내부 상태:

```csharp
enum TaskStage
{
    Idle,
    Coordinator,
    Implementer,
    HighLevel,
    Judge
}

TaskStage CurrentStage;
TaskStage? NextStage;
```

UI의 카드 색/테두리/화살표는 모두 이 상태에서 계산한다. `TaskDirection.Text` 같은 개별 문자열 변경을 UI 상태의 원본으로 삼지 않는다.

### 11-UI-B-D — AI 설정과 메인 카드 연결

현재 설정창에는 이미 coordinator / implementer / high-level / judge 역할별 provider/model/reasoning과 enable 상태가 있다. 새로운 별도 설정 저장소를 만들지 말고 기존 `WorkerTargetSettings` / Effective role 값을 그대로 사용한다.

메인 카드 표시 매핑:

```text
설계 관제     = EffectiveCoordinator
작업          = EffectiveImplementer
고수준 작업   = EffectiveHighLevel
판정          = EffectiveJudge
```

provider에 따라 아이콘을 선택하고, 메인 화면에서는 provider 이름보다 사용자가 설정한 **모델명**을 우선 표시한다.

예:

```text
설계 관제   GPT-6 Sol
작업        GPT-6 Luna
고수준 작업 GPT-6 Astra
판정        JEV
```

GPT Web coordinator를 선택한 경우에도 별도의 "GPT Web" 메시지 탭을 만들지 않는다. 설계 관제 카드의 아이콘/모델 표시에만 반영한다.

### 11-UI-B-E — MESSAGE를 실시간 대화 뷰가 아닌 이벤트 이력으로 변경

현재 `TaskMessage`, `_taskMessages`, `_messageLogItems`, `AddTaskMessage()`는 실제 prompt/response 본문을 메시지 UI에 노출하는 방향이다. 새 메인 화면에서는 이 영역을 **대화 내용 요약창으로 사용하지 않는다.**

이유: Worker가 모든 외부 AI 화면을 실시간으로 계속 감시하는 구조가 아니므로, 사용자가 메인 화면에서 확인해야 할 것은 "무슨 내용이었는가"가 아니라 **어떤 주요 데이터/결과가 언제 도착했는가**다.

새 표시 예:

```text
10:24:18  설계 관제   요청 수신       1건 · 3.2 KB
10:25:42  작업        결과 수신       4 files · 18.6 KB
10:26:10  고수준 작업 검토 결과 수신  9.4 KB
10:27:31  판정        판정 결과 수신  PASS · 2.1 KB
```

권장 이벤트 모델:

```csharp
record WorkerHistoryEvent(
    DateTimeOffset Timestamp,
    TaskStage SourceStage,
    string EventType,
    string Title,
    long? SizeBytes,
    int? ItemCount,
    int? FileCount,
    string? Status,
    string? ReferenceId);
```

초기 이벤트 타입은 과도하게 늘리지 않는다.

```text
TASK_STARTED
REQUEST_RECEIVED
RESULT_RECEIVED
FILES_CHANGED
VALIDATION_RECEIVED
TASK_FINISHED
TASK_FAILED
```

원본 prompt/response/transcript는 내부 기록/디버깅/기존 export 용도로 유지할 수 있지만, 메인 이력 목록에는 전문이나 4,000자 요약을 표시하지 않는다.

기존 `AddTaskMessage()` 호출을 즉시 모두 없애지 말고:

```text
internal transcript
+
user-facing HistoryEvent
```

두 층으로 분리한다. 사용자 화면은 `ObservableCollection<WorkerHistoryEvent>`에 바인딩한다.

### 11-UI-B-F — 이력 목록 UI

승인 이미지의 넓은 리스트 형태를 그대로 따른다.

각 행은:

```text
[역할 아이콘] [단계명]
              [시각]
              [이벤트 제목 / 주요 정보]
```

또는 동일 너비 안에서 2열/3열 정렬해도 되지만, 메시지 전문이 차지하던 영역은 **파일 수, payload 크기, 결과 상태, 도착 여부** 표시를 위해 사용한다.

필수:

- 최신 이벤트가 위에 오도록 한다.
- 스크롤 가능해야 한다.
- 각 행 높이는 일정하게 유지한다.
- Codex/GPT Web 필터 탭은 만들지 않는다.
- source는 서비스 이름이 아니라 `설계 관제 / 작업 / 고수준 작업 / 판정 / 시스템` 역할명으로 표시한다.
- 필요하면 행 클릭 시 tooltip 또는 작은 상세 panel로 reference id/파일 목록을 보여줄 수 있지만 첫 구현에서는 필수가 아니다.

### 11-UI-B-G — 실행 버튼과 기존 RunTask 로직 분리

현재 `RunTask_Click`은 `CommandInput.Text`, `WebInstructionInput.Text`, 메인 ModelCombo/ReasoningCombo에 직접 의존한다. 새 승인 화면에서는 이 컨트롤들이 보이지 않으므로 **시각 컨트롤을 숨긴 채 참조하는 식으로 구현하지 않는다.**

먼저 실행 요청 모델을 분리한다.

권장:

```csharp
record TaskLaunchRequest(
    string Prompt,
    string? WebInstruction,
    string WorkingDirectory,
    string? SessionId);
```

그리고:

```text
Run button
→ BuildTaskLaunchRequest()
→ Preflight
→ 기존 coordinator-first / legacy 실행 엔진
```

형태로 만든다.

모델/추론은 메인 콤보에서 읽지 않고 역할 설정의 Effective role에서 가져온다.

중요:

- 승인 이미지에 없는 숨은 `CommandInput`을 source of truth로 남기지 않는다.
- 실행할 명령이 없으면 실행 버튼을 disabled 처리한다.
- 명령 입력/생성 방식은 coordinator-first 흐름과 이후 별도 지시에 맞춰 연결하되, 이번 시각 개편에서 임의의 새 팝업이나 shell 입력창을 추가하지 않는다.
- 기존 Cancel 동작은 실행 중 `실행` 버튼을 `취소`로 바꾸는 방식으로 보존할 수 있다.

### 11-UI-B-H — 기존 기능 보존 경계

이번 메인 UI 개편에서 아래는 회귀시키지 않는다.

- 설정 아이콘 / 설정 popup.
- popup modal overlay와 Escape/닫기 동작.
- 프로젝트/작업 폴더/서버 주소 표시 및 설정.
- 서버 online/offline 확인.
- Codex CLI capability/model/reasoning enum.
- 역할별 thread 선택 및 working directory 연결.
- coordinator-first 실행 엔진.
- high-level / JEV enable 설정.
- JEV JSON endpoint 테스트 및 결과 표시.
- bridge / GPT Web binding 내부 기능.
- task cancel.
- 내부 transcript/export 기능.

단, 메인 화면에서 서비스별 메시지 탭과 raw command 조작부를 노출하지 않는 것은 의도된 변경이다.

### 11-UI-B-I — 구현 순서

한 번에 로직과 디자인을 모두 바꾸지 않는다. 아래 순서를 지킨다.

```text
A1. XAML 메인 레이아웃을 승인 이미지와 동일하게 변경
    - Header
    - 5개 Current Task 카드
    - History 리스트
    - 우하단 실행 버튼
    - 설정 버튼 유지

A2. 더미 TaskStage 값으로 카드 컬러/그레이스케일/화살표 검증
    - Idle
    - Coordinator
    - Implementer
    - HighLevel
    - Judge
    - optional stage disabled

A3. 설정값을 각 카드 아이콘/모델명에 연결

A4. 기존 실행 코드에서 UI 컨트롤 직접 의존 제거
    - TaskLaunchRequest
    - Effective role settings
    - Run/Cancel 연결

A5. 실행 엔진의 상태 전환을 TaskStage에 연결
    - coordinator 시작/완료
    - implementer 시작/완료
    - high-level optional
    - judge optional

A6. 기존 message UI 대신 HistoryEvent 연결
    - 수신 시각
    - 역할
    - event type
    - bytes/files/count/status

A7. 기존 transcript/export/bridge/JEV 회귀 테스트
```

### 11-UI-B-J — 검증 기준

자동 검증:

```powershell
dotnet build ProjectHub.sln --configuration Debug --no-restore
dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore
git diff --check
```

UI 수용 검증은 반드시 실제 Explorer 실행본에서 수행한다.

확인 항목:

```text
1. 좌측 메뉴가 없고 승인 이미지와 같은 단일 화면인지
2. 우측 상단 설정 아이콘이 동작하는지
3. 현재 작업 5개 카드가 한 줄 고정인지
4. 현재/다음 1~2개만 컬러이고 나머지는 그레이스케일인지
5. 각 카드의 아이콘/모델명이 설정값과 일치하는지
6. 실제 진행 구간의 화살표만 애니메이션 되는지
7. MESSAGE의 Codex/GPT Web 탭이 사라졌는지
8. 이력이 본문 요약이 아니라 도착 이벤트/크기/파일수/상태 중심인지
9. COMMAND 입력 영역이 메인에서 사라졌는지
10. 우하단 실행 버튼이 존재하고 기존 preflight/run/cancel 흐름과 연결되는지
11. 설정창의 모델/추론/thread/JEV test 기능이 회귀하지 않았는지
```

의미 있는 UI와 실행 상태 연결까지 구현되면 다음 Codex 작업에서 JEV 검증을 우선 요청한다. JEV에는 적어도 **단계 전환 상태, optional high-level/judge skip, 실행 버튼 preflight, HistoryEvent 누락/중복**을 판정 항목으로 전달한다.


## 최신 확인

```text
9f05190dedaee23f13808d04e2bd995eca68ac59
Add ProjectHub conversation handoff
```

현재 07 배포 패키지 작업과 기존 ProjectHub 정책은 그대로 유지한다. 아래 내용은 **GPTWeb-Hub 브라우저 확장과 Local Worker 연동을 위한 신규 작업지시**다. 기존 Force Restore / Large Data 안정화 항목을 다시 되돌리지 말고, 확장 기능은 ProjectHub 핵심 로직과 분리된 어댑터 계층으로 구현한다.

# GPTWeb-Hub 목표

GPTWeb-Hub는 ChatGPT Web을 ProjectHub Worker와 연결하는 최소 브리지다.

확장 프로그램 자체가 작업 이력, 로그, Git 상태, 재시도 정책의 원본이 되어서는 안 된다.

```text
ProjectHub Worker
    ↕
GPTWeb-Hub Extension
    ↕
ChatGPT Web conversation
```

책임 분리:

```text
Worker
- 작업 큐와 상태의 원본
- Codex CLI 실행/결과 수집
- Git commit/push
- project/conversation binding 영구 저장
- exclusive lock / lease
- 재시작 복구
- 로그/이력

Extension
- Worker 연결
- 현재 ChatGPT conversation 식별
- binding 조회/초기 연결 UI
- Worker가 지정한 Web 작업만 입력
- ChatGPT 응답 생성 종료 감지
- 최종 응답 수집 후 Worker 반환
- Worker가 제공한 상태를 UI에 표시
```

확장 프로그램은 임의 판단으로 다른 작업을 시작하지 않는다.

# 1. 최종 UI

ChatGPT 페이지 우측 빈 공간에 **폭이 작은 고정형 패널**로 배치한다. 별도 로그 뷰, 상세 콘솔, 복잡한 버튼은 두지 않는다.

권장 형태:

```text
┌─────────────────────────────────┐
│ GPTWeb-Hub                      │
│                                 │
│ Project  MCP-with-MiniPC ● READY│
│ Worker   ProjectHub       ● READY│
│ Web      Connected        ● READY│
│ System   정상                     │
├─────────────────────────────────┤
│ CURRENT REQUEST                 │
│                                 │
│ ● WORKER → GPT WEB              │
│ Force Restore 결과 검토 요청     │
│                                 │
│ ChatGPT 응답 생성 중             │
└─────────────────────────────────┘
```

UI 원칙:

- 두께감 있는 외곽 테두리를 사용한다.
- 전체 패널은 간결하게 유지한다.
- 상단을 Header로 사용한다.
- Header에는 Project / Worker / Web 상태를 최대한 위쪽에 몰아서 표시한다.
- 각 상태는 텍스트와 작은 status dot을 함께 사용한다.
- System message는 별도 카드/영역을 만들지 않는다.
- Header 하단에 한 줄만 사용한다.
- 정상 시 짧게 `정상` 또는 빈 값으로 표현한다.
- 경고/오류가 있으면 그 한 줄에 원인만 표시한다.
- 로그는 Extension에 표시하지 않는다. 로그는 Worker 책임이다.
- CURRENT REQUEST는 Header 바로 아래에 붙인다.
- Request 상세 메타데이터를 여러 줄로 늘어놓지 않는다.
- 나머지 공간은 현재 흐름/진행 상태를 짧게 보여주는 데 사용한다.
- 초기 버전에는 수동 제어 버튼을 최소화하거나 두지 않는다.

# 2. Header 상태 의미

Header의 3개 READY는 서로 독립된 조건이다.

## Project

현재 ChatGPT conversation에 ProjectHub project binding이 존재하고 Worker가 해당 project를 정상 인식하면 READY.

## Worker

Extension이 Local Worker와 통신 가능하고 인증/세션이 유효하면 READY.

## Web

현재 페이지가 유효한 ChatGPT conversation이며 저장된 binding과 일치하고, 자동 입출력을 수행할 수 있는 상태이면 READY.

전체 작업 가능 상태는 아래 조건이 모두 만족되어야 한다.

```text
Project READY
AND Worker READY
AND Web READY
AND exclusive task conflict 없음
= 작업 가능
```

하나라도 실패하면 자동 입력을 시작하지 않는다.

예:

```text
System   Worker disconnected
System   Project mismatch
System   Conversation not bound
System   Task already claimed
System   Response timeout
```

# 3. Conversation별 binding

Binding은 **브라우저 탭이 아니라 ChatGPT conversation별**로 관리한다.

```text
Chat A ↔ Project A
Chat B ↔ Project B
Chat C ↔ unbound
```

초기 연결 흐름은 Web → Worker다.

```text
현재 ChatGPT conversation
→ Extension
→ Worker의 관리 project 목록 조회
→ 사용자가 project 선택
→ 현재 conversation과 project binding 저장
```

처음 방문한 conversation에서만 사용자가 연결한다.

연결 전 Web 행은 다음과 같이 동작할 수 있다.

```text
Web   [이곳을 프로젝트와 연결합니다]
```

한 번 binding된 conversation은 Worker에 영구 저장한다.

권장 최소 정보:

```text
conversation_id
conversation_url
project_id
worker_id
bound_at
last_seen
```

`tab_id`는 재시작 시 변경될 수 있으므로 영구 binding key로 사용하지 않는다. 현재 열린 탭을 추적하는 임시 값으로만 사용한다.

브라우저/PC 재시작 후 기존 conversation으로 돌아오면:

```text
conversation_id 식별
→ Worker binding 조회
→ 기존 project 자동 복원
→ 재설정 없이 READY
```

다른 conversation 페이지로 이동하면 그 페이지는 별도 binding을 갖는다. 기존 binding 페이지로 복귀하면 별도 사용자 작업 없이 이어서 사용할 수 있어야 한다.

# 4. CURRENT REQUEST 4상태

CURRENT REQUEST는 Web 전용 progress가 아니라 **Worker와 Web이 공유하는 하나의 task 흐름**을 표시한다.

UI와 내부 상태를 아래 4개로 통일한다.

```text
IDLE
WEB_TO_WORKER
WORKER_TO_WEB
FINISHED
```

표시 의미:

### 1. IDLE

```text
● 작업 없음
현재 처리할 요청 없음
```

### 2. WEB_TO_WORKER

```text
● GPT WEB → WORKER
Web 결과를 Worker에 전달/처리 중
```

Web이 생성한 피드백을 수집해 Worker가 다음 처리(Codex 재작업, Git 처리 등)를 수행하는 방향이다.

### 3. WORKER_TO_WEB

```text
● WORKER → GPT WEB
Worker 요청을 GPT Web에서 처리 중
```

Worker가 지정한 요청만 Extension이 현재 bound conversation에 입력한다.

세부 진행 문구는 한 줄이면 충분하다.

예:

```text
요청 준비 중
ChatGPT에 요청 전달 중
ChatGPT 응답 생성 중
응답 수집 중
Worker로 전달 중
```

### 4. FINISHED

정상 성공, 사용자 승인 필요, 오류를 UI 상태로 세분화하지 않고 모두 FINISHED로 통합한다.

```text
● 작업 종료
정상 완료
```

또는:

```text
● 작업 종료
사용자 승인 필요
```

또는:

```text
● 작업 종료
오류 발생 · 사용자 확인 필요
```

종료 원인은 Worker의 `finish_reason/message`를 한 줄로 표시한다.

# 5. 작업 실행 권한 / 동시 작업 제한

동시 작업 제어의 최종 권한은 Worker가 가진다.

Extension의 DOM 상태만으로 lock을 판단하지 않는다.

Worker task는 최소 다음 개념을 가진다.

```text
task_id
project_id
owner
lease_id
status
request
response
conversation_id
created_at
started_at
completed_at
```

실행 대상이 Web이면 Worker가 명시적으로 owner/lease를 부여한다.

```text
owner = WEB
conversation_id = <bound conversation>
lease_id = <unique>
```

Extension은 다음 조건을 모두 만족할 때만 ChatGPT 입력창을 조작한다.

```text
1. Worker connected
2. project binding matched
3. conversation_id matched
4. owner == WEB
5. valid lease
6. 다른 active task 없음
7. ChatGPT Web가 현재 입력 가능한 상태
```

하나라도 만족하지 않으면 작업하지 않는다.

여러 ChatGPT 탭이 같은 conversation 또는 project를 열고 있어도 하나의 lease만 claim 가능해야 한다. 다른 탭은 읽기/표시만 하고 입력하지 않는다.

# 6. Worker 연결과 복구

연결은 끊기지 않는 것을 전제로 하지 말고 **자동 재연결**을 전제로 한다.

- Worker는 Windows 시작 시 자동 실행 가능하도록 한다.
- Extension service worker가 중단/재시작되어도 binding/task 상태를 잃지 않는다.
- 상태의 원본은 Worker의 persistent storage다.
- Extension은 시작/탭 활성화/페이지 변경 시 Worker에 다시 연결하고 현재 conversation 상태를 조회한다.
- PC/브라우저 재시작 후 사용자가 binding을 다시 설정하게 하지 않는다.
- 초기 PoC는 localhost HTTP polling으로 충분하다.
- 이후 필요하면 WebSocket/SSE로 교체하되 계약은 유지한다.

초기 최소 API 예:

```text
GET  /bridge/status
GET  /bridge/bindings/{conversationId}
POST /bridge/bind
GET  /bridge/task
POST /bridge/task/{id}/claim
POST /bridge/task/{id}/result
POST /bridge/heartbeat
```

실제 endpoint 명칭은 기존 Server/Worker 구조와 충돌하지 않게 조정해도 된다.

# 7. ChatGPT Web 입출력

Extension은 content script에서 현재 conversation DOM만 담당한다.

필요 기능:

```text
- conversation 식별
- 입력 가능 여부 확인
- Worker 요청을 composer에 입력
- send
- 응답 생성 시작 감지
- 응답 생성 종료 감지
- 마지막 assistant 응답 수집
- task_id/lease와 함께 Worker에 반환
```

응답 완료 판단은 단순 timeout이나 DOM 무변화만 사용하지 않는다. 가능한 경우 생성 중 UI/Stop 상태와 assistant message 변화를 함께 사용한다.

ChatGPT DOM selector는 Worker나 Server 코드에 넣지 않는다. UI 변경 대응은 Extension 어댑터 내부로 격리한다.

# 8. Codex / Git 연계 원칙

Codex CLI 작업 실행, 결과 수집, Git commit/push는 Extension이 하지 않는다.

```text
Codex CLI
→ Worker가 stdout/JSON 결과 수집
→ 변경/검증 결과 정리
→ 필요한 Git commit/push
→ Web 검토가 필요하면 WORKER_TO_WEB task 생성
→ Extension이 bound conversation에서 처리
→ Web 결과를 Worker에 반환
→ 필요 시 다음 Codex task
```

Git 연동이 없는 테스트 프로젝트에서도 Extension/Worker 연결 자체는 동작할 수 있어야 한다. Git 작업은 해당 프로젝트의 capability가 있을 때만 수행한다.

# 9. 최소 PoC 검증 순서

기존 ProjectHub 07 작업을 방해하지 않는 작은 테스트 프로젝트/격리된 경로에서 먼저 검증한다.

```text
1. Extension 설치 후 Worker READY 확인
2. 처음 열린 ChatGPT conversation에서 project 선택/bind
3. 페이지 새로고침 후 binding 자동 복원
4. 다른 conversation으로 이동 → unbound 확인
5. 기존 conversation 복귀 → 자동 READY
6. Worker가 WORKER_TO_WEB 테스트 요청 1건 생성
7. Extension이 지정된 conversation에만 요청 입력
8. ChatGPT 응답 완료 감지
9. 응답을 Worker에 반환
10. CURRENT REQUEST가 WEB_TO_WORKER → FINISHED로 전환
11. 같은 task를 다른 탭에서 동시에 실행하지 못하는지 확인
12. 브라우저/PC 재시작 후 별도 재설정 없이 binding 복구 확인
```

PoC 성공 후 Codex CLI 결과와 연결한다.

# 구현 우선순위

첫 구현은 아래 범위만 수행한다.

```text
A. Worker 연결 + conversation binding 영구 복구
B. 최종 Header / System 한 줄 / CURRENT REQUEST 4상태 UI
C. Worker → Web 요청 1건 자동 입력 및 응답 반환
D. exclusive lease로 중복 실행 차단
```

로그 뷰, 상세 설정 페이지, 다중 사용자, 복잡한 알림, 자동 conflict 해결, 임의 shell 실행 등은 이번 범위에 넣지 않는다.


---

# ProjectHub Worker 신규 프로젝트 작업지시

아래 작업은 GPTWeb-Hub와 연결될 **Windows 데스크톱 Worker 애플리케이션**을 만드는 신규 범위다.

사용자가 별도로 전달하는 Worker UI 이미지를 **시각적 기준(source of truth)** 으로 사용한다. 이 문서에서는 기능/구조/동작만 정의하며, 전달된 UI 이미지가 있으면 레이아웃·간격·색·카드 구성·버튼 배치는 그 이미지를 우선한다.

기존 `ProjectHub.Agent`를 Worker로 개조하지 않는다. Agent는 기존 관찰/ProjectHub 관리 역할을 유지한다.

## 1. 프로젝트 형태

새 프로젝트를 솔루션에 추가한다.

권장:

```text
src/
├─ ProjectHub.Core
├─ ProjectHub.Infrastructure
├─ ProjectHub.Server
├─ ProjectHub.Agent
└─ ProjectHub.Worker
```

`ProjectHub.Worker`는 Windows 전용 데스크톱 앱으로 만든다.

초기 구현은 **WPF + .NET 9** 를 우선 사용한다.

이유:

```text
- Windows tray/background 실행 구현이 단순함
- 1024x768 고정 기준 UI 구현이 쉬움
- 기존 .NET 9 솔루션과 통합이 쉬움
- Codex CLI process 제어와 stdout/stderr 수집이 쉬움
- localhost bridge 및 persistent state 구현이 쉬움
```

필요하면 `System.Windows.Forms.NotifyIcon`을 WPF에서 사용해 tray 기능을 구현한다.

## 2. Worker의 역할

Worker는 자동화 흐름의 **실제 시작점이자 상태의 원본**이다.

```text
사용자 Command
      ↓
ProjectHub Worker
      ↓
Codex CLI
      ↓
결과 수집 / Git 처리
      ↓
필요 시 GPT Web 검토 요청
      ↓
GPTWeb-Hub Extension
      ↓
ChatGPT Web
      ↓
Web 결과
      ↓
Worker
      ↓
다음 Codex 작업 또는 종료
```

Extension은 UI adapter이고, task queue / lock / history / recovery의 원본은 Worker다.

## 3. 메인 UI 기능

메인 윈도우는 **1024 x 768 기준**으로 구성한다.

사용자가 제공한 최신 UI 이미지를 그대로 참고하되 기능적으로 아래 영역을 갖는다.

### Header

표시:

```text
ProjectHub Worker (WORKSTATION_NAME)     ● READY
[Codex Model ▼] [Reasoning ▼]            ⚙
```

필수 동작:

- Worker 전체 READY 상태
- workstation 이름
- Codex CLI 현재 모델
- Codex reasoning effort
- Model dropdown 변경 가능
- Reasoning dropdown 변경 가능
- 설정 아이콘은 존재하되 상세 Settings 요구사항은 별도 지시 전까지 최소 placeholder로 둔다.

모델/추론 선택값은 화면 표시용이 아니라 **다음 Codex CLI 실행에 실제 적용**돼야 한다.

## 4. 연결 상태 카드

메인 상단에 다음 상태를 표시한다.

```text
Project
- project name
- local path
- READY / NOT READY

GPT Web
- bound ChatGPT conversation title
- READY / NOT READY

Server
- ProjectHub Server
- READY / NOT READY

System
- overall runtime health
- READY / warning/error
```

GPT Web은 가능하면 URL/ID보다 **conversation title을 우선 표시**한다.

예:

```text
GPT Web
MCP 프로젝트 진척도 확인
● READY
```

conversation ID / URL은 내부 binding용이며 메인 화면에 노출할 필요 없다.

## 5. CURRENT TASK + TASK FLOW 통합

별도 두 카드로 나누지 않는다.

하나의 넓은 task 카드에서 현재 요청과 흐름을 같이 보여준다.

예:

```text
CURRENT TASK

● WORKER → GPT WEB
Force Restore 결과 검토 요청

CODEX       WORKER       GPT WEB
  ○ ---------- ● ---------- ●

ChatGPT 응답 생성 중
Round 2 / 3
00:01:42
```

내부 구현에는 owner/lease가 있어도 UI에는 `Owner:` 같은 기술 필드를 표시하지 않는다.

사용자에게 보이는 상태는 명확한 방향/행동 중심으로 표시한다.

권장 표시 상태:

```text
● 작업 없음
● CODEX 작업 중
● WORKER → GPT WEB
● GPT WEB → WORKER
● 작업 종료
```

세부 문구 예:

```text
Codex CLI 실행 중
Codex 결과 정리 중
GPT Web에 검토 요청 전달 중
ChatGPT 응답 생성 중
Web 결과 수집 중
Web 결과를 Worker에서 처리 중
정상 완료
사용자 승인 필요
오류 발생 · 확인 필요
```

## 6. LAST RESULT

Codex와 Web 결과를 동시에 좌우 분할해서 보여주지 않는다.

상단에 선택 버튼/탭만 둔다.

```text
[ Codex ] [ GPT Web ]
```

선택된 한 쪽의 결과를 **넓은 단일 영역**에 표시한다.

### Codex 탭 예

```text
Build PASS · Test PASS · Commit a5177f8
2026-09-18 14:32

Force Restore 관련 코드 수정 완료.
빌드/테스트 성공.
commit/push 완료.
```

### GPT Web 탭 예

```text
REVISE
2026-09-18 14:35

보호영역 검증 필요.
...
```

초기 선택은 가장 최근에 갱신된 결과 탭으로 한다.

전체 원본 로그를 이 영역에 넣지 않는다. 결과 summary와 필요한 상세만 표시한다.

## 7. COMMAND

Worker가 실제 작업의 시작점이므로 자연어 입력을 반드시 지원한다.

```text
COMMAND

┌────────────────────────────────────────────┐
│ 작업 지시 입력                            │
│                                            │
└────────────────────────────────────────────┘

                         [ Clear ] [ Run Task ]
```

`Run Task`:

```text
1. 새로운 task 생성
2. exclusive execution 확보
3. 현재 선택 Model / Reasoning 값 확정
4. Codex CLI 실행
5. stdout/stderr/JSON event 수집
6. 결과 상태 갱신
```

Command 영역은 raw shell console이 아니다.

사용자는 자연어 작업 지시를 입력하고 Worker가 Codex CLI invocation으로 변환한다.

임의 PowerShell/cmd 명령을 사용자 입력 그대로 shell에 실행하는 기능은 만들지 않는다.

## 8. Codex CLI 실행

기존 테스트에서 확인한 Desktop bundled CLI 탐색 방식을 사용할 수 있다.

Windows에서 기본 자동 탐색 후보:

```powershell
Get-ChildItem "$env:LOCALAPPDATA\OpenAI\Codex\bin" -Filter codex.exe -Recurse -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
```

hash 디렉터리를 하드코딩하지 않는다.

Worker는 최소 다음 정보를 수집한다.

```text
codex executable path
cli version
model
reasoning effort
session/thread id when available
start/end time
exit code
stdout
stderr
token usage when available
result summary
changed files when available
```

가능하면 structured output / JSON event mode를 사용한다.

## 9. 모델 / Reasoning

Header dropdown으로 변경 가능해야 한다.

Worker 내부 설정 예:

```text
CodexModel
CodexReasoningEffort
```

실행 직전에 현재 UI 값을 snapshot하여 task에 저장한다.

실행 중인 task의 모델/추론값은 중간 변경하지 않는다.

사용자가 dropdown을 바꾸면 **다음 task부터 적용**한다.

CLI가 해당 model/reasoning 값을 거부하면 task를 시작하지 말고 System 상태에 명확히 표시한다.

지원 모델 목록을 코드에 영구 고정하지 않는다. 가능한 경우 CLI/config에서 확인하거나 설정 가능한 목록으로 격리한다.

## 10. Background / Tray

Worker는 메인 창이 닫혀도 작업을 계속할 수 있어야 한다.

필수:

```text
- minimize to tray
- window close → 기본적으로 tray로 숨김
- tray 상태에서도 active task 계속 수행
- tray 상태에서도 Extension bridge 응답
- tray icon에서 Open / Pause / Exit 제공
- 명시적 Exit에서만 process 종료
```

PC 재부팅 뒤 자동 실행은 Settings 범위와 연결하되, startup registration 구현은 설정 요구사항 확정 후 마무리해도 된다.

단, 현재 architecture는 재시작 복구를 전제로 작성한다.

## 11. 상태 저장

Worker process memory만 믿지 않는다.

초기 persistent storage는 간단한 JSON 또는 SQLite 중 구현 복잡도가 낮은 쪽을 선택할 수 있다.

최소 저장:

```text
worker identity
known projects
conversation bindings
selected/default model
selected/default reasoning
task queue
active task state
lease
last Codex result
last Web result
round
timestamps
```

secret/token은 평문 state 파일에 저장하지 않는다.

## 12. 단일 실행 / 동시성

한 Worker 인스턴스에서 동시에 여러 automation task를 실행하지 않는다.

초기 버전 정책:

```text
Active task = 최대 1개
```

새 command가 들어왔는데 active task가 있으면:

```text
- 즉시 병렬 실행 금지
- queue 또는 busy 거부 중 하나를 명확하게 선택
- 초기 구현은 queue 1개 이상보다 "현재 작업 종료 후 실행" FIFO가 바람직
```

Codex/Web 실행 대상은 Worker가 가진 lease가 결정한다.

Worker가 Web 차례라고 지정하지 않으면 Extension은 절대 ChatGPT에 입력하지 않는다.

## 13. Git 처리

Git commit/push는 Worker가 담당할 수 있지만 기존 ProjectHub 안전 규칙을 그대로 따른다.

자동으로 다음 상황을 해결하지 않는다.

```text
detached HEAD
merge/rebase in progress
conflict
push reject
dirty 상태의 위험한 pull
```

해당 상황은 task 종료/개입 필요 상태로 올린다.

Git 저장소가 아닌 프로젝트에서도 Worker와 Codex 실행 자체는 가능해야 한다.

따라서 capability를 구분한다.

```text
GitAvailable = true/false
WebBound = true/false
CodexAvailable = true/false
ServerAvailable = true/false
```

## 14. Extension bridge

Worker는 GPTWeb-Hub Extension용 localhost bridge를 제공한다.

Extension용 계약은 기존 GPTWeb-Hub 지시를 따른다.

Worker가 source of truth다.

최소 요구:

```text
status
project list
conversation binding
pending Web task
claim/lease
Web result submit
heartbeat/reconnect
```

초기 구현은 localhost HTTP polling으로 충분하다.

bridge는 loopback에만 bind하고 외부 LAN에 열지 않는다.

## 15. 설정 아이콘

메인 UI에는 Settings 아이콘을 배치한다.

이번 단계에서는 설정창 전체 기능을 임의로 설계하지 않는다.

최소 placeholder 또는 기본 설정창 shell만 만든다.

후속 요구사항에서 다음을 별도로 확정한다.

```text
Windows startup
tray policy
ProjectHub Server URL
Codex CLI path
default model/reasoning
automation rounds
Git behavior
Extension bridge
storage/recovery
```

## 16. 구현 단계

한 번에 전체 자동화 루프를 만들지 않는다.

### Worker-A — 프로젝트/메인 UI skeleton

```text
- ProjectHub.Worker 프로젝트 생성
- WPF 1024x768 main window
- 사용자 제공 UI 이미지 반영
- header/model/reasoning selectors
- Project/Web/Server/System cards
- integrated Current Task/Task Flow
- Last Result Codex/GPT Web toggle
- Command input
- tray/background skeleton
```

이 단계에서는 mock/demo state 사용 가능.

### Worker-B — Codex CLI

```text
- codex.exe auto-discovery
- version/status
- model/reasoning 적용
- command → codex exec
- async process
- stdout/stderr/JSON result
- cancel/exit handling
- Last Result 갱신
```

### Worker-C — persistent state / exclusive task

```text
- task state machine
- one active task
- persistent state
- restart recovery
- queue
- lease
```

### Worker-D — GPTWeb-Hub bridge

```text
- localhost bridge
- project list
- conversation binding
- WORKER_TO_WEB request
- WEB_TO_WORKER result
- reconnect
```

### Worker-E — Git

```text
- Git capability detect
- safe commit/push flow
- failure/approval state
- Codex result + Git result summary
```

각 단계가 독립적으로 빌드/실행 가능해야 한다.

## 17. 첫 작업 범위

**지금은 Worker-A부터 시작한다.**

Codex CLI, Extension, Git 자동화를 동시에 구현하지 않는다.

Worker-A 완료 기준:

```text
1. ProjectHub.sln에 ProjectHub.Worker 포함
2. dotnet build 성공
3. 1024x768 UI 실행
4. 제공 UI 이미지와 주요 구조 일치
5. Model / Reasoning dropdown 동작
6. Result Codex / GPT Web 전환 동작
7. Command 입력 / Clear / Run Task UI 동작
8. Run Task는 아직 mock task transition이어도 됨
9. 창 닫기 시 tray 숨김
10. tray Open / Exit 동작
11. 기존 Server/Agent/Core 동작 회귀 없음
```

검증 후 Worker-B로 진행한다.


---

# 2026-09-19 최신 상태 확인 및 Worker 진행 표시 피드백

## 최신 확인

현재 `main`의 최신 확인 커밋:

```text
7c9cfbf63db77a42b52c4adfb3e9d48afe8e06ce
Allow explicitly approved direct Git sync
```

직전 Worker 구현 커밋:

```text
624e22585eb16aceb66e2059e91c0dfe97bd473b
Implement Worker dashboard UI and icon flow
```

현재 확인된 Worker-A 상태:

```text
- src/ProjectHub.Worker WPF / net9.0-windows 프로젝트 생성 완료
- ProjectHub.sln 포함 완료
- tray 숨김 / Open / Pause / Exit skeleton 구현
- 메인 UI 및 Command / Result toggle mock 구현
- Codex / Worker / GPT Web 단계별 아이콘 적용
- 활성 단계는 컬러, 비활성 단계는 grayscale 처리
- 단계 사이 진행 표시를 >>> 형태로 변경
- >>> 우측 이동 + 점멸 애니메이션 구현
- mock task에서 Codex → Worker → GPT Web 순으로 활성 단계 전환
- Worker-A build/test 통과
- Worker-B(Codex CLI 실제 연결)는 아직 후속 범위
```

따라서 `>>>` 진행 표시는 새로 만드는 작업이 아니라 **현재 구현을 유지·정리하는 요구사항**으로 취급한다.

## Worker Task Flow 진행 표시 최종 원칙

Task Flow는 이미지 파일을 별도 애니메이션 자산으로 만드는 방식보다 WPF 벡터/텍스트 애니메이션으로 유지한다.

기본 표현:

```text
CODEX   >>>   WORKER   >>>   GPT WEB
```

현재 실행 방향에 해당하는 구간만 애니메이션한다.

예:

```text
Codex 작업 결과를 Worker가 받는 중
CODEX   >>>   WORKER   ---   GPT WEB

Worker가 Web에 요청하는 중
CODEX   ---   WORKER   >>>   GPT WEB

Web 결과가 Worker로 돌아오는 중
CODEX   ---   WORKER   <<<   GPT WEB
```

UI 원칙:

```text
- 활성 진행 구간: 파란색 계열 + 순차 점등/이동
- 비활성 구간: 회색 고정
- 완료된 단계: 컬러 아이콘 유지 가능
- 아직 실행되지 않은 단계: grayscale
- 오류/승인 필요는 화살표 애니메이션을 멈추고 종료 상태로 전환
- 애니메이션은 상태 표현용이며 task state 자체의 원본이 되어서는 안 됨
```

추천 애니메이션은 세 개의 `>`가 왼쪽에서 오른쪽으로 순차적으로 강조되는 방식이다.

```text
>..
>>.
>>>
.>>
..>
(repeat)
```

단순 opacity 변화 또는 짧은 translate 효과만 사용하고, CPU를 지속적으로 많이 사용하는 애니메이션은 피한다. Tray/background 상태에서는 메인 창이 숨겨져 있으면 시각 애니메이션을 중단해도 되며 Worker 실제 작업은 계속 진행한다.

## 현재 구현과 맞춰야 할 상태 전환

Worker-A mock의 시각 흐름은 향후 실제 task state와 아래처럼 연결한다.

```text
IDLE
  ↓
CODEX_RUNNING
  ↓
CODEX_TO_WORKER
  ↓
WORKER_TO_WEB
  ↓
WEB_RUNNING
  ↓
WEB_TO_WORKER
  ↓
FINISHED
```

메인 UI에는 내부 상태 이름을 그대로 노출할 필요 없다.

사용자 표시 예:

```text
● CODEX 작업 중
● CODEX → WORKER
● WORKER → GPT WEB
● GPT WEB → WORKER
● 작업 종료
```

Task Flow의 아이콘 활성/비활성 상태와 `>>>` 애니메이션은 반드시 동일 task state에서 파생시킨다. XAML 애니메이션 자체가 별도 상태를 만들지 않는다.

## 현재 정책 변경 반영

최신 `AGENTS.md`는 Git 동작을 더 이상 Explorer CMD에만 한정하지 않고 **사용자의 명시적 승인에 한해 Git commit/push/fetch/pull을 허용**하도록 수정됐다.

따라서 향후 Worker-E에서 Git 연동 시:

```text
- 사용자가 Worker에서 명시적으로 승인한 Git sync는 허용 가능
- detached HEAD / merge-rebase 진행 / conflict / push reject는 자동 해결 금지
- reset / checkout / 원격 shell 자동 실행 금지
```

를 기준으로 한다.

## 다음 구현 범위

Worker-A의 UI/아이콘/flow mock은 현재 단계에서 충분히 구현됐다.

다음은 기존 계획대로 **Worker-B — Codex CLI 실제 연결**을 우선한다.

Worker-B에서 반드시 현재 UI의 mock state를 실제 상태로 치환한다.

```text
- codex.exe 자동 탐색
- 선택된 Model / Reasoning 실제 적용
- async codex exec
- stdout / stderr / JSON event 수집
- CLI model / reasoning / token usage / session id 가능 범위 수집
- Codex 실행 시작 시 CODEX 단계 활성화
- Codex 완료 결과를 Worker가 받는 동안 CODEX >>> WORKER 표시
- Last Result의 Codex 탭을 실제 결과로 갱신
```

GPT Web bridge는 Worker-D 전까지 mock을 유지하며 Worker-B에서 임의로 브라우저 자동화를 함께 구현하지 않는다.


## >>> 진행 표시 시각 강조 수정

기존 `>>>` 구현은 유지하되, **현재 활성 구간이 한눈에 보이도록 더 굵고 크게 표현**한다.

최종 표시 원칙:

```text
CODEX   >>>   WORKER   >>>   GPT WEB
```

활성 구간의 `>>>`는 단순 텍스트가 아니라 **강한 진행 신호**로 보이게 한다.

권장 스타일:

```text
- FontWeight: Bold 또는 ExtraBold
- FontSize: 현재보다 1.3~1.5배 확대
- 활성 색상: 선명한 Blue 계열
- 비활성 색상: 연한 Gray
- 문자 간격을 너무 벌리지 말고 하나의 덩어리처럼 보이게
- 아이콘보다 작게 숨지 않도록 충분한 폭 확보
- 배경 카드 안에서 수직 중앙 정렬
```

예:

```text
CODEX    >>>    WORKER    ---    GPT WEB
         ^^^
         현재 활성 구간은 굵고 선명하게
```

애니메이션은 이전 제안대로 **세 개의 화살표가 왼쪽에서 오른쪽으로 순차 점등되는 방식**으로 한다.

권장 프레임:

```text
>..
>>.
>>>
.>>
..>
(repeat)
```

다만 실제 UI에서는 점(`.`)을 표시하지 말고 opacity 차이로 표현한다.

즉 시각적으로는:

```text
>  >  >
↑
첫 번째 강조

>  >  >
   ↑
두 번째 강조

>  >  >
      ↑
세 번째 강조
```

으로 보이게 한다.

추가 원칙:

```text
- 활성 화살표는 100% opacity
- 나머지 화살표는 약 25~35% opacity
- 각 단계 간격은 약 120~180ms
- 전체 반복은 빠르되 조급해 보이지 않게 약 0.7~1.0초 주기
- 필요하면 2~4px 정도의 짧은 X축 이동을 함께 사용
- Glow/Shadow는 약하게 사용 가능하나 과도한 네온 효과는 금지
- 완료 또는 대기 상태에서는 애니메이션 정지
- 창이 tray/background로 숨겨지면 시각 애니메이션 정지 가능
```

방향 전환 시 문자 방향도 실제 흐름과 일치시킨다.

```text
CODEX → WORKER
CODEX   >>>   WORKER

WORKER → GPT WEB
WORKER   >>>   GPT WEB

GPT WEB → WORKER
WORKER   <<<   GPT WEB
```

중요: `>>>`는 작은 보조 장식이 아니라 **Task Flow에서 가장 먼저 눈에 들어오는 진행 상태 표시**여야 한다. 현재 단계 아이콘 사이에서 충분한 크기와 굵기를 확보한다.


---

# 2026-09-19 GPTWeb-Hub 기능 구체화 — Settings / 연결 설정

## 최신 상태 확인

현재 `main` 최신 확인 커밋:

```text
631c5272f054eca0da09f2fdd9922e2e35bbc379
Finalize Worker bridge and GPTWeb-Hub status UI
```

현재 구현 기준:

```text
- Worker loopback bridge: http://127.0.0.1:43821
- Extension polling: 1.5초
- Project / Worker 값은 bridge에서 실제 값 수신 시 표시
- bridge 단절 시 Project / Worker 마지막 확인값은 유지
- Status만 Disconnected로 전환
- CURRENT REQUEST는 아직 bridge task 표시 중심
- ChatGPT DOM 자동입력 / 응답 수집은 후속 범위
- 톱니바퀴 버튼은 현재 mock 상태 순환 용도
```

사용자가 제공한 현재 UI 시안을 기준으로, 다음 작업은 **톱니바퀴 mock 동작을 제거하고 실제 Settings 창으로 교체**하는 것이다.

## 1. 톱니바퀴 동작 변경

현재:

```text
⚙ 클릭
→ IDLE / WORKER_TO_WEB / WEB_TO_WORKER / FINISHED 상태 수동 순환
```

이 동작은 제거한다.

변경:

```text
⚙ 클릭
→ GPTWeb-Hub Settings 표시
```

상태 전환은 더 이상 설정 버튼이나 사용자의 수동 클릭으로 만들지 않는다.

```text
CURRENT REQUEST 상태
= Worker bridge의 실제 task 상태 + 이후 ChatGPT DOM 상태에서만 결정
```

## 2. Settings UI

메인 패널과 같은 디자인 언어를 사용한 작은 modal/popover 형태로 만든다.

권장 형태:

```text
┌────────────────────────────────────┐
│ GPTWeb-Hub Settings             × │
├────────────────────────────────────┤
│ WORKER BRIDGE                      │
│                                    │
│ Host     [ 127.0.0.1             ] │
│ Port     [ 43821                 ] │
│ BasePath [ /bridge              ] │
│                                    │
│ Worker   [ 자동 감지 / optional ] │
│                                    │
│ [ Test Connection ]                │
│                                    │
│ Status   ● Connected               │
├────────────────────────────────────┤
│                         [Cancel] [Save] │
└────────────────────────────────────┘
```

메인 UI보다 복잡하게 만들지 않는다.

## 3. 설정값

초기 버전에서 실제로 동작해야 할 설정:

```text
bridgeHost
bridgePort
bridgeBasePath
```

기본값:

```text
bridgeHost     = 127.0.0.1
bridgePort     = 43821
bridgeBasePath = /bridge
```

최종 base URL:

```text
http://{bridgeHost}:{bridgePort}{bridgeBasePath}
```

예:

```text
http://127.0.0.1:43821/bridge
```

현재 구현의 하드코딩된 `http://127.0.0.1:43821` 사용부는 전부 이 설정값에서 생성하도록 변경한다.

## 4. Host 보안 정책

초기 버전에서는 기본적으로 loopback만 허용한다.

허용:

```text
127.0.0.1
localhost
::1
```

LAN IP나 외부 URL은 이번 범위에서 허용하지 않는다.

이유:

```text
Worker bridge는 현재 인증 없는 로컬 bridge 설계이며 외부 노출을 전제로 하지 않음
```

Settings에서 외부 주소가 입력되면 Save 전에 validation으로 거부한다.

## 5. Worker Path

사용자가 말하는 "경로"는 별도 필드로 제공할 수 있으나, **현재 Extension 기능에는 필수값이 아니다.**

권장 필드:

```text
Worker executable path
C:\...\ProjectHub.Worker.exe
```

용도:

```text
- 사용자에게 설치 위치 기록
- 향후 Worker 자동 실행 / Native Messaging / custom protocol 연동 준비
```

현재 단계에서는 브라우저 확장이 이 경로의 EXE를 직접 실행하지 않는다.

따라서:

```text
Worker Path = optional
Bridge Host/Port = 실제 연결에 사용
```

으로 명확히 구분한다.

경로 선택 UI는 이번 단계에서는 text input + 저장만 허용해도 충분하다.

## 6. 설정 저장

Extension 설정은 `chrome.storage.local`에 저장한다.

예:

```json
{
  "bridgeHost": "127.0.0.1",
  "bridgePort": 43821,
  "bridgeBasePath": "/bridge",
  "workerPath": ""
}
```

페이지별 project/conversation binding과 bridge 접속 설정은 분리한다.

```text
Extension global settings
- bridge host
- bridge port
- base path
- optional worker path

Conversation binding
- conversation_id
- project_id
- binding state
```

설정 저장 후 페이지 새로고침 없이 polling endpoint가 즉시 새 설정으로 전환되게 한다.

## 7. Test Connection

Settings에 `Test Connection` 버튼을 둔다.

테스트:

```text
GET {baseUrl}/status
```

성공 조건:

```text
HTTP 200
bridge == ready
loopback == true
```

표시:

```text
● Connected
ProjectHub Worker
127.0.0.1:43821
```

실패:

```text
● Disconnected
Worker bridge에 연결할 수 없습니다.
```

에러 원인은 한 줄만 보여준다.

예:

```text
Connection refused
Invalid port
Invalid host
Bridge response invalid
```

전체 stack trace는 Extension UI에 표시하지 않는다.

## 8. Save / Cancel

`Save`:

```text
1. 입력값 validation
2. chrome.storage.local 저장
3. bridge client base URL 재생성
4. 즉시 refreshBridge()
5. Settings 닫기
```

`Cancel`:

```text
현재 입력 변경 폐기
Settings 닫기
```

Save 후 연결 실패 시 메인 패널:

```text
Status  Disconnected
System  Worker bridge에 연결할 수 없습니다.
```

를 즉시 표시한다.

## 9. 메인 패널 상태 의미 유지

현재 UI의 세 행 구조는 유지한다.

```text
Project   MCP-with-MiniPC
Worker    ProjectHub Worker
Status    Connected / Disconnected
```

중요:

```text
Project
= 마지막으로 Worker가 실제 반환한 project

Worker
= 마지막으로 Worker가 실제 반환한 worker name

Status
= 현재 bridge 통신 상태
```

bridge가 잠시 끊겨도 Project / Worker를 즉시 `—`로 되돌리지 않는다.

```text
Project   MCP-with-MiniPC        (last known)
Worker    ProjectHub Worker      (last known)
Status    Disconnected
```

현재 구현 방향을 유지한다.

## 10. CURRENT REQUEST mock 제거

Settings 작업과 함께 톱니바퀴 기반 mock state 순환 코드를 제거한다.

preview용 수동 상태 변경은 production extension에서 제거한다.

대신:

```text
bridge task 없음
→ 작업 없음

bridge pending/claimed task 존재
→ Worker → GPT Web

Web 응답을 Worker로 제출하는 단계
→ GPT Web → Worker

task terminal state
→ 작업 종료
```

로 실제 상태만 사용한다.

아직 ChatGPT DOM 자동입력이 구현되지 않았으므로, Worker task가 들어왔을 때:

```text
WORKER → GPT WEB
요청 대기 / Web 자동처리 미연결
```

처럼 사실대로 표시한다. 실제 응답 생성 중이라고 허위 표시하지 않는다.

## 11. 연결 설정과 Conversation Binding 구분

Settings는 Worker bridge 접속 설정이다.

프로젝트 binding은 현재 ChatGPT conversation별 동작으로 유지한다.

```text
⚙ Settings
= Worker bridge에 어떻게 접속하는가

Project binding
= 이 ChatGPT conversation을 어떤 ProjectHub project와 연결하는가
```

두 기능을 한 화면에 섞지 않는다.

향후 binding UI는 메인 Project 행 또는 별도 Connect 동작으로 처리한다.

## 12. 구현 우선순위

이번 작업은 아래까지만 수행한다.

```text
A. gear mock state cycling 제거
B. Settings modal/popover 구현
C. Host / Port / BasePath / optional WorkerPath
D. chrome.storage.local 저장/복원
E. Test Connection
F. polling URL을 저장 설정 기반으로 변경
G. 잘못된 설정 / disconnected 상태 표시
H. page reload 없이 Save 즉시 반영
```

아직 구현하지 않는다:

```text
- Worker EXE 자동 실행
- Native Messaging
- LAN/외부 Worker 연결
- ChatGPT DOM 자동 입력
- 이미지 첨부
- Web 응답 자동 수집
```

## 13. 검증

```text
1. extension reload
2. ChatGPT 페이지 refresh
3. ⚙ 클릭 → Settings 표시
4. 기본값 127.0.0.1 / 43821 / /bridge 확인
5. Test Connection → Connected
6. 잘못된 port 입력 → Disconnected 확인
7. Cancel → 기존 설정 유지
8. 올바른 port 저장 → 즉시 Status Connected 복구
9. 페이지 새로고침 → 저장값 유지
10. 브라우저 재시작 후에도 저장값 유지
11. gear 클릭이 CURRENT REQUEST 상태를 변경하지 않는지 확인
12. node --check extension/gptweb-hub/content.js 통과
```

이 작업 완료 후 다음 기능은 **conversation binding UX 구체화 → ChatGPT DOM 자동입력/응답수집** 순서로 진행한다.


---

# 2026-09-19 GPTWeb-Hub TASK 영역 통합 피드백

현재 GPTWeb-Hub UI의 `CURRENT REQUEST`와 `RESULT MESSAGE`를 하나의 작업 영역으로 통합한다.

## 1. 영역 명칭 통합

기존:

```text
CURRENT REQUEST
RESULT MESSAGE
```

변경:

```text
TASK
```

TASK 영역은 현재 작업의 상태, Worker가 보낸 요청 내용, GPT Web 응답 내용을 모두 표시하는 단일 영역이다.

## 2. 수동 테스트 입력 UI 제거

이제 자동화 연결 검증이 진행됐으므로 아래 수동 테스트용 UI는 제거한다.

```text
- 작업 지시 textarea
- 파일 드래그 영역
- 전송 버튼
- 수동 테스트 전송 로직
```

GPTWeb-Hub는 더 이상 사용자가 직접 프롬프트를 입력하는 도구가 아니다.

작업은 Worker에서 생성하고 Extension은 Worker task를 받아 ChatGPT Web에 전달하는 역할만 수행한다.

## 3. Worker가 보낸 원문 표시

TASK 영역에는 Worker가 전달한 요청 내용을 반드시 표시한다.

예:

```text
TASK

● WORKER → GPT WEB
요청 전달 완료

Worker Message
Force Restore 보호영역 검증 결과를 검토하고 문제점만 정리해줘.
```

Worker prompt가 길 경우 해당 메시지 영역에도 고정 높이 + 내부 스크롤을 적용한다.

## 4. 전달 직후 상태 변경

Worker 또는 GPT Web로 내용을 전달한 직후 UI 상태를 즉시 변경한다.

### Worker → GPT Web

Worker task를 ChatGPT 입력창에 실제로 넣고 Send를 실행한 직후:

```text
● WORKER → GPT WEB
메시지 전달 완료 · GPT Web 응답 대기
```

으로 바꾼다.

단순히 Worker task를 조회한 시점에는 "전달 완료"로 표시하지 않는다.

### GPT Web → Worker

GPT Web의 최종 응답을 Worker result API로 POST한 직후:

```text
● GPT WEB → WORKER
응답 전달 완료 · Worker 처리 대기
```

으로 바꾼다.

즉 UI 상태는 실제 I/O 이벤트와 정확히 일치해야 한다.

## 5. 메시지 출력 완료 시 상태 변경

각 실행 대상이 메시지를 완전히 출력한 시점에도 상태를 명확히 변경한다.

### GPT Web 응답 생성 완료

최종 assistant 메시지의 스트리밍이 완전히 끝난 뒤:

```text
● GPT WEB → WORKER
GPT Web 응답 완료
```

로 바꾸고, 같은 TASK 영역 안에 최종 응답을 표시한다.

예:

```text
Web Response
보호영역 제외 규칙은 정상이나 ...
```

그 다음 Worker result endpoint로 실제 전송한다.

### Worker 응답 완료

Worker가 Web 결과를 수신해 후속 처리를 완료하고 terminal 상태를 반환하면:

```text
● 작업 종료
정상 완료
```

또는:

```text
● 작업 종료
사용자 확인 필요
```

또는:

```text
● 작업 종료
오류 발생 · 확인 필요
```

로 변경한다.

중요: Worker의 terminal 상태 확인 전에는 임의로 FINISHED를 표시하지 않는다.

## 6. TASK 영역 권장 구조

예:

```text
TASK

● WORKER → GPT WEB
GPT Web 응답 생성 중

Worker Message
Force Restore 보호영역 검증 결과를 검토해줘.

────────────────────────

Web Response
보호영역 검증 결과 ...
```

작업 단계에 따라 아직 없는 부분은 숨긴다.

예:

```text
Worker task 수신 직후
→ Worker Message만 표시

GPT Web 응답 완료 후
→ Worker Message + Web Response 표시

Worker terminal 완료 후
→ 상태를 작업 종료로 변경
```

## 7. 고정 높이 / 내부 스크롤

TASK 영역 때문에 GPTWeb-Hub 패널 전체 높이가 계속 늘어나면 안 된다.

필수 원칙:

```text
- TASK 카드 전체 최대 높이 고정
- Worker Message 영역 max-height 지정
- Web Response 영역 max-height 지정
- 긴 메시지는 각 영역 내부 overflow-y: auto
- 브라우저 전체 패널 높이는 유지
- 긴 응답 때문에 패널이 아래로 계속 늘어나지 않음
```

권장:

```css
.task-message {
  max-height: 140px;
  overflow-y: auto;
}

.task-response {
  max-height: 220px;
  overflow-y: auto;
}
```

실제 수치는 현재 패널 높이에 맞춰 조정 가능하다.

응답이 짧으면 scrollbar는 보이지 않고, 길 때만 자동 생성한다.

## 8. 상태 전환 예시

전체 1회 왕복:

```text
IDLE
작업 없음

↓ Worker task 수신

WORKER_TO_WEB
Worker 요청 대기

↓ ChatGPT 입력 + Send 실제 실행

WORKER_TO_WEB
메시지 전달 완료 · GPT Web 응답 대기

↓ assistant streaming 시작

WORKER_TO_WEB
GPT Web 응답 생성 중

↓ assistant streaming 완전 종료

WEB_TO_WORKER
GPT Web 응답 완료

↓ Worker result POST 성공

WEB_TO_WORKER
응답 전달 완료 · Worker 처리 대기

↓ Worker terminal 상태 확인

FINISHED
작업 종료
```

이 상태 전환은 실제 이벤트 기반으로 구현한다.

## 9. 응답 완료 판정

단순 MutationObserver에서 텍스트가 한 번 바뀌었다고 완료 처리하지 않는다.

최종 응답 완료는 가능한 경우 다음을 함께 확인한다.

```text
- assistant message 존재
- 생성 중/Stop UI 종료
- 일정 시간 동안 assistant message 내용 변화 없음
```

권장 안정화 시간:

```text
500~1000ms
```

이미지 생성처럼 별도 generation UI가 존재하는 경우 후속 범위에서 별도 완료 조건을 추가한다.

## 10. 구현 범위

이번 변경에서 수행:

```text
A. CURRENT REQUEST + RESULT MESSAGE → TASK 통합
B. textarea / drag & drop / 전송 버튼 제거
C. Worker Message 표시
D. Web Response 표시
E. 실제 send/result POST 직후 상태 변경
F. GPT Web streaming 종료 시 상태 변경
G. Worker terminal 상태 수신 시 FINISHED
H. 긴 메시지 내부 scrollbar
I. 패널 전체 높이 증가 방지
```

이번 변경에서 제외:

```text
- 이미지 생성 결과 asset 수집
- 복수 Web task 병렬화
- 사용자 수동 prompt 입력 복구
```

## 11. 검증

```text
1. Worker task 1건 생성
2. TASK 영역에 Worker Message 표시 확인
3. ChatGPT send 직후 상태가 응답 대기로 변경되는지 확인
4. streaming 중 "응답 생성 중" 표시 확인
5. streaming 종료 후 Web Response가 TASK 영역에 표시되는지 확인
6. Worker result POST 후 "Worker 처리 대기" 표시 확인
7. Worker terminal 상태 후 "작업 종료" 표시 확인
8. 긴 Worker Message에서 내부 scrollbar 확인
9. 긴 Web Response에서 내부 scrollbar 확인
10. 긴 응답에도 패널 전체 높이가 증가하지 않는지 확인
11. textarea / 파일 드롭 / 전송 버튼이 제거됐는지 확인
12. node --check extension/gptweb-hub/content.js 통과
```


---

# 2026-09-19 GPTWeb-Hub 통합 자동 왕복 구현 지시

이 항목은 직전 TASK UI 통합 피드백과 이전 Web↔Worker 자동화 제안을 **하나의 구현 범위로 합친 최종 지시**다.

이번에는 기능을 잘게 나눠 중간중간 멈추지 말고, 아래 항목을 **한 번에 구현한 뒤 마지막에 통합 검증 1회를 수행**한다.

기존 ProjectHub 정책과 충돌하지 않는 범위에서 이 작업은 하나의 GPTWeb-Hub 통합 기능 작업으로 취급한다.

## 1. 최종 목표

이번 작업의 성공 기준은 단순 UI 변경이 아니다.

반드시 아래 1회 왕복이 실제로 끝까지 동작해야 한다.

```text
Worker task 생성
→ 연결된 ChatGPT conversation에서 Extension이 task 수신
→ Worker Message 표시
→ ChatGPT 입력창에 자동 입력
→ 필요한 attachment 자동 첨부
→ Send
→ GPT Web 응답 생성 감지
→ 최종 응답 완료 판정
→ TASK 영역에 Web Response 표시
→ POST /bridge/task/{taskId}/result
→ Worker가 결과 수신
→ Worker terminal 상태
→ Extension FINISHED 표시
```

즉 이번 작업이 끝나면 최소한 **Worker → GPT Web → Worker 1회 완전 왕복 E2E**가 가능해야 한다.

## 2. TASK 단일 영역

기존:

```text
CURRENT REQUEST
RESULT MESSAGE
```

를 제거하고:

```text
TASK
```

하나로 통합한다.

TASK 영역은 아래 정보를 한 카드 안에서 표시한다.

```text
- 현재 상태
- Worker Message
- Web Response
- task id
- 필요한 최소 메타데이터
```

수동 입력 테스트용 UI는 삭제한다.

삭제 대상:

```text
- 작업 지시 textarea
- 파일 drag/drop 테스트 영역
- 수동 전송 버튼
- 테스트 전송 전용 코드
- Extension 사용자가 직접 prompt를 입력하는 경로
```

GPTWeb-Hub는 이제 **Worker가 만든 task를 처리하는 자동 bridge UI**다.

## 3. 전송 엔진 단일화

현재까지 구현한 ChatGPT composer 탐색, text 삽입, file attach, Send 클릭 로직을 재사용한다.

Worker 자동 task용 별도 전송 로직을 새로 복제하지 않는다.

내부적으로 하나의 공통 함수 흐름을 사용한다.

예:

```text
sendToChatGPT(prompt, attachments)
```

이 공통 경로에서:

```text
1. composer 탐색
2. prompt 입력
3. attachment 첨부
4. send button 탐색
5. 실제 click
6. 성공/실패 반환
```

을 처리한다.

## 4. Worker PENDING task 자동 처리

현재 conversation ID로 조회한 Worker task가 `PENDING`이고 아래 조건이 모두 맞을 때만 자동 실행한다.

```text
- bridge connected
- current conversation id 존재
- conversation binding 일치
- task conversation id 일치
- active Web task 없음
- task status == PENDING
- ChatGPT composer 사용 가능
```

조건이 맞으면:

```text
PENDING
→ claim
→ Worker Message 표시
→ ChatGPT 자동 전송
```

순서로 진행한다.

단순히 task를 polling에서 발견했다는 이유만으로 먼저 send하면 안 된다.

claim 성공 이후에만 실제 ChatGPT 입력을 수행한다.

## 5. 한 conversation당 active Web task 1개

반드시 다음 규칙을 적용한다.

```text
conversation_id 하나당 active Web task 최대 1개
```

다음 상태 중 하나라도 존재하면 새 task를 자동 전송하지 않는다.

```text
CLAIMED
SENT_TO_WEB
WEB_GENERATING
WEB_RESULT_READY
RESULT_SUBMITTING
```

새로운 PENDING task는 Worker queue에서 대기시킨다.

동일 task를 여러 탭에서 동시에 처리하지 않는다.

Worker의 claim/lease가 실행 권한의 최종 기준이다.

## 6. 페이지 새로고침 / SPA 이동 중복 전송 방지

ChatGPT 페이지 새로고침 또는 conversation 간 SPA 이동이 발생해도 이미 claim된 task를 다시 보내지 않는다.

복구 흐름:

```text
페이지 로드 / URL 변경
→ conversation_id 확인
→ Worker binding 조회
→ 해당 conversation의 최신 task 조회
→ task status 확인
→ UI 상태 복원
```

중요:

```text
PENDING
→ 자동 처리 가능

CLAIMED 이후
→ 기존 진행 상태 복구
→ 같은 prompt 재전송 금지
```

task id를 Extension 내부 현재 task와 비교하고 중복 send 방지 guard를 둔다.

## 7. Worker Message 표시

Worker task의 실제 prompt를 TASK 영역에 표시한다.

예:

```text
TASK

● WORKER → GPT WEB
요청 전달 준비

Worker Message
Force Restore 보호영역 검증 결과를 검토하고 문제점만 정리해줘.
```

긴 prompt는 내부 scrollbar 사용.

Worker Message는 Extension이 자체 요약하거나 변형하지 않는다.

실제 Worker task prompt를 표시한다.

## 8. 실제 전달 직후 상태 변경

상태 표시는 실제 이벤트와 정확히 맞아야 한다.

### Worker → GPT Web

ChatGPT Send 버튼의 실제 click이 성공한 직후:

```text
● WORKER → GPT WEB
메시지 전달 완료 · GPT Web 응답 대기
```

로 즉시 변경한다.

task를 발견했거나 claim만 했을 때는 `전달 완료`로 표시하지 않는다.

### GPT Web → Worker

Worker result endpoint에 최종 응답 POST가 성공한 직후:

```text
● GPT WEB → WORKER
응답 전달 완료 · Worker 처리 대기
```

로 즉시 변경한다.

## 9. GPT Web streaming 상태 감지

ChatGPT assistant 메시지가 생성되기 시작하면:

```text
● WORKER → GPT WEB
GPT Web 응답 생성 중
```

으로 표시한다.

단순히 MutationObserver 이벤트 1회 발생만으로 완료 처리하지 않는다.

## 10. GPT Web 최종 응답 완료 판정

최종 완료는 가능한 범위에서 아래 조건을 조합한다.

```text
1. 현재 task send 이후 새 assistant message 존재
2. Stop/Generating 계열 UI가 더 이상 active하지 않음
3. assistant message 본문이 안정화 시간 동안 변하지 않음
```

권장 안정화 시간:

```text
500~1000ms
```

완료되면:

```text
● GPT WEB → WORKER
GPT Web 응답 완료
```

로 변경하고 TASK 영역에 최종 Web Response를 출력한다.

중간 streaming text는 Worker 최종 result로 보내지 않는다.

## 11. Web Response 표시

TASK 안에 Worker Message와 Web Response를 함께 표시한다.

예:

```text
TASK

● GPT WEB → WORKER
GPT Web 응답 완료

Worker Message
Force Restore 보호영역 검증 결과를 검토해줘.

────────────────────────

Web Response
보호영역 제외 규칙은 정상이며 ...
```

아직 응답이 없을 때 Web Response 영역은 숨길 수 있다.

## 12. Worker result 실제 반환

GPT Web 최종 응답 완료 후 반드시 현재 task id에 대해:

```text
POST /bridge/task/{taskId}/result
```

를 호출한다.

최소 result payload에는:

```text
task_id
conversation_id
response_text
result_type
completed_at
```

를 전달한다.

현재 서버 계약과 맞게 실제 schema를 조정하되 의미는 유지한다.

POST 성공 전에는 Worker 전달 완료로 표시하지 않는다.

## 13. Worker terminal 상태 확인

result POST 성공 후 Extension이 임의로 즉시 FINISHED 처리하지 않는다.

Worker가 후속 처리를 끝내고 terminal 상태를 반환할 때까지:

```text
● GPT WEB → WORKER
응답 전달 완료 · Worker 처리 대기
```

를 유지한다.

Worker terminal 상태 예:

```text
COMPLETED
FAILED
WAITING_USER
CANCELLED
```

UI에서는 단순화해서:

```text
● 작업 종료
정상 완료

● 작업 종료
사용자 확인 필요

● 작업 종료
오류 발생 · 확인 필요
```

로 표시한다.

## 14. attachment 자동 전달

수동 drag/drop UI는 삭제하지만 attachment 기능 자체는 제거하지 않는다.

Worker task에 attachment가 있으면 Extension이 자동으로 ChatGPT에 첨부한다.

권장 구조:

```text
attachments[]
- id
- fileName
- mimeType
- size
- source
- downloadUrl 또는 bridge asset endpoint
```

Extension 흐름:

```text
Worker task
→ attachment metadata 확인
→ loopback bridge에서 blob 다운로드
→ File 객체 생성
→ 기존 attachFilesToChat() 경로 사용
→ prompt와 함께 Send
```

텍스트 전용 task면 attachment 단계는 건너뛴다.

## 15. result type 구분

향후 이미지 생성 등을 고려해 result를 최소 다음 타입으로 확장 가능하게 설계한다.

```text
TEXT_RESULT
IMAGE_RESULT
FILE_RESULT
```

이번 구현의 필수 완료 범위는 `TEXT_RESULT`다.

다만 schema나 상태 구조가 향후 IMAGE/FILE result를 막지 않게 작성한다.

이미지 생성 결과 asset 수집 자체는 이번 필수 검증에서 제외해도 된다.

## 16. 이미지 작업의 직렬 처리 전제

ChatGPT Web 한 conversation에서는 이미지 생성과 다른 요청을 동시에 실행하지 않는다.

향후 이미지 task가 붙더라도:

```text
WEB_IMAGE
→ 생성 완료
→ asset 회수
→ 다음 WEB_TEXT 또는 다음 task
```

순서로 직렬 처리한다.

같은 conversation에 동시에 여러 Web request를 넣지 않는다.

## 17. 긴 메시지 UI

TASK 카드 때문에 GPTWeb-Hub 전체 패널 높이가 늘어나면 안 된다.

필수:

```text
Worker Message
- max-height 고정
- overflow-y: auto

Web Response
- max-height 고정
- overflow-y: auto

TASK 전체
- 전체 최대 높이 제한
- 패널 전체 높이 유지
```

예:

```css
.task-message {
  max-height: 140px;
  overflow-y: auto;
}

.task-response {
  max-height: 220px;
  overflow-y: auto;
}
```

짧은 메시지에는 scrollbar가 보이지 않고 긴 경우에만 표시한다.

## 18. 상태 머신 최종 기준

이번 구현에서 Extension UI 상태는 최소 다음 흐름을 따른다.

```text
IDLE
↓
PENDING
↓
CLAIMED
↓
SENT_TO_WEB
↓
WEB_GENERATING
↓
WEB_RESULT_READY
↓
RESULT_SUBMITTING
↓
WAITING_WORKER
↓
FINISHED
```

사용자 표시 문구는 단순화한다.

```text
작업 없음
Worker 요청 대기
메시지 전달 완료 · GPT Web 응답 대기
GPT Web 응답 생성 중
GPT Web 응답 완료
GPT Web → Worker 전달 중
응답 전달 완료 · Worker 처리 대기
작업 종료
```

내부 enum 명칭은 현재 코드 구조에 맞춰 달라도 되지만 의미와 전환 순서는 유지한다.

## 19. 실패 처리

다음 실패를 각각 terminal 또는 retry 가능 상태로 구분한다.

```text
- composer 미검출
- send button 미검출
- attachment download 실패
- file attach 실패
- bridge claim 실패
- result POST 실패
- ChatGPT response timeout
- conversation mismatch
- bridge disconnected
```

같은 task를 무조건 다시 send하지 않는다.

특히 send 성공 여부가 불확실한 경우 duplicate prompt 전송 방지가 더 중요하다.

사용자 개입이 필요한 경우 FINISHED/사용자 확인 필요로 표시하거나 Worker가 WAITING_USER 상태를 반환하도록 한다.

## 20. 이번에는 한 번에 구현

이번 지시는 기능별로 하나씩 구현 후 중간 검증하고 멈추는 방식으로 진행하지 않는다.

다음 범위를 한 번에 묶어서 구현한다.

```text
A. TASK UI 통합
B. 수동 입력/drag/drop/send UI 제거
C. Worker Message 표시
D. Worker PENDING 자동 claim
E. 공통 ChatGPT 전송 엔진으로 자동 send
F. Worker attachment 자동 첨부 경로
G. send 직후 상태 변경
H. streaming 시작/종료 판정
I. Web Response 표시
J. result POST
K. Worker terminal 상태 반영
L. conversation별 single active task
M. 새로고침/SPA 이동 중복 send 방지
N. 긴 메시지 scrollbar / 고정 패널 높이
```

중간 단계마다 별도 사용자 확인을 요구하지 않는다.

구현을 모두 끝낸 뒤 한 번에 통합 검증한다.

단, 실제 destructive Git/Force Restore/NAS delete 같은 별도 위험 작업을 자동 실행하라는 의미는 아니다.

## 21. 최종 통합 검증 1회

구현 완료 후 아래 시나리오를 **한 번에 연속으로 검증**한다.

```text
1. Worker와 Extension 실행
2. 기존 conversation binding 복원 확인
3. Worker에서 테스트 task 1건 생성
4. Extension TASK에 Worker Message 표시
5. task claim 성공
6. ChatGPT에 자동 prompt 입력
7. 필요한 경우 test attachment 자동 첨부
8. 실제 Send
9. Send 직후 상태 변경 확인
10. assistant streaming 시작 상태 확인
11. assistant 최종 출력 완료 판정
12. Web Response TASK 영역 표시
13. POST /bridge/task/{taskId}/result 성공
14. 응답 전달 완료 · Worker 처리 대기 표시
15. Worker terminal 상태 수신
16. FINISHED 표시
17. Worker UI Last Result에 Web 결과 반영 확인
18. 페이지 새로고침 후 동일 task 재전송되지 않는지 확인
19. 다른 conversation으로 이동 시 다른 task가 섞이지 않는지 확인
20. 긴 Worker Message / Web Response scrollbar 확인
21. 패널 전체 높이가 늘어나지 않는지 확인
22. 수동 textarea / drag/drop / send 버튼 제거 확인
23. node --check extension/gptweb-hub/content.js
24. dotnet build src/ProjectHub.Worker/ProjectHub.Worker.csproj --no-restore
25. git diff --check
```

필요하면 테스트 task prompt는 짧은 Hello World 수준으로 사용한다.

attachment 검증은 현재 Worker asset endpoint가 아직 없으면 구현 가능한 최소 bridge asset endpoint를 함께 추가해 실제 1회 첨부까지 확인한다.

최종 보고에는 중간 진행 로그를 길게 나열하지 말고 아래만 요약한다.

```text
- 구현 완료 항목
- 실제 E2E 결과
- 실패/제약이 있으면 정확한 원인
- 변경 파일
- build/check 결과
- 남은 후속 범위
```

이번 작업의 완료 기준은 **UI만 바뀐 것**이 아니라 **Worker → GPT Web → Worker의 실제 1회 왕복이 끝까지 성공하는 것**이다.

---

# 2026-09-19 GPTWeb-Hub Web-Controlled Loop Protocol v1

## 목적

현재 구현된 1회 왕복:

```text
Codex
→ Worker
→ GPT Web
→ Worker
→ FINISHED
```

을 Web ChatGPT가 다음 동작을 결정하는 반복 가능한 자동 작업 흐름으로 확장한다.

핵심 원칙:

```text
GPT Web   = Controller
Worker    = Orchestrator / Executor
Extension = Transport
Codex CLI = Task Executor
```

ACTION의 원본은 반드시 **GPT Web의 응답**이어야 한다.

Extension은 ACTION을 생성하거나 판단하지 않는다.
Worker도 Web의 ACTION을 임의로 변경하거나 보정하지 않는다.

## ACTION 계약

### [ACTION=BEGIN]

새 자동 작업의 최초 명령이다.

Web 응답의 첫 번째 유효 제어행이 `[ACTION=BEGIN]`이면 Worker는 그 뒤의 본문을 첫 Codex CLI 명령으로 전달하고 job을 시작한다.

```text
GPT Web
[ACTION=BEGIN]
첫 작업 지시...

→ Worker
→ Codex CLI 실행
→ 결과 수집
→ GPT Web 전달
```

BEGIN은 job 시작 시 1회만 유효하다.
이미 진행 중인 job에서 다시 BEGIN이 나오면 protocol error로 처리한다.

### [ACTION=CONTINUE]

Web이 현재 Codex 결과를 검토한 뒤 추가 Codex 작업이 필요하다고 판단한 상태다.

ACTION 이후의 본문을 다음 Codex CLI 명령으로 전달한다.

```text
GPT Web
[ACTION=CONTINUE]
다음 작업 지시...

→ Worker
→ Codex CLI
→ 결과 수집
→ 동일 GPT Web conversation으로 전달
→ 다음 ACTION 대기
```

이 흐름을 반복한다.

### [ACTION=PAUSE]

자동화만으로 안전하게 계속할 수 없고 사용자의 입력·판단·승인이 필요함을 의미한다.

PAUSE 응답은 Codex CLI에 전달하지 않는다.

Worker는 자동 loop를 종료하고 사용자 개입이 필요한 종료 상태로 전환한다.

```text
[ACTION=PAUSE]
→ Codex 미전달
→ FINISH_PAUSED
```

Web 응답 본문은 사용자에게 그대로 표시한다.

### [ACTION=END]

Web이 현재 목표가 완료됐다고 판단한 정상 종료다.

END 응답은 Codex CLI에 전달하지 않는다.

```text
[ACTION=END]
→ Codex 미전달
→ FINISH_SUCCESS
```

Web 응답 본문은 최종 결과로 표시한다.

## ACTION과 기존 Task Status 분리

현재 bridge의 상태:

```text
PENDING
CLAIMED
COMPLETED
FAILED
...
```

는 그대로 유지한다.

이는 한 번의 Web 요청이 전달·처리되는 **transport lifecycle**이다.

ACTION은 Web 응답이 **다음에 무엇을 해야 하는지**를 나타내는 control protocol이다.

예:

```text
Bridge Task:
PENDING
→ CLAIMED
→ GPT Web 실행
→ result POST
→ COMPLETED

Web Response:
[ACTION=CONTINUE]
다음 작업...

Worker:
→ 다음 Codex round 실행
```

둘을 하나의 enum으로 합치지 않는다.

## 응답 형식

Web 응답의 첫 번째 유효 제어행으로 아래 중 정확히 하나를 사용한다.

```text
[ACTION=BEGIN]
[ACTION=CONTINUE]
[ACTION=PAUSE]
[ACTION=END]
```

BEGIN/CONTINUE의 경우 ACTION 이후 본문만 Codex 명령 payload로 사용한다.

예:

```text
[ACTION=CONTINUE]
현재 결과에서 실패 원인을 확인하고 필요한 수정만 수행해.
```

PAUSE/END 본문은 사용자에게 표시할 설명/최종 결과이며 Codex에는 전달하지 않는다.

한 Web 응답에는 ACTION이 정확히 하나만 존재해야 한다.

## Protocol Error

다음 상황에서 Worker가 의미를 추측하지 않는다.

```text
- ACTION 없음
- ACTION 2개 이상
- 알 수 없는 ACTION
- ACTION 형식 손상
- 진행 중 job에서 BEGIN 재등장
- BEGIN/CONTINUE인데 전달할 본문이 비어 있음
```

이 경우:

```text
FINISH_PROTOCOL_ERROR
```

로 종료하며 응답을 Codex에 전달하지 않는다.

## ACTION과 실행 예외 분리

다음 오류가 발생했다고 Worker나 Extension이 임의로 `[ACTION=PAUSE]` 또는 `[ACTION=END]`를 생성하면 안 된다.

```text
- bridge 연결 실패
- GPT Web timeout
- GPT Web DOM 처리 실패
- Codex 실행 실패
- conversation mismatch
- Worker 내부 예외
- Extension 내부 예외
```

ACTION은 오직 Web 응답에서만 온다.

실행 계층 오류는 별도의 Worker terminal state로 처리한다.

초기 권장 상태:

```text
FINISH_SUCCESS
FINISH_PAUSED
FINISH_PROTOCOL_ERROR
FINISH_CODEX_ERROR
FINISH_WEB_ERROR
FINISH_WORKER_ERROR
FINISH_LIMIT
FINISH_CANCELLED
```

## 반복 안전장치

Web이 계속 CONTINUE를 반환하더라도 무한 실행되지 않도록 Worker가 독립적인 hard limit을 가진다.

초기 기본값 권장:

```text
MAX_ROUNDS  = 30
MAX_RUNTIME = 30 minutes
```

제한을 초과하면 Web ACTION과 무관하게:

```text
FINISH_LIMIT
```

로 종료한다.

테스트처럼 정확히 10회 왕복이 필요한 작업은 job 생성 시 `maxRounds=10`으로 지정할 수 있다.

## Job / Round 관리

round 번호를 AI의 출력에 의존하지 않는다.

Worker가 다음 값을 관리한다.

```text
jobId
round
maxRounds
startedAt
lastWebTaskId
lastAction
actionConsumed
codexSessionId
```

Web이나 Codex가 `3/10` 같은 숫자를 출력하더라도 실제 round의 정답은 Worker 상태다.

권장 전이:

```text
IDLE
  ↓
BEGIN
  ↓
CODEX_RUNNING
  ↓
WORKER_TO_WEB
  ↓
WEB_RUNNING
  ↓
WEB_TO_WORKER
  ↓
ACTION

ACTION=CONTINUE
  → round + 1
  → CODEX_RUNNING
  → ...

ACTION=PAUSE
  → FINISH_PAUSED

ACTION=END
  → FINISH_SUCCESS
```

## 동일 ACTION 중복 소비 방지

각 Web result의 ACTION은 `taskId` 기준으로 정확히 한 번만 소비한다.

같은 taskId의 result가 polling, 새로고침, retry 때문에 다시 보이더라도 다음 Codex round를 두 번 실행하지 않는다.

최소한 다음 값을 저장한다.

```text
jobId
round
webTaskId
action
actionConsumed
```

`actionConsumed=true`인 task는 다시 실행하지 않는다.

Worker 재시작 후에도 가능한 범위에서 이 상태를 복원해 duplicate Codex 실행을 방지한다.

## 통신 Retry와 실행 Retry 분리

Web 전송이나 result POST가 실패했다고 동일 Codex 명령을 다시 실행해서는 안 된다.

올바른 흐름:

```text
Codex 실행 1회
→ 결과 로컬 보관
→ Web 전달 실패
→ 동일 결과를 Web에 재전송
```

금지:

```text
Codex 실행
→ Web 전달 실패
→ 동일 Codex 명령 재실행
```

파일 변경, Git 작업 등 side effect가 있는 명령은 재실행 시 위험할 수 있다.

## PAUSE와 CANCEL 구분

```text
PAUSE
= GPT Web이 정상 프로토콜로 사용자 개입 필요를 결정

CANCEL
= 사용자가 Worker UI에서 현재 job을 명시적으로 취소
```

둘을 같은 상태로 처리하지 않는다.

사용자 Cancel은:

```text
FINISH_CANCELLED
```

로 종료한다.

## Extension 책임

Extension은 다음 역할만 수행한다.

```text
- Worker task 수신
- GPT Web에 전달
- 최종 Web 응답 수집
- 원문 result를 Worker에 반환
- 화면에 ACTION/상태를 표시할 수 있음
```

Extension이 다음을 해서는 안 된다.

```text
- 응답 내용을 보고 CONTINUE/END를 자체 판단
- ACTION이 없을 때 자동 보정
- PAUSE/END를 임의 생성
- 동일 task 재전송
```

## Worker 책임

Worker가 수행한다.

```text
- ACTION parser
- protocol validation
- jobId / round / maxRounds 관리
- BEGIN/CONTINUE의 Codex 전달
- PAUSE/END의 terminal 처리
- ACTION idempotency
- MAX_ROUNDS / MAX_RUNTIME hard limit
- Codex 결과 보관
- transport retry와 Codex 실행 retry 분리
```

## 첫 구현 범위

현재의 Worker → GPT Web → Worker 1회 왕복 코드는 최대한 유지한다.

다음만 추가한다.

```text
1. Web Response ACTION parser
2. BEGIN / CONTINUE / PAUSE / END validation
3. Worker jobId / round / maxRounds
4. BEGIN → 첫 Codex 실행
5. CONTINUE → 기존 Codex 실행 경로 재진입
6. PAUSE → FINISH_PAUSED
7. END → FINISH_SUCCESS
8. protocol error → FINISH_PROTOCOL_ERROR
9. MAX_ROUNDS / MAX_RUNTIME
10. 동일 webTaskId ACTION 중복 소비 방지
11. Codex 결과 저장 후 Web transport retry
12. Worker UI에 현재 Job / Round / terminal 상태 최소 표시
```

Extension에는 ACTION 의사결정 로직을 추가하지 않는다.

## 우선 검증 시나리오

곱셈 문제를 이용해 10회 왕복을 검증한다.

```text
1. Web이 [ACTION=BEGIN] + 첫 문제 생성 지시 반환
2. Worker가 Codex에 전달
3. Codex 결과를 Web에 전달
4. Web이 [ACTION=CONTINUE] + 다음 지시 반환
5. 2~4를 반복
6. Worker round가 정확히 10에 도달하는지 확인
7. 마지막 Web 응답이 [ACTION=END]
8. END가 Codex로 전달되지 않는지 확인
9. FINISH_SUCCESS 확인
10. 동일 마지막 task result를 재조회해도 추가 Codex 실행이 없는지 확인
```

추가 예외 검증:

```text
- ACTION 누락 → FINISH_PROTOCOL_ERROR
- ACTION 중복 → FINISH_PROTOCOL_ERROR
- 진행 중 BEGIN 재등장 → FINISH_PROTOCOL_ERROR
- maxRounds 초과 시 CONTINUE여도 FINISH_LIMIT
- PAUSE → Codex 미전달 + FINISH_PAUSED
- 사용자 Cancel → FINISH_CANCELLED
- Web 전달 재시도 시 Codex 재실행 없음
```

완료 기준은 단순 파싱 성공이 아니라 **Web이 Controller가 되어 Codex와 여러 round를 실제 반복하고, END/PAUSE/오류/limit에서 안전하게 자동 loop가 종료되는 것**이다.


---

# 2026-09-19 GPTWeb-Hub 최우선 과제 — 실제 ChatGPT 메시지 왕복 E2E

최신 main 기준 Worker/Codex 제어 루프와 ACTION 반복 프로토콜은 이미 구현되어 있다. 현재 가장 중요한 미완료 항목은 GPTWeb-Hub Extension이 실제 ChatGPT Web 대화창에 Worker 메시지를 안정적으로 전달하고, 최종 응답을 정확히 회수해 Worker로 되돌리는 브라우저 E2E다.

이번 작업에서는 새로운 기능을 확장하지 말고 Web transport 안정화와 실제 화면 검증을 최우선으로 한다.

## 현재 병목

현재 코드 흐름:

```text
Worker task
→ conversation binding
→ Extension polling
→ claim
→ ChatGPT composer 탐색
→ prompt 주입
→ attachment
→ Send
→ assistant 응답 감시
→ result POST
→ Worker 후속 Codex
→ ACTION 반복
```

문제는 이 전체 흐름이 실제 Explorer Worker + Chrome Extension + 실제 ChatGPT 대화에서 끝까지 검증되지 않았다는 점이다.

## 이번 완료 기준

최소 2회 실제 Web 왕복을 사용자 키보드 입력 없이 성공시킨다.

```text
Worker
→ Extension
→ 현재 바인딩된 ChatGPT conversation
→ 자동 prompt 입력
→ 실제 Send
→ GPT Web 최종 응답 완료 감지
→ result POST
→ Worker
→ 같은 Codex session 후속 실행
→ 두 번째 Web task
→ 같은 conversation 자동 전송
→ 두 번째 최종 응답 회수
→ ACTION=END 또는 정상 종료
```

1회 성공만으로 완료 처리하지 않는다.

## 메시지 전달 검증

1. composer는 실제 ChatGPT 입력창만 선택한다. Extension 자체 input, 숨은 contenteditable, 검색창, modal input을 잡으면 안 된다.
2. prompt 주입 후 실제 ChatGPT composer에 동일 문자열이 표시됐는지 확인한다.
3. 실제 Send selector를 우선 사용하고 넓은 fallback selector는 마지막 수단으로 둔다.
4. sendButton.click()만으로 성공 처리하지 않는다. composer clear, 새 user message 생성, assistant generation 시작 중 가능한 신호를 조합해 실제 전송 성공을 확인한다.

## 비활성 탭

활성 ChatGPT 탭과 비활성 ChatGPT 탭을 각각 실제로 검증한다.

비활성 탭에서 브라우저 정책 때문에 전송이 불안정하면 무리하게 우회하지 말고 WEB_REQUIRES_FOREGROUND 같은 명확한 상태로 표시한다. 사용자가 해당 탭을 활성화하면 이어서 처리할 수 있게 한다.

## 응답 완료 판정

assistant message가 처음 나타났다고 완료 처리하지 않는다.

최종 응답 확정 조건:

```text
1. 현재 task 전송 이후 새 assistant message 존재
2. Stop/중지 또는 streaming/busy 신호 종료
3. assistant 본문이 안정화 시간 동안 변하지 않음
4. task baselineAssistant와 다른 새 응답임
```

현재 5초 안정화 대기는 실제 화면에서 충분한지 확인하되, 단순히 시간을 더 늘리는 방식보다 streaming 신호를 정확히 잡는 것을 우선한다.

## 이전 응답 재사용 방지

task별로 최소 다음 값을 관리한다.

```text
taskId
conversationId
baselineAssistant
sentTaskId
sentAt
```

새로고침, Extension reload, SPA 이동 후 CLAIMED task를 복원해도 기존 assistant 응답을 새 결과로 사용하지 않는다.

## 중복 전송 방지

다음 상황에서 동일 prompt를 다시 보내지 않는다.

```text
Extension refresh
ChatGPT page refresh
SPA navigation
polling 재조회
heartbeat 재연결
result POST retry
```

taskId + sentTaskId + claim 상태 + conversationId를 기준으로 중복 send를 막는다. Send 성공 여부가 불확실하면 자동 재전송보다 사용자 확인 상태를 우선한다.

## conversation 일치 검증

전송 직전에 반드시 다음을 다시 확인한다.

```text
task.conversationId == currentConversationId
binding.projectId == task.projectId
```

불일치 시 다른 방에 보내지 말고 CONVERSATION_MISMATCH로 중단한다.

## Attachment 검증 순서

먼저 TEXT_ONLY 2회 왕복을 완전히 성공시킨 뒤 IMAGE_ATTACHMENT 1회 왕복을 검증한다.

attachment task는 bridge download → File 생성 → ChatGPT 첨부 UI 반영 → upload 완료 → Send 순서를 지킨다.

## 실제 검증 방식

최신 AGENTS.md 기준과 동일하게 가능한 경우 반드시 다음 실환경에서 검증한다.

```text
빌드된 ProjectHub.Worker.exe
+ Chrome에 실제 로드된 GPTWeb-Hub Extension
+ 실제 ChatGPT conversation
```

CLI/API 단독 검증은 보조 검증일 뿐 최종 완료 근거로 사용하지 않는다.

화면 자동화 런타임이 실패하면 실제 Worker/Chrome을 실행하고 사용자에게 필요한 최소 클릭만 요청해서라도 실제 E2E를 끝까지 확인한다. '자동화 런타임 문제로 미검증' 상태를 완료로 기록하지 않는다.

## 우선 테스트 시나리오

복잡한 이미지/코드 대신 단순 텍스트로 2회 왕복부터 검증한다.

Round 1:

```text
Worker → Web
Codex 결과 검토 요청

Web → Worker
[ACTION=CONTINUE]
두 번째 작업 지시
```

Round 2:

```text
Worker/Codex → Web
두 번째 결과 검토 요청

Web → Worker
[ACTION=END]
테스트 완료
```

END가 Codex에 다시 전달되지 않고 FINISH_SUCCESS로 끝나는지 확인한다.

## 실패 단계 기록

실패를 'Web 실패' 하나로 묶지 말고 아래 stage 중 어디서 멈췄는지 기록한다.

```text
BINDING
TASK_POLL
CLAIM
COMPOSER_FIND
TEXT_INSERT
ATTACH
SEND_BUTTON_FIND
SEND_CLICK
SEND_CONFIRM
RESPONSE_START
STREAMING
RESPONSE_STABLE
RESULT_POST
WORKER_RECEIVE
NEXT_ROUND
```

## 이번 우선순위에서 제외

Web transport 검증을 방해하지 않는 한 다음은 이번 작업에서 확장하지 않는다.

```text
새 Worker UI 기능
새 Codex archive 기능
token usage 추가 확장
Server/NAS 기능
Force Restore
추가 ACTION 종류
이미지 생성 결과 asset 수집
다중 conversation 병렬 실행
```

## 문서 최신화

실제 2회 왕복 E2E가 성공하면 CurrentWork.md와 ProjectHub_IMPLEMENTATION_PLAN.md의 오래된 상태 문구도 함께 최신화한다.

특히 'GPT Web DOM 입출력은 후속 범위', 'Chrome polling UI E2E 1건', 'Worker-B 진행' 같은 오래된 표현을 현재 구현 상태에 맞게 제거한다.

## 최종 체크리스트

```text
[ ] 실제 Worker EXE에서 task 시작
[ ] Extension이 실제 현재 conversation에서 claim
[ ] 사용자 입력 없이 ChatGPT composer에 prompt 입력
[ ] 실제 Send 성공
[ ] assistant streaming 시작 확인
[ ] 잘리지 않은 최종 응답 회수
[ ] Worker result POST 성공
[ ] Worker가 Web result 수신
[ ] 같은 Codex session 후속 처리
[ ] 두 번째 Web task 자동 생성
[ ] 같은 conversation으로 두 번째 자동 전송
[ ] 두 번째 최종 응답 회수
[ ] ACTION=END 또는 정상 종료
[ ] duplicate prompt 없음
[ ] 이전 assistant 응답 재사용 없음
[ ] conversation mismatch 없음
[ ] 새로고침/재조회 중복 실행 없음
[ ] build/test/node check 성공
[ ] CurrentWork/Implementation Plan 최신화
```

핵심 완료 기준은 코드상 가능해 보이는 것이 아니라 실제 이 ChatGPT Web 대화에서 Extension이 2회 이상 자동 메시지 왕복을 끝까지 성공하는 것이다.

---

# 2026-09-20 Worker 단일 파일 배포 및 Extension 내장 배포 피드백

## 목표

최종 사용자 PC에는 별도 .NET 설치나 별도 Worker 파일 묶음이 필요하지 않도록 한다.

최종 실행 전제:

```text
필수 설치
- Windows 11
- Codex CLI 또는 Codex Desktop에 포함된 codex.exe
- Chrome

ProjectHub 배포물
- ProjectHub.Worker.exe 1개
```

Worker는 self-contained single-file publish로 배포하고 대상 PC에 .NET 9 Desktop Runtime 설치를 요구하지 않는다.

## 1. Worker publish 방식

ProjectHub.Worker.csproj 또는 별도 publish profile에 다음 성격을 적용한다.

```xml
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<SelfContained>true</SelfContained>
<PublishSingleFile>true</PublishSingleFile>
<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
<PublishTrimmed>false</PublishTrimmed>
```

WPF/WinForms 앱이므로 우선 Trim은 사용하지 않는다. ReadyToRun은 후속 최적화로 두고 안정적인 single-file 실행을 우선한다.

권장 publish 명령:

```powershell
dotnet publish src/ProjectHub.Worker/ProjectHub.Worker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

최종 사용자 배포 기준은 ProjectHub.Worker.exe 1개로 한다. PDB, XML docs, build 임시 파일은 최종 배포물에서 제외한다.

## 2. Codex CLI 탐색

사용자에게 Codex CLI 경로를 수동 지정하게 하는 것을 기본값으로 하지 않는다.

자동 탐색 우선순위:

```text
1. PATH의 codex.exe
2. Codex Desktop bundled CLI
   %LOCALAPPDATA%\OpenAI\Codex\bin\**\codex.exe
3. 저장된 사용자 설정 경로
```

실행 전 codex --version 및 codex login status 수준의 smoke check를 수행한다. Codex가 없으면 Worker 자체는 실행되되 작업 실행만 비활성화하고 설치 필요 상태를 표시한다.

## 3. Chrome 탐색

Chrome 설치는 필수 외부 의존성으로 본다.

자동 탐색 후보:

```text
%ProgramFiles%\Google\Chrome\Application\chrome.exe
%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe
%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe
```

Chrome 미설치 시 GPT Web 기능은 READY로 표시하지 않는다.

## 4. Chrome Extension은 런타임 폴더가 필요함

현재 GPTWeb-Hub는 Manifest V3 unpacked extension 구조다.

```text
extension/gptweb-hub/
├─ manifest.json
└─ content.js
```

Chrome은 일반 unpacked extension을 EXE 내부 리소스에서 직접 로드할 수 없다.

따라서 배포 파일은 Worker EXE 하나로 만들 수 있지만, 실행 시 Extension 파일을 로컬 폴더로 추출해야 한다.

```text
배포 시:
ProjectHub.Worker.exe 1개

첫 실행 후:
%LOCALAPPDATA%\ProjectHub\GPTWeb-Hub\extension\
├─ manifest.json
└─ content.js
```

Extension 파일은 Worker 프로젝트 EmbeddedResource로 포함하고, Worker 첫 실행 또는 Extension 버전 변경 시 위 폴더로 자동 추출한다.

## 5. Extension 설치 방식

초기 배포에서는 Chrome Web Store 등록 없이 unpacked extension 방식을 유지한다.

Worker 설정 화면에:

```text
GPTWeb-Hub Extension
경로: C:\Users\...\AppData\Local\ProjectHub\GPTWeb-Hub\extension

[폴더 열기]
[Chrome 확장 관리 열기]
```

를 제공한다.

사용자 최초 1회 작업:

```text
chrome://extensions
→ 개발자 모드
→ 압축해제된 확장 프로그램을 로드
→ Worker가 생성한 extension 폴더 선택
```

이후 PC 재부팅, Worker 재실행, Chrome 재실행 시 다시 설정할 필요가 없어야 한다.

## 6. 강제 자동 설치는 제외

현재 구조에서 다음은 구현하지 않는다.

```text
- EXE 내부에서 Extension 직접 실행
- Chrome 설정 파일 강제 수정
- 사용자 동의 없는 Extension 자동 설치
- registry policy 기반 강제 설치
```

향후 Chrome Web Store 또는 enterprise 배포를 도입하면 더 자동화할 수 있지만 현재 범위에서는 필요하지 않다.

현재 목표:

```text
사용자가 받는 배포 파일 = EXE 1개
실행 후 Worker가 Extension 폴더 자동 생성
최초 1회 Chrome에서 Load unpacked
이후 자동 연결
```

## 7. Extension 버전 관리

Worker EXE 안에 Extension version을 포함한다. Worker 시작 시 embedded version과 설치 폴더 version을 비교한다.

다르면:

```text
1. 임시 폴더에 새 Extension 파일 추출
2. 파일 검증
3. 기존 extension 폴더 교체
4. UI에 'Chrome 확장 새로고침 필요' 표시
```

한다.

Chrome에서 이미 Load unpacked 된 경로 자체는 바꾸지 않는다. 경로가 유지돼야 사용자가 다시 폴더를 선택할 필요가 없다.

## 8. Worker 런타임 데이터 위치

single-file 배포본의 실행 폴더를 상태 파일로 오염시키지 않는다.

런타임 데이터는:

```text
%LOCALAPPDATA%\ProjectHub\Worker\
├─ config\
├─ state\
├─ Task\
├─ attachments\
├─ logs\
└─ GPTWeb-Hub\extension\
```

처럼 AppData 아래에 저장한다.

현재 EXE 기준 Task 폴더 저장 로직도 최종 배포 단계에서는 이 경로로 이전하는 것을 권장한다. EXE 교체 업데이트 후에도 설정, transcript, extension 상태를 보존한다.

## 9. 첫 실행 진단

Worker 첫 실행 시 다음을 자동 확인한다.

```text
[1] Codex CLI 발견
[2] Codex 인증 상태
[3] Chrome 발견
[4] Extension 파일 추출 상태
[5] localhost bridge 127.0.0.1:43821 시작
[6] GPTWeb-Hub heartbeat 연결 여부
```

UI에는 Codex / Chrome / GPT Web / Worker의 READY 또는 ERROR 상태만 간단히 표시한다.

## 10. Server/Supabase/NAS는 최소 Worker 의존성에서 제외

Web ↔ CLI Worker 자동화 기능 자체는 다음을 필수 의존성으로 두지 않는다.

```text
ProjectHub.Server
Supabase
NAS Gateway
NAS
Agent
Cloudflare Tunnel
```

이들은 ProjectHub 전체 관리 기능을 사용할 때만 추가되는 optional subsystem으로 분리한다.

최소 배포판은 Server 연결이 없어도 다음 루프가 실행돼야 한다.

```text
Codex CLI ↔ Worker ↔ GPTWeb-Hub ↔ ChatGPT Web
```

## 11. 최종 사용자 최소 구성

```text
Windows 11
├─ Chrome
├─ Codex CLI 또는 Codex Desktop
└─ ProjectHub.Worker.exe
```

첫 실행 후 Worker가 자동 생성:

```text
%LOCALAPPDATA%\ProjectHub\...
└─ GPTWeb-Hub\extension\
   ├─ manifest.json
   └─ content.js
```

최초 1회 사용자가 Chrome에서 해당 폴더를 Load unpacked하면 이후 재설정 없이 사용 가능해야 한다.

## 12. 검증

가능하면 깨끗한 Windows 11 PC 또는 별도 테스트 사용자에서 검증한다.

```text
.NET SDK 없음
.NET Runtime 없음
Visual Studio 없음
Node.js 없음
Git 없음
ProjectHub source 없음

설치되어 있는 것:
- Chrome
- Codex CLI/Codex Desktop
```

검증:

```text
1. ProjectHub.Worker.exe 하나만 복사
2. 실행 성공
3. .NET Runtime 설치 요구 없음
4. embedded Extension 자동 추출 확인
5. Chrome에서 Load unpacked 1회
6. Worker bridge READY
7. GPTWeb-Hub Connected
8. Codex 자동 탐색 성공
9. Web → Worker → Codex → Web 2회 왕복 성공
10. PC 재부팅 후 재설정 없이 자동 복구
11. Worker EXE 교체 업데이트 후 기존 설정/Extension 경로 유지
```

완료 기준은 배포물 1개(ProjectHub.Worker.exe)만 전달하고, 대상 PC에 Chrome과 Codex만 이미 설치되어 있으면 최초 Extension 등록 1회를 제외하고 ProjectHub Worker 자동화가 정상 동작하는 것이다.

---

# 2026-09-20 Worker Web 전송 보수 및 Codex 프로젝트 선택 재검토

## 실사용에서 확인된 우선 보수점

실제 Worker 자동 왕복 테스트에서 Codex 첫 작업과 REPORT 생성까지는 완료됐지만 Web 전송 단계가 `SEND_CONFIRM: ChatGPT composer did not clear after send`로 중단됐다. Codex 실행 실패가 아니라 Web transport 확인 로직의 실패로 분리해서 다뤄야 한다.

## 1. SEND_CONFIRM에서 composer clear를 필수 성공 조건으로 쓰지 않는다

ChatGPT Web은 SPA/React rerender 때문에 Send 직후 기존 composer DOM이 바로 비워지지 않거나 node가 교체될 수 있다. `send click -> composer not empty -> fail` 판정은 제거하고 composer clear는 보조 신호로만 사용한다.

전송 성공은 가능한 경우 다음 신호를 조합해 판정한다.

```text
A. task 전송 이후 새 user message bubble 생성
B. 마지막 user message가 전송한 prompt와 일치
C. assistant generation/streaming 시작
D. composer clear
```

A/B/C 중 하나 이상이 명확하면 전송 성공으로 복구할 수 있어야 하며 D만 실패했다고 job을 즉시 실패시키지 않는다.

## 2. 짧은 SEND_CONFIRM polling window와 복구 상태

Send 클릭 직후 1회 검사하지 말고 최대 5~8초 동안 새 user message, assistant streaming, composer 상태를 반복 확인한다. 끝까지 확정할 수 없을 때만 `SEND_UNCONFIRMED`로 둔다.

실패 직후 동일 prompt를 자동 재전송하지 않는다. 실제 전송은 성공했는데 composer clear만 늦었던 경우 중복 전송이 발생할 수 있다. retry 전에 `taskId`, `sentTaskId`, `conversationId`, normalized prompt/hash, 마지막 user message를 비교한다. 이미 동일 user message가 있으면 `SEND_CONFIRM_RECOVERED`로 성공 처리하고 응답 대기로 이동한다.

권장 stage:

```text
SEND_BUTTON_FIND
SEND_CLICK
SEND_CONFIRM_WAIT
SEND_CONFIRMED
SEND_CONFIRM_RECOVERED
SEND_UNCONFIRMED
RESPONSE_START
```

SPA rerender 후에는 기존 composer reference를 계속 쓰지 말고 현재 conversation의 composer를 다시 resolve한다. `<p><br></p>`, `<br>`, zero-width text 등은 빈 입력으로 정규화한다.

## 3. Codex 프로젝트/스레드 선택은 기본 실행의 필수 조건이 아니어야 한다

현재 Worker 코드에서 Codex 실행의 실질 입력은 `workingDirectory`와 optional `sessionId`다. 새 실행은 `sessionId=null`로 시작할 수 있고 첫 실행이 반환한 session ID를 Worker가 이후 round에 고정해서 resume하면 된다.

따라서 기본 Web ↔ Worker ↔ Codex 자동화에서는 사용자가 Codex Desktop 프로젝트나 기존 thread를 먼저 선택하도록 강제할 필요가 없다. 프로젝트/thread ComboBox는 다음 경우의 선택 기능으로 두는 것이 적절하다.

```text
- 현재 폴더가 아닌 다른 working directory를 명시적으로 선택할 때
- 기존 Codex session/thread를 명시적으로 resume할 때
```

기본값은 `현재 폴더 · 새 스레드`가 적절하다.

## 4. 현재 코드의 빈 ProjectPath 처리 수정

현재 UI의 첫 placeholder는 `프로젝트 선택`, `ProjectPath=""`, `SessionId=""`인데 Run Task는 `selectedThread?.ProjectPath ?? Environment.CurrentDirectory`를 사용한다. placeholder 객체가 존재하므로 빈 ProjectPath가 그대로 선택될 수 있다. 새 session 실행은 다시 `codex exec ... -C <workingDirectory>`를 사용하므로 빈 `-C` 가능성을 제거해야 한다.

최소 수정:

```csharp
var workingDirectory = string.IsNullOrWhiteSpace(selectedThread?.ProjectPath)
    ? Environment.CurrentDirectory
    : selectedThread.ProjectPath;
```

RunAsync 진입 전에 workingDirectory가 비어 있지 않고 `Directory.Exists(workingDirectory)`인지 검증한다.

더 나은 UX는 placeholder 대신 실제 실행 가능한 기본 항목인 `(현재 폴더) 새 스레드`를 제공하는 것이다.

## 5. 한 Job이 시작되면 UI 선택보다 Job binding이 우선

첫 Codex 실행 후 Worker가 아래 값을 고정한다.

```text
jobId
workingDirectory
codexSessionId
round
```

`ACTION=CONTINUE`에서는 ComboBox를 다시 읽지 않고 active job state를 사용한다. 첫 실행에서 받은 sessionId를 같은 Job이 끝날 때까지 resume한다. Desktop에서 사용자가 다른 thread를 선택해도 실행 중 Job의 session이 바뀌면 안 된다.

Codex Desktop의 프로젝트 선택 상태를 Worker가 읽어야 하는 구조도 피한다. Worker가 자체적으로 workingDirectory를 정하고 `codex exec -C workingDirectory`를 실행하며 sessionId를 관리하는 것이 기본이다.

## 6. 새 session이어도 파일 기반 작업은 이어갈 수 있다

한 Job 안에서는 같은 session resume가 대화 문맥 유지에 가장 좋다. 하지만 특정 Desktop thread가 없어도 프로젝트 파일이 디스크에 남아 있으면 새 Codex session이 동일 working directory의 현재 파일을 읽고 이어서 작업할 수 있다. 따라서 `기존 Codex Desktop thread가 없으면 작업 자체를 시작할 수 없음`이라는 의존은 두지 않는다.

## 7. 우선 구현 순서

```text
1. SEND_CONFIRM composer-clear 단일 판정 제거
2. 새 user message / assistant streaming 기반 확인
3. SEND_CONFIRM_RECOVERED + 중복 send 방지
4. 빈 ProjectPath fallback 수정
5. 기본값을 '(현재 폴더) 새 스레드'로 변경
6. 프로젝트/thread 선택을 optional UX로 정리
7. jobId ↔ workingDirectory ↔ sessionId 고정
8. 실제 Web 2회 왕복 E2E 재검증
```

## 완료 기준

```text
[ ] Codex 프로젝트/thread 미선택 상태에서 새 Job 시작 가능
[ ] 빈 workingDirectory 또는 -C "" 없음
[ ] 첫 sessionId를 active job에 자동 binding
[ ] ACTION=CONTINUE에서 같은 session resume
[ ] composer clear 지연만으로 작업 중단하지 않음
[ ] 실제 user message 생성으로 Send 성공 확인
[ ] 불확실한 Send에서 자동 중복 재전송 없음
[ ] SEND_CONFIRM_RECOVERED 동작
[ ] 실제 ChatGPT Web에서 최소 2 round 자동 왕복
[ ] ACTION=END에서 FINISH_SUCCESS
```

핵심 방향은 Codex Desktop 프로젝트 선택을 필수 전제에서 제거하고 Worker가 현재 작업 폴더와 sessionId를 직접 관리하는 것, 그리고 Web Send 성공 여부를 composer clear 한 가지 DOM 신호에 의존하지 않는 것이다.


---

# 2026-09-20 TETRIS 장기 자동개발 검증 및 Git/Server 리뷰 전환 피드백

## 1. TETRIS 장기 자동개발 검증

TETRIS 작업은 ProjectHub Worker ↔ GPT Web ↔ Codex CLI 장기 왕복 구조가 실제 소프트웨어 개발 작업에서도 동작한다는 강한 E2E 사례가 됐다.

실제 흐름은 다음과 같았다.

```text
CLI 최초 상태 조사
→ [REPORT 0]
→ GPT Web 전체 계획 수립
→ [ACTION=CONTINUE]
→ Codex 구현/검증
→ REPORT
→ GPT Web 코드 검토/보정 지시
→ 반복
→ 최종 build/run
→ 사용자 체감 QA
```

검증된 범위:
- .NET 9 WPF TETRIS
- 10x20 / 7종 테트로미노
- Line Flash
- Stage 1~5
- Stage별 낙하 속도
- Score / Stage Bonus
- Soft Drop SFX
- 코드 생성 PCM 효과음
- Stage 1~5 코드 생성 BGM
- Stage Clear Curtain
- Final Clear
- Restart 및 비-Playing 상태 가드

실제 Core diagnostic:
- 152 / 152 PASS
- 1/2/3/4줄 삭제 실제 실행 검증
- Stage 1 → 2 → 3 → 4 → 5 → FinalClear 검증
- TotalClearedLines 최종 25
- BonusRows 0 / 1 / 12 / 19 / 20 edge 검증
- 10,000 operation stress
- Game Over 202회
- Restart 202회
- exception 0

WPF:
- 격리된 APPDATA / DOTNET_CLI_HOME / NUGET_PACKAGES와 외부 intermediate/output을 사용해 restore/build 성공
- warning 0 / error 0
- 최신 EXE 생성
- 프로세스 실행, MainWindow 생성, responsive 확인

최종 사용자 QA:
- 기능 정상
- BGM 적당
- SFX 적당
- Curtain은 기능상 정상이나 단일 선보다는 겹겹이 쌓이는 연출을 선호

이 사례는 완전 무인 개발이라기보다는 “사람이 목표와 최종 수용을 맡고, Web Controller가 계획/리뷰/재지시를 수행하며 Codex가 구현/테스트를 반복하는 거의 자동개발” 사례로 기록하는 것이 정확하다.

## 2. 토큰 usage 표시는 Task 실제 소비량으로 재정의 필요

현재 Worker는 각 Codex CLI 결과의 usage를 라운드마다 `_commandUsage.Add(result.Usage)`로 합산한다.

resume session의 usage가 session cumulative snapshot이면 아래처럼 중복 합산된다.

```text
round 1 session total = 300k
round 2 session total = 600k
round 3 session total = 900k

현재 Worker 표시 = 300k + 600k + 900k = 1.8M
실제 최신 session total = 900k
```

따라서 “이번 작업 누적”은 실제 Task 신규 사용량과 크게 다를 수 있다.

개선 권장:
- task 시작 시 session baseline usage 저장
- latest session usage 저장
- task delta = latest - baseline
- cached input / output / reasoning 분리
- round count / duration 함께 저장
- 기존 session resume 여부 표시

UI에서는 최소 다음을 구분한다.

```text
Task delta
Session total
Cached input
Output
Rounds
Duration
5시간/주간 한도: CLI 미제공
```

## 3. 다음 자동개발 방식: 파일 첨부 대신 Git/Server 기준 리뷰

다음 프로젝트부터는 매 라운드마다 소스 파일을 GPT Web에 첨부하는 방식을 기본으로 하지 않는다.

권장 흐름:

```text
Codex CLI
  ↓ local edit / test
Worker
  ↓ Git checkpoint / sync 확인
Git remote + ProjectHub Server
  ↓ exact state
GPT Web
  ↓ exact commit/source review
ACTION
  ↓
Worker → Codex CLI
```

핵심은 GPT Web이 “branch의 최신 상태”를 추정하지 않고 Worker가 확정한 정확한 review commit/state를 읽는 것이다.

## 4. Git 동기화는 즉시라고 가정하지 않는다

다음 상태를 명확히 분리한다.

```text
LOCAL_DIRTY
LOCAL_COMMITTED
PUSHING
PUSHED_UNCONFIRMED
REMOTE_CONFIRMED
SERVER_CONFIRMED
SYNC_MISMATCH
CONFLICT
PUSH_REJECTED
```

의미:

1. 파일 저장
   - local working tree만 변경
   - remote에서는 보이지 않음

2. local commit
   - local HEAD만 변경
   - remote에서는 아직 보이지 않음

3. push 성공
   - push command가 성공했지만 Web review 전에 remote exact SHA를 다시 확인

4. remote 확인
   - remote branch HEAD 또는 exact commit 조회로 pushed SHA 존재 확인

5. Server 확인
   - ProjectHub Server가 관찰한 branch/SHA/observed_at과 대조

기본 Web review 시작 조건은 `REMOTE_CONFIRMED` 이상으로 둔다.

Server까지 authoritative observation으로 사용할 때는 `SERVER_CONFIRMED`를 추가 확인할 수 있다.

branch 이름만 전달하지 말고 반드시 아래 exact 값 중 하나를 review 기준으로 전달한다.

```text
review_commit_sha
```

## 5. Settings에서 Git/Server 주소를 자동 설정 + 수동 입력 가능하게 한다

Git repository 주소와 ProjectHub Server 주소는 메인 화면에 하드코딩하거나 사용자가 매번 입력하게 하지 않는다.

**Settings 창에서 자동 감지된 값을 기본값으로 채우고, 사용자가 필요하면 직접 수정할 수 있게 한다.**

### Git Repository 설정

자동 감지 우선순위 권장:

```text
1. 현재 selected project path의 Git repository 확인
2. git remote get-url origin
3. ProjectHub project metadata에 저장된 repository URL
4. 기존 Worker 저장 설정
5. 값이 없으면 빈 상태 + 수동 입력
```

Settings 필드 예:

```text
Git Repository
[ https://github.com/owner/repo                 ]
[ Auto Detect ] [ Test ]

Branch
[ main                                             ]

Project Path
[ C:\Projects\MyProject                        ]
```

원칙:
- 자동 감지 성공 시 즉시 입력 필드에 표시
- 사용자가 수정하면 명시적 override로 저장
- “Auto Detect”를 다시 누르면 override를 자동값으로 되돌릴 수 있음
- remote URL은 표시용 canonical form과 실제 fetch/push용 값이 다를 수 있으므로 내부적으로 원본도 보존
- access token, PAT, credential은 URL에 포함해 저장/표시하지 않음

### ProjectHub Server 설정

자동 감지 우선순위 권장:

```text
1. PROJECTHUB_AGENT_SERVER_BASE_URL 환경 변수
2. 기존 Worker config
3. project metadata / known server setting
4. 제품 기본값
5. 수동 입력
```

현재 코드의 기본값:
```text
https://projecthub.ornithopter.bid
```

Settings 필드 예:

```text
ProjectHub Server
[ https://projecthub.ornithopter.bid             ]
[ Auto Detect ] [ Test Connection ]
```

원칙:
- 자동 감지된 값을 Settings에 보여준다.
- 사용자가 수동 변경 가능하다.
- 수동 변경값은 Worker persistent config에 저장한다.
- 환경 변수와 수동 설정의 우선순위를 UI에 명확히 정의한다.
- 권장 우선순위는 “사용자가 저장한 명시적 override > 자동 감지 > 제품 기본값”이다.
- credential/API key는 주소와 분리해서 저장하고 화면에 원문 표시하지 않는다.

## 6. Settings의 source mode 표시

각 설정값은 어디서 왔는지 알 수 있어야 한다.

예:

```text
Git Repository
https://github.com/owner/repo
Source: AUTO · origin

ProjectHub Server
https://projecthub.ornithopter.bid
Source: AUTO · environment

또는

Source: MANUAL
```

권장 내부 값:

```text
value
source = AUTO_GIT_REMOTE | AUTO_ENV | AUTO_METADATA | MANUAL | DEFAULT
last_detected_at
last_test_result
last_tested_at
```

사용자가 수동으로 입력한 값과 자동 감지값을 덮어쓰는 규칙이 불명확하면 장기 자동화 중 대상 repository/server가 바뀔 수 있으므로 반드시 source를 저장한다.

## 7. Worker 메인 화면에는 설정값의 요약만 표시

주소 편집은 Settings에서 하고 메인 화면은 현재 실제 연결 대상을 빠르게 확인하는 용도로 사용한다.

권장 메인 UI:

```text
PROJECT
MyProject
C:\Projects\MyProject
GitHub · owner/repo
main · a1b2c3d
CLEAN · REMOTE_CONFIRMED

SERVER
projecthub.ornithopter.bid
CONNECTED
Server SHA · a1b2c3d
Observed · 14:32:10

GPT WEB REVIEW
Source · GIT
Review SHA · a1b2c3d
CONFIRMED
```

전체 URL은 길면 축약하고 tooltip 또는 Settings에서 전체값을 확인한다.

## 8. Git review checkpoint 계약

각 Web review 라운드 직전에 Worker가 최소 다음 값을 확정한다.

```text
project_path
repository_url
branch
working_tree_status
local_head_sha
pushed_head_sha
remote_head_sha
last_push_result
last_push_at
server_observed_sha
server_observed_at
review_commit_sha
sync_state
```

예:

```json
{
  "repository": "https://github.com/owner/repo",
  "branch": "main",
  "local_head": "abc123",
  "pushed_head": "abc123",
  "remote_head": "abc123",
  "server_head": "abc123",
  "review_commit_sha": "abc123",
  "sync_state": "REMOTE_CONFIRMED"
}
```

GPT Web은 반드시 `review_commit_sha`의 파일을 읽고 피드백한다.

## 9. Git/Server review source 선택

Worker가 Web에 전달할 review source를 명시한다.

```text
ATTACHMENT
GIT
SERVER
```

권장 기본:
- Git repository가 있고 remote 확인 가능 → GIT
- Server가 project snapshot/source 조회 기능을 제공하고 exact SHA가 확인됨 → SERVER 가능
- Git/Server가 없는 임시 프로젝트 → ATTACHMENT fallback

다음 자동개발 E2E의 목표는 **ATTACHMENT 0회**다.

## 10. Git 작업 권한

기존 ProjectHub 정책은 유지한다.

commit/push/fetch/pull은 사용자 명시적 승인 범위에서만 수행한다.

장기 자동개발에서는 Task 시작 시 다음과 같은 task-scoped permission을 받는 방식이 적합하다.

```text
Allow auto commit for this Task
Allow push to selected repository/branch for this Task
Allow fetch for sync verification
Allow pull only when clean and policy-safe
```

충돌, dirty pull 대상, detached HEAD, merge/rebase 진행 중, push reject는 자동 해결하지 않고 PAUSE한다.

## 11. Web review 시작 전 sync gate

Web review task를 만들기 전에 Worker가 다음을 확인한다.

```text
repository configured
AND branch configured
AND local commit exists
AND push succeeded
AND remote exact SHA == review_commit_sha
AND no conflict/reject
= Web Git review 가능
```

불일치하면 GPT Web에 “최신 소스 리뷰”를 요청하지 않는다.

표시 예:

```text
SYNC WAITING
local a1b2c3d
remote 98fe210

또는

SYNC MISMATCH
Review blocked
```

push 직후 remote 조회가 아직 기대값과 다르면 짧은 확인 polling을 허용할 수 있으나 무한 대기하지 않는다.

권장:
- 수 초 간격
- 제한된 횟수
- 최종 불일치 시 PAUSE 또는 retry 가능한 상태로 종료

## 12. Server observation의 역할

Server는 Git push 자체의 성공을 대신 증명하지 않는다.

Server가 저장할 수 있는 값:

```text
project_id
workstation
repository_url
branch
observed_commit_sha
working_tree_status
observed_at
```

Worker는 remote Git confirmation과 Server observation을 독립적으로 표시한다.

예:

```text
Remote SHA  abc123 · CONFIRMED
Server SHA  abc123 · CONFIRMED
```

불일치:

```text
Remote SHA  abc123
Server SHA  98fe210
SERVER LAGGING
```

이 경우 Git review 자체는 remote SHA 기준으로 가능할 수 있지만 “Server까지 동기화 완료”라고 표시하면 안 된다.

## 13. 다음 구현 우선순위

```text
1. usage telemetry의 cumulative 중복 합산 수정
2. Settings 창에 Git Repository / Branch / Server URL 추가
3. Git/Server 자동 감지 + 수동 override + source 표시
4. 메인 화면에 현재 Git/Server target 요약 표시
5. Git sync state / exact SHA checkpoint 구현
6. remote exact commit confirmation
7. Server observed SHA/state 표시
8. Web prompt에 review source + exact SHA 전달
9. Git 기반 Web review E2E
10. ATTACHMENT 0회 자동개발 실험
```

## 14. 완료 기준

```text
[ ] Settings에서 Git repository 자동 감지
[ ] Settings에서 Git repository 수동 입력/저장
[ ] Settings에서 Server URL 자동 감지
[ ] Settings에서 Server URL 수동 입력/저장
[ ] 각 값의 AUTO/MANUAL source 확인 가능
[ ] 메인 Worker에서 repository/server 현재 대상 확인 가능
[ ] local/pushed/remote SHA 구분
[ ] REMOTE_CONFIRMED 전에는 Web Git review 차단
[ ] Web prompt에 exact review_commit_sha 포함
[ ] GPT Web이 exact commit source를 직접 읽어 review
[ ] Git 지연/불일치 상태가 사용자에게 명확히 표시
[ ] Server observed SHA와 remote SHA를 독립 비교
[ ] commit/push는 task-scoped explicit approval 안에서만 수행
[ ] 파일 첨부 없이 최소 5 round 자동개발 왕복
[ ] ACTION=END 정상 종료
```

핵심 방향은 **주소는 Settings에서 자동으로 채우되 수동 수정도 가능하게 하고, Web review의 진실 기준은 branch 최신 추정이 아니라 Worker가 동기화 확인한 exact commit SHA로 고정하는 것**이다.

---

# 2026-09-20 Optional Judge Branch v1 — 선택형 Sub AI 분기 구조

## 목적

현재의 안정화된 Web ↔ Worker ↔ Codex CLI 경로를 그대로 유지하면서, 선택적으로 Jev 같은 Sub AI를 중간 판단자(Judge)로 삽입할 수 있는 분기 구조를 추가한다.

핵심 원칙:

```text
기본 경로
GPT Web → Worker → Codex CLI → Worker → GPT Web

Judge 활성 경로
GPT Web → Worker → Codex CLI → Worker → Judge(Jev) → 분기 → GPT Web 또는 Codex
```

Judge는 필수 의존성이 아니다. 비활성화되거나 사용할 수 없으면 기존 경로가 그대로 동작해야 한다.

## 1. 역할 분리

현재 단기 역할은 다음처럼 고정한다.

```text
GPT Web = Manager / Controller / 최종 승인자
Worker  = Orchestrator / 상태·분기 관리자
Codex   = Executor / 실제 작업 수행자
Jev     = Optional Judge / 중간 검수자
```

Jev는 작업 수행자처럼 파일을 직접 수정하는 역할로 시작하지 않는다.

초기 Judge v1은 read-only 검토와 다음 분기 판단만 수행한다.

## 2. Judge는 선택형 기능

Settings에 최소 다음 옵션을 추가한다.

```text
SUB AI / JUDGE

Enable Judge   [ ]

Provider
[ Jev ]

Executable / Endpoint
[ Auto Detect / Manual ]

Timeout
[ 120 sec ]

On Judge Failure
[ Send To GPT Web ]
```

기본값은 `Enable Judge = OFF`다.

OFF일 때는 기존 Web ↔ Codex 동작과 결과가 변경되지 않아야 한다.

## 3. 분기 흐름

Judge 활성 시 Codex 한 라운드가 끝난 뒤 바로 GPT Web으로 보내지 않고 먼저 Judge를 거친다.

```text
CODEX_RUNNING
↓
CODEX_RESULT
↓
JUDGE_RUNNING
↓
JUDGE_RESULT
```

Judge 결과는 최소 세 가지로 제한한다.

```text
[JUDGE=PASS]
상위 관리자(Web) 검토로 전달 가능

[JUDGE=REVISE]
Codex가 추가 수정해야 함

[JUDGE=ESCALATE]
Judge가 확정할 수 없으므로 Web 판단 필요
```

Worker 분기:

```text
PASS
→ GPT Web에 Codex 결과 + Judge 결과 전달

REVISE
→ Judge 본문을 동일 Codex session의 다음 지시로 전달
→ Codex 재실행

ESCALATE
→ GPT Web에 Codex 결과 + Judge 판단/사유 전달
```

## 4. Judge가 전체 작업을 END시키지 않음

중요:

`JUDGE=PASS`는 전체 Task 성공 또는 ACTION=END를 의미하지 않는다.

Judge는 단지 현재 Codex 결과가 상위 Web 검토로 올라갈 수 있다는 판단만 한다.

최종 작업 종료 권한은 기존처럼 GPT Web의 ACTION=END에 둔다.

즉:

```text
Judge PASS ≠ Task END
Web ACTION=END = Task END
```

## 5. Judge 입력 계약

Jev에 Codex raw stdout 전체를 무조건 전달하지 않는다.

Worker가 최소 공통 구조로 정리해서 전달한다.

```text
ROLE=JUDGE

GOAL
<현재 Task 목표>

ROUND
<현재 round>

WORKING_DIRECTORY
<현재 작업 폴더>

CODEX_RESULT
<Codex 최종 메시지>

FILES
- 실제 생성/변경 파일 목록

VALIDATION
- build/test/lint 결과

REVIEW_SOURCE
LOCAL | GIT | ATTACHMENT

REVIEW_COMMIT_SHA
<있으면 exact sha>

REQUEST
현재 결과를 PASS / REVISE / ESCALATE 중 하나로 판단하라.
```

Git exact SHA가 유효하면 Judge도 같은 review_commit_sha를 기준으로 검토할 수 있게 한다.

Git이 없거나 remote가 확인되지 않아도 Judge 기능 자체는 막지 않는다.

## 6. Judge 응답 파서

Judge 응답은 Web ACTION 프로토콜과 별도로 관리한다.

첫 번째 유효행은 정확히 다음 중 하나여야 한다.

```text
[JUDGE=PASS]
[JUDGE=REVISE]
[JUDGE=ESCALATE]
```

REVISE는 뒤에 Codex로 전달할 본문이 반드시 있어야 한다.

예:

```text
[JUDGE=REVISE]
실패한 테스트 2개를 먼저 수정하고 다시 전체 테스트를 실행해.
```

잘못된 Judge 응답은 Worker가 의미를 추측하지 않는다.

```text
JUDGE_PROTOCOL_ERROR
→ 기본 fallback으로 GPT Web에 escalate
```

## 7. 실패 시 기존 경로 유지

Judge는 optional branch이므로 단일 장애점이 되어서는 안 된다.

다음 상황:

```text
- Jev executable/endpoint 없음
- 인증 실패
- timeout
- process crash
- invalid response
- JUDGE protocol error
```

에서 권장 기본 동작은:

```text
Judge 실패
→ Judge를 건너뜀
→ Codex 결과와 Judge 실패 사유를 GPT Web에 전달
→ 기존 Web ACTION 흐름 계속
```

즉 Judge 장애 때문에 Task 자체를 강제 실패시키지 않는다.

설정에서 향후 Strict mode를 추가할 수 있지만 v1 기본은 fallback-to-Web이다.

## 8. Codex session 유지

JUDGE=REVISE일 때 새 Codex 세션을 만들지 않는다.

현재 Job에 고정된:

```text
jobId
workingDirectory
codexSessionId
round
```

를 그대로 사용한다.

Judge가 여러 번 REVISE하더라도 같은 Codex session을 resume한다.

Judge 자체 session이 필요하면 별도 `judgeSessionId`로 분리한다.

## 9. 무한 Judge↔Codex 루프 방지

Web ACTION loop와 Judge revise loop를 분리해서 제한한다.

초기 권장:

```text
MAX_WEB_ROUNDS = 기존 값 유지
MAX_JUDGE_REVISIONS_PER_WEB_ROUND = 3
JUDGE_TIMEOUT = 120 sec
```

예:

```text
Codex
→ Judge REVISE #1
→ Codex
→ Judge REVISE #2
→ Codex
→ Judge REVISE #3
→ 아직 REVISE
→ GPT Web ESCALATE
```

Judge 때문에 Web에 영원히 도달하지 못하는 구조를 만들지 않는다.

## 10. 현재 Codex 전용 코드는 전면 일반화하지 않음

이번 구현에서 바로 전체 IAiProvider 프레임워크로 재작성하지 않는다.

단기 구현:

```text
CodexCliRunner        기존 Executor 유지
JevJudgeRunner        신규 Optional Judge
JudgeRequest          신규
JudgeResult           신규
JudgeDecision enum    PASS / REVISE / ESCALATE / ERROR
```

현재 안정화된 Codex 실행/세션/파일 수집 코드를 최대한 유지한다.

다만 향후 확장을 위해 공통 결과로 변환 가능한 얇은 모델은 허용한다.

예:

```text
AiExecutionResult
- provider
- role
- model
- sessionId
- success
- message
- files
- usage
- startedAt
- finishedAt
```

CodexCliResult를 당장 제거하지 말고 Adapter로 AiExecutionResult로 변환하는 정도만 허용한다.

## 11. Settings 확장성

Jev를 이름으로 UI에 고정하더라도 내부 설정은 provider 기반으로 저장한다.

예:

```json
{
  "judge": {
    "enabled": true,
    "provider": "jev",
    "mode": "review",
    "timeoutSeconds": 120,
    "failurePolicy": "escalate_to_web"
  }
}
```

이렇게 하면 후속에:

```text
Jev
Gemini
Claude
Local Qwen
다른 CLI/API Judge
```

로 교체할 때 Worker orchestration을 다시 뜯지 않아도 된다.

## 12. MESSAGE LOG 표시

현재 누적 MESSAGE LOG에 Judge 이벤트도 포함한다.

예:

```text
[12:31:04] CODEX
구현 및 테스트 완료

[12:31:05] JUDGE STATUS
Jev · REVIEWING

[12:31:12] JUDGE
[JUDGE=REVISE]
예외 처리 테스트가 빠져 있음

[12:31:13] WORKER → CODEX
예외 처리 테스트를 추가하고 다시 검증해.
```

Judge 비활성 상태에서는 기존 로그 형식을 유지한다.

## 13. UI 흐름

현재 Codex → Worker → GPT Web 흐름 표시를 깨지 않는다.

Judge 활성일 때만 보조 상태로:

```text
CODEX
  ↓
WORKER
  ↓
JUDGE
  ├─ REVISE → CODEX
  └─ PASS/ESCALATE → GPT WEB
```

를 표시한다.

초기 v1에서는 메인 화면 전체 레이아웃을 크게 재설계하지 않아도 된다.

`Current Task`, `MESSAGE LOG`, 또는 설정 카드에 Judge 상태 한 줄만 추가해도 충분하다.

## 14. Jev 자동 탐색

Jev가 CLI 형태라면 Codex와 동일하게 자동 탐색 계층을 둔다.

```text
1. PATH
2. 알려진 설치 경로
3. 저장된 manual path
```

API/localhost service 형태라면 endpoint + health check 구조로 adapter를 구현한다.

Jev 연결 실패는 Worker 시작 실패 조건이 아니다.

Judge OFF 또는 unavailable이면 기존 Codex/Web 경로는 READY일 수 있어야 한다.

## 15. 보안/권한

Judge v1은 read-only 원칙을 적용한다.

```text
Codex Executor
→ workspace-write 가능

Jev Judge
→ read-only
→ 직접 파일 수정 금지
→ git commit/push 금지
→ 외부 destructive action 금지
```

Judge가 제안한 수정은 반드시 Worker를 거쳐 Codex에게 전달한다.

기존 commit/push/fetch/pull 승인 정책도 그대로 유지한다.

## 16. 첫 E2E 검증

작은 코드 작업으로 다음을 실제 검증한다.

```text
1. Judge OFF
   Web → Codex → Web 기존 경로 정상

2. Judge ON
   Web → Codex
   → Jev REVISE
   → 동일 Codex session 수정
   → Jev PASS
   → GPT Web 전달
   → Web ACTION=END

3. Judge timeout
   → GPT Web fallback

4. Judge invalid response
   → GPT Web fallback

5. Judge OFF 재전환
   → 기존 경로 정상
```

## 17. 완료 기준

```text
[ ] Judge 기본값 OFF
[ ] Judge OFF에서 기존 E2E 회귀 없음
[ ] Jev 자동/수동 연결 가능
[ ] Judge는 Codex 결과 후에만 실행
[ ] JUDGE=PASS → Web
[ ] JUDGE=REVISE → 같은 Codex session
[ ] JUDGE=ESCALATE → Web
[ ] Judge 오류/timeout → Web fallback
[ ] Judge 오류가 전체 Worker 실행을 막지 않음
[ ] 최대 revise 횟수 제한
[ ] Judge read-only
[ ] MESSAGE LOG에 Judge 이벤트 기록
[ ] Judge enable/disable 설정 영속화
[ ] Jev를 다른 provider로 교체 가능한 설정 구조
[ ] 기존 ACTION=CONTINUE/PAUSE/END 프로토콜 유지
[ ] build/test 통과
[ ] 실제 Judge ON/OFF E2E 검증
```

핵심 완료 기준은 Jev를 추가하면서도 기존 Web ↔ Codex 경로가 필수조건으로 변하지 않는 것이다. Judge는 품질 검수를 위한 선택형 분기이며, 비활성화 또는 실패 시 항상 기존 안정화 경로로 되돌아갈 수 있어야 한다.


---

# 2026-09-21 JEV Contract Gate 분기 시스템 도입 피드백

## 1. 기존 Optional Judge 설계 정정

이전 피드백의 `JUDGE=PASS / REVISE / ESCALATE` 중심 설계를 이번 Contract Gate 기준으로 단순화한다.

JEV와 Codex CLI는 서로 직접 통신하지 않는다.

모든 메시지는 반드시 Worker가 먼저 수신하고 다음 목적지로 전달한다.

```text
GPT Web
  ↓
Worker
  ↓
Codex CLI
  ↓
Worker
  ├─ WEB 분기 → GPT Web
  └─ JEV 분기 → JEV
                  ↓
                Worker
                  ↓
             후속 목적지
```

Worker는 의미적 검증자가 아니라 **Message Router + Task State Holder**로 유지한다.

## 2. JEV footer 계약 파일

Worker 프로젝트 경로에 다음 계약 파일을 기준으로 둔다.

```text
src/ProjectHub.Worker/JEV-FOOTER-CONTRACT.md
```

Worker가 Codex CLI에 작업을 전달할 때 이 계약 내용을 footer로 붙일 수 있는 구조를 만든다.

초기 구현에서는 계약 파일을 코드에 하드코딩해서 복제하지 말고 파일을 기준 원본으로 삼는다.

단일 파일 publish 단계에서 필요하면 EmbeddedResource로 포함한 뒤 런타임에서 읽는 방법을 사용한다.

## 3. 계약은 Variable Contract + Fixed Contract로 분리

### Variable Contract

Codex CLI가 현재 결과에 대해 JEV 검증이 필요하다고 판단했을 때 검증 요청을 정의한다.

지원 타입은 세 가지로 한정한다.

```text
NOUL
- YES/NO 확률 판정
- 질문 + threshold
- 예: YES >= 0.90

SCORE
- 단계/수치형 평가
- 척도 정의 + threshold
- 예: SCORE <= 2.0

CHOICE
- 의미적 상태 분류
- 선택지 정의 + 허용값
- 예: EXPECTED 또는 MINOR
```

질문과 문턱값은 Task마다 바뀔 수 있다.

Worker는 질문의 의미를 이해하거나 자체 평가하지 않는다.

### Fixed Contract

Codex CLI 응답의 첫 유효행은 반드시 둘 중 하나다.

```text
[NEXT : WEB]
[NEXT : JEV]
```

`[NEXT : WEB]`이면 다음에 `[REPORT]`를 작성한다.

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
- ...
```

Worker는 이를 그대로 GPT Web 전달 경로로 보낸다.

`[NEXT : JEV]`이면 다음에 `[VALIDATION REQUEST]`만 작성한다.

```text
[NEXT : JEV]

[VALIDATION REQUEST]

- NOUL | ... | PASS: ...
- SCORE | ... | PASS: ...
- CHOICE | ... | PASS: ...
```

이 경우 보고서나 장문의 자체평가를 붙이지 않는다.

Worker는 이를 JEV 호출 입력으로 전달한다.

## 4. Worker의 책임 범위

Worker가 해야 할 일:

```text
1. Codex CLI 작업 지시에 JEV footer 계약 추가
2. CLI 응답 수신
3. 첫 유효행의 NEXT 태그 파싱
4. [NEXT : WEB] → 기존 GPT Web 전달
5. [NEXT : JEV] → VALIDATION REQUEST를 JEV로 전달
6. JEV 응답 수신
7. Task 상태에 기록
8. 정의된 후속 목적지로 다시 전달
9. MESSAGE LOG에 각 hop 기록
```

Worker가 하지 않을 일:

```text
- 요구사항 충족 여부 자체 판단
- 소스코드 의미 분석
- build/test 결과 자체 추론
- JEV 대신 threshold 의미 판단
- Codex와 JEV 사이의 직접 통신 허용
```

JEV API 응답의 구조화된 숫자/choice와 계약 threshold의 **기계적 비교**만 허용한다.

## 5. JEV 호출 데이터

JEV 호출 시 최소한 다음 두 정보를 함께 전달한다.

```text
1. Codex CLI가 반환한 현재 결과/문맥
2. [VALIDATION REQUEST]에 정의된 검증 항목
```

필요하다면 원래 Task 지시도 state에 함께 포함할 수 있지만 Worker가 내용을 요약하거나 재해석하지 않는다.

JEV API Key는 사용자 환경변수 `TYPESAFE_API_KEY`에서만 읽는다.

키 원문을 설정 파일, 로그, transcript, Git에 기록하지 않는다.

## 6. JEV 결과 이후 분기

초기 v1에서는 복잡한 자유형 Judge protocol을 만들지 않는다.

각 VARIABLE CHECK의 PASS 조건을 기계적으로 비교한다.

기본 정책:

```text
모든 요청 검증 PASS
→ Worker → GPT Web

하나 이상 FAIL
→ Worker → Codex CLI

JEV 호출 실패 / 응답 파싱 불가 / 판단 불확실
→ Worker → GPT Web
```

FAIL 시 Worker가 Codex에 전달할 내용은 실패한 검증 항목과 실제 JEV 결과로 제한한다.

예:

```text
[JEV VALIDATION FAILED]

NOUL | 요구사항을 충족했는가?
Expected: YES >= 0.90
Actual: YES 0.71

SCORE | 범위 이탈 정도
Expected: SCORE <= 2.0
Actual: 3.4

위 검증 실패를 해소한 뒤 다시 최종 응답을 제출하라.
```

이 후속 메시지도:

```text
JEV → Worker → Codex CLI
```

순서를 반드시 지킨다.

## 7. JEV는 선택 기능

JEV는 ProjectHub Worker 실행의 필수조건이 아니다.

```text
Judge OFF
→ 기존 GPT Web ↔ Worker ↔ Codex CLI 경로 그대로 유지

Judge ON
→ [NEXT : JEV]가 있을 때만 JEV 호출
```

Judge ON이라고 해서 모든 Codex 결과를 무조건 JEV에 보내지 않는다.

Codex가 `[NEXT : WEB]`을 반환하면 바로 Web 경로를 사용한다.

즉 JEV 사용 여부는 **Task footer 계약 아래에서 CLI가 요청하는 선택형 검증 분기**다.

## 8. 반복 제한

JEV FAIL → Codex 수정 → 다시 JEV 요청이 반복될 수 있으므로 Worker가 횟수만 관리한다.

권장 초기값:

```text
MAX_JEV_VALIDATION_ROUNDS = 3
```

상한을 넘으면 의미 판단 없이 GPT Web으로 올린다.

```text
JEV_RETRY_LIMIT
→ Worker → GPT Web
```

Web 관리자가 이후 ACTION을 결정한다.

## 9. 기존 ACTION 프로토콜과 관계

GPT Web의 기존:

```text
[ACTION=CONTINUE]
[ACTION=PAUSE]
[ACTION=END]
```

프로토콜은 그대로 유지한다.

NEXT 계약과 ACTION 계약의 역할은 다르다.

```text
NEXT
= Codex 결과를 Worker가 다음 어디로 전달할지 지정

ACTION
= GPT Web이 Worker에게 다음 작업 상태를 지시
```

둘을 하나의 parser나 enum으로 섞지 않는다.

## 10. 첫 구현 범위

이번 단계에서는 범용 Multi-AI workflow engine을 만들지 않는다.

필요한 최소 범위:

```text
- JEV-FOOTER-CONTRACT.md 로드
- CLI prompt footer 삽입
- NEXT parser
- VALIDATION REQUEST parser
- TypeSafe JEV HTTP adapter
- TYPESAFE_API_KEY 환경변수 사용
- NOUL / SCORE / CHOICE 요청 생성
- threshold 비교
- WEB / CLI 후속 routing
- retry limit
- MESSAGE LOG 기록
```

현재 `JevJudgeRunner.cs` scaffold는 이 Contract Gate 방식에 맞게 수정한다.

기존의 `JudgeDecision PASS/REVISE/ESCALATE`가 새 계약과 충돌한다면 호환을 억지로 유지하지 말고 NEXT + validation result 중심으로 정리한다.

## 11. 첫 검증 시나리오

TETRIS 같은 작은 코드 변경으로 다음만 먼저 확인한다.

```text
Case A
Codex → Worker
[NEXT : WEB]
→ GPT Web 정상 전달

Case B
Codex → Worker
[NEXT : JEV]
NOUL 검증 PASS
→ JEV → Worker → GPT Web

Case C
Codex → Worker
[NEXT : JEV]
NOUL/SCORE/CHOICE 중 하나 FAIL
→ JEV → Worker → 동일 Codex session
→ Codex 재작업
→ Worker
→ JEV 재검증
→ PASS
→ Worker → GPT Web

Case D
JEV API timeout/error
→ Worker → GPT Web fallback

Case E
Judge OFF
→ 기존 Web/Codex E2E 회귀 없음
```

## 12. 완료 기준

```text
[ ] JEV footer 계약 파일이 Worker 프로젝트에 존재
[ ] Worker가 CLI 요청에 footer를 붙일 수 있음
[ ] 첫 유효행 [NEXT : WEB] 파싱
[ ] 첫 유효행 [NEXT : JEV] 파싱
[ ] WEB 선택 시 REPORT 포함
[ ] JEV 선택 시 VALIDATION REQUEST만 사용
[ ] NOUL 질문 + threshold 지원
[ ] SCORE 척도 + threshold 지원
[ ] CHOICE 선택지 + 허용값 지원
[ ] 모든 통신이 Worker를 경유
[ ] JEV와 Codex의 직접 통신 없음
[ ] TYPESAFE_API_KEY를 환경변수에서만 읽음
[ ] API key 로그/파일/Git 기록 없음
[ ] JEV PASS → Worker → Web
[ ] JEV FAIL → Worker → Codex
[ ] JEV error/uncertain → Worker → Web
[ ] JEV retry 상한 존재
[ ] Judge OFF 기존 흐름 유지
[ ] 기존 GPT Web ACTION protocol 유지
[ ] 실제 Judge ON/OFF E2E 검증
```

핵심은 **Worker가 AI처럼 관측·판단하도록 확장하는 것이 아니라, Codex가 선택한 NEXT 계약과 JEV의 구조화 결과를 받아 모든 메시지를 정확한 다음 목적지로 전달하는 분기 허브가 되는 것**이다.


---

# 2026-09-21 JEV Contract Gate 본 구현 진행 피드백

## 1. 중간 구현 확인 결과

최신 커밋 `7f61afc12588499ba5c3c1f9776bcf738c29c06a`의 JEV Contract Gate 중간 구현을 확인했다.

현재까지 다음 뼈대는 적절하게 반영됐다.

```text
- JEV-FOOTER-CONTRACT.md를 EmbeddedResource로 포함
- Judge ON에서 Codex prompt footer 삽입
- Codex 결과 첫 유효행의 [NEXT : WEB] / [NEXT : JEV] 파싱
- [VALIDATION REQUEST] 추출
- 모든 hop을 Worker가 수신/전달하는 구조 유지
- JEV FAIL 시 동일 Codex session으로 되돌리는 틀
- 최대 3회 제한
- JEV error 시 Web fallback
- Judge OFF에서 기존 흐름 유지
```

이제 scaffold 단계에서 멈추지 말고 실제 TypeSafe JEV API를 연결해 E2E까지 마무리한다.

## 2. 중요: API 계약이 이미 저장소에 존재함

현재 `CurrentWork.md`에는 다음 취지의 기록이 있다.

```text
현재 저장소에는 TypeSafe/JEV provider의 실제 실행 계약(endpoint payload/response)이 제공되지 않았으므로
외부 호출을 추측해 추가하지 않았다.
```

하지만 이 구현 커밋의 parent인 `ec79f43db8ad12ccbdec778ca05150b2323bda48`에 이미 다음 파일이 존재한다.

```text
src/ProjectHub.Worker/JEV-API-CONTRACT.md
```

따라서 이제는 provider 미정 상태로 간주하지 않는다.

본 작업 전에 반드시 최신 main을 다시 동기화하고 다음 두 파일을 함께 기준으로 읽는다.

```text
src/ProjectHub.Worker/JEV-FOOTER-CONTRACT.md
src/ProjectHub.Worker/JEV-API-CONTRACT.md
```

API 구조를 새로 추측하거나 다른 비공식 endpoint로 바꾸지 않는다.

공식 TypeSafe 경로 기준:

```text
POST https://api.typesafe.ai/v1/systemone
Authorization: Bearer <TYPESAFE_API_KEY>
Content-Type: application/json
```

환경변수:

```text
TYPESAFE_API_KEY
```

## 3. 이번 작업 목표

이번 작업의 목표는 UI scaffold 추가가 아니라 **실제로 [NEXT : JEV]가 발생했을 때 TypeSafe JEV API를 호출하고, 구조화 응답을 Worker가 기계적으로 판정한 뒤 다음 hop으로 전달하는 것**이다.

최소 완성 흐름:

```text
Codex CLI
  ↓
Worker
  ↓ [NEXT : JEV]
TypeSafe JEV API
  ↓
Worker
  ├─ FAIL → 같은 Codex session
  └─ PASS → 같은 Codex session에 WEB 보고서 생성 요청
                ↓
              Worker
                ↓ [NEXT : WEB] + [REPORT]
              GPT Web
```

모든 통신은 계속 Worker를 경유한다.

## 4. 가장 중요한 라우팅 정정: JEV PASS 직후 Web으로 바로 보내지 말 것

현재 구현은 JEV PASS일 때 `directive.Body + [JEV PASS]`를 Web prompt로 만들 수 있다.

하지만 footer 계약상 `[NEXT : JEV]` 뒤에는 **검증 요청만** 존재한다.

즉:

```text
[NEXT : JEV]
[VALIDATION REQUEST]
...
```

에는 GPT Web에 보여줄 작업 완료 보고서가 없다.

따라서 JEV PASS를 곧바로:

```text
JEV → Worker → GPT Web
```

으로 보내면 안 된다.

PASS 시에는 Worker가 같은 Codex session에 고정된 후속 지시를 한 번 보낸다.

예:

```text
[JEV VALIDATION PASSED]

요청한 JEV 검증이 모두 통과했다.
추가 구현이나 변경은 하지 말고 현재 작업 상태를 기준으로
[NEXT : WEB]으로 시작하는 [REPORT]를 작성하라.
```

그 후:

```text
Codex
→ Worker
→ [NEXT : WEB]
→ GPT Web
```

으로 전달한다.

이렇게 해야 고정 계약:

```text
WEB이면 REPORT
JEV이면 VALIDATION REQUEST만
```

이 끝까지 유지된다.

## 5. JevJudgeRunner를 실제 HTTP adapter로 구현

현재 `JevJudgeRunner`의 항상 Error fallback 하는 scaffold를 실제 API 호출로 교체한다.

요구사항:

```text
- HttpClient 재사용
- endpoint = https://api.typesafe.ai/v1/systemone
- TYPESAFE_API_KEY 환경변수에서만 key 읽기
- Bearer 인증
- timeout은 기존 Judge setting 사용
- model 기본값 jev-latest
- API key를 로그/예외/설정/transcript에 남기지 않기
```

API key가 없으면 외부 호출 없이 명확한 JEV ERROR로 반환하고 Web fallback한다.

## 6. VALIDATION REQUEST 파서 구현

현재 `ExtractValidationRequest`는 문자열 추출만 한다.

이번 작업에서는 이를 실제 typed request로 파싱한다.

지원 범위는 footer 계약의 세 타입으로 제한한다.

```text
NOUL
- question
- PASS YES >= threshold

SCORE
- question
- ordered criteria
- PASS SCORE <= / >= threshold

CHOICE
- question
- key = description criteria
- PASS allowed choices
```

내부적으로 각 항목에 `C1, C2, C3...` ID를 부여한다.

JEV에는 여러 질문을 한 번의 `questions` map으로 보낸다.

Worker는 질문 의미를 재작성하지 않는다.

## 7. state 구성

JEV의 `state`에는 최소 다음을 포함한다.

```json
{
  "task": "<원래 Codex 작업 지시 원문>",
  "codex_result": "<현재 Codex 최종 응답 원문>"
}
```

필요하면 다음 비해석 메타데이터만 추가한다.

```text
round
working_directory
```

Worker가 내용을 요약하거나 평가해서 새로운 의미를 넣지 않는다.

## 8. NOUL / SCORE / CHOICE 응답 판정

판정은 의미 추론 없이 계약과 API 응답의 기계 비교만 한다.

```text
NOUL
answer.noul >= threshold

SCORE
answer.score 와 정규화된 threshold 비교

CHOICE
answer.choice 가 허용값 집합에 포함되는지 비교
```

SCORE는 중요하다.

JEV API의 score는 criteria 배열의 0-based index 공간을 사용한다.

Footer의 사람용 표기가:

```text
1 = ...
2 = ...
3 = ...
4 = ...
5 = ...
PASS: SCORE <= 2.0
```

이면 API 비교 threshold는:

```text
1.0
```

으로 정규화한다.

이 규칙은 `JEV-API-CONTRACT.md` 기준으로 구현하고 테스트를 추가한다.

## 9. FAIL 경로

검증 항목 중 하나라도 FAIL이면 Worker가 같은 Codex session으로 보낸다.

Worker가 새로운 의미적 수정안을 만들지 않는다.

전달 내용은 실패한 계약과 실제 결과로 제한한다.

예:

```text
[JEV VALIDATION FAILED]

C2
TYPE: SCORE
QUESTION: 요구사항 대비 범위 이탈 정도는 어느 수준인가?
EXPECTED: SCORE <= 2.0
ACTUAL: 3.4
RESULT: FAIL

해당 검증 실패를 해소한 뒤
다시 [NEXT : WEB] 또는 [NEXT : JEV] 형식으로 최종 응답을 제출하라.
```

그 결과 역시 반드시:

```text
Codex → Worker
```

로 돌아온 뒤 다음 분기를 수행한다.

## 10. retry count 범위 정정

현재 `_judgeRound`가 Task 전체에서 누적되면 이후 Web ACTION=CONTINUE 라운드에서 JEV를 다시 사용할 수 없게 될 수 있다.

JEV 반복 제한은 **현재 Web→Codex 작업 라운드별**로 관리한다.

권장:

```text
MAX_JEV_VALIDATION_ROUNDS_PER_WEB_ROUND = 3
```

다음 상황에서 JEV validation count를 0으로 reset한다.

```text
- 새 Task 시작
- GPT Web ACTION=CONTINUE로 새로운 Codex 작업 라운드 시작
- JEV PASS 후 [NEXT : WEB] 보고서 전달 완료
```

Web 전체 Task round와 JEV validation round를 같은 counter로 섞지 않는다.

## 11. 계약 형식 검증

Judge ON 상태에서는 NEXT 태그만 보지 말고 최소 구조도 확인한다.

```text
[NEXT : WEB]
→ [REPORT] 필요

[NEXT : JEV]
→ [VALIDATION REQUEST] 필요
```

형식이 잘못됐다고 Worker가 내용을 추측하지 않는다.

v1 fallback:

```text
CONTRACT_PROTOCOL_ERROR
→ Worker → GPT Web
```

원래 Codex 결과와 오류 이유를 함께 전달한다.

무한 자동 재질문은 만들지 않는다.

## 12. 실제 API smoke test를 먼저 수행

본격적인 Worker E2E 전에 실제 API 1회 호출부터 확인한다.

환경변수는 이미 사용자가 준비한 `TYPESAFE_API_KEY`를 사용한다.

최소 smoke test:

```text
state:
간단한 문자열

question:
NOUL 1개

확인:
- HTTP 2xx
- answers 존재
- noul 값 0.0~1.0
- 인증 성공
- key가 로그에 노출되지 않음
```

실제 API 응답 형태가 `JEV-API-CONTRACT.md`와 다르면 추측으로 보정하지 말고 실제 응답 샘플에서 비밀값을 제거한 뒤 문서를 먼저 갱신한다.

## 13. 단위 테스트

최소 다음 테스트를 추가한다.

```text
JevContract
- 첫 유효행 [NEXT : WEB]
- 첫 유효행 [NEXT : JEV]
- 뒤쪽에 NEXT 문자열이 있어도 첫 유효행만 사용
- WEB인데 REPORT 없음
- JEV인데 VALIDATION REQUEST 없음

Validation parser
- NOUL 1개
- SCORE 1개 + 1-based→0-based threshold 정규화
- CHOICE 1개 + 복수 PASS choice
- NOUL/SCORE/CHOICE 혼합
- malformed contract 거부

Response evaluation
- ALL PASS
- NOUL FAIL
- SCORE FAIL
- CHOICE FAIL
- question ID missing
- response type mismatch
- invalid choice
```

HTTP adapter는 실제 네트워크 대신 mock handler를 사용한 단위 테스트를 추가한다.

## 14. E2E 완료 조건

단위 테스트만으로 완료 처리하지 않는다.

실제 Explorer 실행본 기준으로 최소 다음을 수행한다.

```text
A. Judge OFF
Web → Worker → Codex → Worker → Web
기존 동작 회귀 없음

B. Judge ON / [NEXT : WEB]
JEV 호출 없이 바로 Web에 REPORT 전달

C. Judge ON / [NEXT : JEV] / PASS
Codex → Worker → 실제 JEV API
→ Worker → 같은 Codex session에 REPORT 생성 요청
→ Worker → Web

D. Judge ON / [NEXT : JEV] / FAIL
Codex → Worker → 실제 JEV API
→ Worker → 동일 Codex session 보완
→ Worker → JEV 재검증
→ PASS
→ Codex WEB REPORT
→ Worker → Web

E. API error 또는 key 없음
→ Worker → Web fallback

F. malformed contract
→ Worker → Web fallback
```

MESSAGE LOG에서 각 hop이 실제 순서대로 보여야 한다.

```text
CODEX → WORKER
WORKER → JEV
JEV → WORKER
WORKER → CODEX
CODEX → WORKER
WORKER → WEB
```

JEV와 Codex가 직접 통신하는 것처럼 기록하지 않는다.

## 15. 문서 갱신

작업 완료 후 `CurrentWork.md`의 기존:

```text
provider 계약이 없어 실제 호출을 연결하지 않았다
```

기록은 역사 기록으로 남겨도 되지만, 최신 섹션에는 실제 연결 상태를 명확히 적는다.

최종 보고에는:

```text
- 변경 파일
- 실제 API smoke test 결과
- build 결과
- test 결과
- Explorer E2E 결과
- Judge OFF 회귀 결과
- Judge ON PASS/FAIL 경로 결과
- 남은 제한사항
```

을 포함한다.

## 16. 이번 작업에서 하지 않을 것

```text
- 범용 Multi-AI workflow engine 재설계
- JEV 이외 provider 추가
- Worker가 파일/코드 의미를 관측·판단하도록 확장
- JEV가 직접 Codex를 호출하는 구조
- API key를 설정 UI에 저장
- 기존 GPT Web ACTION 프로토콜 변경
- unrelated UI 리팩터링
```

이번 목표는 **현재 만들어진 Contract Gate scaffold를 실제 TypeSafe JEV 호출까지 연결하고, Worker가 모든 hop을 관리하는 완전한 선택형 검증 분기를 E2E로 증명하는 것**이다.

---

# 2026-09-22 GPT Web 최신 피드백 — 09-B JEV v1 계약 정합성·라우팅 보수

## 1. 이번 활성 범위

최신 `main`의 `Master-Polish.md`와 `tasks/09-ai-role-dev-tool.md`를 기준으로 다음 한 단계만 진행한다.

```text
09-B — JEV v1 계약 정합성과 라우팅 보수
```

09-A 문서 설계는 완료 상태다. 09-C의 evidence 전달, 10-A 이후 비용/복구/adapter 작업, 07 Force Restore 잔여 검증을 이번 구현에 섞지 않는다.

작업 시작 전 반드시 최신 `main`을 동기화하고 다음 순서로 읽는다.

```text
1. AGENTS.md
2. ProjectHub_IMPLEMENTATION_PLAN.md
3. CurrentWork.md 상단 최신 요약
4. tasks/09-ai-role-dev-tool.md
5. GPT-Web-Feedback.md의 이 최신 섹션
6. Master-Polish.md의 1~3절, 8절, 11절
7. src/ProjectHub.Worker/JEV-FOOTER-CONTRACT.md
8. src/ProjectHub.Worker/JEV-API-CONTRACT.md
```

`GPT-Web-Feedback.md`는 GPT Web 관제 전용 문서다. Codex Desktop에서는 **읽기 전용**으로 사용하고 수정하지 않는다.

## 2. 보존할 공개 계약

다음 계약은 이번 작업에서 이름·의미·wire 형식을 바꾸지 않는다.

```text
GPT Web → Worker
[ACTION=CONTINUE]
[ACTION=PAUSE]
[ACTION=END]

Codex → Worker
[NEXT : WEB]
[NEXT : JEV]
```

역할도 유지한다.

```text
GPT Web = 관리자/관제
Worker = 모든 메시지 수신, 상태 보유, 기계적 파싱/비교, 라우팅
Codex CLI = 실제 구현 작업자
JEV = 선택형 의미 검증자
```

Codex와 JEV는 직접 통신하지 않는다.

```text
Codex → Worker → JEV
JEV → Worker → Codex 또는 Web
```

Worker가 코드나 요구사항의 의미를 대신 판단하도록 확장하지 않는다. 허용되는 판단은 계약 파싱, 타입/범위 검증, threshold의 기계적 비교, 상태 전이 검증뿐이다.

Judge OFF 경로는 기존 동작을 그대로 보존한다.

## 3. 이번 09-B의 핵심 문제

현재 구현에는 실제 JEV HTTP adapter와 NOUL/SCORE/CHOICE 평가 틀이 이미 있다. 새 provider 연결 작업이 아니다.

이번에 고칠 대상은 다음이다.

```text
A. parser가 계약 문법을 정확히 받아들이는가
B. 정상 FAIL과 잘못된 응답 ERROR를 구분하는가
C. NEXT WEB / NEXT JEV 구조를 강제하는가
D. JEV PASS 뒤 보고서 전용 단계가 JEV로 재진입하지 않는가
E. JEV retry count가 현재 Web→Codex round 범위로 제한되는가
F. ERROR fallback 이유가 GPT Web에 전달되는가
```

## 4. Parser / Contract 정합성

### 4.1 NEXT

Codex 결과의 **첫 유효행**만 NEXT 제어행으로 판정한다.

허용:

```text
[NEXT : WEB]
[NEXT : JEV]
```

본문, 코드블록, 인용문 안의 뒤쪽 NEXT 문자열을 새로운 제어 명령으로 재해석하지 않는다.

Judge ON에서:

```text
[NEXT : WEB]
→ [REPORT] 필수

[NEXT : JEV]
→ [VALIDATION REQUEST] 필수
```

`NEXT : JEV` 결과를 Web용 완료 보고서로 간주하지 않는다.

구조가 잘못되면 Worker가 내용을 추측해서 고치지 않는다.

```text
CONTRACT_PROTOCOL_ERROR
→ Worker → GPT Web
```

원래 Codex 결과와 짧은 기계적 오류 이유를 함께 보낸다.

### 4.2 NOUL

현재 문서 이력에 한 줄형과 두 줄형이 모두 존재하므로 둘 다 안전하게 파싱한다.

한 줄형:

```text
- NOUL | 제공된 증거가 AC-1을 뒷받침하는가? | PASS: YES >= 0.90
```

두 줄형:

```text
- NOUL | 제공된 증거가 AC-1을 뒷받침하는가?
  PASS: YES >= 0.90
```

중요:

- 한 질문의 PASS가 다음 질문에 잘못 연결되지 않아야 한다.
- 질문이 비어 있으면 protocol error다.
- threshold는 유한한 `0.0..1.0` 범위만 허용한다.
- API의 NOUL 값도 유한한 `0.0..1.0` 범위여야 한다.
- 정상 범위의 값이 threshold를 못 넘은 경우만 `FAIL`이다.
- 누락, NaN/Infinity, 범위 밖 값, type mismatch는 `ERROR`다.

### 4.3 SCORE

Footer의 사람용 criteria 번호는 1-based, JEV API score 공간은 0-based라는 현재 계약을 유지한다.

예:

```text
1 = 정상
2 = 작은 인접 변경
3 = 요구하지 않은 변경
4 = 다른 목표

PASS: SCORE <= 2.0
```

API 비교 threshold:

```text
2.0 - 1.0 = 1.0
```

검증:

- criteria 번호는 1부터 연속이어야 한다.
- criteria가 비거나 중복 번호/건너뛴 번호가 있으면 protocol error다.
- 사람용 threshold가 정의된 criteria 범위를 벗어나면 protocol error다.
- API score는 유한하고 `0..criteriaCount-1` 범위여야 한다.
- 정상 score의 threshold 미충족만 `FAIL`.
- 누락/type mismatch/NaN/Infinity/범위 밖 score는 `ERROR`.

### 4.4 CHOICE

- 선택지 key와 설명을 그대로 parser가 보존한다.
- PASS에 적힌 허용 choice는 반드시 정의된 선택지여야 한다.
- API answer.choice가 정의된 선택지 중 하나일 때만 정상 응답으로 본다.
- 정의된 값이지만 허용 집합 밖이면 `FAIL`.
- 누락/type mismatch/정의되지 않은 choice는 `ERROR`.

### 4.5 Question ID와 응답 구조

Worker가 요청 항목에 `C1, C2, C3...`를 부여한 현재 방식을 유지한다.

다음은 모두 JEV 결과의 `ERROR`다.

```text
- 요청한 Cn 응답 누락
- 알 수 없는/중복 응답 ID로 정상 응답을 대체할 수 없음
- 요청 type과 응답 type 불일치
- type별 필수 값 누락
- type별 값 범위 위반
- 파싱할 수 없는 JSON/응답 구조
```

이 오류들을 구현 실패인 `FAIL`로 바꿔 Codex에 재작업시키지 않는다.

## 5. PASS / FAIL / ERROR 라우팅

### PASS

모든 검증이 정상 응답이며 threshold를 통과했을 때만 PASS다.

PASS 직후 Web으로 바로 보내지 않는다. 기존 v1 계약대로 **같은 Codex session에 보고서 전용 요청을 1회** 보낸다.

```text
[JEV VALIDATION PASSED]

요청한 JEV 검증이 모두 통과했다.
추가 구현이나 변경은 하지 말고 현재 작업 상태를 기준으로
[NEXT : WEB]으로 시작하는 [REPORT]를 작성하라.
```

이 시점부터 해당 호출은 `REPORT_ONLY` 성격으로 취급한다.

보고서 전용 응답에서 허용되는 정상 종료는:

```text
[NEXT : WEB]
[REPORT]
...
```

뿐이다.

보고서 전용 응답이 다시 `[NEXT : JEV]`를 요구하면 **JEV를 다시 호출하지 않는다.**

```text
REPORT_PHASE_REENTERED_JEV
→ CONTRACT_PROTOCOL_ERROR
→ Worker → GPT Web fallback
```

이 보수로 PASS→report 요청→JEV 재진입 루프를 차단한다.

### FAIL

JEV 응답 자체는 정상이고 하나 이상의 계약 조건만 미달한 경우다.

```text
JEV → Worker
Worker → 같은 Codex session
```

Worker가 새로운 해결책을 만들지 않는다. 실패한 항목의 다음 정보만 전달한다.

```text
ID
TYPE
QUESTION
EXPECTED
ACTUAL
RESULT: FAIL
```

Codex가 다시 결과를 내면 반드시 Worker가 NEXT를 다시 파싱한다.

FAIL 상태가 남아 있는데 Codex가 `NEXT : WEB`을 선택했다고 해서 Worker가 검증 성공으로 바꾸지 않는다. Web에는 미해결 JEV FAIL이 존재한다는 기계적 상태를 함께 전달할 수 있다.

### ERROR

계약 위반, 응답 누락/type mismatch/범위 오류, key 없음, HTTP 오류, timeout, 잘못된 JSON 등은 ERROR다.

```text
JEV ERROR
→ 자동 구현 재작업으로 변환하지 않음
→ Worker → GPT Web fallback
```

Web prompt에는 원래 Codex 결과를 보존하고 최소한 다음 기계 정보를 추가한다.

```text
[JEV FALLBACK]
CODE: <stable short code>
ROUND: <current>/<max>
DETAIL: <secret 없는 짧은 이유>
```

API key, Authorization header, 민감 URL, 응답 전체 dump를 넣지 않는다.

## 6. JEV validation round 범위

JEV validation count는 Task 전체 누적이 아니라 **현재 GPT Web → Codex 작업 라운드별**로 관리한다.

초기 상한:

```text
MAX_JEV_VALIDATION_ROUNDS_PER_WEB_ROUND = 3
```

첫 JEV 검증도 1회로 센다. 따라서 추가 보완 기회는 최대 2회다.

count를 새로 시작하는 경계:

```text
- 새 Task 시작
- GPT Web ACTION=CONTINUE로 새 Codex 작업 라운드 시작
```

정상 PASS 후 report가 Web에 전달되어 해당 라운드가 끝나면 그 round의 judge state를 종료한다.

JEV retry count를 Web ACTION round count와 하나의 변수로 섞지 않는다.

상한 초과 시 의미 판단 없이:

```text
JEV_RETRY_LIMIT
→ Worker → GPT Web
```

으로 보낸다.

## 7. Worker 전용 fixture 테스트

이번 09-B 완료에는 Worker/JEV 전용 자동 테스트가 필요하다.

기존 `tests/`에 적절한 Worker test project가 없다면 이번 범위 안에서 최소 테스트 프로젝트를 추가할 수 있다. 테스트를 위해 제품 계약을 별도 복제하지 말고 실제 parser/evaluator/routing 코드를 참조한다.

최소 fixture:

```text
NEXT / structure
- 첫 유효행 NEXT WEB
- 첫 유효행 NEXT JEV
- 뒤쪽 NEXT 문자열 무시
- WEB인데 REPORT 없음 → protocol error
- JEV인데 VALIDATION REQUEST 없음 → protocol error

NOUL
- 한 줄 PASS
- 두 줄 PASS
- threshold 경계값
- threshold 범위 밖
- answer 0.0 / 1.0 경계
- answer 누락
- answer type mismatch
- answer <0 / >1 / non-finite

SCORE
- 정상 1-based→0-based 변환
- 정확한 threshold 경계
- 연속되지 않은 criteria 번호
- threshold 범위 밖
- API score 음수/상한 초과/non-finite
- response type mismatch

CHOICE
- 단일 허용값 PASS
- 복수 허용값
- 정의됐지만 비허용값 → FAIL
- PASS에 정의되지 않은 choice → protocol error
- API가 정의되지 않은 choice 반환 → ERROR
- type mismatch

Response envelope
- question ID missing → ERROR
- malformed JSON → ERROR
- 정상 ALL PASS
- 정상 하나 FAIL

Routing
- Judge OFF 기존 Web 경로
- NEXT WEB + REPORT → JEV 호출 0회
- JEV PASS → 같은 Codex session report 요청
- report-only에서 NEXT JEV → Web protocol fallback, JEV 재호출 0회
- JEV FAIL → 같은 Codex session
- JEV ERROR → Web fallback
- ACTION=CONTINUE 새 round에서 JEV count reset
- round당 3회 상한
```

HTTP adapter 테스트는 실제 외부 API 대신 mock handler를 사용한다. 기존 실제 HTTP 200 smoke 기록을 단위 테스트가 대신했다고 표현하지 않는다.

## 8. 실제 실행 검증

코드와 자동 테스트가 통과한 뒤 **빌드된 Explorer 실행본을 우선** 사용한다.

최소 시나리오:

```text
A. Judge OFF
Web → Worker → Codex → Worker → Web
기존 동작 회귀 없음

B. Judge ON / NEXT WEB
JEV 호출 없이 REPORT가 Web으로 전달

C. Judge ON / NEXT JEV / PASS
Codex → Worker → JEV
JEV → Worker
Worker → 같은 Codex session report 요청
Codex → Worker / NEXT WEB + REPORT
Worker → Web

D. Judge ON / NEXT JEV / FAIL
Codex → Worker → JEV
JEV → Worker
Worker → 같은 Codex session 보완
재검증 후 PASS
report-only
Worker → Web

E. JEV ERROR
Worker → Web fallback
오류 이유 표시
Codex 구현 재작업으로 오인하지 않음

F. malformed contract
CONTRACT_PROTOCOL_ERROR
Worker → Web
```

MESSAGE LOG의 실제 hop 순서도 확인한다.

Explorer 화면 검증이 환경상 불가능하면 CLI/API/direct process 같은 다음 가능한 방법으로 검증하되 **대체 검증**이라고 명확히 기록하고 Explorer E2E 완료로 표시하지 않는다.

## 9. 변경 금지 / 이번 범위 밖

이번 09-B에서 하지 않는다.

```text
- 09-C evidence bundle / 실제 diff·test 증거를 JEV state에 연결
- ACTION 또는 NEXT 공개 wire protocol 변경
- Web-first 실행으로 전환
- JEV PASS 뒤 report 전용 Codex 호출 제거
- 범용 Multi-AI workflow engine
- JobRunner/전체 crash recovery 구현
- provider adapter 일반화
- Codex/JEV 직접 통신
- JEV 외 새 provider
- usage/비용 최적화 10-A
- unrelated UI polish
- 07 Force Restore 잔여 검증 병행
```

`JEV-FOOTER-CONTRACT.md`와 `JEV-API-CONTRACT.md`를 구현 편의를 위해 임의로 바꾸지 않는다. 현재 계약과 코드가 충돌하면 우선 코드를 계약에 맞춘다. 계약 자체의 변경이 필요하다고 판단되면 변경하지 말고 결과에 별도 이슈로 보고한다.

## 10. 문서와 Git 정책

구현 완료 후 Codex Desktop은 다음을 최신 사실로 갱신한다.

```text
- CurrentWork.md 상단 현재 요약
- tasks/09-ai-role-dev-tool.md의 09-B 결과/검증
- 필요 시 ProjectHub_IMPLEMENTATION_PLAN.md의 상태
```

단, `GPT-Web-Feedback.md`는 수정하지 않는다.

결과에는 반드시 구분해서 기록한다.

```text
- 코드검토
- 자동 fixture test
- build/test
- 실제 API 사용 여부
- Explorer 실화면 E2E
- 대체 검증
- 미검증/잔여
```

Git commit/push는 별도 사용자 명시 승인이 없으면 수행하지 않는다.

## 11. 09-B 완료 조건

다음이 모두 충족되어야 09-B 완료로 기록한다.

```text
[ ] 한 줄/두 줄 NOUL 계약 파싱이 서로 독립적으로 정상
[ ] SCORE criteria/threshold/answer 범위 검증
[ ] CHOICE 정의/허용값 검증
[ ] 정상 FAIL과 invalid ERROR 분리
[ ] question ID missing/type mismatch가 ERROR
[ ] NEXT WEB에는 REPORT 필수
[ ] NEXT JEV에는 VALIDATION REQUEST 필수
[ ] JEV PASS 후 같은 Codex session report 요청
[ ] REPORT_ONLY에서 JEV 재진입 차단
[ ] JEV FAIL은 같은 Codex session으로 보완
[ ] JEV ERROR는 Web fallback
[ ] fallback에 secret 없는 기계적 원인 전달
[ ] JEV counter가 Web→Codex round별 최대 3회
[ ] Judge OFF 회귀 없음
[ ] Worker 전용 fixture 테스트 통과
[ ] 전체 관련 build/test 통과
[ ] Explorer ON/OFF PASS/FAIL/ERROR 경로 검증 또는 미완료를 명시
```

이번 단계의 목적은 JEV를 더 똑똑하게 만드는 것이 아니다.

**Worker가 기존 v1 계약을 정확히 파싱하고, 잘못된 응답을 구현 실패와 구분하며, 모든 메시지를 올바른 다음 hop으로만 전달하도록 만드는 것**이 09-B의 완료 기준이다.



---

# 2026-09-23 GPT Web 피드백 — JEV 질문 원자화·중요도별 threshold 보완

## 1. 배경과 결론

실제 Worker → JEV 반복 검증에서 넓은 질문 하나에 여러 사실을 묶으면, 구현이 정상이고 로컬 증거가 있어도 JEV의 NOUL 값이 어느 하위 주장 때문에 낮아졌는지 구분하기 어려웠다.

예를 들어 “SoundManager가 잘못된 WAV를 격리하고 UI를 막지 않으며 종료 시 모든 자원을 정리하는가?”는 한 항목으로 사용하지 않는다. 이 질문에는 WAV 격리, 재생 예외, UI 비차단, queue 상한, stopping 이후 enqueue 차단, async shutdown, SoundPlayer 정리, 동기화 자원 정리처럼 서로 독립적으로 실패 가능한 사실이 섞여 있다.

핵심 원칙은 다음과 같다.

- JEV 질문은 가능한 한 하나의 YES/NO 사실만 묻는 원자적 질문으로 분리한다.
- 질문 수를 줄이기 위해 복합 명제를 만드는 것보다, 동일 evidence bundle 안에서 여러 원자적 NOUL 질문을 한 호출에 묶는 편을 우선한다.
- 호출 수 절약과 질문 단순화는 별개의 문제다.
- 기존 [NEXT : JEV], [VALIDATION REQUEST] 공개 계약은 유지한다.

## 2. 원자적 질문 원칙

JEV용 NOUL 질문은 기본적으로 다음 조건을 만족한다.

1. 질문 하나는 검증 가능한 주장 하나만 포함한다.
2. 가능한 한 YES/NO로 직접 답할 수 있게 쓴다.
3. “그리고”, “및”, “동시에”, “~하면서”로 독립 조건을 연결하지 않는다.
4. 파일·클래스·메서드·상태·상한값 등 확인 지점을 구체적으로 적는다.
5. 정상 경로와 실패 경로를 별도 질문으로 나눈다.
6. 정적 코드 사실과 실제 runtime 체감 품질을 한 질문에 섞지 않는다.
7. 빌드 성공, exit code, 파일 존재, hash 일치처럼 도구가 직접 판정할 수 있는 사실은 로컬 검증이 원본이다. JEV는 필요할 때 그 증거가 해당 AC를 뒷받침하는지만 판단한다.
8. 질문의 importance와 threshold는 JEV 호출 전에 확정한다. 실패한 뒤 통과시키기 위해 importance나 threshold를 낮추지 않는다.

“A가 맞고 B도 맞으며 C도 안전한가?”는 C1=A, C2=B, C3=C처럼 나눈다. 여러 항목이 모두 만족해야 하나의 AC가 통과되는 경우에도 각 사실은 별도 C 번호로 검증하고 Worker/관제가 결과를 AC 단위로 종합한다.

## 3. 중요도와 NOUL threshold

JEV의 NOUL 값은 프로그램의 완성도 비율이나 실제 오류 확률로 해석하지 않는다. 경험상 명확한 코드에서도 매우 높은 값이 항상 나오지는 않으므로 0.98 같은 사실상 과도한 문턱을 기본 정책으로 두지 않는다.

| 중요도 | 기본 PASS | 용도 |
| --- | --- | --- |
| LOW | YES >= 0.60 | 문서 표현, 부가 정보, 비핵심 정리, 사용자 체감으로 최종 판단 가능한 항목 |
| MEDIUM | YES >= 0.70 | UI 상태, transient effect 정리, 리소스 설명, 유지보수 품질 |
| HIGH | YES >= 0.80 | 핵심 기능, 자원 상한, 상태 전이, 충돌·아이템·멀티볼 등 기능 신뢰성 |
| CRITICAL | YES >= 0.90 | crash, deadlock, 빌드 불가, 데이터/상태 손상, 잘못된 범위 접근, 필수 안전 계약 위반 |

importance는 영향도를 나타내며 JEV가 높은 숫자를 잘 만들 수 있는지를 나타내는 등급이 아니다.

예를 들어 README의 M 키 설명이 실제 입력과 일치하는지는 LOW 또는 MEDIUM일 수 있고, Stage 5 이후 존재하지 않는 Stage 6 배열 원소에 접근할 실행 경로가 없는지는 CRITICAL로 둘 수 있다.

## 4. 권장 질문 형식

현재 parser 호환성을 위해 PASS는 별도 줄을 우선 사용한다.

예시:

[NEXT : JEV]

[VALIDATION REQUEST]

- NOUL | [CRITICAL] Stage 5 완료 후 StageManager의 6번째 항목에 접근하는 실행 경로가 없는가?
  PASS: YES >= 0.90

- NOUL | [HIGH] MultiBall 상태에서 Ball 하나만 제거됐을 때 Lives 값이 감소하지 않는가?
  PASS: YES >= 0.80

- NOUL | [MEDIUM] ResetRound() 완료 후 PickupMessageTime이 0인가?
  PASS: YES >= 0.70

- NOUL | [LOW] README에 M 키가 음소거 토글로 문서화되어 있는가?
  PASS: YES >= 0.60

importance는 당장 별도 wire field를 추가하지 않고 질문 앞의 [CRITICAL], [HIGH], [MEDIUM], [LOW] 표기로 전달할 수 있다. 향후 typed importance를 지원한다면 별도 계약 변경 task에서 도입한다.

## 5. 좋은 질문 / 피할 질문

피할 질문:

- SoundManager가 안전하고 빠르며 자원을 모두 잘 정리하는가?
- 그래픽 효과가 성능과 게임 로직에 문제를 일으키지 않는가?
- 기존 게임 기능이 모두 정상이고 README도 맞는가?

권장 질문:

- [HIGH] SoundManager.Play()에서 SoundPlayer.PlaySync()를 직접 호출하지 않는가?
- [HIGH] pending queue의 원소 수가 QueueCapacity를 초과할 수 없는가?
- [HIGH] stopping=true 이후 새 SoundEffect가 pending queue에 추가되지 않는가?
- [HIGH] DisposeAsync()가 audio worker 완료를 await하는가?
- [MEDIUM] audio worker 종료 경로에서 생성된 SoundPlayer를 모두 Dispose하는가?
- [HIGH] Shake를 적용할 때 Ball.Position 값을 수정하지 않는가?
- [HIGH] Shake를 적용할 때 Paddle.Bounds 값을 수정하지 않는가?
- [MEDIUM] Ball.RecordTrail() 이후 Trail.Count가 MaxTrailPoints 이하인가?
- [MEDIUM] Particle 수가 선언된 MaxParticles를 초과하지 않는가?
- [LOW] README의 audio asset 상대 경로가 csproj의 배포 경로와 같은가?

“자원 관리가 안전한가?” 같은 넓은 표현보다 코드에서 참/거짓을 확인할 수 있는 불변조건을 질문으로 만든다.

## 6. 증거도 질문 단위로 좁힌다

JEV가 전체 파일 목록만 받고 스스로 관련 위치를 추측하게 하지 않는다. 가능하면 각 질문에 대응하는 근거를 evidence index에서 연결한다.

예:

- C1 → SoundManager.cs / Play()
- C2 → SoundManager.cs / QueueCapacity, Play()
- C3 → SoundManager.cs / RequestStop(), DisposeAsync()
- C4 → GameForm.cs / OnPaint() shake transform
- C5 → GameModels.cs / Ball.RecordTrail()
- C6 → build-release.txt / final summary

향후 EvidenceBundle에서는 question ID 또는 AC와 evidence ref를 직접 연결하는 방식을 우선 검토한다.

증거가 없으면 질문을 넓혀서 추론시키지 않는다. INSUFFICIENT_EVIDENCE 또는 Web/user 확인 대상으로 분류한다.

## 7. JEV와 직접 검증의 역할 분리

다음 사실은 가능한 한 실제 도구 결과가 원본이다.

- build exit code
- test exit code
- warning/error count
- 파일 존재
- 파일 크기
- hash 일치
- enum 개수
- 배열/리스트 상한을 확인하는 deterministic test

JEV에게 “빌드가 성공했는가?”를 추측하게 하기보다 실제 build log를 Worker가 기계적으로 확인한다. 의미 검증이 필요하면 “제공된 build log가 AC-3에서 요구한 Release build 실행의 직접 증거인가?”처럼 묻는다.

반대로 다음은 실제 사용자 검증이 최종 판정에 더 적합하다.

- 효과음이 듣기 좋은가
- 타격감이 충분한가
- 화면 흔들림이 과하지 않은가
- 색상 대비가 자연스러운가
- 플레이 감각이 답답하지 않은가

JEV PASS만으로 이런 체감 조건을 완료 처리하지 않는다.

## 8. 재검증과 다음 행동

점수를 맞추기 위한 무한 수정 루프를 만들지 않는다.

기본 정책:

- CRITICAL 기준 미달: 구체적인 코드/증거 결함이 있으면 CONTINUE. 실제 runtime 사용자 확인 없이는 판정할 수 있으면 PAUSE.
- HIGH 기준 미달: 재현 가능한 기능/자원 문제면 CONTINUE. 단순 confidence 부족이고 직접 검증이 통과했다면 Web이 판단한다.
- MEDIUM / LOW 기준 미달: 구체 결함이 있으면 수정한다. 구체 결함 없이 confidence만 낮으면 점수 맞추기식 수정은 금지하고 사용자 실검증으로 넘길 수 있다.

기존 JEV round 상한을 유지한다. 같은 질문이 round 상한까지 기준을 넘지 못했는데 JEV가 수정 가능한 구체 결함을 제시하지 않았다면 같은 검증을 그대로 반복하지 않는다.

Web 보고에는 최소 다음을 구분한다.

- 실제로 재현된 결함
- JEV confidence만 기준 미달인 항목
- evidence 부족
- provider/parser/error
- 사용자 실검증 필요 항목

## 9. 질문 수 정책 보완

Master-Polish.md 8.2에는 현재 “검증 질문은 가능한 한 1~3개로 제한”한다고 되어 있다. 비용 절감 취지는 유지하되 실제 사용에서는 이 규칙이 한 질문에 여러 독립 주장을 결합시키는 압력이 될 수 있다.

후속 Master 개정 시 다음 취지로 보완하는 것을 권한다.

- 한 질문에는 한 주장만 둔다.
- 질문 수보다 원자성이 우선이다.
- 관련 원자 질문은 한 JEV 호출에 batch한다.
- 사소한 구현 사실을 무제한 질문으로 늘리지는 않는다.
- 하나의 AC에서 독립적으로 실패 가능한 사실이 여러 개면 각각 별도 질문으로 만든다.
- 질문이 많아지면 기능군/AC별로 묶되 복합 질문으로 되돌리지 않는다.

초기 운영값으로는 의미 있는 검증이 필요한 기능군 하나당 약 4~12개의 원자 질문을 우선 고려한다. 복잡한 변경에서는 더 많을 수 있으며 숫자 자체를 강제 상한으로 사용하지 않는다.

비용은 질문을 합쳐서 줄이지 말고, 불필요한 질문을 제거하고 관련 질문을 한 API 호출에 batch해서 줄인다.

## 10. 향후 Worker/JEV 검증 테스트 권고

후속 구현 task에서는 다음 회귀 사례를 추가하는 것이 좋다.

- LOW 0.60 threshold parse/evaluate
- MEDIUM 0.70 threshold parse/evaluate
- HIGH 0.80 threshold parse/evaluate
- CRITICAL 0.90 threshold parse/evaluate
- threshold는 validation 시작 후 자동 하향되지 않음
- 질문 앞 importance label이 질문 본문을 손상시키지 않음
- 여러 NOUL 질문을 한 request에서 독립 C ID로 유지
- 한 질문 실패가 다른 질문의 PASS threshold와 잘못 연결되지 않음
- round 상한 뒤 동일 검증 무한 반복 없음
- JEV FAIL / insufficient evidence / provider error / user verification 필요 상태를 구분

importance 자체를 Worker의 새 공개 wire field로 승격하는 작업은 별도 계약 변경으로 다룬다. 이번 피드백은 기존 NOUL + PASS 문법 안에서 운용 가능한 정책을 먼저 제안한다.

## 11. 적용 우선순위

1. 하나의 질문 = 하나의 검증 주장
2. 가능하면 YES/NO로 답할 수 있는 질문
3. 중요도에 따라 0.60 / 0.70 / 0.80 / 0.90 threshold
4. 실제 도구 검증과 JEV 의미 검증 분리
5. JEV 점수 맞추기식 코드 수정 금지
6. 사용자 실검증을 최종 품질 판정으로 유지
7. 질문 수 제한보다 질문 원자성을 우선하되 한 호출에 batch하여 비용 제어

현재 BrickBreaker 실험에서 얻은 경험은 JEV의 역할을 제거하자는 근거가 아니라, JEV에게 더 작고 명확한 판단 단위를 주고 직접 실행 검증과 사용자 체감 검증의 경계를 더 분명히 해야 한다는 근거로 반영한다.


---

# 2026-09-23 GPT Web 종합 피드백 — JEV 질문 설계 v2와 다음 실험

## 1. 이번 BrickBreaker 실험에서 확인한 핵심

이번 실험은 JEV가 단순히 “질문을 많이 받으면 잘 판정한다”는 결론보다 **질문이 어떤 증거를 가리키는지가 매우 중요하다**는 점을 보여줬다.

첫 MultiBall 검증에서는 다음처럼 기능 자체를 묻는 원자 NOUL과 증거 상태 CHOICE를 보냈다.

- 적용 후 Ball 수가 정확히 2배인가
- 새 Ball이 같은 호출에서 재복제되지 않는가
- 생성 Ball이 Stage MaxBallSpeed 이하인가
- 다른 Ball이 남아 있을 때 Life가 감소하지 않는가
- Trail이 Ball별로 독립적인가
- 전체 증거가 SUPPORTED / INSUFFICIENT / CONTRADICTORY 중 무엇인가

질문은 이미 비교적 잘 쪼개져 있었지만, 각 질문에 직접적인 코드/test/log 위치가 붙지 않은 첫 요청에서는 NOUL이 대체로 0.5~0.6대에 머물고 CHOICE는 INSUFFICIENT를 반환했다.

같은 구현을 불필요하게 다시 바꾸지 않고 다음 재요청에서 각 질문에 실제 근거를 붙였다.

예:

```text
근거:
- GameWorld.cs의 snapshot 생성/복제 위치
- ProjectValidator.cs의 해당 assertion
- runtime-validation.txt의 PASS line
```

그 결과 같은 핵심 요구가 ALL_PASS로 바뀌었다.

따라서 JEV 결과가 낮을 때 첫 대응은 “코드를 더 고친다”가 아니라 다음 순서여야 한다.

```text
1. 질문이 원자적인가
2. 질문이 실제 evidence를 직접 가리키는가
3. 판정 범위가 명확한가
4. 어떤 반례가 NO/FAIL인지 명시돼 있는가
5. 그래도 contradiction이 있으면 그때 구현을 수정한다
```

## 2. 질문 설계 기본 공식

앞으로 의미 있는 JEV 질문은 가능한 한 다음 네 요소를 갖춘다.

```text
CLAIM + EVIDENCE + SCOPE + COUNTEREXAMPLE
```

### CLAIM

한 질문에서 판정할 단 하나의 주장이다.

나쁜 예:

```text
SoundManager가 안전하고 UI를 막지 않으며 모든 자원을 정상 정리하는가?
```

좋은 예:

```text
SoundManager.Play() 호출 경로에서 PlaySync()가 UI thread에서 실행되지 않는가?
```

### EVIDENCE

판단에 사용할 코드, test, log, diff, artifact 위치를 지정한다.

예:

```text
근거:
SoundManager.cs / Play(), PlaybackLoop()
ProjectValidator.cs / audio queue assertion
Validation/runtime-validation.txt / 해당 PASS line
```

JEV가 전체 저장소나 전체 로그에서 스스로 핵심 근거를 찾게 하지 않는다.

### SCOPE

이번 질문이 무엇만 판정하는지 한정한다.

예:

```text
이번 질문은 재생 품질이나 실제 스피커 출력은 판정하지 않는다.
UI-thread blocking 경로 존재 여부만 판정한다.
```

### COUNTEREXAMPLE

어떤 상태가 발견되면 실패인지 명시한다.

예:

```text
Play() 또는 UI event handler에서 PlaySync()로 이어지는 실행 경로가 있으면 NO.
```

이 네 요소를 모두 매 질문에 장문으로 반복할 필요는 없다. 같은 evidence bundle을 공유하는 질문은 공통 evidence index를 두고 C1/C2가 해당 ref를 가리켜도 된다.

## 3. 질문은 원자성을 가질 때까지 최대한 분리

기존 Master의 “가능한 한 1~3개 질문” 정책은 폐기하는 것이 맞다.

질문 개수보다 다음 기준을 우선한다.

- 서로 독립적으로 실패할 수 있는 명제는 반드시 별도 질문으로 만든다.
- 한 질문 안의 “그리고 / 및 / 동시에 / ~하면서”가 독립 조건을 연결한다면 분리 후보로 본다.
- 정상 경로와 금지 경로도 가능하면 따로 묻는다.
- 직접 계산 가능한 여러 assertion을 하나의 “전체가 정상인가?”로 다시 합치지 않는다.
- 질문이 많아져도 관련 질문을 한 API request에 batch하여 호출 수를 줄인다.
- provider payload 한계를 넘으면 원자 질문을 합치지 말고 batch를 여러 개로 나눈다.

즉 비용 절감의 대상은 **질문 개수 자체가 아니라 중복 context, 불필요한 evidence, 반복 API call**이다.

## 4. 질문 타입별 역할

### NOUL — 단일 사실

YES/NO로 의미가 명확한 단일 주장에 사용한다.

예:

```text
- NOUL | [HIGH] MultiBall 한 번 적용 후 활성 Ball 수가 적용 직전의 정확히 2배인가?
  근거: E-MULTI-COUNT
  범위: 이번 ApplyItem 호출 1회
  반례: afterCount != beforeCount * 2
  PASS: YES >= 0.80
```

또 다른 예:

```text
- NOUL | [HIGH] MultiBall 생성 Ball의 speed magnitude가 현재 Stage MaxBallSpeed를 초과하지 않는가?
  근거: E-MULTI-SPEED
  반례: 새 Ball 중 하나라도 magnitude > CurrentMaxBallSpeed
  PASS: YES >= 0.80
```

### CHOICE — 의미적 상태 분류

증거 상태, 실패 원인, 변경 성격처럼 여러 상태 중 하나를 고를 때 사용한다.

증거 상태의 기본 후보:

```text
SUPPORTED
= 코드와 직접 실행 증거가 요구사항을 모두 지지한다

PARTIAL
= 일부 하위 주장은 증명됐지만 하나 이상 직접 증거가 빠졌다

INSUFFICIENT
= 요구사항을 판정할 직접 근거가 부족하다

CONTRADICTORY
= 코드 또는 실행 증거가 요구사항과 충돌한다
```

PARTIAL을 두는 이유는 “증거가 전혀 없음”과 “1→2, 2→4는 확인했지만 4→8은 아직 없음”을 구분하기 위해서다.

예:

```text
- CHOICE | MultiBall count 요구에 대한 evidence 상태를 분류하라.
  SUPPORTED = 1→2, 2→4, 4→8 직접 실행 검증이 모두 있고 코드와 모순 없음
  PARTIAL = 일부 배수 사례만 직접 검증됨
  INSUFFICIENT = count를 판정할 실행 증거 없음
  CONTRADICTORY = 구현 또는 실행 결과가 2배 규칙과 충돌
  PASS: SUPPORTED
```

CHOICE는 NOUL을 다양하게 보이게 하려고 억지로 추가하지 않는다. 실제로 분류 문제가 있을 때만 사용한다.

### SCORE — 순서가 있는 정도

SCORE는 사실 확인에 사용하지 않는다.

적절한 예는 범위 이탈이나 회귀 위험처럼 단계가 자연스럽게 존재할 때다.

```text
- SCORE | 이번 변경의 요구 범위 이탈 정도는?
  1 = 요청 범위 안의 변경만 있음
  2 = 요구 구현에 필요한 작은 인접 변경만 있음
  3 = 요구하지 않은 동작 변경이 있으나 핵심 기능에는 영향 없음
  4 = 기존 동작에 의미 있는 비요구 변경이 있음
  5 = 사실상 다른 작업 범위까지 변경됨
  PASS: SCORE <= 2.0
```

각 단계는 “좋음/나쁨” 같은 감상적 표현보다 diff로 구분 가능한 관찰 상태여야 한다.

## 5. 중요도와 threshold

초기 운영값은 다음을 유지한다.

| 중요도 | NOUL 기본 PASS | 의미 |
| --- | --- | --- |
| LOW | YES >= 0.60 | 문서·부가 정보·최종 사용자 확인으로 보완 가능한 항목 |
| MEDIUM | YES >= 0.70 | UI state·transient·유지보수 품질 |
| HIGH | YES >= 0.80 | 주요 기능·상태 전이·resource invariant |
| CRITICAL | YES >= 0.90 | crash/deadlock/data loss/build-blocking/필수 안전 계약 |

이 값은 프로그램 완성도를 뜻하지 않는다.

중요도가 높으면 “JEV가 더 높은 값을 잘 내야 한다”는 의미도 아니다. 높은 중요도 항목은 오히려 deterministic test와 직접 실행 증거를 더 강하게 요구해야 한다.

실패 후 threshold를 낮춰 통과시키지 않는다.

## 6. deterministic evidence와 JEV 분리

다음은 Worker/Codex/local validator가 직접 판정한다.

```text
build/test exit code
warning/error count
파일 존재/크기/hash
collection count
timer 숫자
enum 값
Max/Min bound
정확한 state transition assertion
seeded deterministic 결과
```

JEV는 이런 숫자를 다시 추측하는 용도가 아니다.

예를 들어 build가 실제로 0 warning / 0 error라면 JEV에게 단순히 “빌드됐는가?”라고 묻기보다 필요할 때 다음을 묻는다.

```text
제공된 build artifact/log가 이번 AC에서 요구한 최종 Release source revision의 직접 증거인가?
```

build 결과 자체의 진실값은 실제 process exit/log가 authoritative하다.

## 7. 질문 개수와 256에 대한 정책

2026-09-23 확인한 TypeSafe 공개 API 문서에서 요청의 `questions`는 `map<string, Question>`으로 정의돼 있으며, **한 request에 넣을 수 있는 question 수의 최대값은 공개 문서에 명시돼 있지 않다.**

공식 문서에 명시된 별도 제한은:

```text
Choice: 한 질문의 option 최대 255개
Score: 한 질문의 level 2~10개
```

이다.

따라서 Choice의 255개 option을 “질문 약 256개까지 가능”으로 해석하지 않는다.

운영 정책:

```text
질문 상한을 임의로 3개, 12개, 256개로 고정하지 않는다.
→ 먼저 원자화한다.
→ 한 request에 batch한다.
→ 실제 provider 한계는 smoke test로 측정한다.
→ 한도를 넘으면 여러 request로 나눈다.
```

향후 smoke 후보:

```text
32 questions
64 questions
128 questions
256 questions
```

각 단계에서 확인:

```text
HTTP status
모든 question ID에 answer가 반환되는가
응답 누락/중복 여부
input/output usage
latency
429/422/529 여부
같은 작은 fixture에서 answer 안정성
```

최대 성공값을 발견해도 이를 영구 API 계약으로 간주하지 않는다. provider/model revision별 측정 시각과 함께 기록한다.

## 8. JEV FAIL 처리 순서

JEV FAIL을 바로 “코드 FAIL”로 번역하지 않는다.

```text
NOUL threshold 미달
→ evidence reference가 실제로 전달됐는지 확인
→ 질문이 복합 명제인지 확인
→ 반례가 실제로 존재하는지 확인
→ 구체 contradiction이면 코드 수정
→ contradiction 없이 evidence 부족이면 evidence 보완

CHOICE = PARTIAL
→ 빠진 하위 주장/evidence만 추가

CHOICE = INSUFFICIENT
→ 코드 변경보다 evidence 수집 우선

CHOICE = CONTRADICTORY
→ 요구와 코드/실행 결과 충돌을 찾아 구현 수정

provider/parser ERROR
→ 구현 재작업 금지
→ Web fallback 또는 provider 재시도 정책
```

동일 질문·동일 evidence로 round 상한까지 confidence만 반복해서 낮으면 score-chasing 수정은 중단한다.

## 9. Master-Polish에 이번에 반영한 결정

이번 Web 변경에서 Master를 다음 방향으로 직접 갱신했다.

- “질문 1~3개 제한” 제거.
- 질문은 독립 판정 가능할 때까지 최대한 원자화.
- `CLAIM + EVIDENCE + SCOPE + COUNTEREXAMPLE` 기본형.
- NOUL / CHOICE / SCORE 역할 분리.
- LOW 0.60 / MEDIUM 0.70 / HIGH 0.80 / CRITICAL 0.90.
- deterministic evidence와 의미 판단 분리.
- 공개 API 문서에 없는 256 question limit를 계약으로 고정하지 않음.
- 실제 질문 batch 한도는 단계적 smoke로 측정.
- 질문을 합쳐 비용을 줄이지 않고 context/evidence 중복과 호출 수를 줄임.

## 10. Master 방침에 따른 다음 ProjectHub 진행

현재 활성 09 task에는 09-B가 “구현 완료, Explorer 검증 대기”, 09-C가 대기로 기록돼 있다.

따라서 Master의 “번호 작업 하나 + A/B/C 하나” 원칙상 순서는 다음과 같다.

### 먼저 09-B 실화면 마감

최신 Explorer 실행본에서 남아 있는 E2E를 확인한다.

```text
Judge OFF
NEXT WEB
JEV PASS
JEV FAIL → 동일 Codex session 보완
JEV ERROR/provider fallback
malformed contract fallback
PASS 후 report-only에서 JEV 재진입 차단
```

이미 같은 build에서 직접 수행한 증거가 있다면 다시 돌리지 말고 해당 transcript/evidence를 CurrentWork와 09 task에 연결한다.

### 그 다음 09-C를 하나만 활성화

09-C의 핵심은 이번 BrickBreaker 실험에서 드러난 문제와 정확히 맞는다.

```text
AC
↕
atomic question
↕
evidence ref
↕
source/diff/test/log hash
```

목표:

- 질문마다 관련 evidence를 실제 JEV state에 전달.
- 전체 파일을 던지는 대신 question→evidence index를 제공.
- SUPPORTED / PARTIAL / INSUFFICIENT / CONTRADICTORY를 구현 FAIL과 분리.
- 검증 이후 관련 source/evidence가 바뀌면 과거 PASS 자동 무효화.
- v1 ACTION/NEXT 공개 wire 형식은 유지.
- JEV의 낮은 confidence를 코드 결함으로 자동 변환하지 않음.

09-C가 안정화된 뒤에만 10-A usage/token 최적화로 넘어간다.

## 11. 다음 테스트 프로젝트 제안 — Minesweeper / 지뢰찾기

테트리스 → 벽돌깨기 다음은 **지뢰찾기(Minesweeper)** 를 권장한다.

이유는 앞의 두 프로젝트와 검증 성격이 겹치지 않으면서도 구현 규모가 지나치게 커지지 않기 때문이다.

테트리스에서 확인한 것:

```text
discrete grid
rotation
collision
line clear
게임 state
키 입력
```

BrickBreaker에서 추가로 확인한 것:

```text
continuous movement
collision response
timers
multi-object state
sound
particles/trail/shake
runtime UI
user play feel
```

Minesweeper는 새롭게 다음을 검증할 수 있다.

```text
random board generation
seeded deterministic replay
정확한 mine count invariant
first-click safety
adjacent mine number 계산
flood-fill / connected reveal
right-click flag state
flag와 reveal 상호 배타성
win/loss 판정
difficulty별 board size / mine count
timer
restart/reset
mouse input
대량의 작고 원자적인 invariant
```

특히 JEV 질문 설계 실험에 적합하다. 물리·체감보다 **정확한 불변조건이 많아서 원자 질문을 수십 개로 자연스럽게 분리**할 수 있다.

권장 첫 범위:

```text
C# / .NET 8 WinForms
Beginner / Intermediate / Expert 3단계
첫 클릭은 mine가 아니도록 보장
첫 클릭 주변 3x3 safe 여부는 요구사항에서 명확히 선택
좌클릭 reveal
우클릭 flag
0-cell flood reveal
mine 클릭 Game Over
모든 non-mine reveal 시 Win
timer
restart
seed 표시 또는 debug seed 입력
headless validator
```

seed 기능은 자동 검증에서 특히 중요하다. 같은 seed로 동일 board를 재현할 수 있어 JEV evidence와 deterministic test를 직접 연결할 수 있다.

예상 원자 질문군:

```text
보드 크기
mine 정확한 개수
첫 클릭 안전
mine 중복 배치 없음
각 숫자가 인접 mine 수와 일치
0 flood가 대각 포함 8-neighbor 규칙을 따름
flagged cell 좌클릭 처리
revealed cell flag 금지
mine click loss
마지막 safe cell reveal win
reset 후 timer/state 초기화
difficulty 변경 후 새 board 생성
같은 seed 동일 board
다른 seed에서 생성 경로 정상
```

NOUL은 각 invariant에, CHOICE는 evidence 상태·실패 원인 분류에, SCORE는 변경 범위 이탈이나 회귀 위험처럼 실제 단계가 있는 평가에만 사용한다.

Snake는 다음 실험으로는 구현 난도가 너무 낮고 BrickBreaker와 실시간 이동 면에서 겹치는 부분이 많다. Pac-Man은 pathfinding/ghost AI까지 들어가 09-C evidence 전달 자체보다 게임 구현 복잡도가 먼저 커질 가능성이 있다.

따라서 **Minesweeper는 Tetris의 grid deterministic 성격과 BrickBreaker의 UI/runtime 성격 사이에서, 새 JEV 원자 질문·evidence 설계를 검증하기 좋은 세 번째 프로젝트**다.

이 프로젝트를 시작하기 전에 먼저 09-B 마감 → 09-C evidence 전달을 완료하고, 개선된 JEV 경로를 Minesweeper에서 처음부터 적용하는 것을 권장한다.


---

# 2026-09-23 GPT Web 피드백 — Minesweeper JEV 실사용 결과

## 1. 이번 실험에서 JEV가 실제로 유용했던 지점

Minesweeper 구현에서는 JEV를 단순한 최종 PASS/FAIL 판정기로 쓰기보다, **현재 검증 설계에서 근거가 약한 부분을 찾아내는 리뷰어**로 사용했을 때 가장 유용했다.

첫 JEV 요청은 이미 많은 항목을 원자화했고 각 질문에 `EVIDENCE / SCOPE / COUNTEREXAMPLE`를 붙였지만, 세 항목이 기준을 넘지 못했다.

- C42: 좌클릭 Reveal / 우클릭 Mark / 가운데 클릭 Chord를 한 질문에 함께 묶은 입력 매핑 질문 — HIGH, 실제 0.79.
- C44: F2 / Ctrl+R / 1 / 2 / 3 / M / F3 / F4 / Esc를 한 질문에 묶은 키보드 입력 질문 — MEDIUM, 실제 0.68.
- C48: 손상 또는 누락 WAV가 게임 실행을 중단시키지 않는가 — MEDIUM, 실제 0.69.

이 세 항목의 공통점은 **구현이 틀렸다는 직접 contradiction이 없었고, 질문이 여러 독립 입력을 묶었거나 실제 실행 evidence가 약했다**는 점이다.

따라서 첫 FAIL 뒤에 threshold를 낮추거나 구현을 점수 맞추기식으로 바꾸지 않았다.

## 2. JEV FAIL 뒤에 한 올바른 보완

Codex는 실패 항목을 다시 분석해 다음처럼 보완했다.

### 입력 질문을 더 원자화

기존:

```text
좌클릭 Reveal, 우클릭 Mark, 가운데 클릭 Chord가 각각 지정 동작에 연결되는가?
```

보완 후:

```text
좌클릭은 Reveal인가?
우클릭은 Mark인가?
가운데 클릭은 Chord인가?
좌+우 동시 입력은 Chord인가?
```

키보드도 하나의 질문에 전부 묶지 않고 다음처럼 분리했다.

```text
Esc → Exit
F2 → New Game
Ctrl+R → Replay
1 → Beginner
2 → Intermediate
3 → Expert
M → ToggleSound
F3 → DebugWin
F4 → DebugLoss
```

이 방식은 “전체 단축키가 맞는가?”보다 어떤 입력이 실제로 실패했는지 즉시 식별할 수 있다.

### 실제 deterministic evidence를 추가

입력 경로를 `InputMapping`으로 분리하고 validator가 각 key/mouse mapping을 직접 검사하도록 근거를 강화했다.

Audio 역시 단순히 `try/catch가 있다`는 코드 사실만 제시하지 않고:

- 임시 `Reveal.wav`에 잘못된 byte를 기록
- 나머지 WAV가 누락된 임시 폴더 사용
- SoundManager 초기화가 중단되지 않는지 확인
- Play 호출이 예외 없이 끝나는지 확인

하는 직접 검증을 추가했다.

최종 Release validator는 `VALIDATION PASS: 101449 checks`를 반환했고, 보완된 JEV 재검증은 `ALL_PASS`가 됐다.

## 3. 이번 실험에서 얻은 JEV 운영 규칙

### 3.1 JEV FAIL은 곧바로 코드 FAIL이 아니다

다음 순서로 해석한다.

```text
JEV threshold 미달
→ 실제 contradiction 존재 여부 확인
→ 질문이 복합 명제인지 확인
→ evidence가 코드 주장만인지 실행 증거까지 있는지 확인
→ 질문을 더 원자화
→ deterministic evidence 보강
→ 그 뒤에도 contradiction이 있으면 구현 수정
```

이번 C42/C44/C48은 이 순서를 따랐고, 불필요한 기능 수정 없이 검증 품질만 높여 통과했다.

### 3.2 하나의 질문에 여러 입력을 묶지 않는다

다음 형태는 피한다.

```text
F2, Ctrl+R, 1, 2, 3, M, F3, F4, Esc가 모두 올바르게 동작하는가?
```

각 키는 독립적으로 실패할 수 있으므로 각각 별도 질문으로 만든다.

Mouse도 동일하다.

```text
Left
Right
Middle
Left+Right
```

를 별도 질문으로 둔다.

### 3.3 코드 존재보다 실행 evidence가 강하다

다음 둘을 같은 수준의 evidence로 취급하지 않는다.

```text
코드에 Middle → Chord 분기가 있다.
validator가 Middle → Chord mapping을 실제 assertion으로 검사해 PASS했다.
```

후자가 더 직접적인 증거다.

특히 오류 처리에서는:

```text
try/catch가 존재한다
```

보다:

```text
손상 파일을 실제로 공급했는데 초기화/호출이 실패하지 않았다
```

가 더 강한 evidence다.

### 3.4 JEV는 validator를 개선하는 계기로 사용할 수 있다

이번 실험에서 JEV의 가장 유용한 효과는 코드 변경 자체가 아니라 **검증 코드의 약한 부분을 드러낸 것**이다.

첫 FAIL 이후:

- input mapping을 별도 검증 가능 구조로 만듦
- 각 key/mouse 입력을 독립 assertion으로 만듦
- 손상 WAV fixture를 실제로 실행함
- missing WAV 상황을 실제로 확인함

으로 validator coverage가 향상됐다.

즉 JEV의 역할을 다음처럼 보는 것이 적절하다.

> JEV는 deterministic test의 대체물이 아니라, 현재 evidence가 요구사항을 실제로 증명하는지 검토하는 의미적 reviewer다.

## 4. CHOICE의 PARTIAL이 유용했던 이유

이번 첫 JEV 요청에서는 Input/UI와 Audio를 무조건 SUPPORTED로 요구하지 않았다.

Input/UI:

```text
SUPPORTED = 실제 WinForms 조작 및 화면 확인까지 완료
PARTIAL = 코드 경로는 확인됐지만 실화면/실조작 확인은 미완료
```

Audio:

```text
SUPPORTED = 효과음 파일과 실제 재생/음량 검증까지 완료
PARTIAL = 파일 및 재생 코드는 있으나 실제 재생 특성은 미검증
```

처럼 정의했다.

이 방식은 자동화가 확인할 수 있는 영역과 사용자가 최종 판단해야 하는 영역을 분리하는 데 유용했다.

즉:

```text
코드/validator 수준에서 맞음
!=
사용자 체감까지 검증 완료
```

를 JEV 계약 자체에 표현할 수 있다.

## 5. SCORE는 범위 판단에만 사용한 것이 적절했다

이번 요청에서 SCORE는 사실 확인용으로 쓰지 않고:

```text
이번 구현의 요구 범위 이탈 정도
```

만 평가했다.

이 방향을 유지한다.

```text
NOUL
= 단일 사실

CHOICE
= 증거 상태 / 원인 / 분류

SCORE
= 범위 이탈 / 위험도처럼 순서가 있는 평가
```

타입을 다양하게 쓰는 것이 목표가 아니라, 판정 성격에 맞는 타입을 선택한다.

## 6. 질문 구조의 실사용 효과

이번 Minesweeper에서는 다음 형식이 실제로 사용됐다.

```text
CLAIM
EVIDENCE
SCOPE
COUNTEREXAMPLE
PASS
```

예:

```text
- NOUL | [HIGH] 가운데 클릭은 Chord 명령으로 전달되는가?
  EVIDENCE: InputMapping validator가 Middle → InputCommand.Chord를 검사하고 Release validator가 PASS함.
  SCOPE: BoardSurface 마우스 입력 라우팅.
  COUNTEREXAMPLE: 가운데 클릭이 Chord가 아니거나 assertion이 실패하면 NO.
  PASS: YES >= 0.80
```

이 구조는 JEV에게 다음을 동시에 알려준다.

- 무엇을 판정할지
- 어떤 evidence를 볼지
- 어디까지 판정할지
- 무엇이 발견되면 실패인지

향후 질문 생성기의 기본 형태로 유지할 가치가 있다.

## 7. 09-C에 직접 연결되는 결론

현재 v1 footer는 `EVIDENCE: E-INPUT` 같은 continuation line을 JEV instructions에 보존할 수 있지만, **경로 문자열을 적었다고 JEV가 로컬 파일 내용을 자동으로 읽는 것은 아니다.**

Minesweeper에서 성공한 재검증도 Codex가 관련 코드/validator 결과를 질문 text에 요약해서 넣었기 때문에 가능했다.

따라서 09-C에서는 다음을 실제 제품 기능으로 만드는 것이 핵심이다.

```text
Question ID
→ Evidence ID
→ source excerpt / diff excerpt / test result / runtime log
→ digest / revision
```

권장 내부 구조:

```text
C42
  claim: Middle click maps to Chord
  evidence:
    - E-INPUT-MIDDLE
      source_ref: InputMapping.cs
      test_ref: ProjectValidator input mapping test
      result_ref: runtime-validation line
      digest: ...
```

이렇게 해야 JEV가 문자열로 적힌 파일명을 믿는 것이 아니라, 실제 전달된 evidence를 기준으로 판단할 수 있다.

## 8. 09-C 수용 조건에 추가할 항목

Minesweeper 실험을 근거로 다음을 09-C 후보 AC에 추가하는 것을 권장한다.

```text
- 원자 질문마다 0개 이상 evidence ref를 가질 수 있다.
- evidence가 필요한 질문에서 ref가 없으면 SUPPORTED로 완료하지 않는다.
- question ref가 가리키는 source/test/log 내용이 실제 JEV state에 포함된다.
- 같은 evidence를 여러 질문이 재사용할 수 있다.
- source/test/log가 변경되면 digest가 바뀌고 과거 JEV PASS를 재사용하지 않는다.
- PARTIAL / INSUFFICIENT는 구현 FAIL과 별도 상태다.
- deterministic assertion PASS는 원본 증거로 보존한다.
- 사용자 체감 검증은 자동 SUPPORTED로 승격하지 않는다.
```

## 9. 이번 실험의 한 줄 결론

**Minesweeper에서 JEV는 버그를 직접 찾아내는 도구보다, “현재 질문과 evidence가 요구사항을 정말 입증하고 있는가”를 압박하는 reviewer로 사용할 때 가장 효과적이었다.**

JEV FAIL을 이용해 코드 점수를 맞추는 대신 질문 원자화와 deterministic evidence를 강화했고, 그 결과 최종 검증은 더 설명 가능하고 재현 가능한 형태가 됐다.


---

# 2026-09-23 GPT Web 피드백 — MiniStore JEV 효율과 ProjectHub 후속 보완

## A. MiniStore 관찰 결과: 실험 사실과 한계

테스트 프로젝트: 정적 `file://` 매장관리/POS 웹앱. 초기 Worker 작업 모델은 `gpt-6-luna`, reasoning `medium`.

확인된 흐름:

1. Codex는 앱, `MiniStoreValidation.runAll()`, 고유 check ID 56개, `EvidenceIndex.md`를 만들었다.
2. 초기 작업에서는 Browser Use가 `file://` 접근을 차단하자 validator 자체도 실제 실행하지 못한 상태로 Web에 `[NEXT : WEB]` 보고했다. 검증 불가능이라고 정직하게 표시했지만 **브라우저 UI와 무관한 엔진 검증까지 중단**됐다.
3. Web은 사용자에게서 받은 실제 JS로 메모리 `window`/storage를 제공하는 로컬 Node 래퍼를 사용해 validator를 실행했다. 결과: `passed=true, total=56, failed=[]`. 이는 **엔진/상태/저장 복구의 직접 실행 증거**이지 실제 브라우저 UI E2E가 아니다.
4. 새 Worker/Codex 세션에서 15개 JEV 질문(12 NOUL, 2 CHOICE, 1 SCORE)을 일괄 요청했고 JEV 결과는 `ALL_PASS`. transaction atomicity evidence는 `SUPPORTED`, 브라우저 UI evidence는 `PARTIAL`로 요청·보고됐다.
5. 후속 사용자 실검증 7/7에서 특이사항이 없다고 확인했다.

해석상의 한계:

- 56/56은 **실제 실행한 56개 assertion 범위**만 증명한다. 전체 구현에 미발견 결함이 없다는 뜻은 아니다.
- JEV에 전달된 `EVIDENCE:`는 함수명·check ID·PASS 결과를 설명하는 **텍스트 요약**이었다. Worker가 원본 소스/실행 로그를 구조화해 JEV state에 자동 첨부한 결과가 아니다.
- 이번 JEV에는 알려진 결함/모순 fixture를 일부러 삽입한 대조 실험이 없었다. `ALL_PASS`는 판단 성공 사례지만, **JEV의 결함 탐지율이 입증된 것은 아니다**.
- 사용자가 언급한 약 7만 토큰은 유용한 예산 참고값이지만, 이번 Worker 로그만으로 input/cached/output/reasoning과 Web/JEV 사용량을 단계별로 독립 확인할 수는 없다.
- Worker 로그에서 긴 지시문과 JEV footer가 누적·반복 보인다. 로그 중복을 곧바로 실제 과금 토큰 중복이라고 단정하지 않고 **실제 전송 payload 크기와 provider usage를 계측**해야 한다.

## B. 다음 ProjectHub 보완 후보

### P0-1. 도구 제약 시 검증 경로 분리·대체

문제: `file://` 브라우저 접근 제한이 headless 실행 가능 validator까지 검증 중단으로 이어졌다.

제안: 작업 카드에서 검증을 `ENGINE_HEADLESS`, `UI_BROWSER`, `HUMAN_UX`처럼 분리하고, 작업 시작 시 가능한 runner/capability를 확인한다.

- UI 브라우저 접근 불가여도 별도 로컬 validator 실행이 가능한지 확인한다.
- 안전하고 승인된 대체 runner(Node VM/CLI/오프라인 harness)가 있으면 엔진 검증을 먼저 수행한다.
- 대체 runner가 실제 브라우저 동작을 증명한다고 표시하지 않는다.
- 불가능한 검증은 `BLOCKED_BY_TOOL`로 보존하고, 이미 통과한 독립 검증까지 미실행으로 덮어쓰지 않는다.
- 대체 실행에 별도 사용자 승인이나 권한이 필요하면 정책을 존중한다.

AC 후보: 브라우저 접근 차단 fixture에서도 엔진 validator는 수행 가능하면 실제 exit/result가 수집되고, UI는 별도로 `PARTIAL/BLOCKED`로 남아야 한다.

### P0-2. 09-C 원본 Evidence 자동 전달과 provenance

문제: 현재의 JEV `SUPPORTED`는 구조적으로 전달된 원본 증거가 아니라 Codex/Web이 생성한 함수·검증 요약을 기반으로 할 수 있다.

09-C 최소 envelope 후보:

```text
job_id, ac_id, question_id
evidence_id, kind(source|diff|test|runtime|user)
source_revision, content_digest
runner, command, exit_code, checked_at
excerpt_or_structured_result, scope
provenance(EXECUTED|SOURCE_EXCERPT|SUMMARY_ONLY|USER_VERIFIED)
```

- Worker가 실제 접근 가능한 산출물/실행 결과를 수집하고 question→evidence mapping을 실제 JEV state에 넣는다.
- 참조 문자열만 존재하거나 `SUMMARY_ONLY`인 evidence를 실행 증거와 구분한다.
- claim에 필요한 직접 근거가 빠지면 `SUPPORTED`로 자동 승격하지 않는다.
- 관련 source·AC·fixture·evidence digest가 바뀌면 과거 PASS를 무효화한다.
- 하나의 evidence 묶음을 관련 원자 질문들이 공유하되, 관계없는 파일·누적 stdout을 보내지 않는다.
- 증거 수집 시 로컬 접근권한/사용자 승인, 민감값 제거, 크기 상한을 적용한다.

AC 후보: JEV가 실제 원본 evidence를 받은 경우와 문자열 참조만 받은 경우가 결과·상태에서 분명히 구별돼야 한다.

### P0-3. 검증 계층별 상태와 최종 완료 gate

```text
ENGINE: PASS (56/56; runner=local Node wrapper)
BROWSER_UI: PARTIAL 또는 BLOCKED (미실행)
JEV: ALL_PASS (evidence provenance 명시)
HUMAN_UX: PASS (7/7; user-reported)
```

- 서로 다른 검증 범위를 전체 `VALIDATION PASS` 한 값으로 합치지 않는다.
- 실행 이력과 사용자 보고를 분리하고, 주장의 scope를 좁혀 유지한다.
- `JEV ALL_PASS`가 `BROWSER_UI PASS`를 암시하지 않게 한다.
- 관련 AC에 실제 증거가 남아 있을 때만 최종 END를 허용한다.

### P1-1. 10-A 비용 계측과 중복 문맥 절약

이번 약 7만 토큰/완료 사례를 **자기보고 baseline**으로 보존한다. 정밀 분석을 위해 다음 항목을 호출별로 계측한다.

```text
job / round / role / model / reasoning / purpose
input / cached_input / output / reasoning / provider_total
prompt_bytes / footer_bytes / evidence_bytes
latency / retry_reason / usage_known
JEV question counts / batch size / failure classification
```

- 누적 snapshot과 incremental usage를 혼동하거나 캐시 입력을 두 번 더하지 않는다.
- Web 내부 사용량 미공개는 `unknown`으로 둔다.
- JEV PASS 뒤 report-only Codex 호출 1회의 실측 비용을 별도 집계한다.
- 동일 내용의 장문 Web instruction/계약/footer 반복 전달을 payload fingerprint로 관찰한다.
- v1 계약은 보존하고, 우선 초기 full footer + 후속 short reminder, evidence index 기반 부분 전송을 검토한다.
- 최적화 성공은 총 질문 수나 총 토큰 감소만으로 판정하지 않고 **검증 완료한 AC 1개당 입력·출력 비용, 인간 개입 횟수, 재작업률**로 평가한다.

### P1-2. JEV 음성 대조군으로 실제 판별 능력 측정

MiniStore의 알려진 정답 fixture를 유지한 채, 격리된 테스트 상태에서만 다음 오류를 하나씩 주입한 **mutation/contradiction benchmark**를 제안한다.

```text
SALE 기록만 남고 재고 차감 없음
재고 차감은 있으나 SALE ledger 없음
동일 취소에서 재고 두 번 복원
CLOSED인데 입고 허용
코드상 경로 설명과 실행 log가 서로 모순
validator 미실행인데 요약에는 PASS라고 기재
```

각 경우 엔진 validator와 JEV가 무엇을 판정하는지 기록한다.

- validator가 발견한 deterministic 결함과 JEV가 발견한 evidence contradiction을 구분한다.
- `SUPPORTED` 오판, `PARTIAL`/ `INSUFFICIENT` 구분, 응답 누락, usage, latency를 기록한다.
- 모델·질문·evidence revision을 고정한 대조 실험 없이는 JEV 탐지율·절감률을 단정하지 않는다.
- 검증 실험은 실제 업무 데이터나 운영 코드에 결함을 남기지 않는 독립 fixture에서만 수행한다.

## C. 다음 실행 순서 — Master 및 활성 task 준수

1. **먼저 기존 09-B 실화면 마감.** 최신 Explorer 실제 실행본의 Judge OFF/NEXT WEB, JEV PASS, FAIL→동일 session, ERROR fallback, report-only 재진입 차단을 확인하고 현재 task/CurrentWork에 빌드와 실행 증거를 연결한다. 이번 외부 MiniStore 성공을 ProjectHub Explorer E2E 완료로 오인하지 않는다.
2. **이후 09-C만 활성화.** P0-2의 원본 evidence envelope과 출처/무효화, P0-3의 검증 상태 경계를 기존 09-C 범위 안에서 구현한다.
3. 도구별 대체 검증 경로(P0-1)는 09-C와 무관한 변경까지 섞지 말고 후속 작업 카드로 등록한다.
4. 09-C 완료 뒤 **10-A 비용 최적화**에서 P1-1을 실측하고, JEV 판별 대조군(P1-2)을 후속 별도 실험으로 운영한다.
5. 기존 07 Force Restore의 잔여 검증과 외부 변경/배포 승인 정책은 그대로 보존한다.

**핵심 결론:** MiniStore는 GPT-6 Luna Medium이 앱+validator+evidence index를 한 작업에서 구성하고 JEV 질문을 의미 검증에 집중할 수 있음을 보여줬다. 다음 ProjectHub 보완의 우선 목표는 더 많은 JEV 질문이 아니라 **검증 가능한 실행 경로를 자동 선택하고, 원본 evidence를 신뢰 가능한 provenance로 전달하고, 그 과정의 실측 비용을 기록하는 것**이다.

---

# 2026-09-23 GPT Web 상세 설계 피드백 — 네 가지 AI 역할 및 CLI-to-CLI 아키텍처

> **정책 동기화:** 이 피드백은 사용자 지시에 따라 같은 날 수정한 `Master-Polish.md`의 **목표 아키텍처**를 구현 관점으로 풀어 쓴 설계안이다. Master 개정 커밋: `c4dfba8e4ba900793c4d679f4de9b12fa5b41e8f`. **새 CLI-to-CLI·네 역할 설정창은 아직 제품 구현 완료가 아니다.** 기존 09-B Explorer 검증과 09-C evidence 전달, 07 잔여 작업은 유지한다. 활성 번호 작업·A/B/C를 임의 변경하지 않는다.

## 1. 사용자 결정 및 설계의 핵심

새 ProjectHub의 공식 AI 역할 명칭은 네 개로 고정한다.

| 역할 | 필수 | 책임 | 목표 기본값 |
| --- | --- | --- | --- |
| **1. 설계·관제 AI** | 필수 | 요구 정리, 시스템 설계, 작업 카드·AC 작성, 결과 검토, 다음 단계 및 종료 판단 | OpenAI GPT-6 Sol CLI |
| **2. 작업 AI** | 필수 | 실제 구현·수정, 도구 실행, 로컬 validator, 증거 생성 | OpenAI GPT-6 Luna CLI, Medium |
| **3. 작업 판단 AI** | 선택 | 코드·diff·테스트·실행 evidence를 요구에 대조해 판단 | OFF; 활성화 시 기존 JEV 연결 우선 |
| **4. 고수준 작업 AI** | 선택 | 작업 AI가 해결하기 힘든 **허가된 하위 구현·분석 작업** 수행 | OFF; 필요 시 별도 OpenAI 모델/추론 설정 |

**역할과 모델은 1:1 고정하지 않는다.** 같은 모델을 복수 역할에 할당하더라도 각 역할은 독립 세션과 별도 사용량을 가진다. 기본값은 사용자 요청에 따른 **목표 구성 예시**이며 계정/CLI에서 모델 및 reasoning을 지원하는지 확인하기 전에는 실행 성공을 보장하지 않는다.

이번 변경은 'GPT Web을 쓰지 못하면 Luna가 혼자 설계한다'가 아니다. **Worker가 사용자 요구를 Sol 설계·관제 CLI에 먼저 전달하고, Sol이 작업 범위를 고정한 다음 Luna가 구현한다.** 브라우저 Web/Extension은 레거시/선택 모드로 유지하지만 CLI-to-CLI 기본 실행에 필요하지 않아야 한다.

## 2. 역할별 결정권과 금지 사항

### 설계·관제 AI: 필수 · read-only 기본

- 사용자 목표를 보존하고 단일 활성 작업의 `goal / scope / AC / evidence_requirements / validation_plan`을 정한다.
- '실행 가능한 작업 카드'만 작업 AI에 전달한다. 누적 Master·원본 로그 전문을 매번 보내지 않는다.
- 로컬 테스트 실패는 작업 AI에 좁게 재지시, evidence 부족은 EvidenceCollector 또는 추가 테스트, 판단 불확실성은 사용자 확인/별도 판단으로 분기한다.
- 완료의 **의미적 제안**은 관제 AI가 하되, 완료 가능한 **기계적 조건**(필수 AC evidence freshness, unresolved FAIL, 승인 범위)은 Worker가 강제한다.
- 소스 수정이나 shell/Git push를 관제 역할의 독자 권한으로 제공하지 않는다.

### 작업 AI: 필수 · 승인된 작업 범위 내 쓰기

- 지정된 하나의 카드와 해당 AC를 수행하며 자체 판단으로 AC·threshold를 완화하지 않는다.
- 변경 파일·실행한 명령·exit code·테스트 check ID·evidence digest·잔여 문제를 Worker에 구조화해 보고한다.
- 같은 카드의 국소 수정은 동일 작업 세션에서 resume한다.
- 작업 결과를 `ALL_PASS`라고 선언하는 대신 로컬 검증 결과와 증거를 제출한다. 최종 수용은 관제/Worker 규칙을 통과해야 한다.

### 작업 판단 AI: 선택 · 소스 쓰기 금지

- 초기의 검증 엔진 연결은 **현행 JEV**를 재사용한다. JEV는 OpenAI 모델이 아니므로 'AI 공급사에 타사를 올린다'는 뜻이 아니다. Judge 카드의 검증 엔진 선택·연결 항목으로 별도 표시한다.
- 장차 OpenAI 모델을 판단 AI로 사용하려면 CLI judge 어댑터와 결과 계약을 별도 구현·검증한 뒤 열어 준다. 구현되지 않은 모델 선택이 실제로 JEV를 실행한다고 표시하지 않는다.
- ON이면 가능한 원자 질문을 `CLAIM + EVIDENCE + SCOPE + COUNTEREXAMPLE`로 batch한다. deterministic 산술·exit code는 로컬 도구가 권위자다.
- OFF여도 필수 validator는 그대로 실행한다. evidence 미충족 또는 실제 UI 미검증을 자동 PASS로 승격하지 않는다.
- `FAIL / INSUFFICIENT / PARTIAL / PROVIDER_ERROR`를 구분한다. confidence만 낮고 직접 모순이 없으면 점수 맞추기식 코드 수정을 시키지 않는다.

### 고수준 작업 AI: 선택 · 위임받은 작업자

- '설계·관제 AI보다 더 높은 권한'이라는 뜻이 아니라 **별도 역할·별도 세션의 고난도 실행 자원**이다.
- 기본 OFF. ON이어도 모든 문제에 자동 호출하지 않는다. 관제가 실패 증거와 이유를 좁히고 설정된 에스컬레이션 규칙·예산·승인 조건이 충족된 경우만 위임한다.
- 최초 위임은 필요에 따라 읽기 전용 분석만 가능하며, 쓰기가 필요한 경우 Worker가 한시적으로 해당 작업의 exclusive write lease를 이양한다. Luna와 동일 워크스페이스에 동시에 쓰지 않는다.
- 결과물은 관제에 직접 최종 수용시키지 않고, 원래 AC에 대해 검증 후 설계·관제 AI가 검토한다.

**Worker 경계:** AI가 자신의 역할을 넘어 승인·권한·예산·최종 상태를 직접 덮어쓸 수 없다. provider 오류 시 다른 모델/과금 경로로 무단 전환하지 않는다. Git commit/push·배포 등 외부 변경은 기존 사용자 명시적 승인 정책을 유지한다.

## 3. 설정창: 단일 모델 선택을 네 역할 카드로 이관

최종 UI에는 별도 `설정 > AI 역할 설정` 섹션을 만든다. 기존 메인 화면 하단의 공용 모델/추론 선택은 **새 UI가 실제 연결·검증된 뒤** 제거/이관한다. 기능을 잃지 않은 채 이행해야 한다.

**공통 역할 카드:** 역할 명칭, 필수/선택 배지, 공급사, 모델, 지원되는 reasoning, 연결/사용 가능 상태, 변경 적용 시점을 표시한다.

- 설계·관제 AI와 작업 AI는 필수: ON 고정, 실행 전 모델 선택/인증/필요 capabilities 확인.
- 작업 판단 AI와 고수준 작업 AI는 선택: OFF일 때 추가 실행·비용 0(로컬 validator는 계속 실행); ON일 때 해당 연결/모델/정책 활성화.
- AI 공급사 드롭다운에는 **현재 OpenAI(ChatGPT 계열)만 노출**한다. 향후 타사 확장 가능하도록 내부 `provider_id`와 `model_id`는 분리한다. 지원되지 않은 미래 회사/모델을 placeholder로 노출하지 않는다.
- 모델 목록은 당시 실행 가능한 후보를 CLI capability/preflight에서 검증한다. reasoning 옵션도 모델별 지원 값을 사용한다. 지원하지 않는 조합은 조용히 대체하지 말고 명시적 상태 표시.
- Judge 카드의 JEV는 `판단 방식=JEV 엔진 연결`, timeout/엔진 상태 등으로 별도 표현한다. 향후 `OpenAI 판단 모델` 방식은 구현 후 활성화. JEV의 제3자 엔진명을 현재 AI 공급사 목록에 추가하지 않는다.
- 역할별 모델 변경은 이미 실행 중인 세션에 소급하지 않는다. Worker가 Job 시작 시 모델·추론·권한을 snapshot하고, 다음 새 작업/안전한 단계에서 반영한다.
- 동일 모델을 여러 역할에 지정할 수 있어도 역할별 session ID와 usage는 절대 합쳐 저장하지 않는다.

### 예제 A — 목표 설정 JSON (미구현 내부 schema v2 제안)

```json
{
  "schema_version": 2,
  "execution_mode": "cli_to_cli",
  "roles": {
    "coordinator": {
      "label": "설계·관제 AI",
      "required": true,
      "enabled": true,
      "provider_id": "openai",
      "transport": "codex-cli",
      "model_id": "gpt-6-sol",
      "reasoning": "high",
      "permission_profile": "read-only"
    },
    "implementer": {
      "label": "작업 AI",
      "required": true,
      "enabled": true,
      "provider_id": "openai",
      "transport": "codex-cli",
      "model_id": "gpt-6-luna",
      "reasoning": "medium",
      "permission_profile": "approved-workspace-write"
    },
    "judge": {
      "label": "작업 판단 AI",
      "required": false,
      "enabled": false,
      "backend": "jev",
      "engine_model": "jev-latest"
    },
    "advanced_implementer": {
      "label": "고수준 작업 AI",
      "required": false,
      "enabled": false,
      "provider_id": "openai",
      "transport": "codex-cli",
      "model_id": "gpt-6-sol",
      "reasoning": "high",
      "permission_profile": "delegated-write-only"
    }
  },
  "policy": {
    "allow_paid_fallback": false,
    "allow_automatic_escalation": false,
    "max_concurrent_workspace_writers": 1
  }
}
```

주의: 위 JSON은 **현재 UI·현재 config 파일 계약이 아니다.** 저장 포맷을 바꿀 때 버전 및 기존 설정 마이그레이션을 설계하고 테스트해야 한다. Web 모드와 기존 모델 설정을 임의 폐기하지 않는다.

## 4. 제안 내부 아키텍처와 정상 단계

```text
UserCommand
    → JobRunner / JobState
    → ICoordinatorAdapter (Sol 전용 CLI session / read-only)
       → CoordinatorPlan (작업 카드 1개, AC)
       → IImplementerAdapter (Luna 전용 CLI session / write lease)
          → ImplementerResult + EvidenceIndex
          → deterministic validator (권한/실행 사실은 Worker가 확인)
          → EvidenceCollector (실제 source/diff/log digest 및 증거 범위)
          → [judge ON] IJudgeAdapter (JEV)
              ├─ PASS → coordinator review
              ├─ 부족 → evidence collection
              ├─ FAIL → 같은 implementer session에서 좁은 보완
              └─ ERROR → 재시도/대기 또는 정책 fallback (구현 실패 아님)
          → [judge OFF] coordinator review (로컬 validator 생략 금지)
       → coordinator: 다음 카드 / 위임 / 대기 / 완료 제안
       → Worker: 승인된 범위 및 모든 필수 AC freshness gate
```

구현 시 `ICoordinatorAdapter`, `IImplementerAdapter`, `IJudgeAdapter`, `IAdvancedImplementerAdapter`, `EvidenceCollector`, `JobRunner`로 경계만 명확히 만들되, 09-B/09-C보다 앞서 대규모 추상화 리팩터링을 실행하지 않는다.

상태별 provider 장애(인증/429/연결), 사용자 취소, Job 복구와 budget unknown은 Worker의 영속 상태에서 처리한다. 세션 간에 과거 채팅 전문을 복제하는 대신 **task packet / evidence index / 짧은 review packet**만 전달한다. 사고로 같은 쓰기 작업을 두 번 실행하거나 두 CLI가 같은 파일을 경합하지 않게 한다.

## 5. 복사 가능한 내부 메시지 예제 (신규 계약 제안)

**중요:** 아래 JSON 블록은 **향후 CLI-to-CLI Worker 내부 계약**의 설계 샘플이지 기존 v1 `[ACTION]/[NEXT]` parser가 바로 인식하는 메시지가 아니다. 기존 Web 모드의 공개 wire는 별도 유지한다.

### 예제 B — 사용자 → 설계·관제 AI (초기 설계 요청)

```json
{
  "kind": "COORDINATOR_REQUEST",
  "job_id": "JOB-EXAMPLE",
  "user_goal": "로컬 매장관리 웹앱 제작",
  "constraints": ["index.html 더블클릭 실행", "외부 서버 금지"],
  "current_project_state": "신규 작업",
  "governance": {
    "active_work_item_limit": 1,
    "no_unapproved_git_write": true,
    "coordinator_permissions": "read-only"
  },
  "output_required": [
    "AC와 검증 기준이 고정된 작업 카드 1개",
    "필요한 근거와 허용된 변경 경계"
  ]
}
```

### 예제 C — 설계·관제 AI → Worker → 작업 AI (작업 카드)

```json
{
  "kind": "WORK_CARD",
  "job_id": "JOB-EXAMPLE",
  "work_id": "W01",
  "parent_goal": "MiniStore local POS",
  "scope": ["StoreEngine.completeSale", "해당 deterministic validator"],
  "acceptance_criteria": [
    {
      "ac_id": "AC-SALE-01",
      "claim": "재고 부족 결제 실패 시 Sale·stock·ledger가 전혀 변경되지 않는다",
      "check_ids": ["FAIL-STOCK-REJECT", "FAIL-STOCK-ATOMIC"],
      "evidence_required": ["실제 실행한 검증 출력", "관련 구현 구간과 diff digest"]
    }
  ],
  "validation_commands": ["기존 headless validator 실행"],
  "permissions": "approved-workspace-write",
  "prohibited": ["AC 변경", "JEV threshold 완화", "승인 없는 외부 변경"],
  "result_contract": "IMPLEMENTER_RESULT"
}
```

※ `validation_commands`는 프로젝트 실제 실행 명령으로 작업 카드 생성 시 확정한다. 존재하지 않는 도구 명령을 임의로 실행하지 않는다.

### 예제 D — 작업 AI → Worker (구현 결과와 증거)

```json
{
  "kind": "IMPLEMENTER_RESULT",
  "job_id": "JOB-EXAMPLE",
  "work_id": "W01",
  "session_role": "implementer",
  "changed_paths": ["js/store-engine.js", "js/validation.js"],
  "validation": [
    {
      "runner": "local-node-wrapper",
      "command": "실제로 실행된 검증 명령",
      "exit_code": 0,
      "checked_ids": ["FAIL-STOCK-REJECT", "FAIL-STOCK-ATOMIC"],
      "result": "PASS",
      "observed_at": "실제 실행 시각"
    }
  ],
  "evidence_refs": ["E-SALE-SOURCE", "E-SALE-TEST", "E-SALE-RUN"],
  "unverified": ["실제 file:// 사용자 입력 UX"],
  "next": "VERIFY"
}
```

※ `result=PASS`는 Worker가 해당 명령의 출력을 수집·대조한 뒤 신뢰한다. Codex가 적은 문자열 자체는 실행 증명으로 사용하지 않는다.

### 예제 E — Worker → JEV (기존 v1 호환 질문 텍스트 예시)

```text
[NEXT : JEV]

[VALIDATION REQUEST]

- NOUL | [CRITICAL] 재고 부족 결제 실패 뒤 Sale이 추가되지 않는가?
  EVIDENCE: E-SALE-SOURCE + E-SALE-TEST + E-SALE-RUN
  SCOPE: completeSale() 재고 부족 경로의 Sale collection
  COUNTEREXAMPLE: 실패 이후 Sale count 증가
  PASS: YES >= 0.90

- NOUL | [CRITICAL] 재고 부족 결제 실패 뒤 상품 stock이 바뀌지 않는가?
  EVIDENCE: E-SALE-SOURCE + E-SALE-TEST + E-SALE-RUN
  SCOPE: 해당 product stock
  COUNTEREXAMPLE: 실패 이후 stock 불일치
  PASS: YES >= 0.90

- NOUL | [CRITICAL] 재고 부족 결제 실패 뒤 ledger가 추가되지 않는가?
  EVIDENCE: E-SALE-SOURCE + E-SALE-TEST + E-SALE-RUN
  SCOPE: InventoryTransaction collection
  COUNTEREXAMPLE: 실패 이후 새 ledger row 존재
  PASS: YES >= 0.90
```

위 `E-...`는 09-C 이후 **실제 전달된 원본 evidence 객체**를 가리켜야 한다. 단순한 로컬 경로나 테스트 ID 문자열만으로 JEV가 내용을 자동 열람한다고 가정하지 않는다. JEV 응답의 `confidence`를 시스템 완성도로 재해석하지 않는다.

### 예제 F — JEV OFF / 증거 부족 / 고수준 작업 위임

```json
{
  "kind": "COORDINATOR_REVIEW",
  "job_id": "JOB-EXAMPLE",
  "work_id": "W01",
  "local_validation": "PASS",
  "judge": { "enabled": false, "status": "SKIPPED" },
  "browser_ui": { "status": "UNVERIFIED", "reason": "실행 증거 없음" },
  "decision": "COLLECT_EVIDENCE",
  "next_request": "실제 브라우저 UI 검증과 해당 콘솔 결과 확보"
}
```

```json
{
  "kind": "ADVANCED_DELEGATION_PROPOSAL",
  "job_id": "JOB-EXAMPLE",
  "work_id": "W01",
  "origin": "coordinator",
  "reason": "동일 실패 재현 근거를 갖춘 고난도 하위 문제",
  "scope": ["지정한 하위 문제와 관련된 파일만"],
  "required_permissions": "read-only-first",
  "max_additional_budget": "사용자가 사전 승인한 구체 한도",
  "state": "AWAITING_POLICY_AND_BUDGET_APPROVAL"
}
```

고수준 작업 AI가 OFF면 위 예제의 위임은 **실행되지 않는다**. 먼저 Sol 관제가 작업 범위를 다시 좁히거나, 승인 필요 상태로 멈춘다. ON 상태도 permission/budget/lease 조건을 통과해야 한다.

## 6. CLI 세션·권한·비용 설계

- Job 안에 `coordinator_session_id`, `implementer_session_id`, `advanced_session_id`(선택)를 분리 저장한다. Judge가 JEV라면 CLI 세션이 아니라 JEV 요청 ID·model revision·usage를 별도로 저장한다.
- 각 실행에는 role/model/reasoning/permissions snapshot과 `request_id`·`work_id`를 붙인다. 실행 도중 설정창 변경이 현재 호출에 영향을 주지 않는다.
- 실제 사용량은 role/provider/model 단위로 input/cached/output/reasoning(확인 가능한 경우), JEV 별도 usage, retry·latency를 분리한다. 불명 사용량은 0이 아니라 `unknown`.
- Luna의 작은 수정·재검증에 Sol을 반복 호출하지 않는다. Sol은 최초 설계, 작업 경계, 본질적 실패 재분해, 최종 수용 검토에 집중한다. 고수준 AI가 켜져도 비용 상한·호출 조건 없이는 실행하지 않는다.
- Luna/고수준 작업 AI 사이의 작업 위임은 한 시점에 하나의 쓰기 lease만 허용한다. 두 모델 간 임의 세션 공유·자동 모델 상향은 금지한다.
- 현재 Web 모드의 provider 사용 제한·로그인/권한과 새로운 CLI 모드의 계정/usage 정책을 섞지 않는다. 토큰 절감률을 실측 전 약속하지 않는다.

## 7. 수용 조건 및 필수 회귀 테스트 설계

| ID | 확인 조건 | 검증 방식 |
| --- | --- | --- |
| ROLE-01 | 설계·관제 AI와 작업 AI는 비활성화할 수 없다 | UI·설정 validation fixture |
| ROLE-02 | 판단·고수준 AI OFF라도 기본 CLI-to-CLI Job이 완료된다 | mock coordinator/implementer E2E |
| ROLE-03 | 설계·관제 모델과 작업 모델 및 reasoning을 서로 다르게 지정할 수 있다 | 설정 round-trip / 실행 snapshot |
| ROLE-04 | 현재 AI 공급사 목록에는 OpenAI만 표시된다 | UI 목록 확인; Judge의 JEV는 엔진 카드 |
| FLOW-01 | 최초 사용자 지시는 Sol 관제 세션으로 가며 첫 작업 카드 확정 전 Luna 실행이 0회 | mock CLI invocation log |
| FLOW-02 | 모델이 같아도 역할별 세션 ID·usage는 분리된다 | CLI session/usage fixture |
| FLOW-03 | 관제 read-only 위반, 승인되지 않은 Git 변경을 Worker가 거부한다 | permission/approval fixture |
| FLOW-04 | 동일 폴더에 작업 AI와 고수준 AI가 동시에 쓰지 못한다 | write lease 경합 fixture |
| JUDGE-01 | Judge OFF에서 deterministic validator는 계속 실행된다 | judge OFF E2E |
| JUDGE-02 | Judge ON에서 실제 evidence refs와 원자 질문만 전달되고 오래된 PASS는 무효화된다 | 09-C evidence digest fixture |
| JUDGE-03 | JEV FAIL / 부족 / ERROR는 각각 수정·증거 수집·대기로 분기한다 | mock judge responses |
| HIGH-01 | 고수준 AI OFF/예산 부족/미승인 상태의 자동 호출은 0회 | escalation guard fixture |
| RECOVER-01 | 취소·재시작 후 확인된 phase와 역할별 snapshot으로 복구, 중복 side effect 없음 | JobRunner restart fixture |
| LEGACY-01 | 기존 Web ACTION/NEXT/JEV 경로가 새 CLI 모드 추가 뒤에도 유지된다 | 현재 09-B E2E 회귀 fixture |

표의 test ID는 구현 전 계획이며 실제 PASS 근거가 아니다. mock과 실제 CLI 실행, CLI/API 대체와 Explorer 실화면의 검증 등급을 분리해 기록한다.

## 8. 단계별 구현 인계 — 기존 Master 순서 보존

**이번 피드백은 설계 결정이므로 Codex가 현재 활성 09-B를 건너뛰어 설정 UI·CLI 아키텍처 전체를 동시에 구현하면 안 된다.**

1. **09-B Explorer 마감**: Web 기존 Judge OFF, NEXT WEB, PASS, FAIL 동일 session, ERROR fallback, PASS 후 report-only 차단의 실화면 증거 확보. 기존 체계는 호환 baseline이다.
2. **09-C evidence envelope / AC freshness**: 역할에 무관한 원본 증거 전달을 먼저 완성한다. MiniStore처럼 `EVIDENCE: 테스트 이름`만 있고 원본이 없는 경우 JEV가 실제 증거를 본 것으로 표시하지 않는다.
3. **10-A 토큰/메시지 계측**: 이후 Sol 관제 호출과 Luna 작업 호출을 분리해 측정할 수 있는 필드부터 마련한다. 정확성 유지 조건으로 긴 반복 footer·맥락 재전송을 줄인다.
4. **10-B 개정**: 신규 `Coordinator-first CLI-to-CLI`와 필수 두 역할의 설정창/독립 세션을 **하나의 순차 작업 범위로 세분화**하여 구현한다. 첫 Sol CLI capability smoke, Sol→작업 카드→Luna→로컬 검증→Sol review 정상 경로를 확인한다. 기존 Web 모드는 그대로 유지한다.
5. **10-C JobRunner 복구**: 모델 snapshot, 관제/작업의 별도 세션, 취소·재개·중복 side effect를 검증한다.
6. **11-C 개정**: 선택적 작업 판단 AI ON/OFF와 고수준 작업 AI ON/OFF를 추가하고, JEV 연동·고수준 위임 lease/승인/budget의 실제 회귀를 통과한다. 다른 회사 AI의 실통합은 **이번 범위가 아니며**, 독립 인터페이스 경계만 준비한다.

`ProjectHub_IMPLEMENTATION_PLAN.md`, `CurrentWork.md`, 기존 09 task는 과거 완료·미완료의 원본 상태로 남긴다. 코드·UI 구현이 없는 상태에서 이를 완료로 바꾸지 않는다. 구현 순서나 범위를 다시 확정할 때 해당 활성 task에서 별도 반영한다.

## 9. 설계 완료 판정

이번 문서 작업의 완료 조건은 **네 역할 명칭/권한/기본값이 Master와 이 피드백에서 일치하고, 초기 OpenAI 모델 카탈로그와 별도 JEV 엔진을 혼동하지 않으며, 사용자가 선택한 설정창/CLI-to-CLI 경로가 구현 가능한 인터페이스·예제·AC로 구체화되어 있는 것**이다.

제품 완료 조건은 별개다: 실제 Sol CLI와 Luna CLI의 동작, 최소 2-CLI E2E, 설정 실연결, JEV ON/OFF, 선택적 고수준 실행, 상태 복구, 비용 계측은 차후 각 작업의 **실행 evidence**가 있어야 PASS라고 한다.

---

# 2026-09-24 GPT Web 설계 정정 — Worker는 계약 라우터만 수행

> **최우선 정정 / 이후 구현 기준:** 이 절은 앞선 GPT Web 피드백 중 CLI-to-CLI에서 Worker가 AC, validation command, evidence freshness, JEV 의미 결과를 직접 판정하도록 제안한 부분을 **대체한다.** 기존 Web ↔ Codex 장기 개발에서 검증된 핵심은 Worker가 답의 의미를 심사한 것이 아니라, **고정 Footer 계약을 매 호출에 주입하고 첫 제어 태그에 따라 다음 상대에게 전달한 것**이었다. 새 네 역할 구조도 이 방식만 범용화한다.
>
> 이 절의 목표는 “Worker를 더 똑똑하게 만들기”가 아니라 반대다. **Worker는 계약 문법·행선지·세션·프로세스만 관리하고, 작업 내용의 진위·충분성·완료 여부는 AI 역할들이 판단한다.**

## 1. 최종 설계 원칙 — Worker = Contract Router

Worker가 수행할 책임은 아래 범위로 제한한다.

### Worker가 해야 하는 일

1. 역할별 설정에 따라 대상 AI/모델/세션을 실행한다.
2. 각 역할 호출 prompt 끝에 해당 역할용 **고정 계약(Footer)** 을 주입한다.
3. 응답의 **첫 제어행**만 파싱한다.
4. 정해진 행선지에 응답 본문을 가능한 그대로 전달한다.
5. 역할별 session ID를 보존하고 같은 역할의 후속 턴은 같은 세션으로 resume한다.
6. 선택 역할이 OFF이거나 provider/CLI가 실행 불가능한 경우 **라우팅 불가 상태**만 표시한다.
7. 프로세스 시작/종료, 취소, timeout, 인증, transport 오류, transcript, usage 등 실행 인프라를 관리한다.
8. 파일 첨부/CLI artifact 전달이 필요한 경우 경로 접근 권한과 크기·보안 제한만 적용한다.
9. 기존 Web 모드의 `[ACTION]`, `[NEXT : WEB|JEV]` 계약은 legacy로 보존한다.
10. 새 CLI-to-CLI 모드는 동일 철학으로 역할명만 일반화한다.

### Worker가 하지 말아야 하는 일

- 작업 지시의 내용이 적절한지 판단
- AC가 실제로 만족됐는지 판단
- 테스트가 충분한지 판단
- shell command가 관제 AI가 요구한 command와 의미상 같은지 비교
- 실행 결과를 보고 PASS/FAIL을 자체 생성
- 작업 AI의 `IMPLEMENTED/PARTIAL/BLOCKED`를 의미적으로 검증
- 관제 AI의 `[ACTION=END]`를 별도 evidence gate로 거부
- JEV/Judge 결과를 다시 threshold 계산해 PASS/PARTIAL로 판정
- evidence digest 변화만으로 과거 판정을 무효화
- 몇 회 수정했는지를 이유로 자동 FINISH_LIMIT 처리
- 관제 AI 대신 “다음에는 같은 작업 AI를 다시 호출해야 한다”고 결정
- Luna 결과를 분석해 고수준 AI로 자동 승격
- 작업 내용을 읽고 다음 행선지를 추론

**핵심:** Worker는 **문법을 확인할 수는 있지만 의미를 판정하지 않는다.** 예를 들어 `[NEXT : COORDINATOR]` 다음에 `[REPORT]`가 있는지는 확인할 수 있지만, REPORT에 적힌 “테스트 통과”가 사실인지는 판단하지 않는다.

## 2. 기존 Web 관제 방식에서 그대로 가져올 부분

기존 `JEV-FOOTER-CONTRACT.md`의 가장 중요한 구조는 다음이었다.

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
```

여기서 Worker의 본래 역할은 **중계자**였다. Codex에게 Footer를 붙여 “첫 줄에 어디로 갈지 적어라”라고 요구하고, 결과가 Web이면 Web에, JEV면 JEV에 전달했다.

새 구조는 이것을 아래처럼 일반화한다.

```text
사용자
  ↓
Worker
  ↓
설계·관제 AI
  ↓
Worker
  ├─ [ACTION=CONTINUE] + [NEXT : IMPLEMENTER] → 작업 AI
  ├─ [ACTION=CONTINUE] + [NEXT : HIGH_LEVEL]  → 고수준 작업 AI
  ├─ [ACTION=CONTINUE] + [NEXT : JUDGE]       → 작업 판단 AI
  ├─ [ACTION=PAUSE]                            → 사용자 대기
  └─ [ACTION=END]                              → 작업 종료

작업 AI / 고수준 작업 AI
  ↓
Worker
  ├─ [NEXT : COORDINATOR] → [REPORT]를 설계·관제 AI에 전달
  └─ [NEXT : JUDGE]       → [VALIDATION REQUEST]를 작업 판단 AI에 전달

작업 판단 AI
  ↓
Worker
  → 판단을 요청한 원래 AI 세션으로 결과 전달
  → 그 AI가 다시 자신의 고정 계약에 따라 다음 행선지를 선택
```

**Judge 결과의 복귀 원칙:** Judge를 요청한 주체를 Worker가 기억한다. 작업 AI가 Judge를 요청했으면 같은 작업 AI session으로 Judge 결과를 돌려주고, 설계·관제 AI가 Judge를 요청했으면 같은 관제 session으로 돌려준다. Worker가 Judge 결과를 해석해 다음 목적지를 고르지 않는다.

이 구조는 과거 **Codex → JEV → 동일 Codex session → Web 보고** 패턴을 역할 독립적으로 일반화한 것이다.

## 3. 범용 계약 — 제어부와 본문을 분리

계약은 **CONTROL HEADER + OPAQUE BODY**로 본다.

Worker가 이해하는 것은 CONTROL HEADER뿐이다. BODY는 대상 AI가 읽는다.

### 3.1 설계·관제 AI 계약

설계·관제 AI의 **첫 유효행은 반드시 ACTION**이다.

허용:

```text
[ACTION=CONTINUE]
[ACTION=PAUSE]
[ACTION=END]
```

`CONTINUE`일 때만 다음 유효 제어행으로 행선지를 요구한다.

```text
[NEXT : IMPLEMENTER]
[NEXT : HIGH_LEVEL]
[NEXT : JUDGE]
```

그 뒤 내용은 Worker가 해석하지 않는다. 권장 block marker만 구분한다.

작업 AI/고수준 작업 AI로 갈 때:

```text
[ACTION=CONTINUE]
[NEXT : IMPLEMENTER]

[INSTRUCTION]

현재 요구사항을 구현한다.
필요한 테스트를 수행한다.
결과와 남은 문제를 보고한다.
```

또는:

```text
[ACTION=CONTINUE]
[NEXT : HIGH_LEVEL]

[INSTRUCTION]

일반 작업 AI가 해결하기 어려운 특정 문제를 분석하고 수정한다.
...
```

Judge를 직접 선택할 때:

```text
[ACTION=CONTINUE]
[NEXT : JUDGE]

[VALIDATION REQUEST]

<판정 AI가 확인할 내용>
```

대기:

```text
[ACTION=PAUSE]

[REPORT]

사용자 승인 또는 실제 화면 확인이 필요하다.
```

종료:

```text
[ACTION=END]

[REPORT]

요청한 목표를 완료했다고 판단한다.
```

**Worker 동작:**
- ACTION 값이 세 종류 중 하나인지 확인한다.
- CONTINUE이면 NEXT가 허용 역할인지 확인한다.
- PAUSE이면 사용자 대기 상태로 바꾼다.
- END이면 종료 상태로 바꾼다.
- **END가 맞는 판단인지 재검증하지 않는다.**
- 본문의 AC, 테스트, 증거, 주장 내용은 파싱하지 않는다.

### 3.2 작업 AI / 고수준 작업 AI 계약

두 작업 역할은 같은 범용 Footer를 사용한다. 차이는 설정된 모델·권한·세션뿐이다.

첫 유효행:

```text
[NEXT : COORDINATOR]
```

또는:

```text
[NEXT : JUDGE]
```

관제 복귀 시:

```text
[NEXT : COORDINATOR]

[REPORT]

수행 내용:
- ...

변경 사항:
- ...

검증 결과:
- ...

남은 사항:
- ...
```

판정 요청 시:

```text
[NEXT : JUDGE]

[VALIDATION REQUEST]

- NOUL | ...
  EVIDENCE: ...
  PASS: ...
```

**Worker 동작:**
- NEXT가 허용 대상인지 검사한다.
- COORDINATOR이면 REPORT 존재만 확인하고 본문 전체를 같은 관제 session에 전달한다.
- JUDGE이면 VALIDATION REQUEST 존재만 확인하고 판정 AI에 전달한다.
- 작업 AI의 보고에서 `테스트 성공`, `변경 완료`, `미검증` 등을 Worker가 자체 판정하지 않는다.
- 별도의 `IMPLEMENTER_RESULT JSON → IMPLEMENT_ROUTE 두 번째 호출` 구조를 만들지 않는다. **작업을 수행한 바로 그 응답의 첫머리에 NEXT를 출력**하도록 Footer를 주입한다.

### 3.3 작업 판단 AI 계약

판정 AI는 요청받은 판단만 수행한다.

백엔드가 자유형 AI라면 권장 응답:

```text
[JUDGMENT]

<판정 결과 전체>
```

JEV처럼 별도 typed API라면 provider adapter는 **transport/schema 변환만** 한다. 응답의 NOUL/SCORE/CHOICE 값, confidence, provider 원문을 보존해 요청자에게 전달한다.

Worker는:
- HTTP/JSON 형식이 깨졌는지,
- provider 응답 자체가 없는지,
- timeout/transport error인지

같은 **기술 오류**만 구분한다.

Worker가 하면 안 되는 것:
- threshold를 적용해 `JudgeDecision.Pass/Partial`을 직접 산출
- direct evidence 부족을 이유로 JEV PASS를 PARTIAL로 변경
- evidence digest 변화로 Judge 결과를 무효화
- Judge 응답을 근거로 구현 코드 재작업을 자동 선택

이 판단은 Judge를 요청한 작업 AI 또는 최종 설계·관제 AI가 한다.

## 4. 현재 코드에서 제거/비활성화할 의미 게이트

아래 항목은 **CLI-to-CLI control path에서 제거**한다. Legacy Web 계약을 깨뜨릴 필요가 없으면 코드 삭제 대신 legacy 전용 경로로 격리할 수 있다.

### 4.1 `CoordinatorFirstContracts.cs`

CLI-to-CLI 라우팅에서 다음 의미 검사를 사용하지 않는다.

- `WorkCardSchema`를 필수 제어 계약으로 사용하는 구조
- `ImplementerResultSchema`를 필수 제어 계약으로 사용하는 구조
- `ReviewSchema`의 AC 집합/상태 일치 검사
- `ACTION_REVIEW_CONFLICT`
- `REVIEW_AC_SET_MISMATCH`
- `REVIEW_INVALID_STATUS`
- `HasRequiredValidationEvidence()`
- `CommandMatches()`

새 parser의 책임은 다음 정도로 줄인다.

```text
ParseCoordinatorControl:
  first = ACTION
  if CONTINUE: NEXT required and target allowed
  if PAUSE/END: NEXT not required
  body = opaque remainder

ParseWorkerRoleControl:
  first = NEXT
  COORDINATOR → REPORT marker required
  JUDGE → VALIDATION REQUEST marker required
  body = opaque remainder
```

JSON을 사용하고 싶다면 BODY 내부에서 AI 간 합의 형식으로 쓸 수 있지만 Worker 완료 gate가 되어서는 안 된다.

### 4.2 `RunCoordinatorFirstJobAsync()`

현재 루프에서 제거해야 할 변수/결정:

- `evidenceOk`
- `evidenceDetail`
- `judgeOk`
- `accepted = evidenceOk && judgeOk && ...`
- `ALL_REQUIRED_COMMANDS_OBSERVED_EXIT_ZERO`
- `NOT_OBSERVED`
- `FINISH_LIMIT`을 만드는 고정 `maxRounds = 3`
- CONTINUE이면 반드시 같은 카드/같은 deficiency만 해결하도록 Worker가 강제하는 분기
- REPORT의 status/AC를 해석해 END를 거부하는 분기

바꿀 핵심:

```text
invoke coordinator
→ parse ACTION/NEXT
→ route raw instruction body

invoke selected worker role
→ parse NEXT
→ route REPORT or VALIDATION REQUEST

if Judge requested
→ invoke Judge
→ return raw Judge result to requester session
→ requester emits another normal contract response

when coordinator emits PAUSE
→ pause

when coordinator emits END
→ end
```

**CONTINUE 횟수는 Worker의 내용 판단 한도가 아니다.** 무한 실행 보호가 필요하면 사용자 설정의 일반 실행 한도(시간/호출 수/예산)로 별도 관리하고, 한도 도달 시 `SYSTEM_LIMIT`으로 PAUSE한다. “세 번 수정했으니 구현 실패” 같은 의미 판단을 만들지 않는다.

### 4.3 Codex command execution 기록

`CodexCliRunner.ExtractCommandExecutions()`는 **관측/로그용으로 남겨도 된다.**

허용 사용:
- transcript에 실제 CLI command/exit code 표시
- 관제 AI에게 원문 또는 요약 전달
- 사용자가 펼쳐보기로 확인

금지 사용:
- required command와 문자열 비교해 Worker PASS/FAIL 생성
- Sol END를 차단하는 증거 gate
- JEV 요청 여부 자동 결정

이번 `hello.txt` 사례의 `NOT_OBSERVED` 오류는 이 기능을 의미 gate로 사용했기 때문에 생겼다. 해당 실환경 quote-stitching 문자열을 matcher 개선 과제로 쫓지 말고, **matcher 자체를 완료 gate에서 제거**하는 것이 이번 설계의 답이다.

## 5. JEV / Evidence 코드의 역할 축소

### 유지

- JEV endpoint 호출
- 인증/timeout/HTTP 오류 처리
- 요청/응답 schema의 기술적 parsing
- 요청 ID, provider revision, usage, latency 기록
- 허용된 파일/텍스트 artifact의 안전한 전달
- transcript/history 저장

### CLI-to-CLI 자동 판단에서 제거

- `JevJudgeRunner.Evaluate()`가 threshold를 읽어 Worker `Pass/Partial`로 만드는 의미 판정
- direct evidence가 없다는 이유로 결과를 Worker가 PARTIAL로 변경
- `JevEvidenceArchive.Save()`의 digest 변화 → prior PASS invalidation → `NEEDS_RECHECK`
- failed question ID를 Worker가 해석해 다음 재검증 질문 선택
- JEV 결과를 근거로 Worker가 작업 AI 재호출/관제 복귀를 선택

필요하면 이 기존 기능은 **legacy Web/JEV 실험용 evaluator**로 격리한다. 새 범용 router path에서는 Judge의 원시 결과를 요청자 AI가 해석한다.

## 6. 고수준 작업 AI 실제 연결

현재 설정에는 고수준 역할이 있으나 실행 preflight에서 활성화하면 차단한다. 새 계약에서는 제거한다.

관제 AI가:

```text
[ACTION=CONTINUE]
[NEXT : HIGH_LEVEL]

[INSTRUCTION]
...
```

을 반환하면 Worker는 설정의 `EffectiveHighLevel` 모델·reasoning·session을 사용해 실행한다.

고수준 작업 AI에도 **작업 AI와 동일한 Worker-role Footer**를 붙인다.

```text
[NEXT : COORDINATOR]
[REPORT]
...

또는

[NEXT : JUDGE]
[VALIDATION REQUEST]
...
```

Worker는 “이 문제가 정말 고수준 모델이 필요한가?”를 판단하지 않는다. 호출 선택은 관제 AI 책임이다.

선택 역할이 OFF인데 관제 AI가 HIGH_LEVEL/JUDGE를 선택한 경우에만:

```text
ROUTE_UNAVAILABLE: HIGH_LEVEL_DISABLED
ROUTE_UNAVAILABLE: JUDGE_DISABLED
```

같은 **구성 오류**로 관제 AI에 되돌려준다. 다른 역할로 자동 대체하지 않는다.

## 7. Prompt/Footer 파일 제안

모델명별 prompt를 만들지 말고 역할별 고정 계약으로 둔다.

### `COORDINATOR-ROUTING-CONTRACT.md`

핵심 내용:

```text
첫 유효행:
[ACTION=CONTINUE|PAUSE|END]

CONTINUE인 경우 다음 제어행:
[NEXT : IMPLEMENTER]
[NEXT : HIGH_LEVEL]
[NEXT : JUDGE]

다음 역할에게 보낼 실제 내용은 제어행 뒤에 작성한다.
Worker는 본문의 의미를 판단하지 않는다.
최종 작업 완료 여부는 설계·관제 AI가 판단한다.
```

### `WORKER-ROLE-ROUTING-CONTRACT.md`

작업 AI와 고수준 작업 AI 공용:

```text
첫 유효행:
[NEXT : COORDINATOR]
또는
[NEXT : JUDGE]

COORDINATOR:
[REPORT] 양식 사용

JUDGE:
[VALIDATION REQUEST] 양식 사용

Worker는 이 태그에 따라 전달할 뿐 결과의 의미를 판정하지 않는다.
```

### `JUDGE-RETURN-CONTRACT.md`

Judge가 자유형 AI라면:

```text
[JUDGMENT]
요청받은 항목의 판정과 근거를 작성한다.
결과는 Worker가 해석하지 않고 요청자에게 전달한다.
```

JEV API 자체 형식이 고정이면 Footer를 억지로 주입하지 말고 adapter가 API 계약에 맞춰 전달한다.

## 8. 예제 전체 왕복

### 예제 1 — 일반 작업

관제:

```text
[ACTION=CONTINUE]
[NEXT : IMPLEMENTER]

[INSTRUCTION]

hello.txt를 만들고 내용이 정확한지 확인해라.
검증 방법은 네가 적절히 선택하고 결과를 보고하라.
```

Worker → 작업 AI.

작업 AI:

```text
[NEXT : COORDINATOR]

[REPORT]

hello.txt를 생성했다.
내용을 다시 읽어 PROJECTHUB_OK와 일치함을 확인했다.
실행한 명령과 결과:
- ...
남은 사항: 없음
```

Worker는 REPORT를 분석하지 않고 같은 관제 session에 전달.

관제:

```text
[ACTION=END]

[REPORT]

요구사항과 작업 보고를 검토했고 종료한다.
```

Worker 종료.

**여기에는 `HasRequiredValidationEvidence`, `CommandMatches`, `accepted` gate가 없다.**

### 예제 2 — 작업 AI가 Judge를 요청

관제:

```text
[ACTION=CONTINUE]
[NEXT : IMPLEMENTER]

[INSTRUCTION]

기능을 구현하고, 의미적 판단이 필요하면 판정 AI를 사용해라.
```

작업 AI:

```text
[NEXT : JUDGE]

[VALIDATION REQUEST]

- NOUL | [HIGH] 현재 변경이 요구 범위 밖의 기능을 추가하지 않았는가?
  EVIDENCE: 변경 요약 및 관련 diff
  PASS: YES >= 0.80
```

Worker → Judge.

Judge 결과:

```text
[JUDGMENT]

Q1: noul=0.92
근거: ...
```

Worker는 PASS를 계산하지 않고 **Judge를 요청했던 같은 작업 AI session**에 그대로 전달.

작업 AI 후속:

```text
[NEXT : COORDINATOR]

[REPORT]

판정 AI 결과를 확인했다.
구현 및 판정 결과:
- ...
```

Worker → 관제.

관제가 최종 판단.

### 예제 3 — 관제가 고수준 작업 AI 선택

관제:

```text
[ACTION=CONTINUE]
[NEXT : HIGH_LEVEL]

[INSTRUCTION]

일반 작업 결과의 특정 동시성 문제를 분석하고 해결하라.
...
```

Worker → 고수준 작업 AI의 독립 session.

고수준 작업 AI:

```text
[NEXT : COORDINATOR]

[REPORT]

원인:
- ...

수정:
- ...

검증:
- ...
```

관제가 다음 ACTION을 결정한다.

### 예제 4 — 관제가 직접 Judge 선택

관제:

```text
[ACTION=CONTINUE]
[NEXT : JUDGE]

[VALIDATION REQUEST]

현재 작업 보고가 원래 사용자 목표를 충족한다고 볼 수 있는가?
...
```

Judge 결과는 같은 관제 session으로 반환한다.

관제 후속:

```text
[ACTION=END]

[REPORT]

판정 결과까지 검토했고 종료한다.
```

## 9. 회귀 테스트 — 내용 정답이 아니라 라우팅만 검증

새 테스트의 초점은 **AI가 옳은 판단을 했는지**가 아니다. Worker가 계약대로 전달했는지다.

| ID | 테스트 | 기대 결과 |
| --- | --- | --- |
| ROUTE-C01 | Coordinator `CONTINUE + NEXT IMPLEMENTER` | Implementer 정확히 1회 호출 |
| ROUTE-C02 | Coordinator `CONTINUE + NEXT HIGH_LEVEL` | HighLevel 정확히 1회 호출 |
| ROUTE-C03 | Coordinator `CONTINUE + NEXT JUDGE` | Judge 정확히 1회 호출 |
| ROUTE-C04 | Coordinator `PAUSE` | AI 추가 호출 0, 사용자 대기 |
| ROUTE-C05 | Coordinator `END` | AI 추가 호출 0, 완료 |
| ROUTE-W01 | Worker-role `NEXT COORDINATOR + REPORT` | 동일 coordinator session으로 body 전달 |
| ROUTE-W02 | Worker-role `NEXT JUDGE + VALIDATION REQUEST` | Judge에 body 전달 |
| ROUTE-J01 | Implementer가 Judge 요청 | Judge 결과를 **같은 implementer session**에 반환 |
| ROUTE-J02 | Coordinator가 Judge 요청 | Judge 결과를 **같은 coordinator session**에 반환 |
| ROUTE-ERR01 | 첫 제어행 누락 | 계약 오류로 중지/요청자에게 protocol error |
| ROUTE-ERR02 | 비활성 HIGH_LEVEL 선택 | `ROUTE_UNAVAILABLE`을 coordinator에 전달; 자동 대체 0 |
| ROUTE-ERR03 | 비활성 JUDGE 선택 | `ROUTE_UNAVAILABLE`을 requester에 전달; 자동 대체 0 |
| ROUTE-BODY01 | REPORT에 거짓 PASS 문자열 포함 | Worker는 의미 판정하지 않고 그대로 전달 |
| ROUTE-BODY02 | 서로 다른 shell quoting이 포함됨 | Worker 라우팅 결과에 영향 0 |
| ROUTE-END01 | Coordinator가 END 반환 | Worker 별도 AC/evidence gate 없이 종료 |
| LEGACY-01 | 기존 Web `NEXT WEB/JEV` | 기존 모드 회귀 없음 |

특히 **ROUTE-BODY01/02와 ROUTE-END01**은 이번 설계 정정의 핵심 회귀 테스트다.

## 10. 구현 순서

이번 변경은 의미 gate를 더 보강하는 작업이 아니다. **기존 Web 방식으로 단순화하는 작업**이다.

1. **현재 hello.txt transcript를 회귀 근거로 보존**
   - 실제 명령 exit 0인데 Worker `NOT_OBSERVED`로 반복된 사례.
   - 목표는 matcher 개선이 아니라 이 matcher가 routing/END gate에 관여하지 않게 하는 것.

2. **범용 Control parser 추가**
   - Coordinator: ACTION + optional NEXT.
   - Worker role: NEXT + REPORT/VALIDATION REQUEST.
   - BODY opaque.
   - Legacy Web parser는 그대로 둔다.

3. **Coordinator-first 루프 교체**
   - WorkCard JSON 강제 대신 coordinator의 ACTION/NEXT 본문 전달.
   - 필요 시 관제가 BODY에서 JSON/Markdown을 자유롭게 사용할 수 있으나 Worker가 내용 schema를 요구하지 않는다.

4. **Implementer의 두 번째 routing 호출 제거**
   - 현재 `IMPLEMENT → IMPLEMENT_ROUTE` 두 호출을 하나로 통합.
   - 구현 호출 prompt에 Worker-role Footer를 처음부터 붙인다.
   - 작업 AI 최종 답 첫 줄이 직접 NEXT여야 한다.

5. **Worker semantic gate 제거**
   - `HasRequiredValidationEvidence`, `CommandMatches`, `accepted`, `judgeOk`를 flow decision에서 제거.
   - command execution 기록은 transcript/관제 전달용으로만 남긴다.

6. **Judge return-to-requester 구현**
   - requester role/session ID를 push/pop 가능한 작은 route context로 저장.
   - Judge 결과를 같은 requester session에 전달.
   - 그 AI가 다시 ACTION 또는 NEXT를 낸다.

7. **HighLevel route 연결**
   - 현재 preflight의 `HighLevelEnabled이면 차단` 제거.
   - coordinator NEXT에 따라 `EffectiveHighLevel` 별도 session 실행.
   - 작업 AI와 같은 Worker-role Footer 사용.

8. **JEV evaluator 의미 gate 분리**
   - transport/schema 오류와 의미 결과를 분리.
   - 새 router path에서는 raw typed result를 requester에게 전달.
   - 기존 threshold/digest evaluator가 필요하면 legacy 모듈로 격리.

9. **고정 3회 자동루프 제거**
   - 필요하면 사용자 설정의 예산/시간/호출 횟수 같은 인프라 limit만 둔다.
   - limit 도달은 `PAUSE/SYSTEM_LIMIT`; 구현 FAIL로 판단하지 않는다.

10. **라우팅 회귀 테스트 후 실제 Explorer E2E**
    - Judge OFF: Sol → Luna → Sol → END.
    - Judge ON: Sol → Luna → Judge → 동일 Luna → Sol → END.
    - HighLevel ON: Sol → HighLevel → Sol → END.
    - Coordinator direct Judge: Sol → Judge → 동일 Sol → END.
    - 각 경로에서 Worker가 report/test 내용을 자체 판정하지 않는 로그를 확인.

## 11. 유지해야 하는 인프라 안전 경계

“Worker가 내용을 판단하지 않는다”와 “아무 보안도 없다”는 다르다. 아래는 **내용 판단이 아니라 실행 인프라 경계**이므로 유지한다.

- 선택된 working directory가 존재하는지 확인
- role/provider/model/session 실행 가능 여부
- CLI 인증 상태
- 사용자 취소
- process timeout / provider transport error
- 외부 Git commit/push/배포의 기존 사용자 승인
- 허용되지 않은 path/file attachment 차단
- credential 마스킹/host 보안
- 동시에 같은 workspace를 두 writer가 수정하지 않도록 하는 기계적 write lock을 유지할지 여부는 별도 인프라 정책으로 결정
- transcript/usage 기록

단, 이러한 인프라 오류도 **작업 내용의 실패**와 섞지 않는다. 예: `ROUTE_UNAVAILABLE`, `PROVIDER_TIMEOUT`, `AUTH_REQUIRED`처럼 별도 상태로 반환한다.

## 12. 이전 피드백 중 폐기되는 지시

이 절 이후 다음 종류의 과거 제안은 CLI-to-CLI 신규 경로의 구현 기준으로 사용하지 않는다.

- “Worker가 실제 validation command와 계획 command를 대조해 PASS해야 한다.”
- “Worker가 모든 AC freshness를 확인해야 END 가능하다.”
- “Worker가 JEV confidence/threshold를 계산해 PASS/PARTIAL을 만든다.”
- “Worker가 direct evidence 부족을 판단해 JEV 결과를 강등한다.”
- “Worker가 evidence digest가 바뀌면 해당 질문의 PASS를 무효화한다.”
- “Worker가 세 번 재작업 후 FINISH_LIMIT으로 자동 중단한다.”
- “작업 AI structured result를 받은 뒤 별도 routing-only CLI 턴을 실행한다.”
- “Judge FAIL/PARTIAL에 따라 Worker가 자동으로 구현 AI를 재호출한다.”

과거 문서·테스트 이력은 역사 기록으로 보존하지만, **새 범용 CLI-to-CLI의 정답은 ‘Footer 계약 주입 + 첫 제어행 라우팅 + 본문 opaque 전달’이다.**

## 13. 설계 완료 기준

다음이 만족되면 이 구조의 설계 의도가 구현된 것으로 본다.

- 설계·관제 AI의 모든 정상 응답 첫 유효행이 `[ACTION=...]`.
- CONTINUE이면 관제 AI가 다음 AI 역할을 직접 선택.
- 작업 AI와 고수준 작업 AI의 모든 정상 응답 첫 유효행이 `[NEXT : ...]`.
- COORDINATOR 목적지는 REPORT, JUDGE 목적지는 VALIDATION REQUEST 형식으로 AI가 작성.
- Judge 결과는 Worker 판단 없이 요청자 session으로 반환.
- 최종 END는 설계·관제 AI만 결정.
- Worker가 test/AC/evidence/보고서 의미를 분석해 END를 거부하는 코드가 CLI-to-CLI control path에 없음.
- 기존 Web ↔ Codex ↔ JEV 계약은 legacy 모드에서 그대로 동작.
- 모델명을 바꿔도 동일 계약이 작동하며 역할 이름으로만 routing.

---

# 2026-09-24 GPT Web 최종 라우팅 계약 — ACTION + GOTO 상태 머신

> **최우선 / supersede:** 이 절은 바로 앞의 “Worker는 계약 라우터만 수행” 원칙을 유지하되, 그 절에서 사용한 NEXT, IMPLEMENTER, HIGH_LEVEL, COORDINATOR, ACTION=HQ 표기를 **새 CLI-to-CLI 경로에서 폐기하고 최종 상태 계약으로 대체한다.**
>
> 새 계약의 핵심은 단순하다. **ACTION은 HQ만 사용한다. GOTO는 다음 상태만 나타낸다. Worker는 현재 상태에서 허용된 GOTO인지 기계적으로 확인하고 전달할 뿐 본문 의미를 판단하지 않는다.**
>
> 기존 GPT Web ↔ Codex의 [NEXT : WEB|JEV]는 레거시 호환 wire이므로 이번 변경에서 즉시 파괴하지 않는다. **새 네 역할 CLI-to-CLI 경로에서는 NEXT를 사용하지 않고 GOTO만 사용한다.** 레거시 Web까지 GOTO로 이관하려면 Extension/Bridge 호환을 포함한 별도 작업으로 한다.

## 1. 최종 역할/상태 이름

새 CLI-to-CLI 런타임의 역할/상태 이름은 아래 다섯 개만 사용한다.

| 상태 | 기존 대응 | 의미 |
| --- | --- | --- |
| **HQ** | 설계·관제 AI / Coordinator | 유일한 전체 의사결정자. 사용자 목표 해석, 다음 작업 선택, PAUSE/END 결정 |
| **WORK** | 작업 AI / Implementer | 일반 구현·수정·테스트·보고 수행 |
| **HIGH** | 고수준 작업 AI / HighLevel | 사용자가 명시적으로 허가한 경우에만 1회 호출되는 고수준 작업자 |
| **JUDGE** | 작업 판단 AI / JEV | WORK가 요청한 판단만 수행하고 반드시 WORK로 복귀 |
| **UNKNOWN** | 기존 ROUTE_UNAVAILABLE/transport/protocol error 등 | 정상 AI 역할이 아니라 오류 envelope 상태. 오류 내용을 가지고 즉시 HQ로 복귀 |

신규 코드/로그/UI 내부 상태에서는 가능하면 Coordinator/Implementer/HighLevel보다 HQ/WORK/HIGH/JUDGE를 기준 명칭으로 사용한다. UI 표시 문구는 기존 “설계 관제 / 작업 / 고수준 작업 / 판정”을 유지할 수 있다.

## 2. ACTION 최종 규약 — 오직 HQ 전용

허용 ACTION은 아래 세 개뿐이다.

~~~text
[ACTION=CONTINUE]
[ACTION=PAUSE]
[ACTION=END]
~~~

### 권한

- **HQ만 ACTION을 출력할 수 있다.**
- WORK, HIGH, JUDGE, UNKNOWN은 ACTION을 출력하지 않는다.
- 기존 [ACTION=HQ]는 새 CLI-to-CLI 경로에서 **완전히 제거한다.**
- Worker가 ACTION을 생성하거나 의미적으로 수정하지 않는다.

### HQ 응답 형식

#### 계속 진행

~~~text
[ACTION=CONTINUE]
[GOTO : WORK]

[INSTRUCTION]
...
~~~

또는 HIGH가 이번 Job에서 실제 사용 가능한 경우에만:

~~~text
[ACTION=CONTINUE]
[GOTO : HIGH]

[INSTRUCTION]
...
~~~

HQ의 CONTINUE에서 허용되는 GOTO는 **WORK 또는 HIGH뿐**이다.

#### 사용자 대기

~~~text
[ACTION=PAUSE]

[REPORT]
...
~~~

- GOTO 없음.
- Worker는 추가 AI 호출 없이 사용자 대기 상태로 이동한다.

#### 종료

~~~text
[ACTION=END]

[REPORT]
...
~~~

- GOTO 없음.
- Worker는 별도 AC/evidence/test 의미 gate 없이 종료한다.
- 완료 판단 책임은 HQ에 있다.

## 3. GOTO 최종 규약

새 CLI-to-CLI에서는 NEXT라는 단어를 사용하지 않는다.

허용 토큰:

~~~text
[GOTO : HQ]
[GOTO : WORK]
[GOTO : HIGH]
[GOTO : JUDGE]
[GOTO : UNKNOWN]
~~~

GOTO는 **오직 다음 상태/행선지**를 뜻한다. 성공/실패/PASS 여부를 뜻하지 않는다.

### 상태 전이표

| 현재 상태 | 정상 허용 GOTO | 비고 |
| --- | --- | --- |
| **HQ** | WORK, HIGH | HQ는 CONTINUE와 함께 선택. JUDGE 직접 호출 금지 |
| **WORK** | JUDGE, HQ | 판정 필요 시 JUDGE, 아니면 보고 후 HQ |
| **JUDGE** | **WORK만** | 판정 후 반드시 요청한 동일 WORK 세션으로 복귀 |
| **HIGH** | **HQ만** | JUDGE를 거치지 않음. 결과는 무조건 HQ |
| **UNKNOWN** | **HQ만** | 오류 정보 그대로 HQ에 전달 |

정상 상태 전이:

~~~text
USER
 ↓
HQ
 ├─ GOTO:WORK ──> WORK ── GOTO:JUDGE ──> JUDGE ── GOTO:WORK ──> 같은 WORK
 │                  └────────────────────────────── GOTO:HQ ───────────────┐
 └─ GOTO:HIGH ──> HIGH ─────────────────────────── GOTO:HQ ────────────────┤
                                                                            ↓
                                                                            HQ
~~~

오류:

~~~text
어느 상태에서든 protocol / provider / transport / session / unavailable 오류
    ↓
[GOTO : UNKNOWN]
[ERROR]
...
    ↓
UNKNOWN
    ↓
HQ
~~~

## 4. HIGH 사용 규칙 — 설정 ON + 사용자 명시 허가 + Job당 1회

HIGH는 일반 작업의 자동 상향 모델이 아니다.

**세 조건이 모두 참일 때만 선택 가능하다.**

~~~text
HIGH_AVAILABLE =
    high_enabled
    AND high_user_authorized
    AND NOT high_consumed
~~~

### 4.1 high_enabled

- 설정창의 “고수준 작업 AI 사용” 옵션.
- OFF이면 HQ에게 [GOTO : HIGH] 선택지를 **아예 주입하지 않는다.**
- OFF 상태에서 HIGH를 정상 route로 보여준 뒤 ROUTE_UNAVAILABLE로 되돌리는 현재 방식은 제거한다.

### 4.2 high_user_authorized

사용자가 **이번 Job에서 HIGH 사용을 명시적으로 허가했다는 기계적 상태**다.

중요: Worker가 자유형 사용자 문장을 의미 분석해 “고수준이 필요하다는 뜻”이라고 추론하면 안 된다.

권장 구현은 다음 중 하나의 **명시적 UI/제어 입력**으로 Job 시작 시 one-shot permit을 만든다.

- 작업 실행 전에 “이번 작업에서 고수준 AI 1회 사용 허용” 체크/토글
- 또는 사용자가 직접 누르는 “고수준 1회 허용” control
- 향후 명시적 wire flag가 생기면 동일 boolean에 매핑

자유형 본문에 “어려우면 알아서 고수준 써”와 같은 문장이 있어도 Worker가 직접 NLP로 판정하지 않는다. 필요하면 HQ가 사용자에게 PAUSE로 명시 허가를 요청하게 한다.

**목표:** HIGH 호출 비용/권한은 사용자 명시 행위로 생성된 one-shot permit이 있어야 한다.

### 4.3 high_consumed

- 실제 HIGH 호출을 **시작하는 순간** true로 바꾼다.
- 한 Job에서 HIGH 정상 호출은 최대 1회다.
- HIGH의 provider/transport 실행이 실패해도 이미 한 번 호출을 시도했으므로 permit은 소모된 것으로 본다. 다시 HIGH가 필요하면 사용자에게 새 명시 허가를 받아야 한다.
- HIGH 완료 후 HQ로 돌아왔을 때 HIGH 선택지는 제거한다.

### 4.4 HIGH 내부 계약

HIGH는 JUDGE를 절대 호출하지 않는다.

정상 HIGH 응답:

~~~text
[GOTO : HQ]

[REPORT]

수행 내용:
- ...

변경 사항:
- ...

검증/분석:
- ...

남은 사항:
- ...
~~~

허용 전이:

~~~text
HIGH -> JUDGE    X
HIGH -> WORK     X
HIGH -> HIGH     X
HIGH -> HQ       O
~~~

HIGH 응답이 잘못된 GOTO를 내면 Worker는 의미를 수정하지 않고 UNKNOWN envelope로 HQ에 보고한다.

## 5. WORK 계약

WORK는 일반 구현 담당이며 ACTION 권한이 없다.

정상 선택은 둘뿐이다.

### 5.1 HQ에 보고

~~~text
[GOTO : HQ]

[REPORT]

수행 내용:
- ...

변경 사항:
- ...

검증 결과:
- ...

남은 사항:
- ...
~~~

Worker는 REPORT의 테스트 결과나 완료 주장을 검증하지 않고 같은 HQ session에 전달한다.

### 5.2 JUDGE 요청

~~~text
[GOTO : JUDGE]

[VALIDATION REQUEST]

<판정할 내용>
~~~

- JUDGE 옵션이 활성화되어 있을 때만 실제 호출 가능.
- JUDGE가 비활성인데 WORK가 JUDGE를 요청한 경우 정상 대체 route를 Worker가 선택하지 않는다.
- 대신 UNKNOWN 오류 envelope를 HQ에 전달한다.
- 즉 비활성 JUDGE 상태에서 Worker가 임의로 WORK → HQ를 성공 경로로 바꾸지 않는다.

WORK → HIGH는 금지한다. 고수준 작업을 사용할 권한과 선택은 HQ에만 있다.

## 6. JUDGE 계약 — 반드시 WORK로 복귀

JUDGE는 전체 작업을 종료하거나 HQ로 직접 보고하지 않는다.

~~~text
WORK
  ↓ GOTO:JUDGE
JUDGE
  ↓ GOTO:WORK
같은 WORK session
~~~

자유형 판단 AI라면:

~~~text
[GOTO : WORK]

[JUDGMENT]

...
~~~

JEV처럼 API 응답 자체에 GOTO 태그가 없는 provider는 Worker adapter가 **라우팅 wrapper만 기계적으로 추가**할 수 있다.

~~~text
[GOTO : WORK]

[JUDGMENT]
<provider raw response>
~~~

이 wrapper는 의미 판정이 아니다. 현재 상태 JUDGE의 유일한 정상 목적지가 WORK라는 고정 계약을 표현할 뿐이다.

### Judge 오류

JUDGE provider timeout/HTTP/schema/인증 오류가 발생하면:

~~~text
[GOTO : UNKNOWN]

[ERROR]
source: JUDGE
code: ...
detail: ...
~~~

→ UNKNOWN → HQ.

오류를 WORK에 성공 응답처럼 돌려주지 않는다.

## 7. UNKNOWN 계약

UNKNOWN은 AI 모델 역할이 아니다.

다음 상황을 **해석하지 않고 포장해 HQ로 반환하기 위한 시스템 상태**다.

- 허용되지 않은 GOTO
- ACTION을 쓸 수 없는 역할이 ACTION 출력
- HQ CONTINUE인데 GOTO 누락
- HIGH permission 없음
- JUDGE 비활성/연결 불가
- provider timeout
- process nonzero exit
- session resume 실패
- transport/schema/protocol 오류

표준 envelope 제안:

~~~text
[GOTO : UNKNOWN]

[ERROR]
source_state: WORK
code: GOTO_NOT_ALLOWED
requested_goto: HIGH
detail: <원본 기술 오류 또는 응답>
~~~

Worker는 UNKNOWN의 오류 원인을 보고 어떤 모델로 재시도할지 결정하지 않는다.

기계적 전이:

~~~text
UNKNOWN -> HQ
~~~

HQ에는 예를 들어 다음 inbound envelope만 전달한다.

~~~json
{
  "message_type": "UNKNOWN",
  "body": "<ERROR 원문>"
}
~~~

HQ가 이후 CONTINUE/PAUSE/END 및 WORK/HIGH를 새로 선택한다.

## 8. Worker가 파싱할 최소 문법

### HQ parser

~~~text
첫 유효행 ACTION 필수
CONTINUE -> 두 번째 제어행 GOTO 필수
            허용 = WORK 또는 HIGH(available일 때만)
PAUSE    -> GOTO 금지
END      -> GOTO 금지
~~~

### WORK parser

~~~text
첫 유효행 GOTO 필수
허용 = HQ 또는 JUDGE
ACTION 존재 -> protocol error -> UNKNOWN
~~~

### HIGH parser

~~~text
첫 유효행 [GOTO : HQ]만 허용
ACTION / JUDGE / WORK / HIGH -> UNKNOWN
~~~

### JUDGE parser

~~~text
정상 목적지 = WORK 고정
provider native 응답이면 adapter가 GOTO:WORK wrapper
ACTION 사용 불가
~~~

### UNKNOWN

~~~text
다음 상태 = HQ 고정
~~~

**본문은 opaque다.** REPORT, INSTRUCTION, VALIDATION REQUEST, JUDGMENT, ERROR marker는 가독성과 상대 AI prompt 구성을 위한 양식이다. Worker가 그 안의 사실관계를 평가하지 않는다.

## 9. 현재 코드에서 반드시 바꿀 지점

2026-09-24 최신 main 기준 실제 런타임에는 아직 아래 구계약이 있다.

### 9.1 ACTION=HQ 제거

현재:

~~~text
[ACTION=CONTINUE]
[ACTION=PAUSE]
[ACTION=END]
[ACTION=HQ]
~~~

변경:

~~~text
[ACTION=CONTINUE]
[ACTION=PAUSE]
[ACTION=END]
~~~

- WorkerAction.Hq 제거.
- HQ 자기호출 shortcut 제거.
- HQ가 자기 자신으로 GOTO하는 정상 전이도 제거.

### 9.2 NEXT → GOTO 및 이름 변경

현재:

~~~text
[NEXT : IMPLEMENTER]
[NEXT : HIGH_LEVEL]
[NEXT : JUDGE]
[NEXT : COORDINATOR]
~~~

신규 CLI:

~~~text
[GOTO : WORK]
[GOTO : HIGH]
[GOTO : JUDGE]
[GOTO : HQ]
[GOTO : UNKNOWN]
~~~

기존 WorkerNextRole은 WorkerGotoState 또는 동등한 enum으로 정리한다.

### 9.3 HQ allowed routes를 동적으로 주입

현재 관제 prompt는 IMPLEMENTER/HIGH_LEVEL/JUDGE/COORDINATOR를 항상 노출한다. 이를 제거한다.

일반 Job:

~~~text
Allowed GOTO for HQ:
- WORK
~~~

HIGH one-shot이 사용 가능한 Job:

~~~text
Allowed GOTO for HQ:
- WORK
- HIGH

HIGH is user-authorized for one call only.
~~~

HQ에게 JUDGE를 정상 선택지로 노출하지 않는다.

### 9.4 HIGH_LEVEL → HIGH

- 신규 CLI control wire와 내부 route enum에서 HIGH_LEVEL을 HIGH로 단순화.
- UI 한글 “고수준 작업”은 유지 가능.
- 저장 설정의 기존 property 이름은 migration 호환이 필요하면 읽되 내부 상태로 변환.
- 기존 transcript는 역사 기록이므로 rewrite하지 않는다.

### 9.5 WORK와 HIGH Footer 분리

현재 WORK/HIGH가 같은 footer를 사용해 둘 다 JUDGE를 선택할 수 있다. 이를 분리한다.

WORK:

~~~text
[GOTO : HQ]
또는
[GOTO : JUDGE]
~~~

HIGH:

~~~text
[GOTO : HQ]
~~~

HIGH에는 JUDGE 문구 자체를 주입하지 않는다.

### 9.6 JUDGE requester 고정

신규 규약에서는 Judge requester는 항상 WORK다.

- HQ → JUDGE 제거.
- HIGH → JUDGE 제거.
- Judge request context에는 WORK session만 존재.
- 결과는 같은 WORK session으로 복귀.
- WORK가 결과를 읽고 다시 JUDGE 또는 HQ를 선택.

## 10. 새 Footer 예시

### HQ-ROUTING-CONTRACT

~~~text
You are HQ, the only role allowed to emit ACTION.

First nonempty line:
[ACTION=CONTINUE]
[ACTION=PAUSE]
[ACTION=END]

If ACTION=CONTINUE, use exactly one GOTO from the Allowed GOTO list supplied by the Worker.

Normal route:
[GOTO : WORK]

Only when HIGH is explicitly available for this Job:
[GOTO : HIGH]

Do not emit GOTO:JUDGE, GOTO:HQ, or GOTO:UNKNOWN as a normal HQ decision.
PAUSE and END do not have a GOTO.
The Worker routes control tags only and treats the body as opaque.
~~~

### WORK-ROUTING-CONTRACT

~~~text
You are WORK. Do not emit ACTION.

First nonempty control line:
[GOTO : HQ]
or
[GOTO : JUDGE]

Use GOTO:HQ with [REPORT].
Use GOTO:JUDGE with [VALIDATION REQUEST].
Do not route to HIGH. Only HQ can choose HIGH.
~~~

### HIGH-ROUTING-CONTRACT

~~~text
You are HIGH. This is a one-shot high-level task delegated by HQ.

Do not emit ACTION.
Do not request JUDGE.

Your first nonempty control line must be:
[GOTO : HQ]

Follow it with [REPORT].
~~~

### JUDGE return wrapper

~~~text
[GOTO : WORK]

[JUDGMENT]
<raw judge result>
~~~

## 11. 예제 전체 왕복

### 11.1 기본

~~~text
USER
  ↓
HQ
[ACTION=CONTINUE]
[GOTO : WORK]
[INSTRUCTION] 숫자 야구게임을 구현하라.
  ↓
WORK
[GOTO : HQ]
[REPORT] 구현 및 테스트 결과...
  ↓
HQ
[ACTION=END]
[REPORT] 완료 판단...
~~~

### 11.2 Judge

~~~text
HQ
[ACTION=CONTINUE]
[GOTO : WORK]
...
  ↓
WORK
[GOTO : JUDGE]
[VALIDATION REQUEST]
...
  ↓
JUDGE
[GOTO : WORK]
[JUDGMENT]
...
  ↓ same WORK session
WORK
[GOTO : HQ]
[REPORT]
...
  ↓
HQ
[ACTION=CONTINUE|PAUSE|END]
~~~

### 11.3 HIGH one-shot

사전 상태:

~~~text
high_enabled = true
high_user_authorized = true
high_consumed = false
~~~

HQ:

~~~text
[ACTION=CONTINUE]
[GOTO : HIGH]

[INSTRUCTION]
...
~~~

Worker는 HIGH 호출 시작 직전에 high_consumed=true로 바꾸고 이후 같은 Job의 HQ allowed route에서 HIGH를 제거한다.

HIGH:

~~~text
[GOTO : HQ]

[REPORT]
...
~~~

### 11.4 UNKNOWN

WORK가 잘못 HIGH를 선택한 경우:

~~~text
WORK:
[GOTO : HIGH]
~~~

Worker:

~~~text
[GOTO : UNKNOWN]

[ERROR]
source_state: WORK
code: GOTO_NOT_ALLOWED
requested_goto: HIGH
detail: <원본 응답>
~~~

→ HQ에 전달. HQ가 새 ACTION을 결정한다.

## 12. 회귀 테스트

| ID | 입력/상태 | 기대 |
| --- | --- | --- |
| GOTO-HQ-01 | HQ CONTINUE + GOTO WORK | WORK 정확히 1회 호출 |
| GOTO-HQ-02 | HQ CONTINUE + GOTO HIGH, HIGH available | HIGH 1회 호출, permit consumed |
| GOTO-HQ-03 | HIGH option OFF | HQ allowed list에 HIGH 없음 |
| GOTO-HQ-04 | user permit 없음 | HQ allowed list에 HIGH 없음; 출력 시 UNKNOWN→HQ |
| GOTO-HQ-05 | HIGH 이미 consumed | HIGH 재호출 0, 출력 시 UNKNOWN→HQ |
| GOTO-HQ-06 | HQ CONTINUE + GOTO JUDGE | UNKNOWN→HQ, Judge 호출 0 |
| GOTO-HQ-07 | HQ PAUSE | 추가 AI 호출 0 |
| GOTO-HQ-08 | HQ END | 추가 AI 호출 0, Worker semantic gate 없음 |
| GOTO-ACT-01 | WORK가 ACTION 출력 | UNKNOWN→HQ |
| GOTO-ACT-02 | HIGH가 ACTION 출력 | UNKNOWN→HQ |
| GOTO-ACT-03 | JUDGE가 ACTION 출력 | UNKNOWN→HQ |
| GOTO-W-01 | WORK → HQ | 같은 HQ session에 REPORT 전달 |
| GOTO-W-02 | WORK → JUDGE | Judge 뒤 같은 WORK session |
| GOTO-W-03 | WORK → HIGH | UNKNOWN→HQ |
| GOTO-J-01 | JUDGE 정상 응답 | WORK로 복귀 |
| GOTO-J-02 | Judge timeout | UNKNOWN→HQ |
| GOTO-H-01 | HIGH → HQ | 같은 HQ session으로 복귀 |
| GOTO-H-02 | HIGH → JUDGE | UNKNOWN→HQ |
| GOTO-H-03 | HIGH → WORK | UNKNOWN→HQ |
| GOTO-U-01 | provider/session/protocol error | UNKNOWN envelope 보존 후 HQ |
| GOTO-BODY-01 | REPORT에 잘못된 테스트 주장 | Worker는 해석하지 않고 전달 |
| GOTO-LEGACY-01 | 기존 Web NEXT:WEB/JEV | legacy 모드 회귀 없음 |

## 13. Explorer E2E 최소 세트

### A. 기본

~~~text
HQ → WORK → HQ → END
~~~

### B. Judge

~~~text
HQ → WORK → JUDGE → WORK → HQ → END
~~~

### C. HIGH one-shot

사용자 one-shot 허가 후:

~~~text
HQ → HIGH → HQ → WORK 또는 END
~~~

확인:
- HIGH는 한 번만 활성.
- HIGH 뒤 JUDGE 상태가 나타나지 않음.
- HIGH 사용 후 HQ의 route 후보에서 HIGH 제거.

### D. UNKNOWN

~~~text
WORK → HIGH (invalid)
→ UNKNOWN
→ HQ
~~~

확인:
- Worker가 다른 정상 역할로 자동 대체하지 않음.
- 원본 오류를 HQ에 전달.
- HQ가 다음 ACTION을 선택.

## 14. 구현 체크리스트

- [ ] ACTION=HQ 삭제
- [ ] 신규 CLI-to-CLI의 NEXT를 GOTO로 변경
- [ ] IMPLEMENTER → WORK
- [ ] HIGH_LEVEL → HIGH
- [ ] COORDINATOR → HQ
- [ ] UNKNOWN 오류 상태 추가
- [ ] HQ는 WORK/HIGH만 선택 가능
- [ ] WORK는 HQ/JUDGE만 선택 가능
- [ ] JUDGE는 WORK로만 복귀
- [ ] HIGH는 HQ로만 복귀하고 JUDGE 미사용
- [ ] ACTION parser는 HQ 응답에서만 활성
- [ ] high_enabled + high_user_authorized + high_consumed 상태 구현
- [ ] HIGH OFF/미허가/소모 상태면 HQ allowed list에서 HIGH 제거
- [ ] HIGH dispatch 시작 시 permit 즉시 consume
- [ ] 잘못된 GOTO/기술 오류를 UNKNOWN→HQ로 통일
- [ ] WORK/HIGH Footer 분리
- [ ] HQ/JUDGE 직접 경로 제거
- [ ] JUDGE 결과를 같은 WORK session으로 복귀
- [ ] Worker semantic gate를 신규 경로에 다시 도입하지 않음
- [ ] 기존 Web NEXT 계약은 legacy 모드 회귀 없음

## 15. 최종 불변식

~~~text
HQ      -> WORK | HIGH
WORK    -> JUDGE | HQ
JUDGE   -> WORK
HIGH    -> HQ
UNKNOWN -> HQ
~~~

그리고:

~~~text
ACTION = HQ only
HIGH = option ON + explicit user one-shot authorization + not consumed
Worker = control contract router, not semantic evaluator
~~~

이 규약이 신규 CLI-to-CLI의 최종 기준이다. 이전 피드백/현재 코드에서 이 규약과 충돌하는 ACTION=HQ, NEXT, HQ→JUDGE, HIGH→JUDGE, 자동 대체, Worker 자체 의미 판정은 신규 경로 기준에서 폐기한다.
