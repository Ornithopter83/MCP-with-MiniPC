# GPTWeb-Hub extension

Version: 0.1.7 / build 2026-09-25.1

ProjectHub Worker와 ChatGPT Web 대화를 loopback bridge로 연결한다.

## 역할 연결

각 ChatGPT 대화는 사용자가 확장 패널에서 역할을 명시적으로 연결한다.

- HQ 연결: 설계·관제 AI가 ChatGPT Web target을 사용할 때의 전용 대화
- RESOURCE 연결: 이미지 생성용 리소스 AI 전용 대화

HQ와 RESOURCE는 같은 conversationId를 동시에 사용할 수 없다. heartbeat는 대화가 살아 있는지와 확장 버전을 확인하는 데만 사용하며 task 목적지를 선택하지 않는다. Worker는 저장된 역할 binding의 conversationId로 task를 명시적으로 생성한다.

## Task 처리

- task 조회/claim/result는 conversationId로 격리한다.
- 일반 HQ Web task는 assistant 텍스트를 TEXT_RESULT로 반환한다.
- RESOURCE IMAGE task는 자연어 요청을 그대로 ChatGPT Web에 보내고, 최신 assistant turn의 로드 완료된 생성 이미지들을 모두 수집해 base64 배열로 Worker에 반환한다.
- 이미지 없이 텍스트 응답만 끝나면 기계적 대기 후 `resource_image_not_generated`로 실패 처리한다.
- progress POST는 직렬 queue로 전송해 transcript 순서를 보존한다.
- Worker가 `assets/resources/<requestId>/image-01.*`, `image-02.*` 형태로 요청별 폴더에 복수 이미지를 저장한다.
- RESOURCE는 저장까지만 수행하며 코드/CSS/HTML 연결은 하지 않는다.
- 현재 RESOURCE transport는 IMAGE만 지원한다.

## 안전 경계

- bridge host는 127.0.0.1/localhost만 허용한다.
- 생성 이미지 다운로드를 위해 ChatGPT/OpenAI image host 권한을 사용한다.
- RESOURCE 저장 경로는 Worker에서 workspace 하위인지 다시 검증한다.


### Image completion detail

이미지 element가 assistant turn에 먼저 추가되고 실제 bytes 로딩이 나중에 끝나는 경우를 지원한다. 확장은 해당 image의 load 이벤트를 기다려 다시 검사하며, DOM mutation이 추가로 발생하지 않더라도 최대 120초 검사 시점에 이미지가 로드되어 있으면 정상 결과 전송으로 이어간다.


## Sidecar queue

RESOURCE 요청은 메인 HQ/WORK/JUDGE 진행과 분리된 single-reader FIFO queue에서 실행한다. RESOURCE Web에는 동시에 한 task만 보내며, 실행 중 새 요청은 실패시키지 않고 queue에 적재한다. HQ가 END를 반환해도 queue가 실행/대기 중이면 Worker는 FINALIZING 상태로 남고 모든 RESOURCE 요청이 종료될 때까지 DONE을 만들지 않는다.

RESOURCE 응답 감시는 MutationObserver 외에 1초 watchdog도 사용한다. 이미지 생성 UI의 streaming 표시가 오래 남아도 이미지 목록 snapshot이 안정되면 제한 시간 후 다운로드 단계로 넘어가므로 무한 대기를 방지한다.

- content script의 이미지 fetch가 브라우저 CORS/권한 문제로 실패하면 background service worker가 허용된 ChatGPT/OpenAI 이미지 host에서 재시도한다.
- Worker는 한 RESOURCE 응답의 모든 이미지 파일을 먼저 임시 파일로 기록한 뒤 최종 이름으로 이동하며, 저장 실패 시 해당 요청의 부분 파일을 정리한다.


## Download stall hardening — 2026-09-25

- RESOURCE 시작 시 main 영역의 기존 image URL을 baseline으로 기록하고, 이후 새로 나타난 큰 이미지들을 assistant bubble과 main 영역에서 함께 탐색한다.
- image completion timeout은 response snapshot 변화와 독립된 절대 120초 deadline으로 동작한다.
- 생성 이미지가 하나 이상 로드되면 streaming 표기가 남아 있어도 이미지 집합이 잠시 안정된 뒤 IMAGE_READY -> DOWNLOAD_START로 진행한다.
- IMAGE_DETECTED progress에 candidate/loaded 수를 기록해 생성 감지와 실제 다운로드 진입을 구분한다.
- Worker sidecar에도 5분 transport timeout이 있어 extension이 고착돼도 해당 bridge task를 FAILED 처리하고 FIFO 슬롯을 해제한다.
