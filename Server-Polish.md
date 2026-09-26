# Server-Polish — ProjectHub Server 정책

갱신일: 2026-09-26 (KST)

이 문서는 `src/ProjectHub.Server`의 장기 정책을 정의한다.

제1조 (책임)

① Server는 Mini PC에서 ProjectHub의 중앙 HTTP 서비스를 제공한다.
② Server는 Core의 도메인 계약과 Infrastructure의 외부 시스템 구현을 조합해 API를 제공한다.
③ Server는 개발 PC Agent의 로컬 수집 책임이나 Worker의 AI 관제 책임을 대신하지 않는다.

제2조 (의존성)

① Server는 Core와 Infrastructure를 참조할 수 있다.
② Server의 HTTP 표현이나 호스트 설정을 Core의 도메인 계약으로 역전파하지 않는다.

제3조 (API 경계)

① HTTP 입력의 필수 값, 형식과 기계적 유효성은 API 경계에서 검증한다.
② 도메인 또는 저장소 처리는 Core 계약과 Infrastructure 구현을 통해 수행한다.
③ 기술 오류와 인증 오류를 제품 의미의 성공이나 실패로 임의 변환하지 않는다.

제4조 (운영)

① 서버 상태와 주요 작업은 비밀값을 제외한 범위에서 운영 로그로 남길 수 있다.
② 외부 저장소와 대용량 데이터 경로의 설정은 런타임 구성으로 주입한다.
③ Server 변경은 Agent 및 외부 호출자가 사용하는 API 호환 영향을 확인한다.
