# 기반·카드게임 검증 기록

2026-09-13, Unity 6000.3.10f1, 설치된 KDI 2.0.0 소비 프로젝트에서 uLoop로 실행했다. 다른 Unity 인스턴스와 혼동하지 않도록 프로젝트 루트에서 실행했다. 변경 뒤 일반 compile을 사용했고 force recompile은 사용하지 않았다.

| 검증 | 실제 결과 |
| --- | --- |
| 마지막 `uloop compile` | 오류 0, 경고 0 |
| Foundation EditMode | 74개 통과 |
| CardGame EditMode | 54개 통과, 실패/건너뜀 0 |
| Foundation PlayMode | 5개 통과, 실패/건너뜀 0 |
| KDI 구조 audit | C# 50개, 오류/경고/advisory/UNKNOWN 0 |
| 최종 Unity Error 로그 | 0개 |
| 실제 BG binary 임포트/로드 | 20개 테이블, 224행 검증 후 생성. CardGameModule→FoundationSettings 연결 확인 |

실행 순서는 Foundation+CardGame 결합 EditMode 126개 통과 후, 공격 금지와 0공격 지원 스킬의 회귀 사례 2개를 추가하여 CardGame 54개를 다시 모두 통과한 것이다. 따라서 최종 어셈블리별 커버리지는 Foundation 74 + CardGame 54다. 128개를 단일 실행한 결과라고 표현하지 않는다. PlayMode 5개는 그 뒤 다시 실행했다.

```powershell
uloop compile
uloop run-tests --test-mode EditMode --filter-type regex --filter-value '^Project16\.(Foundation|CardGame)\.' --unsaved-changes fail
uloop run-tests --test-mode EditMode --filter-type assembly --filter-value Project16.CardGame.Tests --unsaved-changes fail
uloop run-tests --test-mode PlayMode --filter-type assembly --filter-value Project16.Foundation.PlayModeTests --unsaved-changes fail
uloop get-logs --log-type Error --max-count 20
```

Audit는 PATH의 Python 별칭 대신 설치된 Python 3를 사용하여 `.agents/skills/kdi-kylin-soul-injector/scripts/audit_kdi_architecture.py .`로 실행했다. advisory 도구이며 실제 컴파일러·KDI 분석기·런타임 테스트와 구분했다. 불변 Spec 집합이 자기 소유의 테이블 값을 복사하는 한 메서드의 KDI009만 근거 주석과 함께 좁게 억제했다. 서비스 DI나 레이어 분석을 전역으로 끄지 않았다.

## 게임에서 검증한 계약

- BG 관계와 키를 불변 테이블로 변환하고, 전역 BG repo를 로드/변경하지 않는다. 중복 actor 정의가 두 플레이어 인스턴스 자리를 유지한다.
- 같은 `sleep` 카드의 Duration 행에서 `occurrences`만 1→2로 바꾸면, 두 번째 몬스터 응답까지 공격 금지가 유지된다. C# 카드 분기 변경은 없다.
- 시전자 기준 만료, 다른 플레이어 턴 무시, 시작 사건 전 만료, 매 두 번째 해당 턴 회복, 강화 효과 추가를 검증했다.
- Attack 제한은 기본 공격만 막으며 비공격 효과는 진행한다. PlayCard 제한과 구분한다. 0공격 지원 스킬은 불필요한 공격 대상 선택을 요구하지 않는다.
- 여러 몬스터의 합산 피해에 방어를 한 번 적용한다. 사망 전 자동 반응과 HP 상한, 마비의 라운드 종료, 손패 교체를 검증했다.
- 불법 명령, 비용/선택 실패, 무한 연쇄의 실행 한도 초과 시 상태·RNG·ID·revision을 일부만 확정하지 않는다.
- 카드 기본 공격과 아바타 공격 식을 구분한다. 불변 Snapshot과 Commit 복사의 외부 변경 격리, 잘못된 참조·순환·지원되지 않는 직접 모디파이어를 검증했다.
- GameState 6종 DTO의 81개 저장 필드에 명시적 formatter가 대응한다. 저장 재개 후 Switch/지속시간, RNG/순서/강화 상태가 유지되며 손상·미지원 버전·읽기 오류는 기본값 덮어쓰기로 바뀌지 않는다.
- 실제 CardGameModule이 App/Session/Scene Scope에서 생성되고 preferences와 게임을 별도 dirty 저장 단위로 다룬다.

첫 게임 테스트에서 oversized-map fixture가 SDK의 잘린 입력 검사에 먼저 걸리는 실패 1개가 있었다. 테스트가 충분한 map 원소를 포함하도록 수정하여 실제 크기 제한 경로를 검증했고 이후 모두 통과했다.

## 실행하지 않은 검증과 남은 범위

모바일 세로 UI, 실제 사용자 입력/연출, 플레이어/IL2CPP 빌드, 모바일 실기기 pause/force-kill, 전체 원문 카드팩의 재현 테스트는 실행하지 않았다. AOT용 명시적 formatter와 linker 설정을 넣은 것은 플레이어 빌드 통과의 증거와 다르다.

원문 전체에 필요한 공유 덱·광장/영입·필드 진행·복잡한 선택 비용·피해 대체 등의 미지원 의미는 [SourceEffectCoverage.md](SourceEffectCoverage.md)에 구분했다. 동료와 스킬로 전환할 때 영입·주민 사망 규칙을 유지할지 바꿀지는 사용자 기획 확인 항목이다. 카드 번역 시트의 미제공 기본 수치를 임의의 정식 밸런스로 채우지 않았다.

사용자가 승인한 SampleScene 저장 이후 모든 테스트는 `--unsaved-changes fail`로 실행했다. 기존 씬·프리팹·Localization·View Assets 그룹을 재작성하지 않았다. 테스트 저장 파일은 임시 경로를 사용했다.

## 공개 저장소 준비 검증

같은 날짜에 공개용 작성본에서 원문 전사 100행만 별도 로컬 백업으로 분리했다. 20개 테이블의 실행용 124행은 유지했고 BG binary를 다시 생성했다. 이후 일반 `uloop compile` 오류/경고 0, `Project16.CardGame.Tests` EditMode 54개 통과를 확인했다. 이 검증은 필요한 BGDatabase/NuGet 패키지가 설치된 기존 Editor에서 실행했다. 공개 clone의 의존성 설치 과정과 별도의 플레이어 빌드는 실행하지 않았다. 제외·복원 규칙은 [RepositorySetup.md](../RepositorySetup.md)에 기록했다.
