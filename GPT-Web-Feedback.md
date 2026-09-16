# GPT Web Feedback

Updated: 2026-09-16

## 우선순위

1. `AGENTS.md`
2. `ProjectHub_IMPLEMENTATION_PLAN.md`
3. `CurrentWork.md`
4. 현재 활성 `tasks/*.md`
5. `GPT-Web-Feedback.md`

충돌 시 앞선 관리 문서와 활성 task를 우선한다.

---

## 이번 검토의 기준

최신 구현 커밋:

```text
b0a8e3369d8db85869f1406ae0d175b2f544b50c
Add large-data assertion and NAS upload integration
```

현재 GitHub 상태를 다시 검토한 결과, 05는 완료됐고 06 Large Data/NAS는 이미 상당 부분 구현되어 있다.

확인된 구현:

```text
[x] 05 실제 DEV PC root 프로젝트 E2E 완료
[x] watcher -> GitStateCollector -> Server -> Supabase 자동 갱신 완료
[x] last_file_activity 보완 완료
[x] LargeData Core 계약 추가
[x] LargeData lifecycle / assertion scope / upload session 계약 추가
[x] RS256 assertion issuer/verifier 구현
[x] content-addressed storage / resumable upload 구현
[x] chunk write/status/finalize 구현
[x] 최종 SHA-256 + size 검증 구현
[x] Supabase large-data metadata repository/schema 추가
[x] Agent LargeDataScanner + threshold + SHA-256 scan 추가
[x] 전체 build/test 통과 기록
```

`CurrentWork.md`는 다음 작업을 `06 Large Data/NAS 최종 통합 검증`으로 기록하고 있고, `tasks/06-large-data-nas.md`도 실제 NAS1DUAL 최종 통합 검증과 checkpoint 검증만 남았다고 기록한다.

따라서 지금 단계에서 06을 처음부터 다시 설계하거나 구현하지 않는다. **이미 구현된 06을 실제 NAS1DUAL 환경에 맞춰 검증·보정·완료하는 단계**로 진행한다.

---

# 사용자 실환경에서 새로 검증된 NAS1DUAL 사실

아래는 2026-09-16 사용자가 직접 실제 NAS1DUAL에서 확인한 결과다. 문서와 06 task 결과에 반드시 반영한다.

## 1. 실제 NAS Apache/PHP 경로

NAS Apache 설정:

```text
HTTP port  = 8888
HTTPS port = 8443
Server Root   = /HDD1/ServerRoot   (NAS UI 표기)
Document Root = /HDD1/DocRoot      (NAS UI 표기)
```

하지만 PHP 실행환경에서 실제 보이는 경로는 `/mnt/HDD1/...`다.

실제 PHP 확인값:

```text
__DIR__       = /mnt/HDD1/DocRoot/projecthub
DOCUMENT_ROOT = /mnt/HDD1/DocRoot
realpath ../.. = /mnt/HDD1
```

따라서 실제 ProjectHub NAS storage root는 다음을 기준으로 한다.

```text
/mnt/HDD1/ProjectHub
```

`/HDD1/ProjectHub`를 PHP에서 사용하면 `mkdir(): No such file or directory`가 발생했다.

## 2. 실제 storage 디렉터리와 권한

사용자가 NAS에 다음을 생성했다.

```text
/mnt/HDD1/ProjectHub/
├─ objects/
│  └─ sha256/
└─ staging/
```

PHP에서 직접 검사한 결과 다음 네 경로는 모두:

```text
exists   = true
is_dir   = true
realpath = 정상
writable = true
```

였다.

```text
/mnt/HDD1/ProjectHub
/mnt/HDD1/ProjectHub/objects
/mnt/HDD1/ProjectHub/objects/sha256
/mnt/HDD1/ProjectHub/staging
```

반면 `/mnt/HDD1` 자체는 PHP 계정 기준 writable=false였다. 따라서 Gateway가 NAS volume root에 임의 디렉터리를 생성하는 구조로 의존하지 않는다. ProjectHub root는 운영자가 미리 준비하고 Gateway는 그 내부만 사용한다.

## 3. 실제 Gateway URL

실제 Apache/PHP Gateway 테스트 URL:

```text
https://suhonas.ipdisk.co.kr:8443/projecthub/
```

`index.php`에서 다음 JSON 응답을 실제 확인했다.

```json
{"service":"ProjectHub NAS Gateway","status":"ok"}
```

현재 브라우저/CLI에서는 인증서 경고가 있어 수동 테스트 시 `curl -k`를 사용했다. 이것은 최종 운영 상태가 아니다. Agent 자동 연동 전에 정상 TLS 인증서 검증이 가능하도록 정리한다. Agent 코드에서 인증서 검증을 영구 비활성화하지 않는다.

