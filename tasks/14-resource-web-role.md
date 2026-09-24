# 14 RESOURCE Web role + HQ Web restore

Updated: 2026-09-24

정책 원본은 Master-Polish.md다.

## Goal

기존 HIGH 역할을 완전히 제거하고, 별도 ChatGPT Web 대화에서 최종 생성 이미지를 만들고 지정 경로에 저장하는 RESOURCE 역할을 도입한다. 동시에 HQ는 ChatGPT Web 또는 CLI Provider를 선택할 수 있게 복원한다.

## State graph

~~~text
HQ       -> WORK
WORK     -> HQ | JUDGE | RESOURCE
JUDGE    -> WORK
RESOURCE -> WORK
UNKNOWN  -> HQ summary once
~~~

## A — HIGH removal

- WorkerRoleState.High 제거
- GOTO:HIGH 제거
- HIGH-ROUTING-CONTRACT 삭제
- JobHighLevelPermit 삭제
- highLevel settings/provider/model/reasoning/session 제거
- 고수준 작업 허용 checkbox 제거
- pipeline/history의 HighLevel 의미 제거

## B — HQ Web target

- HQ target = ChatGPT Web / CLI
- Web 선택 시 Provider/Model/Reasoning/session 숨김
- CLI 선택 시 OpenAI/Claude/Muse provider 구조 유지
- WORK는 CLI-only 유지
- coordinator transport=web을 codex_cli로 자동 normalize하지 않음
- HQ Web도 CLI HQ와 동일한 HQ contract 사용

## C — explicit Web binding

브라우저 확장에서 각 conversation을 명시적으로:
- HQ
- RESOURCE

중 하나로 binding한다.

같은 conversation을 두 역할에 동시에 binding하지 않는다. heartbeat는 liveness 확인에만 사용한다. Worker task 생성은 role binding conversationId를 사용한다.

## D — RESOURCE transport

WORK body schema:

~~~json
{
  "type": "IMAGE",
  "prompt": "...",
  "targetDirectory": "assets/tiles",
  "targetFileName": "fruit_tiles.png"
}
~~~

Worker는 JSON/schema/path safety만 기계적으로 검사한다.

ResourceRequest:
- Id
- Type
- Prompt
- TargetDirectory
- TargetFileName
- RequestedBy
- Status
- SavedPath
- WorkspaceRoot (transport 내부 저장 안전 경계)

상태:
REQUESTED -> GENERATING -> SAVED | FAILED

IMAGE:
- RESOURCE ChatGPT Web에서 생성
- 확장이 생성 이미지 bytes를 반환
- Worker가 workspace 하위 target path에 저장
- same WORK session으로 복귀

SOUND:
- schema 예약
- 실제 transport 미구현

## E — role contracts

HQ:
- 새/대규모 사용자 목표는 필요한 수준으로 구현 방향 설계
- 작은 후속은 영향 범위만 갱신
- PAUSE는 사람 확인/취향/로그인/권한/사용자 선택이 필요할 때 사용

WORK:
- 최종 이미지/아이콘/스프라이트/배경/생성 음향은 RESOURCE 우선
- placeholder는 임시 확인용만 허용
- RESOURCE 저장 결과를 사용자 후속 명령 없이 자동 연결하지 않음

RESOURCE:
- 생성/저장만
- 코드 작성/연결/통합 금지

## Verification

자동:
- Worker/Test source에서 HIGH 구조 잔존 없음
- HQ only GOTO:WORK
- WORK GOTO:HQ/JUDGE/RESOURCE
- RESOURCE transport traversal 차단
- coordinator web transport 보존
- HQ/RESOURCE 동일 conversation binding 거부

실환경:
- solution test/build
- HQ CLI roundtrip
- HQ Web roundtrip
- 두 Web conversation 동시 heartbeat
- RESOURCE image 실제 생성/저장
- 저장 뒤 WORK session 복귀
- 자동 integration 없음
- JUDGE 회귀


## F — settings/UI polish follow-up

화면 확인 피드백:
- 대기 상태 전체 컬러는 정상 정책이므로 변경하지 않는다.
- WORK 기본값은 OpenAI / GPT-6 Luna / Medium으로 둔다.
- 저장된 WORK 모델을 별도 schema migration으로 치환하지 않는다.
- 설정의 HQ/RESOURCE Web 카드는 역할별 binding/heartbeat/extension 상태를 표시한다.
- role-specific Web preflight를 사용하여 다른 Web 창의 latest heartbeat가 실행 여부에 영향을 주지 않게 한다.
- Provider icon contrast, text wrapping, scroll/fixed footer, 역할명 표기를 정리한다.
