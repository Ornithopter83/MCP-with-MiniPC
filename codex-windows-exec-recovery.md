# Codex Windows 명령 실행 오류와 복구 기록

- 발생 및 복구 확인: 2026-10-08, 한국 시간(Asia/Seoul)
- 확인한 Codex CLI: `0.162.0-alpha.2`
- 확인한 Windows 앱 패키지: `OpenAI.Codex_26.1002.7124.0_x64`
- 기존 Windows 샌드박스 구현: `elevated`
- 복구 상태: 사용자 승인 후 `unelevated`로 임시 전환하고 앱을 완전히 재시작하여 일반 명령 실행을 복구함

## 증상

일반 실행 도구로 `Get-Location` 같은 읽기 전용 명령을 호출해도 프로세스가 시작되기 전에 다음 오류가 발생했다.

```text
Failed to create unified exec process:
helper_unknown_error: setup refresh had errors
```

승인된 권한으로 실행하는 진단 경로는 동작했으므로, 그 경로로 로그와 실행 중인 보조 프로세스를 확인할 수 있었다.

## 로그에서 확인한 원인

다음 파일을 확인했다.

- `%USERPROFILE%\.codex\.sandbox\setup_error.json`
- `%USERPROFILE%\.codex\.sandbox\sandbox.2026-10-07.log`

이번 환경에서는 한국 시간 10월 8일의 기록이 위 날짜의 회전 로그에 기록됐다. 다른 환경에서는 최근 수정된 `sandbox.*.log`도 확인해야 한다.

`setup_error.json`에는 다음 요약만 있었다.

```json
{
  "code": "helper_unknown_error",
  "message": "setup refresh had errors"
}
```

상세 로그에는 아래 단계에서 Windows 오류 32가 기록됐다.

```text
runtime read/execute validation failed
open ACL target for root-only update
다른 프로세스가 파일을 사용 중이기 때문에 프로세스가 액세스 할 수 없습니다. (os error 32)
```

오류가 발생한 파일은 설치된 컴퓨터 사용 런타임 안의 다음 항목이었다.

```text
%LOCALAPPDATA%\OpenAI\Codex\runtimes\cua_node\<runtime-id>\bin\node_repl.exe
%LOCALAPPDATA%\OpenAI\Codex\runtimes\cua_node\<runtime-id>\bin\node_modules\@oai\sky\bin\windows\swift\x64\VCRUNTIME140_1.dll
%LOCALAPPDATA%\OpenAI\Codex\runtimes\cua_node\<runtime-id>\bin\node_modules\@oai\sky\bin\windows\swift\x64\codex-computer-use-swift.exe
```

해당 파일을 사용하는 `node_repl.exe` 및 `codex-computer-use-swift.exe` 프로세스를 확인했다. ACL에는 `CodexSandboxUsers`의 읽기·실행 권한이 이미 있었다. 로그와 프로세스 확인 결과, 직접적인 실패 원인은 실행 중인 런타임 파일의 잠금과 샌드박스 ACL 검증·갱신 단계의 충돌이었다.

이 기록은 위 설치 조합에서 관찰한 원인이다. 동일한 상위 오류 메시지가 다른 환경에서도 반드시 같은 원인을 뜻하는 것은 아니다. 내부 구현의 결함 여부는 확정하지 않았다.

## 시도한 조치와 결과

| 조치 | 관찰 결과 |
| --- | --- |
| 일반 실행 도구로 명령 호출 | 설정 갱신 오류를 재현함 |
| 승인된 진단 경로에서 로그·ACL·프로세스 확인 | 실행 가능했고 파일 잠금 원인을 확인함 |
| 확인된 `node_repl.exe` 보조 프로세스 종료 | 해당 잠금은 해제됐으나 다음 보조 DLL에서 실패함 |
| 컴퓨터 사용 보조 프로세스 종료 및 제한된 시간 동안 잠금 해제 시도 | 앱이 보조 프로세스를 자동 재실행하여 잠금이 재발함 |
| 기존 `elevated` 설정으로 앱 전체 재시작 | 동일한 `node_repl.exe` 잠금 오류가 다시 발생함 |
| `unelevated` 설정을 읽는 새 CLI 프로세스로 확인 | 제한된 작업 폴더 명령 실행에 성공함 |
| 설정 변경 후 앱 전체 재시작 및 일반 실행 재검증 | 일반 명령 실행과 임시 파일 생성·읽기·삭제에 성공함 |