## 4. PHP 호환성

NAS의 PHP 런타임은 `str_starts_with()`를 지원하지 않았다.

실제 증상:

```text
GET provision.php -> 정상 405 JSON
POST -> 500, body 없음
```

`str_starts_with()`를 `substr()` 기반 검사로 변경한 뒤 정상 동작했다.

따라서 NAS Gateway PHP는 현재 NAS 런타임과 호환되는 구문을 사용하고 PHP 8 전용 함수/문법에 불필요하게 의존하지 않는다.

## 5. Authorization 헤더 전달

초기에는 `$_SERVER['HTTP_AUTHORIZATION']`만 사용했을 때 Bearer 헤더를 읽지 못했다.

다음 fallback 경계를 적용한 뒤 Bearer 헤더 수신을 실제 확인했다.

```text
HTTP_AUTHORIZATION
REDIRECT_HTTP_AUTHORIZATION
getallheaders()
apache_request_headers()
```

실제 검증:

```text
GET provision.php
-> 405 {"error":"method_not_allowed"}

POST, Authorization 없음
-> 401 {"error":"upload_session_required"}

POST, Bearer test-token
-> 501 {"error":"assertion_validation_not_implemented"}
```

즉 HTTP method와 Authorization header 경계가 실제 Apache/PHP 환경에서 정상 동작했다.

## 6. RS256 key / assertion 실제 검증 성공

RSA keypair을 생성해:

```text
private key -> Server 쪽
public key  -> NAS Gateway
```

로 배치했다.

NAS public key 위치는 실제 환경에서:

```text
/mnt/HDD1/DocRoot/projecthub/keys/projecthub-public.pem
```

형태로 배치했다.

사용자가 Server에서 테스트 JWT를 RS256으로 발급하고 NAS Gateway에서 public key로 검증했다.

실제 검증 중 만료 토큰은:

```text
401
{"error":"assertion_expired"}
```

로 거절됐다. 이는 Authorization 전달, JWT parsing, signature/claim 검증, exp 검증 경로가 실제로 동작한다는 의미다.

새 assertion 발급 후 Server에서 provision 호출이 성공했고, 이어서 **동일한 short-lived JWT를 DEV PC로 전달해 DEV PC -> NAS Gateway provision도 성공**했다.

따라서 실제로 다음 경로가 검증됐다.

```text
ProjectHub Server/test issuer
  -> RS256 signed assertion
  -> DEV PC client
  -> HTTPS NAS Gateway
  -> public-key verification
  -> claim validation
  -> /mnt/HDD1/ProjectHub access
  -> write/flush/read/delete check
  -> 200 ready
```

이 결과는 06의 **실제 NAS provision/authentication E2E 성공**으로 기록한다.

---

# 현재 06에서 남은 실제 작업

Provision/authentication 자체는 더 이상 미검증 항목으로 두지 않는다.

이제 핵심은 이미 구현된 large-data 코드와 실제 NAS Gateway를 연결해 다음을 끝까지 검증하는 것이다.

```text
1. 실제 ProjectHub.Server assertion issuer를 테스트 script가 아니라 운영 코드 경로로 사용
2. Agent가 Server에서 upload assertion/session을 받아 사용
3. Agent -> NAS Gateway 직접 chunk upload
4. upload status 조회
5. 중단 후 resume 검증
6. NAS staging에 실제 chunk 저장 확인
7. finalize 시 전체 size 확인
8. finalize 시 SHA-256 확인
9. content-addressed objects/sha256/... 최종 object 생성
10. 동일 hash 재업로드 시 dedup 확인
11. Gateway 검증 성공 후 Supabase STAGED metadata 반영
12. 실제 Git commit 변화와 LargeDataSet/checkpoint 연결
13. CHECKPOINTED dataset item 검증
14. Agent 재시작 시 startup reconciliation / 미완료 session 복구 검증
```

가능하면 위 흐름을 별도 설계 task로 다시 쪼개지 말고 **06 최종 통합 검증** 안에서 연속 수행한다.

---

# 중요한 구현 경계 정리

현재 `tasks/06-large-data-nas.md`의 문구 중:

```text
Agent는 Supabase와 NAS에 직접 접근하지 않는다.
```

는 현재 의도와 오해가 없도록 정리할 필요가 있다.

정확한 경계는:

```text
Agent -> Supabase 직접 접근 금지
Agent -> SMB/NAS filesystem 직접 접근 금지
Agent -> NAS credentials 보유 금지
Agent -> ProjectHub NAS Gateway HTTPS 접근 허용/필수
```

