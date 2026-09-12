# 원문 효과와 현재 실행 기반의 대응 범위

검토일: 2026-09-13. 기준은 현재 `Project16.CardGame`의 Data/Domain/Application 코드와 `SAO Card Complete Set Korean.pdf` 전체 10쪽이다. UI, 씬 구성, 그림 데이터는 이 검토 범위에 포함하지 않는다.

현재 구현은 **명시적으로 저작한 테스트/게임 팩을 실행하는 일반 규칙 기반**이다. 한국어 번역 시트의 99개 카드/아바타 정의를 모두 원문대로 실행하는 완성 팩은 아니다. 원자료 카탈로그 100개 레코드에는 99개 카드/아바타와 마비독 표식 1종이 포함되어 있다. 원자료의 판독 완료, 효과 원소의 구현, 원문 카드의 완전한 재현은 서로 다른 상태다.

## 원자료와 실행 정의를 구별하는 기준

| 상태 | 의미 | 이번 작업의 자료 |
| --- | --- | --- |
| 판독 완료 | 제목/효과를 읽고 PDF 페이지와 반복 칸 수를 검증함 | 로컬 원자료 `CardSourceInventory.md`, `CardSourceInventory.json` — 원문 전문이 있어 공개 Git에서는 제외 |
| 원소 구현 | 특정 selector/condition/value/operation/duration을 Domain이 실제 처리함 | [GameSpecTypes.cs](../../Assets/_Project/CardGame/Specs/GameSpecTypes.cs), [EffectRules.cs](../../Assets/_Project/CardGame/Domain/EffectRules.cs), [ModifierRules.cs](../../Assets/_Project/CardGame/Domain/ModifierRules.cs) |
| 실행 정의 검증 | 관계와 수치가 갖춰진 immutable Spec 집합이 로드되고 명령을 처리함 | [GameSpecCatalog.cs](../../Assets/_Project/CardGame/Specs/GameSpecCatalog.cs), [GameRulesDomain.cs](../../Assets/_Project/CardGame/Domain/GameRulesDomain.cs) |
| 원문 재현 완료 | 기본 수치와 의미가 확정되고 카드별 모든 창/대상/비용/결과를 검증함 | **99개 전체에 대해 주장하지 않음**. 아래 미지원 의미와 누락 입력을 해결해야 함 |

원자료 JSON은 `catalogueKind: SourceEvidenceOnly`이고 모든 레코드의 `executableDefinitionReady`가 false다. 효과 숫자 토큰을 기본 공격력, 가격, HP로 대입하거나 인쇄 반복 칸 수를 초기 덱 수량으로 간주해서는 안 된다.

## 현재 코드로 조합할 수 있는 기반

다음 표는 공통 원소의 구현 범위다. 예시 카드 이름은 그 원소를 요구하는 원문 사례이며 **해당 카드 전체가 완성되었다는 표시가 아니다**. enum 값의 선언만으로 런타임 지원으로 간주하지 않는다.

