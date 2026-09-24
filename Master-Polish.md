# Master-Polish — ProjectHub 현재 정책

Updated: 2026-09-24 (KST)

이 문서는 ProjectHub의 현재 최상위 정책 원본이다.

ProjectHub의 목표는 AI가 설계·판단하고 Worker가 흐름·세션·transport·telemetry만 기계적으로 관리하는 역할 분리형 개발 도구다.

---

## 1. 최상위 불변식

Worker는 의미 판단 주체가 아니다.

Worker가 처리할 수 있는 것:
- 현재 역할 상태 저장 및 허용 상태 전이 검사
- ACTION/GOTO 제어행 문법 파싱
- 역할별 session/transport/process 실행
- timeout/cancel/auth/schema/path-safety 오류 처리
- Web conversation binding 및 heartbeat 생존 확인
- transcript/usage/file telemetry 기록
- 전용 JUDGE/RESOURCE transport의 기계적 schema 변환
- UNKNOWN 원문 로그와 HQ용 한글 오류 요약
- 이미 알고 있는 실행 사실을 History UI에 표시

Worker가 하지 않는 것:
- 요청 난이도·의도·우선순위 판단
- 다음 역할을 본문 의미로 추론
- 요구사항/AC/test/evidence 충족 여부 판정
- JUDGE 결과 의미 해석 후 자동 PASS/FAIL 생성
- RESOURCE 이미지의 미적/기능적 품질 판정
- 생성 리소스가 어느 컴포넌트에 맞는지 판단
- 사용자의 후속 명령 없이 저장 리소스를 코드에 자동 연결

---

## 2. 역할과 상태

| 상태 | UI 역할명 | 책임 | 실행 |
| --- | --- | --- | --- |
| HQ | 설계·관제 AI | 사용자 요청 해석, 구현 방향 설계, WORK 지시, JUDGE 질문 검토, CONTINUE/PAUSE/END | ChatGPT Web 또는 CLI Provider |
| WORK | 작업 AI | 코드 구현·수정·빌드·테스트·보고, RESOURCE/JUDGE 요청 | CLI Provider |
| RESOURCE | 리소스 AI | 최종 생성 이미지 제작·지정 파일 저장 | 별도 ChatGPT Web 고정 |
| JUDGE | 작업 판단 AI | HQ 검토를 거친 WORK 질문 판정 | JEV |
| UNKNOWN | 오류 상태 | 기계적 오류 기록 및 HQ 요약 복귀 | Worker 내부 |

상태 전이:

~~~text
HQ       -> WORK
WORK     -> HQ | JUDGE | RESOURCE
JUDGE    -> WORK
RESOURCE -> WORK
UNKNOWN  -> HQ 요약 복귀 (Job당 1회)
UNKNOWN 재발 -> 로그 기록 후 종료
~~~

HIGH 역할, HIGH GOTO, HIGH one-shot permit, high_uses_remaining, 고수준 작업 허용 UI는 현재 정책에 존재하지 않는다.

---

## 3. HQ 실행 대상

HQ target은 두 종류다.

~~~text
HQ
├─ ChatGPT Web
└─ CLI
   ├─ OpenAI
   ├─ Claude
   └─ Muse
~~~

- ChatGPT Web 선택 시 Provider/Model/Reasoning/CLI session UI를 숨긴다.
- CLI 선택 시 Provider → Model → Reasoning → Session 구조를 사용한다.
- OpenAI Codex CLI는 실제 실행이 연결되어 있다.
- Claude/Muse는 기존 provider abstraction을 유지하되 실제 runner가 연결되기 전에는 미연결 오류를 반환한다.
- 과거 transport=web을 CLI_TO_CLI에서 자동으로 codex_cli로 바꾸지 않는다.
- WORK에는 ChatGPT Web target을 추가하지 않는다.

---

## 4. Web binding

HQ Web과 RESOURCE Web은 반드시 서로 다른 ChatGPT conversation을 사용한다.

Bridge는 다음 역할 binding을 명시적으로 저장한다.

~~~text
HQ       -> conversationId A
RESOURCE -> conversationId B
~~~

- 사용자가 각 ChatGPT 대화의 확장 패널에서 HQ 또는 RESOURCE 역할을 명시적으로 연결한다.
- 하나의 conversationId를 HQ와 RESOURCE에 동시에 binding하지 않는다.
- heartbeat는 대화가 살아 있는지/확장 버전이 맞는지 확인하는 용도다.
- 마지막 heartbeat conversation을 task 목적지로 사용하지 않는다.
- Worker는 역할 binding에서 얻은 conversationId로 task를 명시적으로 생성한다.

---

## 5. 출력 계약

HQ만 ACTION을 사용한다.

~~~text
[ACTION=CONTINUE]
[GOTO : WORK]
<opaque body>
~~~

또는:

~~~text
[ACTION=PAUSE]
<opaque body>
~~~

~~~text
[ACTION=END]
<opaque body>
~~~

WORK:

~~~text
[GOTO : HQ]
<opaque body>
~~~

~~~text
[GOTO : JUDGE]
<JUDGE transport body>
~~~

~~~text
[GOTO : RESOURCE]
<RESOURCE transport JSON>
~~~

JUDGE:

~~~text
[GOTO : WORK]
<opaque body>
~~~

RESOURCE는 Web transport 완료와 파일 저장 후 Worker가 기계적으로 같은 WORK session으로 복귀시킨다.

