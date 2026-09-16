# Large Data/NAS

## 목표

ProjectHub-native assertion과 NAS Gateway 계약으로 대용량 파일을 Server가 중계하지 않고 안전하게 저장·검증한다.

## 세부 작업

### A. 계약·보안 경계와 구현 구조

ProjectHub.Server assertion issuer, ProjectHub NAS Gateway verifier/provision, content-addressed storage, resumable upload의 계약과 환경 변수 경계를 확정한다.

### B. Server assertion issuer

RS256 계열 서명, 짧은 만료, project/workstation/session/operation/object scope claim을 발급한다.

### C. Gateway verifier와 provision

공개키 검증, operation allowlist, hash·size·scope 검증, path escape/reparse 보호와 안전한 provision을 구현한다.

## 진행

현재 A 진행. 외부 NAS 계정·절대 경로·비밀키는 저장소에 기록하지 않는다.

## 변경 금지

- ProjectHub.Server가 대용량 바이너리를 relay하지 않는다.
- Agent는 Supabase와 NAS에 직접 접근하지 않는다.
- 자동 Git commit/push/pull/reset/merge/delete는 수행하지 않는다.

## 최종 검증

local/temp filesystem adapter 통합 테스트 후 실제 NAS1DUAL 환경에서만 NAS root, Gateway 실행 방식과 URL을 주입해 검증한다.