| 영역 | 실제 구성/처리 범위 | 원문과 연결할 수 있는 부분 | 남는 주의점 |
| --- | --- | --- | --- |
| 피해/회복/자원 | 고정 또는 수식 값으로 피해, 몬스터 응답 batch의 대상별 피해 합산, 최대 HP를 넘지 않는 회복, 플레이어 자원 증감 | 일반 몬스터 공격, 시리카 회복, 빵 드로우 | 회복량/목표 HP 표현, 실제 피해와 피해 예정값, 피해 대리 수령은 별도 의미 |
| 개인 덱 | 개인 덱 드로우, 버림 더미 셔플 복귀, 카드의 덱/손패/버림/제외/장착 영역 이동 | 사치, 빵, 고기, 유리엘의 일부 | 공개 덱/광장/덱 위아래 선택/타인에게 소유권 이전은 별도 기능 |
| 순차 효과 | 순서가 있는 EffectGroup, 그룹 참조, 조건부 각 효과, 처리 단계 상한 | 회복 뒤 드로우 같은 고정 연속 효과 | 선택 비용 성공 여부를 후속 효과 값/조건으로 전달하는 기능은 부족 |
| 사용 phase/긴급 창 | 카드별 허용 phase, 전투 외 사용, 현재 행동/Switch 대기 중 참가자의 손패 긴급 Item/Skill 사용, 이미 소지한 긴급 Equipment 능력 사용 | 마을용 드로우, 긴급 해독처럼 현재 저장된 창에서 사용할 수 있는 효과 | 손패 긴급 Equipment는 임시 장착 대상 계약이 없어 명시적으로 거부. 몬스터 응답 내부의 사망 직전 선택을 중단·재개하는 창과도 별개 |
| 대상 | 자기/소스 소유자/현재/다음/이전 플레이어, 선택한 플레이어·몬스터·카드, 전체/전투 참가자/전체 몬스터, source card | 좌우 인접 공격, 전체 해독, 특정 대상 회복, 범위 피해 | 최고값 동률 집합, owner+zone+종류 복합 카드 선택, 다중 선택 등은 미지원 |
| 중복 대상 | selector의 deduplicate 설정으로 중복 제거 여부를 명시 | 소인원에서 좌우 대상 중복 | 원문의 공통 규칙을 확정한 후 카드별 설정 필요 |
| 조건/값 | 논리 All/Any/Not, 태그, HP 임계, 자원, phase, event, counter; HP/공격/아바타 기본 공격/자원/인원 참조 수식과 카드 공격·몬스터 HP 수식 연결 | 성별/길드 태그 조건, HP 임계, 리-버의 아바타 공격 참조, 수치 보정 일부 | 조건의 대상과 event source/직전 행동/전투 이력 구분은 아직 제한적 |
| 수치 수정 | Add/MultiplyPercent/Set/Minimum/Maximum, 우선순위, stack 정책 | 공격 보정, 피해 하한 0/1, 추가 행동 횟수 | source 한정·피해 합산 경계·사용 횟수 규칙까지 자동 구현되는 것은 아님 |
| 지원 스킬과 행동 제한 | 고정 0 공격이고 대상 HP 의존 수식/BeforeAttack hook이 없으면 기본 공격과 대상 선택을 생략. Attack 금지는 기본 공격만 억제하고 지원 효과는 유지. PlayCard 금지는 카드 사용 명령 전체를 거부 | 드로우/회복 같은 지원 스킬과 공격 억제를 구분 | Attack 금지는 모든 Damage operation을 지우는 전면 효과 무효화가 아님. 공격 준비 hook/대상 수식이 있으면 필요한 공격 문맥을 보존 |
| 기간 | 현재 턴, 특정 owner/source owner/current player의 다음 턴 시작/끝, phase 시작/끝, 전투 끝, round 끝, 영구 | 유이, 히스클리프의 다음 자기 턴 시작까지 효과 같은 기간 | 원문의 공격 면역 의미 전체는 기간과 별개로 구현 필요 |
| 이벤트 | phase/turn/attack/damage/defeat/switch/battle/round 이벤트, 주기 trigger와 카운터 | 턴마다 발동, 피해 반응, 전투 종료 효과 | 원래 이벤트 source identity, 실제 피해 대상/수치의 질의, 출현/획득/영입 창은 부족 |
| 전투 흐름 | 개인 행동, Switch 창, 다음 참가자의 Switch/거절, 몬스터 응답, 도주, 남은 참가자, 전투 보수와 마지막 공격 보너스, 다음 encounter | 기본 전투와 Switch/응답, 스컬 리퍼 후 연속 보스 구성 | 효과가 즉시 도주/승리를 강제하거나 별도 필드를 조작하는 범용 operation은 없음 |
| 동료/스킬/강화 | CompanionId와 카드 효과를 분리, 카드에 upgrade/effect group/modifier 부착, 소유 동료 검사 | 같은 인물의 여러 변형 스킬, 이후 추가 강화 | 원본 캐릭터 덱/손패/사망의 의미를 새 동료 모델에 완전히 대응시키는 정책은 미확정 |
| 트랜잭션 | 명령은 복제 상태에서 처리, 선택 누락·불법 명령·범위 초과 시 commit 하지 않음, revision 확인 | 카드 선택/효과 처리 중 실패 시 일부 비용만 소실되는 문제 방지 | 선택 결과를 받아 같은 명령을 재제출하는 구조. 선택 UI는 별도 |
| 저장 | RNG, ID 증가값, 순서 있는 개인 덱, 모든 카드/강화 부착, modifier source/anchor/발동 수/기간, pending Switch 응답을 명시적으로 저장 | 중단 뒤 같은 전투 시점 재개 | 원문 수치의 확정은 세이브 codec이 보장하지 않음. 현재 팩에 대한 저장 참조 검증은 아래의 별도 절차 |
| 저장과 현재 Spec 연결 | Module의 OnSessionBuilt에서 GameStateSpecValidation으로 로드 상태의 Spec 참조를 확인한 뒤 SaveSession.Track 실행 | 교체된 팩에서 사라진 카드/강화/기간 등 저장 참조를 그대로 사용하지 않도록 검사 | 참조가 존재한다는 사실이 원문 수치/동작의 동일성이나 임의 팩 간 마이그레이션까지 보장하지 않음 |