일반 body는 opaque다. JUDGE/RESOURCE destination에서 필요한 schema 검사는 transport 계층의 기계적 유효성 검사이며 작업 의미 판단이 아니다.

---

## 6. HQ 설계와 ACTION 의미

새 사용자 요청 또는 목표가 크게 바뀐 요청에서 HQ는 단순 전달자가 아니다. 필요한 만큼 구현 방향을 설계해 WORK에 전달한다.

설계에 필요할 수 있는 항목:
- 목표
- 주요 구조
- 핵심 제약
- 검증 방향
- 필요한 리소스
- 사용자만 결정할 수 있는 부분

작은 후속 수정에는 전체 설계를 반복하지 않고 영향 범위만 갱신한다.

ACTION 사용 예:
- CONTINUE: AI/Worker가 스스로 다음 의미 있는 진전을 만들 수 있음
- PAUSE: 화면 인상, 조작감, 음질, 취향, 외부 로그인/권한, 사용자 전용 선택 등 사람 개입 없이는 다음 판단이 의미 없음
- END: 요청 목표가 충족됐고 사용자 확인을 기다릴 이유도 없음

HQ가 Web이든 CLI든 같은 역할 계약을 사용한다.

---

## 7. JUDGE 흐름

~~~text
WORK -> HQ      판정 초안 + evidence 검토
HQ   -> WORK    질문 범위/evidence/응답형태/수치화 기준 검토안
WORK -> JUDGE   실제 NOUL/SCORE/CHOICE 요청
JUDGE -> WORK   raw 결과
~~~

- NOUL/SCORE/CHOICE는 quota나 의무 비율이 아니다.
- Worker는 HQ 검토가 의미적으로 충분했는지 검사하지 않는다.
- JEV raw response는 같은 WORK session으로 반환한다.

---

## 8. RESOURCE 흐름

최초 구현 범위는 IMAGE 생성 → 저장 → 기록이다.

~~~text
WORK
 -> GOTO:RESOURCE + Resource transport JSON
 -> RESOURCE Web 별도 대화
 -> 이미지 생성
 -> 확장이 생성 이미지 bytes 반환
 -> Worker가 workspace 하위 지정 경로/파일명으로 저장
 -> RESOURCE_RESULT
 -> 같은 WORK session 복귀
~~~

WORK의 RESOURCE JSON:

~~~json
{
  "type": "IMAGE",
  "prompt": "fruit tile sprite sheet ...",
  "targetDirectory": "assets/tiles",
  "targetFileName": "fruit_tiles.png"
}
~~~

기계적 ResourceRequest 기록:
- Id
- Type: IMAGE / SOUND
- Prompt
- TargetDirectory
- TargetFileName
- RequestedBy
- Status: REQUESTED / GENERATING / SAVED / FAILED
- SavedPath

현재 SOUND는 schema에 예약하지만 실제 Web 결과 transport는 구현하지 않는다.

RESOURCE가 하지 않는 것:
- 자동 코드 연결
- 자동 CSS/HTML 반영
- 생성 결과의 사용 컴포넌트 의미 판단
- 자동 빌드 반영
- 복잡한 병렬 resource queue
- 자동 품질 판정

사용자가 이후 별도 명령으로 "연결 대상인 리소스를 연결해줘"라고 요청하면 새 USER -> HQ -> WORK 흐름에서 저장된 리소스를 통합한다.

---

## 9. UI

상단 Pipeline:

~~~text
대기 / 설계·관제 / 작업 / 리소스 / 판정
~~~

표시:
- 설계·관제: ChatGPT Web 또는 선택된 CLI model
- 작업: 선택된 WORK model
- 리소스: ChatGPT Web
- 판정: JEV

대기 상태에서는 다섯 Pipeline 카드를 모두 역할 컬러로 표시하고 gold active border/orbit은 사용하지 않는다. 실행 중에는 현재 역할이 gold active border/orbit으로 강조된다. RESOURCE는 기존 네 번째 카드 위치를 사용하지만 의미는 HIGH와 완전히 다르다.

설정:
- HQ: 실행 대상 Web/CLI + CLI일 때 Provider/Model/Reasoning/Session
- WORK: Provider/Model/Reasoning/Session
- RESOURCE: ChatGPT Web 고정
- JUDGE: JEV 설정
- HQ/RESOURCE Web 카드는 각각 명시적 role binding의 연결/heartbeat/확장 동기화 상태와 연결된 대화 정보를 보여준다.
- 설정 본문은 작은 화면에서도 세로 스크롤되며 하단 닫기/적용 버튼은 항상 별도 footer에 남는다.
- 역할 표기는 설계·관제 / 작업 / 리소스 / 판정으로 통일한다.

---

## 10. 현재 활성 작업

활성 task는 tasks/14-resource-web-role.md다.

구현 코드 범위:
1. HIGH 제거 / RESOURCE state
2. HQ Web target 복원
3. HQ/RESOURCE explicit conversation binding
4. HQ 설계 책임 + PAUSE 예시
5. WORK RESOURCE 위임 계약
6. RESOURCE IMAGE 생성 결과 transport와 저장
7. Pipeline/Settings/History 교체
8. 테스트/문서 갱신

실제 Windows build/test/Explorer E2E는 실행 가능한 .NET/Explorer 환경에서 검증해야 한다.
