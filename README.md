# ProjectHub

ProjectHub는 개발 PC, Mini PC 중앙 서비스, 공통 도메인·인프라, AI Worker와 ChatGPT Web 브리지를 분리해 구성하는 프로젝트다.

## 구성

- `src/ProjectHub.Core`: 공통 도메인 모델과 계약
- `src/ProjectHub.Infrastructure`: Supabase·파일시스템·NAS 등 외부 인프라 구현
- `src/ProjectHub.Server`: Mini PC의 ASP.NET Core 중앙 HTTP 서비스
- `src/ProjectHub.Agent`: 개발 PC 상태 수집 및 Server 통신
- `src/ProjectHub.Worker`: AI 관제와 로컬 작업 실행용 Windows Worker
- `extension/gptweb-hub`: ChatGPT Web과 Worker 사이의 브라우저 확장 브리지
- `tests/`: 각 프로젝트의 자동 테스트

## 정책 문서

현재 의미 원본은 다음 순서로 해석한다.

1. `Master-Polish.md`: ProjectHub 전체 공통 영구 정책
2. 프로젝트별 `*-Polish.md`: Core / Infrastructure / Server / Agent / Worker / Web 장기 정책
3. 역할·API·전송 계약: 해당 프로젝트의 세부 프로토콜
4. `ProjectHub_IMPLEMENTATION_PLAN.md`: 현재 정책 기준 구현 로드맵
5. `CurrentWork.md`: 사용자가 현재 상태를 확인할 때 보는 프로젝트별 표지판

`tasks/*.md`, `Conversation-Handoff.md`, `NewThreadHandoff.md`, `GPT-Web-Feedback.md`와 E2E 기록은 구현 과정과 검증 이력을 보존하는 자료다. 이 문서 안의 `현재`, `활성`, 버전, 경로, 복구 방식과 역할 구조는 작성 시점의 사실일 수 있으므로 현재 정책이나 현재 런타임 계약을 덮어쓰지 않는다.

## Server 실행

```powershell
dotnet run --project src/ProjectHub.Server
```

상태 확인: `GET /api/status`

## Worker와 Web 런타임

Worker는 HQ가 WorkGraph와 작업 분해를 관제하고 WORK가 배정된 WorkItem checklist를 수행하는 실행 흐름을 관리한다. RESOURCE, JUDGE, OBSERVATION은 일반 WorkItem과 분리된 sidecar로 현재 요청 문맥에 귀속된다. WORK가 일반 파일을 수정하는 동안 Git metadata와 Git 원격 접근은 AI 실행 경계에서 분리하고, checkpoint commit과 CODE_CHANGE Commit Manifest 생성은 Worker가 담당한다. 세부 실행 정책은 `Worker-Polish.md`와 Worker 전용 계약 문서에 둔다.

HQ와 RESOURCE는 서로 다른 persistent profile의 관리형 Chromium을 사용한다. 각 슬롯은 일반 탭 브라우저가 아니라 ChatGPT URL 하나를 여는 app window로 실행되며, runtime token이 없는 일반 Chrome과 임의 ChatGPT 페이지는 Worker bridge에 연결하지 않는다. HQ Web 응답은 task별 correlation KEY와 `[RESPONSE=OK]` 완료 표식을 사용하고, RESOURCE 완료는 생성 파일 준비와 capture 상태를 별도로 사용한다. 세부 상관·완료 계약은 `Web-Polish.md`와 Worker의 Web 전송 계약에 둔다.

Worker의 저장소별 실행 격리와 기계 상태는 target workspace의 `.projecthub` 아래에 두며 정상 Worker 종료 뒤에는 해당 폴더를 남기지 않는 것을 원칙으로 한다. 짧은 파일 잠금 때문에 즉시 삭제할 수 없는 경우 종료 후 cleanup helper가 정리를 재시도한다.

Worker의 메시지 및 작업 이력 입력은 파일 drag-and-drop과 화면 캡처 이미지 Ctrl+V 첨부를 지원한다. Web 전달에서는 로컬 파일 hash 검증과 ChatGPT UI의 기계적 준비 상태를 구분하고, 일반 Web 결과 파일은 Worker가 안전한 결과 경로에 저장한다.
