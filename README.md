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
4. `CurrentWork.md`: 사용자가 현재 진행 방향을 확인할 때 보는 간단한 상태 표지판

`tasks/*.md`, `Conversation-Handoff.md`, `NewThreadHandoff.md`, `GPT-Web-Feedback.md`와 E2E 기록은 구현 과정과 검증 이력을 보존하는 자료다. 이 문서 안의 `현재`, `활성`, 버전, 경로, 복구 방식과 역할 구조는 작성 시점의 사실일 수 있으므로 현재 정책이나 현재 런타임 계약을 덮어쓰지 않는다.

## Server 실행

```powershell
dotnet run --project src/ProjectHub.Server
```

상태 확인: `GET /api/status`

## Worker와 Web 런타임

**적용 범위:** `UnityWorker` 브랜치의 현재 실행 정책은 `Worker-Polish.md`, `UnityWorker-ROADMAP.md` 및 활성 역할 계약을 따른다. 범용 `main` Worker의 과거 QA·RESOURCE 설명을 이 브랜치의 활성 실행으로 취급하지 않는다.

UnityWorker에서는 HQ(Web/CLI)가 마일스톤을 설계한다. #1 ASSET MANAGER는 외부 에셋 검색·획득·연결, #0 DESIGN은 그래픽·씬 시각 제작, #10+ WORK는 코드·CLI 구현을 담당하고 HIGH(PL)가 BUILD/TEST 증거를 검토·수정한다. 독립 QA와 이미지 추출용 RESOURCE sidecar는 실행하지 않는다. HIGH는 수정과 최종 검증을 직접 수행하며, 추가 DESIGN 작업이 필요하면 미해결 결함과 증거를 HQ에 보고한다. HIGH에서 DESIGN으로 재호출하지 않는다.

WORKITEM별 원본 ID·세션·체크포인트를 보존하고 미완료 작업만 `CONTINUE`한다. 빌드 복구도 원본 소유 WORKITEM ID로 수행하며 한 작업에 귀속할 수 없으면 HQ에 판단을 요청한다. Git commit·push와 기계적 BUILD/TEST는 Worker가 맡는다. **Worker 제품 소스의 `UnityWorker` 브랜치와 사용자의 게임 프로젝트 `main`은 별개**다.

HQ Web은 관리형 Chromium·브리지, HQ CLI는 로컬 CLI 연결과 개별 상태 확인 경로를 사용한다. HQ는 설계·판단 중심의 읽기 전용 역할이다. 실시간 사용자 메시지는 특정 HQ에 따로 보관하지 않고 다음 제어권을 받는 AI 역할에 원문으로 한 번만 전달한다. Worker는 자연어를 PAUSE로 판정하지 않으며 명시적 UI 취소는 별도 제어다. 신규 사용자 작업은 새 HQ CLI 세션과 `.projecthub/last-handoff.md`를 참고하고, 동일 작업 중단 복구는 기존 세션·체크포인트를 재사용한다. 구형 QA/RESOURCE 설정·보고서·전송 클래스 잔존은 활성 호출을 의미하지 않는다.

Worker의 작업별 기계 상태는 실제 프로젝트 루트의 `.projecthub` 아래에 둔다. WORKITEM 실행 체크포인트(`work-executions`), WorkGraph·event log·transcript·continuation을 저장한다. 임시 실행·RESOURCE 결과 수집에는 프로젝트 루트의 `temp/`도 사용한다. `.projecthub/`와 `temp/`는 Worker 내부 상태·임시 작업영역이며, Worker가 관리하는 로컬 Git 제외 규칙으로 일반 코드 결과와 구분한다. WORK의 실제 소스 수정은 사용자가 지정한 프로젝트 루트에서 수행한다.

Worker의 메시지 및 작업 이력 입력은 파일 drag-and-drop과 화면 캡처 이미지 Ctrl+V 첨부를 지원한다. Web 전달에서는 로컬 파일 hash 검증과 ChatGPT UI의 기계적 준비 상태를 구분하고, 일반 Web 결과 파일은 Worker가 안전한 결과 경로에 저장한다.