실행 흐름의 근거는 [CombatRules.cs](../../Assets/_Project/CardGame/Domain/CombatRules.cs)이고, 저장 필드는 [GameState.cs](../../Assets/_Project/CardGame/Data/GameState.cs) 및 [GameStateFormatter.cs](../../Assets/_Project/CardGame/Persistence/GameStateFormatter.cs)에 명시되어 있다.

저장 데이터는 formatter의 구조 검증 뒤에도 현재 실행 팩에 대한 참조 검증을 거친다. [CardGameModule.cs](../../Assets/_Project/CardGame/Composition/CardGameModule.cs)의 `OnSessionBuilt`는 `GameStateSpecValidation.Validate(data.CaptureSnapshot(), specs)`를 먼저 호출하고, 성공한 뒤에만 `saves.Track(data)`를 호출한다. 이 순서를 일반 세이브 형식 검증과 혼동하지 않는다.

## 수치 입력이 없어 보류하는 항목

이 항목들은 원소를 더 구현하더라도 원자료 자체에서 값을 채울 수 없는 문제다.

| 누락/불확정 입력 | 영향 | 처리 |
| --- | --- | --- |
| 원본 카드의 기본 공격력/몬스터 HP/일반 가격/권유도/명성도 | 완전한 카드/시나리오 밸런스 구성 불가 | 별도 원본 수치 자료로 확인. 명시적으로 테스트용인 팩과 출처 확정 팩을 다른 ID로 둠 |
| 번역 시트의 반복 칸 수와 초기 덱 구성의 관계 | 인쇄 수량만으로 덱을 확정할 수 없음 | 공통 룰북의 구성표 또는 원본 구성 자료로 확인 |
| 동료 영입 방식, 기본 슬롯, 스킬 지급/사망 의미 | 원작 캐릭터 카드에서 동료+스킬로 전환할 때 새 게임 정책이 필요 | 카드마다 문자열 분기로 때우지 않고 전환 정책과 SetupDefinition으로 명시 |
| `HP를 2/4/5까지 회복`, β테스터 `그 외에는 공격력 -3` | 효과 숫자는 읽히지만 수식/조건이 불명확 | 원문/공통 룰북 해석을 확정하기 전 자동 변환하지 않음 |
| 로자리아의 `명성도의 한계`, 니시다의 `손패에서 잃는다`, 대장장이의 나머지 카드 목적지 | 비교식/이동 영역/덱 위치가 불명확 | 카탈로그 issue를 유지. 비슷한 다른 카드의 문장으로 대신 확정하지 않음 |

## 숫자와 별도로 아직 필요한 규칙 의미

아래는 현재 모델/operation에 없는 의미다. 원본 공격력과 HP를 받는 것만으로 해결되지 않으며 별도 모듈 및 회귀 테스트가 필요하다.

