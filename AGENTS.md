# ProjectHub 작업 지침

- 변경 전 ProjectHub_IMPLEMENTATION_PLAN.md, CurrentWork.md, 활성 작업 파일을 먼저 읽는다.
- AI 역할·라우팅 정책은 Master-Polish.md의 가장 최신 최종 정책을 원본으로 본다. 다른 문서의 과거 완료 기록이 충돌하면 이력으로만 해석한다.
- Worker 비판단 원칙: Worker는 흐름 제어 도구다. 작업 내용, 요구사항 충족, 테스트 충분성, JUDGE 결과, 리소스 품질을 의미적으로 판단하지 않는다. 프로토콜/전송/세션/스키마/경로 안전성 같은 기계적 오류만 미확인으로 처리한다.
- 현재 신규 역할은 HQ / WORK / RESOURCE / JUDGE / 미확인이다. HIGH와 일회성 허가은 사용하지 않는다.
- 신규 AI 출력 제어 계약은 ACTION/GOTO만 사용한다. 일반 본문는 불투명다. JUDGE는 전용 스키마를 기계적으로 검사하고, RESOURCE는 자연어 본문가 비어 있지 않은지만 검사한 뒤 FIFO 사이드카 대기열에 넣는다.
- HQ는 설계·관제 역할이며 ChatGPT Web 또는 CLI 제공자로 실행할 수 있다. WORK는 CLI 제공자 실행을 사용한다. RESOURCE는 별도 ChatGPT Web 대화에 고정하고, Worker 내부 single-reader FIFO 대기열가 한 번에 1건씩 실행한다. JUDGE는 JEV 전송다.
- HQ Web과 RESOURCE Web은 서로 다른 conversationId에 명시적으로 연결한다. 생존 신호는 생존 확인용이며 작업 목적지 선택에 사용하지 않는다.
- RESOURCE는 ChatGPT Web이 생성해 파일로 반환할 수 있는 생성 리소스의 제작·다운로드·지정 경로 저장까지만 담당한다. 이미지·오디오·문서 등 구체 형식은 역할 의미가 아니라 반환 파일의 MIME 형식과 파일 정보로 구분한다. 자동 코드/CSS/HTML 연결, 의미 기반 컴포넌트 선택, 자동 품질 판정은 하지 않는다.
- [GOTO : RESOURCE] 한 번은 새로운 생성 리소스 요청 한 건을 만든다. 기존 요청의 상태 조회·취소·추적·확인·보고를 RESOURCE로 라우팅하지 않는다.
- HQ의 [ACTION=END]는 현재 실행 구간의 의미 작업 종료를 확정한다. Worker는 같은 실행 구간에서 HQ/WORK/JUDGE 의미 흐름을 자동으로 다시 열지 않고, 남은 기계적 대기 작업만 확인해 모두 끝난 뒤 DONE/DONE_WITH_ERROR로 전환한다. END 이후 같은 실행 구간의 WORK 보고가 HQ로 향하면 Worker가 "HQ의 작업은 종료되었습니다."로 차단한다. PAUSE 또는 DONE/DONE_WITH_ERROR 뒤 사용자가 명시적으로 작업 추가를 실행하면 기존 HQ/WORK 세션을 보존한 USER_FOLLOWUP 새 실행 구간을 HQ부터 시작할 수 있다.
- History는 Worker가 이미 가진 역할/상태/사용량/file 계측로 만든다. AI 본문 tag나 출처 문자열을 routing 판단에 사용하지 않는다.
- 한 번에 하나의 활성 구조 작업을 기준으로 수행하고, 완료/잔여/실제 검증 결과를 문서에 갱신한다.
- 변경 규모에 맞는 빌드·테스트를 실행하고 실제 결과만 기록한다. 실행 환경에 도구가 없으면 미실행 사실과 대체 정적 검증을 명시한다.
- 향후 실검증은 빌드된 Explorer 실행파일에서 HQ(Web/CLI), WORK, RESOURCE, JUDGE 흐름과 실제 생성 리소스 파일 저장을 우선 확인한다.
- 비밀키, 자격증명, 토큰, 결제정보, 민감한 URL을 코드·문서·로그에 남기지 않는다.
- 명시적 승인 없이 commit/push/배포/외부 시스템 변경을 수행하지 않는다.
- 저장소 동기화 작업에서는 최신 main과 GPT-Web-피드백.md를 확인한다. 피드백이 정책 원본과 충돌하면 Master-Polish.md와 활성 작업를 우선한다.

## 프로젝트 기준

- ProjectHub v0.2는 개발 PC 에이전트 → Mini PC 서버 → Supabase 흐름을 보존한다.
- Git 동작은 충돌, 분리된 HEAD, 변경 사항이 있는 상태의 pull, rebase 진행 상태를 자동 해결하지 않는다.
- Supabase 서비스 역할 키는 서버 환경 변수에만 둔다.

- 역할 프롬프트의 대괄호는 실제 ACTION/GOTO 제어 토큰에만 사용하고, 역할/inbound/availability 같은 메타데이터는 평문으로 쓴다.

- 역할 계약는 장기 불변식만 담는다. 특정 사용자 요청, 테스트 시나리오, 도메인 예시, 파일명, 횟수/목록, 일회성 장애 대응 문구를 계약에 추가하지 않는다.
- 계약 변경 전 “무관한 다른 Job에도 그대로 적용되는가?”를 확인한다. 아니라면 tests/fixtures/작업 history/validation note에만 둔다.
- 장애를 고칠 때 관측된 실패 문장을 그대로 계약에 넣지 말고, 필요한 경우 일반 프로토콜 invariant로 최소화한다.

- 역할 계약, 정책 문서, 작업 계획, 작업 기록, AI 역할 프롬프트의 설명 문장은 한글로 작성한다. ACTION/GOTO/QID, 상태 코드, 클래스명, 파일명 등 상호 운용과 코드 식별에 필요한 토큰은 예외다.
