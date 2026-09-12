# 카드게임 Spec과 규칙 엔진

이 문서는 룰북을 모바일 세로 게임으로 옮길 때 유지할 규칙 경계와 데이터 작성 방법을 정의한다. 화면·터치 입력·연출은 이번 작업 범위 밖이다. 원본 카드 목록은 `CardSourceInventory.md`, 전투 판독은 `RulebookCombatNotes.md`를 함께 본다.

## 원문과 모바일 기획의 경계

원문 캐릭터와 변형 카드는 동료 identity와 그 동료가 제공하는 스킬로 분리한다. `키리토`, `아스나` 같은 조건은 표시 이름의 문자열 포함 여부로 판정하지 않는다. 동료 키 또는 태그를 사용하므로 번역, 이름 변경, 스킬 강화가 판정을 바꾸지 않는다. 플레이어 HP, 동료 identity, 스킬 카드 인스턴스, 장비 인스턴스는 서로 다른 상태다. 동료 카드를 HP가 있는 별도 전투 유닛으로 바꾸는 것은 원문 규칙이 아니므로 추후 기획 결정이 필요하다.

첨부 PDF는 번역 스티커 시트다. 대부분 카드의 기본 공격력·최대 HP·명성도·가격은 수록되어 있지 않다. 따라서 출처 기록과 실행용 밸런스 Spec을 나눈다. 원문에서 확인한 효과 문구는 출처로 보존하고, 미확인 수치는 임의로 확정하지 않는다. 테스트/프로토타입 수치에는 명시적인 provenance를 둔다. 인쇄된 반복 칸 수를 초기 덱 장수로 사용하지 않는다.

원문 그대로 확정되지 않은 마비 중첩, 여러 방어 효과의 처리 순서, 일부 회복 문구는 데이터 정책 또는 명시된 모바일 가정으로 관리한다. 구현이 해석할 수 없는 효과나 불완전한 정의를 조용히 무시하는 방식은 사용하지 않는다.

## 세 개의 시간 축

| 시간 축 | 의미 | 예 |
| --- | --- | --- |
| 전체 라운드/페이즈 | 시작 → 마을 → 탐색 → 종료 | 시작 시 주민 덱 사망, 종료 시 손패 교체·몬스터 피해 초기화·마비 해제 |
| 전투 개인턴 | 플레이어 행동 → Switch 창 → 살아 있는 몬스터의 응답 → 다음 플레이어 | 일반 행동 횟수, 현재/좌/우 플레이어 타깃 |
| 전투 인스턴스 | 한 필드의 적들과 싸우는 구간 | 전투당 1회, 전투 지속 버프, 격파 보상 후보 |

지속시간을 `remainingTurns--` 하나로 만들면 다른 사람의 턴에도 감소하거나 라운드 종료와 혼동된다. 모디파이어는 기준 플레이어 identity, 세기 시작한 사건 번호, 남은 일치 사건 수를 저장한다. `SourceOwner + PersonalTurnStarted + 1`은 시전자의 다음 개인턴 시작, 횟수 `2`는 그 다음 개인턴 시작이다. 대상 전체에 붙여도 각 대상의 턴이 아닌 동일한 시전자 기준으로 만료한다. 카드가 버림/제외 영역으로 이동해도 독립 모디파이어의 기준 identity는 유지한다.

`every N` 트리거의 주기와 `N번째 사건까지` 만료는 별도 데이터다. 주기 카운터는 해당 모디파이어 인스턴스가 보관한다. 데이터 변경으로 2턴마다 회복과 3턴마다 회복을 바꿀 수 있어야 하며, 저장/재개해도 카운터가 초기화되면 안 된다.

## 효과 구성

효과는 순서 있는 연산 목록이다. 각 연산은 대상 selector, 값 식, 선택적 조건, 필요하면 모디파이어/후속 효과 참조를 갖는다. 연산 종류는 엔진이 구현하는 유한한 규칙 언어다. 카드 ID별 분기나 카드 이름별 메서드를 엔진에 추가하지 않는다. 새로운 카드와 강화는 기존 언어의 행을 조합한다. 전혀 새로운 규칙 의미를 도입할 때는 원시 연산 또는 사건 종류를 추가하고 그 의미를 테스트한다.

| 구성 | 분리 이유 |
| --- | --- |
| 대상 | 자신/시전자/현재 플레이어/다음/좌/우/같은 필드/적 전체/명시적 선택을 재사용 |
| 값 | 상수, 플레이어·대상 스탯, 참여자 수, 스택 수 등을 읽는 식 |
| 조건 | 태그, 특정 동료, 보스 전투, HP 비교, 앞선 사건 등 효과 실행 자격 |
| 효과 그룹 | 순서·조건·비용을 갖는 여러 연산 묶음 |
| 지속시간 | 사건, 기준 identity, N번째 발생, 전투 종료 경계 |
| 모디파이어 | 공격 금지, 수치 보정, 반복 효과, 해제 태그, 중첩 정책 |
| 강화 | 기존 스킬의 효과 그룹에 여러 그룹을 부착하는 독립 정의 |

