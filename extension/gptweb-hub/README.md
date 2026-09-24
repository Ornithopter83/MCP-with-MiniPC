# GPTWeb-Hub extension

Version: 0.1.4 / build 2026-09-24.1

ProjectHub Worker와 ChatGPT Web 대화를 loopback bridge로 연결한다.

## 역할 연결

각 ChatGPT 대화는 사용자가 확장 패널에서 역할을 명시적으로 연결한다.

- HQ 연결: 설계·관제 AI가 ChatGPT Web target을 사용할 때의 전용 대화
- RESOURCE 연결: 이미지 생성용 리소스 AI 전용 대화

HQ와 RESOURCE는 같은 conversationId를 동시에 사용할 수 없다. heartbeat는 대화가 살아 있는지와 확장 버전을 확인하는 데만 사용하며 task 목적지를 선택하지 않는다. Worker는 저장된 역할 binding의 conversationId로 task를 명시적으로 생성한다.

## Task 처리

- task 조회/claim/result는 conversationId로 격리한다.
- 일반 HQ Web task는 assistant 텍스트를 TEXT_RESULT로 반환한다.
- RESOURCE IMAGE task는 새 assistant turn에서 생성 이미지를 찾고 image bytes를 base64 payload로 Worker에 반환한다.
- Worker가 workspace 하위의 요청된 targetDirectory/targetFileName에 저장한다.
- RESOURCE는 저장까지만 수행하며 코드/CSS/HTML 연결은 하지 않는다.
- SOUND는 현재 transport 예약만 되어 있고 실제 결과 캡처는 구현하지 않았다.

## 안전 경계

- bridge host는 127.0.0.1/localhost만 허용한다.
- 생성 이미지 다운로드를 위해 ChatGPT/OpenAI image host 권한을 사용한다.
- RESOURCE 저장 경로는 Worker에서 workspace 하위인지 다시 검증한다.