| 필요한 기능 | 원문 사례 | 현재 한계와 필요한 추가 계약 |
| --- | --- | --- |
| 공유 source deck/광장/필드 상태 | 아르고(p8), 미라쥬 스피어(p3), Poh(p7), 길드 풍림화산 클라인(p8), 필드(p2/9) | GameState에는 개인 deck/hand/discard와 현재 battle만 존재. 캐릭터/장비/레어/필드/몬스터 덱과 광장, 공개 영역, 출현/공략 이력을 별도 상태로 표현해야 함 |
| top-N 공개·선택·나머지 재정렬/분할 | 아르고(p8), 대장장이(p10), 호수의 주인(p6) | Draw/MoveCard는 원문 선택 절차와 덱 위/아래 순서를 표현하지 못함. reveal 결과의 소유권과 순서 선택, 남은 카드 처리 계약 필요 |
| 덱 검색·조건까지 진행·카드 사망 | 크리스탈 라이트 잉곳(p3), 무기점 리즈벳(p8), 우절포정(p1), Poh(p7) | source deck 탐색/사망 영역/빈 덱 종료 정책이 없음. 단순 AddCard는 원본 덱의 유한 재고를 소비하는 검색과 다름 |
| 카드 소유권 이전 | 키바오(p7), 잡화상 에길(p8) | MoveCard는 현재 OwnerId의 영역 사이만 이동. source owner와 destination player를 따로 선택하고 두 소유자의 영역/주체를 원자적으로 갱신해야 함 |
| 장착 대상과 출처를 지정하는 자동 장착 | 리즈벳(p8), 무기점 리즈벳(p8) | 손패 장비와 스킬의 동시 플레이는 존재하나 범용 MoveCard(Equipped)는 EquippedTo를 선택/설정하지 않음. 검색한 무기와 특정 동료/스킬 장착 관계를 명시해야 함 |
| 카드 선택 비용과 선택 수의 후속 사용 | 코밧츠(p7), 어질리티 링(p1), 마스터 스미스 리즈벳(p8), 잉곳(p3) | 현재 SelectOne은 고정 1개 선택이고 resource 비용만 존재. 0..N 선택, 선택적 지불, 성공한 비용/버림 수를 후속 효과에 전달하는 지역 결과가 필요 |
| 최고 HP 동률 전원 | 드렁크 에이프(p5) | 전체 플레이어 집합에서 max(HP)와 동률을 계산하는 selector가 없음. 단일 대상 선택 또는 고정 HP 조건으로 바꾸면 원문과 달라짐 |
| 주체를 구분하는 조건 및 전투 이력 | 아스나/키리토(p7/8), 최종 클라인/에길(p8), 솔로 키리토(p8), 니시다(p7) | 범용 event-source/previous-attacker/이 전투의 companion-play history/defeated-spec history가 없음. 관찰용 소스와 원래 사건 소스를 별도로 보존해야 함 |
| 타인의 사망 직전 긴급 반응 | 영혼의 성창석(p3) | 일반 긴급 카드 사용과 BeforeDefeat 이벤트는 있지만 SelectedPlayer는 생존자만 선택하고 일반 카드 관찰은 소유자 사건 중심. 몬스터 응답 내부의 사망 예정 선택 창을 중단/재개하는 상태가 없으므로 사망 예정 대상 선택, 긴급 카드 소모, 사망 취소와 이후 드로우를 명시해야 함 |
| 피해 대리 수령 | 탱커(p10) | 피해 대상 redirect/replacement가 없음. 자기 회복 또는 상대 피해 0으로만 바꾸는 것은 원문의 대신 피해 받기와 다름 |
| 실제 피해 사건 조건과 소스별 후속 처리 | 스카벤지 토드/이분 플라워(p5), 배교자 니콜라스(p6), 시리카&피나(p7) | 몬스터 응답 batch의 대상별 피해 합산은 구현되었으나, 실제 피해 대상 집합과 각 소스의 피해 성공을 질의하는 명시 계약은 없음. 공격 그룹의 피해는 batch에서 나중에 적용되므로 단순 Damage 다음 ApplyModifier가 원문의 ‘이번 차례 피해를 받은 플레이어에게’를 보장하지 않음. 0 피해/방어/여러 적 공격 후 올바른 대상에게만 후속 효과를 적용해야 함 |
| 기간과 별개인 사용 횟수 | 코트 오브 미드나이트(p1), 실버 아머(p1), 전사/탱커(p10) | 현재 장비 ActivateAbility는 개인 턴당 UsesThisTurn==0 검사. 원문의 source별 전투당/턴당/사건당 budget과 reset 경계를 저작 가능한 제한으로 분리해야 함 |
| 획득/장착/효과 문장 교체 | 다크 리펄서/길티손(p1), 어새신/대장장이/잡화점(p10) | 행동 금지 modifier 일부는 있으나 획득 경로 검사→덱 아래 반환, 원문 텍스트 무효화, 획득 draw 대체, 특정 아이템의 대상 확대 같은 replacement를 일반적으로 표현하지 못함 |
| 즉시 도주/선택 필드 공략/보상 생략 | 전이 결정(p4), 호수의 주인(p6), 코랄/주인 있는 호수(p9) | 플레이어 도주 명령과 encounter 종료/후속 전투는 있으나 효과 자체가 도주 성공/별도 필드 공략/보상 생략을 요청하는 operation은 없음 |
| 광장 출현/강제 영입 창 | 로자리아/크라딜/Poh/그림록(p7) | 기존 phase/card/battle 이벤트로는 광장 출현 이후 최초 일치 행위 또는 종료 드로우 후 비교/강제 영입을 완전히 표현할 수 없음 |