동일 엔티티가 여러 역할로 선택될 수 있다. 솔로에서 `현재 플레이어 + 왼쪽 플레이어`는 자기 자신을 두 번 뜻한다. selector별 중복 정책을 사용하며 일괄 `Distinct()`로 원문 공격 횟수를 잃지 않는다. 선택지가 필요한 효과는 사용자 선택을 입력받기 전까지 상태를 확정하지 않는다.

## 상태, 저장, 명령

Spec은 변하지 않는 규칙이다. HP, 손패, 덱 순서, 카드 인스턴스의 강화 목록, 현재 턴, 모디파이어의 기준과 카운터, PRNG 상태는 UserData다. 실행 도중 임의의 전역 랜덤을 읽지 않는다.

하나의 명령은 상태 복사에서 자격/비용/선택을 확인하고 효과와 연쇄 사건을 처리한 뒤 한 번에 확정한다. 거절·추가 선택 필요·예산 초과는 원본 상태, RNG, ID 발급 카운터를 바꾸지 않는다. 무한 트리거를 막는 실행 예산은 잘못된 데이터의 일부 효과만 적용하는 용도가 아니다. 예산을 넘으면 명령 전체가 실패한다.

몬스터 응답은 그 시점에 살아 있는 몬스터들의 공격을 수집한 한 배치로 처리한다. 마지막 일격 보너스는 처치 순간 후보로 기록하고 필드 전투에 성공한 경우 지급한다. 도주/패배는 후보를 보상으로 확정하지 않는다. Switch는 다음 플레이어가 끼어드는 명시적 창이며, 성공하면 직전 몬스터 응답을 취소한다.

## KDI 소유권

| 타입/역할 | 레이어 | 소유 Scope | 생성 / 종료 |
| --- | --- | --- | --- |
| 검증된 게임 Spec snapshot | Data | Application | 구성 경계에서 BG 독립 repo를 값으로 변환한 뒤 Bind; App 종료에 참조 해제 |
| 진행 중 게임 상태 | Data | LocalSession | 로컬 저장에서 detached snapshot 로드; 변경 명령만 dirty; Session 종료 전 저장 |
| 규칙 처리 서비스 | Domain | LocalSession | KDI private field injection; 해당 GameData의 typed owner |
| 사용자 명령 서비스 | Application | LocalSession | Domain 명령 API를 호출; UI/Unity/저장장치 참조 없음 |
| 화면/전투 연출 | 향후 ViewModel/View | Scene 하위 화면 Scope | 아직 구현하지 않음 |

BGDatabase의 가변 행과 전역 repo는 게임 서비스에 주입하지 않는다. private repo에서 검증한 불변 Spec만 들어간다. 저장 포맷은 MessagePack이며 BGDatabase를 UserData 저장 수단으로 쓰지 않는다. 세이브는 상태 단위의 원자적 교체이고 여러 저장 단위에 걸친 트랜잭션은 아니다.

## 작성과 실행 경로

구체적인 20개 테이블의 열과 관계는 [ImplementedTables.md](ImplementedTables.md), 원문 대비 실행 범위와 남은 기능은 [SourceEffectCoverage.md](SourceEffectCoverage.md)를 따른다.

1. `Assets/_Project/SpecAuthoring/CardGame.tables.json`에서 행을 편집한다. 공개본은 실행용 124행이며 `sourceCards` 근거 테이블은 비어 있다. 공개 준비 전의 원문 근거 100행은 로컬 자료에 보존했다. [공개 저장소 구성](../RepositorySetup.md)을 참고한다. `catalog.sourceComplete=false`와 `prototype-balance` 태그는 미제공 기본 수치를 복원한 정식 팩이 아님을 뜻한다.
2. Unity 메뉴 `Tools/Project16/Import Card Game Specs (JSON to BG)`를 실행한다. 형식, enum, required reference, 중복 key, 효과 순서, 조건/그룹/encounter 순환을 검증한 뒤 BG bytes를 갱신한다. BG 관계 목록은 중복을 보존한다.
3. `CardGameModule.asset`이 bytes를 참조하고 FoundationSettings에 연결되어 있다. App Scope에 Spec을 넣고 LocalSession에 GameData → GameRulesDomain → GameApplication을 구성한다. `GameStarter`가 기존 저장 스케줄을 그대로 제공한다. 새 게임을 자동으로 시작하지 않는다.
4. Scene 구성 경계에서 `scene.ResolveEntry<IGameApplication>()`를 얻어 `StartRun("prototype-solo", seed)`를 호출한다. 실제 ViewModel은 `IGameApplication`과 `IGameData`를 private field로 주입받아 사용한다. `prototype-coop`은 2인 전투, `prototype-town`은 전투 밖의 스킬 강화/마을 효과 검증용이다.
5. `CommandResult.Events`로 연출할 사건을 받고 `IGameData.Snapshot`으로 결과를 읽는다. `NeedsChoice`이면 Choice의 key/options/min/max를 표시하고 기존 선택에 답을 추가해 **같은 명령**을 재제출한다. `ExpectedRevision`으로 오래된 화면의 명령을 거절할 수 있다. 선택을 받기 전 비용과 RNG는 확정되지 않는다.