보조 프로세스 종료를 반복하는 방법은 지속적인 해결책으로 확인되지 않았다.

## 적용한 복구 방법

### 1. 사용자 설정 백업

사용자 승인 후 `%USERPROFILE%\.codex\config.toml`을 백업했다. 이번 백업 파일명은 `config.toml.before-unelevated-20261008-064939.bak`이다.

동일 조치를 수행할 때는 변경 직전에 별도 백업을 만든다.

```powershell
$cfgPath = Join-Path $env:USERPROFILE '.codex\config.toml'
$backupPath = $cfgPath + '.before-unelevated-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.bak'
Copy-Item -LiteralPath $cfgPath -Destination $backupPath -ErrorAction Stop
```

### 2. Windows 샌드박스 구현 임시 변경

기존 `[windows]` 표 안의 `sandbox` 값만 변경했다. 다른 설정과 표는 유지했다.

```toml
[windows]
sandbox = "unelevated"
```

실행 중인 앱에는 기존 설정이 계속 적용돼 설정 파일 변경만으로는 오류가 해소되지 않았다.

### 3. 앱 완전 종료 및 재실행

사용자가 승인한 Codex 앱과 그 앱의 보조 프로세스를 종료한 뒤 등록된 앱 실행 항목으로 다시 열었다. 앱 종료와 함께 재실행 담당 프로세스까지 중단되는 문제를 피하기 위해 앱과 독립된 일회성 Windows 작업을 사용했다. 이 작업은 재실행을 확인한 뒤 자동 삭제됐다.

### 4. 실제 실행 경로 검증

새 CLI 프로세스에서는 다음 형태로 임시 모드 동작을 먼저 확인했다. 아래 명령은 이번 CLI 버전 기준이다.

```powershell
codex sandbox -P :workspace -C C:\path\to\repo -- powershell.exe -NoProfile -Command "Write-Output 'fallback_sandbox_ok'; whoami"
```

앱 재시작 후에는 일반 실행 도구를 통해 다음을 확인했다.

- `Get-Location`, `whoami` 및 출력 명령이 정상 실행됨
- 작업 폴더 안에 임시 파일을 만들고 내용을 읽을 수 있음
- 임시 파일을 삭제할 수 있고 검증 파일이 남지 않음
- 반복 호출에서도 기존 설정 갱신 오류가 재현되지 않음

최종 파일 생성·읽기·삭제 검증 명령의 종료 코드는 `0`이었다. 승인된 별도 진단 경로의 성공과 일반 실행 경로의 복구를 구분해 확인했다.

## 임시 조치의 한계와 원복

`unelevated`는 공식 지원 대체 구현이다. 현재 사용자에서 파생된 제한 토큰과 ACL로 파일 접근을 제한하지만, `elevated`의 별도 샌드박스 사용자 경계와 오프라인 사용자 방화벽 규칙을 사용하지 않아 격리 수준이 더 약하다. 보호 수준 변경을 설명하고 사용자 승인을 받은 뒤 적용했다.

이번 조치는 실행을 복구한 임시 대안이며, `elevated` 구현의 파일 잠금 문제를 영구적으로 수정한 것은 아니다. 설치 환경이나 구현 문제가 해결된 후 권장 구현으로 복귀할 때는 다음 순서로 확인한다.

1. 사용자 설정의 `[windows]` 안에서 `sandbox = "elevated"`로 변경한다.
2. Codex 앱을 완전히 종료하고 다시 실행한다.
3. 일반 실행 경로에서 읽기 명령과 임시 파일 생성·읽기·삭제를 재검증한다.
4. 실패하면 최근 샌드박스 로그에서 실제 하위 오류를 다시 확인한다.

설정 백업 이후 다른 설정이 변경됐다면 원복할 때 `windows.sandbox` 값만 수정하여 이후 변경사항을 보존한다.

## 공식 참고 자료

- [Windows sandbox: 구현 차이와 문제 해결](https://learn.chatgpt.com/docs/windows/windows-sandbox)
- [Config basics: Windows 샌드박스 설정과 설정 우선순위](https://learn.chatgpt.com/docs/config-file/config-basic)