조건/선택 범위는 [EffectRules.cs](../../Assets/_Project/CardGame/Domain/EffectRules.cs)의 `Targets`, `SelectOne`, `Condition`, `MoveCard`에서 확인할 수 있다. 사건 source와 source별 기간 처리는 [ModifierRules.cs](../../Assets/_Project/CardGame/Domain/ModifierRules.cs)의 `Emit`, `EventApplies`, `DurationBoundary`가 기준이다. 단계적 확장을 하더라도 기존 원문 레코드를 실행 준비 상태로 자동 승격하지 않는다.

## 다음 확장에서 지켜야 할 경계

1. 공개 덱/광장/필드, 선택 비용/결과, 전투 사건 이력, replacement/usage budget은 서로 다른 저작/상태 계약으로 추가한다. 이 추가 상태도 GameState clone/Validate/명시적 세이브 슬롯과 함께 변경한다.
2. 카드별 C# `if (name == ...)`를 추가하지 않는다. SourceRecordId는 근거 연결용이고 Domain의 분기 기준은 검증된 operation과 selector 등이다.
3. 새로운 primitive마다 대표 원문과 반례를 같이 검증한다. 예: 최고 HP 동률, 소인원 이웃 중복, 방어 후 0 피해, 빈 덱 검색 종료, source가 제외된 뒤 기간 유지, 재개 후 비용/카운터 중복 적용 방지.
4. 알 수 없는 수치/불명확한 번역은 명시적 미확정 자료로 둔다. 기본값이 필요한 테스트는 테스트 수치라는 출처를 함께 둔다.

## 검증 결과의 보고 범위

이 문서는 코드/원문 대응 검토이며 Unity 컴파일·테스트 통과 로그를 대신하지 않는다. [GamePersistenceTests.cs](../../Assets/_Project/CardGame/Tests/GamePersistenceTests.cs)는 모든 저장 필드, 실제 Switch/기간 재개, 손상된 payload/미지원 schema/read 실패의 보존을 검증하도록 작성되어 있다. 최종 컴파일/테스트 결과는 작업의 실제 uLoop 결과와 구분하여 보고한다. 일반 규칙 테스트가 통과해도 이 문서의 미지원 원문 의미가 자동으로 구현되는 것은 아니다.