즉 binary data plane은:

```text
Agent -> NAS Gateway -> NAS1DUAL
```

이고 control plane은:

```text
Agent -> ProjectHub.Server -> Supabase
```

이다.

ProjectHub.Server가 대용량 binary를 relay하지 않는 기존 원칙을 유지한다.

---

# 문서 상태 불일치도 함께 정리

현재 문서 사이에 다음 불일치가 있다.

```text
CurrentWork.md
  -> 다음 작업: 06 Large Data/NAS 최종 통합 검증

ProjectHub_IMPLEMENTATION_PLAN.md
  -> 현재 작업: 06 A. ProjectHub-native 계약과 구현 경계

NewThreadHandoff.md
  -> 아직 05-C E2E가 현재 작업으로 기록됨
```

실제 최신 구현과 검증 기준으로 맞춘다.

권장 상태:

```text
05 = 완료
06 = 진행
06 A~기본 구현 = 완료
06 provision/RS256/NAS write E2E = 완료
현재 작업 = 06 Large Data/NAS 실제 upload/finalize/checkpoint 최종 통합 검증
```

`CurrentWork.md`, `ProjectHub_IMPLEMENTATION_PLAN.md`, `tasks/06-large-data-nas.md`, `NewThreadHandoff.md`가 같은 현재 작업을 가리키게 한다.

---

# 보안 관련 즉시 확인

현재 저장소 `.gitignore`에는 PEM key ignore 규칙이 없다.

현재 확인한 root 파일 목록에는 private PEM 자체가 보이지 않지만, 사용자가 실제 Server PC에서 `projecthub-private.pem`을 생성해 테스트하고 있으므로 다음을 바로 보강한다.

```text
*.pem
*.key
projecthub-private.pem
```

등 적절한 secret-key ignore 규칙을 추가하고, `git status` / tracked files를 확인하여 private key가 Git에 추가되지 않았음을 검증한다.

이미 commit/history에 private key가 들어간 흔적이 발견될 경우 단순 삭제로 끝내지 말고 key를 폐기/재발급한 뒤 기록을 정리한다.

테스트용 `make_projecthub_token.py`는 private key 내용을 포함하지 않는 helper여야 하며, 운영 assertion 발급의 최종 경로로 사용하지 않는다. 운영에서는 `ProjectHub.Server`의 `LargeDataAssertionIssuer`를 사용한다.

---

# Codex 수행 지침

1. 최신 commit `b0a8e336...`와 `CurrentWork.md`, `tasks/06-large-data-nas.md`를 기준으로 기존 구현 상태를 먼저 확인한다.
2. 05를 다시 열지 않는다. 실제 DEV PC project-state E2E는 완료된 상태로 처리한다.
3. 위 사용자 실환경 NAS 검증 결과를 관리 문서에 사실대로 반영한다.
4. 문서의 현재 작업을 `06 Large Data/NAS 최종 통합 검증`으로 통일한다.
5. ProjectHub NAS root는 실제 PHP-visible path `/mnt/HDD1/ProjectHub`를 사용한다. `/HDD1/ProjectHub`로 추측하지 않는다.
6. 운영자는 ProjectHub root를 미리 생성하고 Gateway는 그 내부만 사용하도록 한다.
7. NAS Gateway PHP는 현재 런타임 호환성을 유지하고 `str_starts_with()` 같은 비호환 API에 의존하지 않는다.
8. Authorization 헤더 fallback 로직을 실제 NAS Apache 환경 기준으로 유지한다.
9. provision/RS256/NAS write E2E는 완료된 것으로 기록한다.
10. 다음 구현/검증은 실제 chunk upload -> status/resume -> finalize -> hash/size -> STAGED -> checkpoint 순서로 진행한다.
11. 실제 binary는 Agent -> NAS Gateway로 직접 전송한다. Server는 relay하지 않는다.
12. Agent는 Supabase/SMB/NAS filesystem에 직접 접근하지 않지만 NAS Gateway HTTPS에는 직접 접근한다.
13. 운영 assertion은 테스트 Python helper가 아니라 ProjectHub.Server 구현을 사용한다.
14. `.gitignore`에 private key/PEM 보호를 추가하고 private key가 tracked되지 않았는지 확인한다.
15. 실제 TLS 검증 문제를 최종 Agent E2E 전에 해결한다. `curl -k` 또는 certificate bypass를 운영 코드에 넣지 않는다.
16. build/test를 다시 실행하고 실제 결과만 문서에 기록한다.
17. 사용자 승인 없이 자동 Git commit/push/reset/merge/delete를 수행하지 않는다.