## 현재 규칙 언어의 정확한 의미

- `Card.AttackValueId`가 없으면 `Card.Attack`이 기본 공격력이다. 있으면 Value 식을 읽는다. 아바타 공격력은 `PlayerBaseAttack` 식으로 선택하며 모든 동료 스킬에 자동 가산하지 않는다. 그 위에 장비·강화·모디파이어를 적용한다.
- `Monster.MaxHpValueId`로 전체 게임 인원 또는 실제 전투 참가자 수에 비례하는 HP를 지정할 수 있다. 두 값은 별도 ValueKind다.
- `Card.AllowedPhases`와 `IsEmergency`가 사용 창을 정한다. 일반 카드의 공통 연산은 마을/전투 모두 재사용한다. 긴급 사용은 현재 저장된 행동/Switch 창에서 일반 사용 횟수를 소비하지 않는다. 몬스터 응답 중 새로운 UI 선택 창을 보존하는 인터럽트 스택은 아직 없다.
- 시작 경계에 만료되는 상태는 그 시작 사건의 트리거보다 먼저 제거한다. 종료 경계는 트리거 뒤에 제거한다. `CurrentTurn`은 현재 개인턴 종료를 포함하며, `OwnerTurnEnd`는 적용 시점보다 뒤의 개인턴 종료부터 센다.
- Stat 연산은 Priority 오름차순으로 적용하며 동률은 적용된 순서다. `Add`, `MultiplyPercent`, `Set`, `Minimum`, `Maximum`의 순서를 데이터로 정한다. 예를 들어 합산 피해 `-1`과 최저 피해 `1`은 별도 모디파이어 두 개다.
- 모디파이어의 `Independent`, `Reject`, `Replace`, `Refresh`, `Add`는 서로 다른 재적용 정책이다. `Add`는 `MaxStacks`로 제한한다. 원문에서 확정되지 않은 중첩을 카드 이름에 따라 추정하지 않는다.
- 강화의 `EffectGroupId`는 여러 대상/기간/주기 효과를 부착하는 일반 경로다. 직접 `ModifierId`는 스킬 자체의 공격 보정 또는 행동 금지에 한정한다. 그 외 종류를 직접 부착하면 로드 시 거부한다.
- 몬스터 응답의 피해는 대상별로 합산한 뒤 방어·피해 반응을 한 번 적용한다. 비피해 효과는 MonsterOrder 순서로 실행한다. 여러 동시 반응의 우선권과 피해 출처별 replacement는 별도 확장이 필요하다.

## 저장 호환성

`card-game-run`은 preferences와 다른 저장 단위다. GameState의 RNG, ID 카운터, 카드 영역과 순서, 부착된 강화, 턴/페이즈 serial, 모디파이어 기준과 카운터, Switch 대기 상태를 명시적인 MessagePack formatter로 저장한다. 주기/백그라운드/종료 저장 모두 동일한 dirty unit 경로를 사용한다.

로드 시 GameState 구조와 현재 Spec의 모든 참조를 확인한 후 저장 단위에 등록한다. 유효했던 key를 없앤 팩으로 이전 세이브를 읽으면 기본값으로 덮어쓰지 않고 초기화를 실패시킨다. 같은 key의 수치를 변경하는 라이브 밸런스 호환성은 자동 판정하지 않는다. 배포 후에는 Spec 버전과 migration 정책을 확정해야 한다. formatter는 런타임 reflection 없이 명시 슬롯을 사용하며 IL2CPP 보존 설정은 추가했지만 플레이어 빌드는 별도 검증 대상이다.

`GameSpecCatalog`은 row 값만 받아 내부 불변 `SpecTable`을 복사·검증하는 구성 팩토리이기도 하다. 외부 레이어 의존성을 생성하는 코드가 아니므로 audit 구성 경계에 그 파일만 포함하고 해당 복사 메서드의 KDI009 경고만 좁게 억제했다. 서비스 생성·주입에 대한 분석기는 계속 활성화되어 있다.
