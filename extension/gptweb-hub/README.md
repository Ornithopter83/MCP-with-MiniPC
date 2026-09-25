# GPTWeb-Hub 확장

버전: 0.1.9 / build 2026-09-25.3

ProjectHub Worker와 ChatGPT Web 대화를 루프백 브리지로 연결한다.

## 역할 연결

각 ChatGPT 대화는 사용자가 확장 패널에서 역할을 명시적으로 연결한다.

- HQ 연결: 설계·관제 AI가 ChatGPT Web target을 사용할 때의 전용 대화
- RESOURCE 연결: ChatGPT Web 생성 파일용 리소스 AI 전용 대화

HQ와 RESOURCE는 같은 conversationId를 동시에 사용할 수 없다. heartbeat는 대화가 살아 있는지와 확장 버전을 확인하는 데만 사용하며 작업 목적지를 선택하지 않는다. Worker는 저장된 역할 binding의 conversationId로 작업를 명시적으로 생성한다.

## 작업 처리

- 작업 조회/클레임/결과는 conversationId로 격리한다.
- 일반 HQ Web 작업은 assistant 텍스트를 TEXT_RESULT로 반환한다.
- RESOURCE 작업은 자연어 요청을 그대로 ChatGPT Web에 보내고, 최신 assistant turn에서 생성되어 파일로 받을 수 있는 결과를 모두 수집해 공통 `resultFiles[]` 배열로 Worker에 반환한다.
- 생성 파일 없이 텍스트 응답만 끝나면 기계적 대기 후 `resource_not_generated`로 실패 처리한다.
- 진행 상황 POST는 직렬 대기열로 전송해 기록 순서를 보존한다.
- Worker가 `assets/resources/<requestId>/` 아래에 반환된 파일명과 MIME 형식을 기계적으로 검증해 복수 생성 파일을 저장한다.
- RESOURCE는 저장까지만 수행하며 코드/CSS/HTML 연결은 하지 않는다.

## 안전 경계

- 브리지 호스트는 127.0.0.1/localhost만 허용한다.
- 생성 파일 다운로드를 위해 필요한 ChatGPT/OpenAI 파일 호스트 권한을 사용한다.
- RESOURCE 저장 경로는 Worker에서 작업공간 하위인지 다시 검증한다.


### 생성 파일 완료 세부사항

이미지는 DOM element의 load 완료를 확인하고, 다른 생성 파일은 다운로드 가능한 링크·첨부 요소가 준비됐는지 확인한다. DOM 변화가 추가로 없어도 제한 시간까지 주기적으로 재검사해 생성 결과가 준비되면 공통 파일 결과로 전송한다.


## 사이드카 대기열

RESOURCE 요청은 메인 HQ/WORK/JUDGE 진행과 분리된 single-reader FIFO 대기열에서 실행한다. RESOURCE Web에는 동시에 한 작업만 보내며, 실행 중 새 요청은 실패시키지 않고 대기열에 적재한다. HQ가 END를 반환해도 대기열가 실행/대기 중이면 Worker는 FINALIZING 상태로 남고 모든 RESOURCE 요청이 종료될 때까지 DONE을 만들지 않는다.

RESOURCE 응답 감시는 MutationObserver 외에 1초 watchdog도 사용한다. 생성 UI의 streaming 표시가 오래 남아도 생성 파일 후보 집합이 안정되면 다운로드 단계로 넘어가므로 무한 대기를 방지한다.

- 콘텐츠 스크립트의 생성 파일 fetch가 브라우저 CORS/권한 문제로 실패하면 백그라운드 서비스 워커가 허용된 ChatGPT/OpenAI 파일 호스트에서 재시도한다.
- Worker는 한 RESOURCE 응답의 모든 생성 파일을 먼저 임시 파일로 기록한 뒤 최종 이름으로 이동하며, 저장 실패 시 해당 요청의 부분 파일을 정리한다.


## 다운로드 고착 방지 강화 — 2026-09-25

- 이미지 수집 어댑터는 RESOURCE 시작 시 main 영역의 기존 이미지 URL을 기준선으로 기록하고, 이후 새로 나타난 큰 이미지를 assistant 응답 영역과 main 영역에서 함께 탐색한다.
- image completion 시간 초과은 응답 스냅샷 변화와 독립된 절대 120초 마감 시간으로 동작한다.
- 이미지 수집 어댑터는 생성 이미지가 하나 이상 로드되면 streaming 표기가 남아 있어도 이미지 집합이 잠시 안정된 뒤 다운로드 단계로 진행한다.
- IMAGE_DETECTED 진행 상황에 candidate/loaded 수를 기록해 생성 감지와 실제 다운로드 진입을 구분한다.
- Worker 사이드카에는 30분 전송 시간 초과가 있어 확장이 최종적으로 고착돼도 해당 bridge 작업을 실패 처리하고 FIFO 슬롯을 해제한다.


## 생성 파일 일반화 — 2026-09-25

- 확장 0.1.8부터 RESOURCE는 이미지 전용이 아니라 생성 파일 공통 결과를 처리한다.
- 기존 생성 이미지 DOM 탐지는 이미지 수집 어댑터로 유지한다.
- 다운로드 가능한 링크, 첨부 요소, 오디오·비디오 소스는 일반 파일 수집 어댑터로 감지한다.
- 결과는 모두 `RESOURCE_FILES`의 `resultFiles[]`로 전달하며 각 항목은 base64, MIME 형식, 파일명을 포함한다.
- 구버전 이미지 fetch 메시지는 호환을 위해 백그라운드 서비스 워커에서 계속 허용한다.

## 전송 확인 복구와 수동 재수집 — 2026-09-25

- Send 버튼 클릭 뒤 새 사용자 메시지 DOM만 기다리지 않고, composer 비움, 새 assistant turn, 새 RESOURCE 후보도 전송 성공의 기계적 증거로 사용한다.
- 새 RESOURCE 결과가 보이면 전송 확인 상태에서도 재전송하지 않고 응답 수집 단계로 전환한다.
- RESOURCE가 WAIT_RESPONSE에 진입하는 즉시 120초 절대 수집 마감 시간을 시작해 결과 탐지가 전혀 되지 않는 경우에도 명확히 실패 처리한다.
- RESOURCE 작업 카드에 `현재 결과 다시 수집` 버튼을 추가했다. 이 버튼은 프롬프트를 다시 보내지 않고 현재 assistant 결과만 재탐색·다운로드한다.
- Worker Pipeline의 RESOURCE 카드에는 확장이 보고한 전송 확인, 생성 결과 대기, 결과 확인, 다운로드, Worker 전달 단계를 그대로 표시한다.
