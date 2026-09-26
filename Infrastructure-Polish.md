# Infrastructure-Polish — ProjectHub Infrastructure 정책

갱신일: 2026-09-26 (KST)

이 문서는 `src/ProjectHub.Infrastructure`의 장기 정책을 정의한다.

제1조 (책임)

① Infrastructure는 Core가 정의한 계약에 필요한 외부 시스템 구현을 제공한다.
② 현재 외부 구현 범위에는 Supabase, 파일시스템, NAS 및 대용량 데이터 전송 관련 인프라가 포함될 수 있다.
③ Infrastructure는 ProjectHub의 UI, AI 관제 또는 사용자 작업 의미를 결정하지 않는다.

제2조 (의존성)

① Infrastructure는 Core를 참조할 수 있다.
② Core가 Infrastructure를 역참조하게 만들지 않는다.
③ Server나 Agent의 호스트별 실행 정책을 Infrastructure의 공통 구현에 불필요하게 결합하지 않는다.

제3조 (외부 시스템 경계)

① 네트워크, 저장소, 파일시스템과 외부 서비스의 실패는 숨기지 않고 호출 계층이 처리할 수 있는 기술적 실패로 전달한다.
② 외부 제품의 세부 형식을 공통 도메인 의미로 오인하지 않는다.
③ 파일 경로와 외부 입력은 사용하는 경계에서 기계적으로 검증한다.

제4조 (설정과 보안)

① 자격증명과 비밀값은 소스 코드나 저장소 문서에 기록하지 않는다.
② Supabase 서비스 역할 키와 같은 서버 비밀값은 환경 변수 또는 승인된 런타임 설정에서만 읽는다.
③ 로그에는 인증 헤더, 비밀키 또는 민감한 원문을 남기지 않는다.
