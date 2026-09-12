# 구현된 BG 테이블 열

모든 행의 `key`는 BG entity Name이다. `relation`/`relations`는 BG의 실제 관계 필드이며 런타임에는 키 값만 전달한다. enum은 숫자가 아닌 정확한 C# 이름으로 작성한다. `sourceCards`는 출처 기록으로 실행용 데이터가 아니다.

| 테이블 | 열 |
| --- | --- |
| catalog | `schemaVersion`: int, `description`: string, `sourceComplete`: bool |
| rules | `initialPhaseId`: relation→phases, `normalDrawCount`: int, `soloDrawCount`: int, `cardActionsPerTurn`: int, `itemActionsPerTurn`: int, `maxResolutionSteps`: int, `combatPhaseId`: relation→phases, `restHeal`: int, `restWithExileHeal`: int |
| phases | `kind`: string, `nextPhaseId`: relation→phases, `enterGroupId`: relation→groups, `exitGroupId`: relation→groups |
| actors | `maxHp`: int, `baseAttack`: int, `team`: string, `tags`: strings |
| companions | `displayName`: string, `tags`: strings |
| cards | `kind`: string, `attack`: int, `effectGroupId`: relation→groups, `companionId`: relation→companions, `cost`: int, `resourceKey`: string, `canSwitch`: bool, `tags`: strings, `attackValueId`: relation→values, `allowedPhases`: strings, `isEmergency`: bool |
| groups |  |
| effects | `groupId`: relation→groups, `order`: int, `operation`: string, `targetId`: relation→selectors, `amount`: int, `valueId`: relation→values, `conditionId`: relation→conditions, `referenceId`: string, `resourceKey`: string, `destination`: string, `trigger`: string |
| selectors | `targets`: strings, `deduplicateTargets`: bool, `requiredTag`: string, `team`: string |
| values | `kind`: string, `amount`: int, `referenceId`: string, `multiplier`: int, `divisor`: int |
| conditions | `kind`: string, `amount`: int, `referenceId`: string, `children`: relations→conditions, `eventKind`: string |
| durations | `kind`: string, `occurrences`: int, `phaseId`: relation→phases, `anchor`: string |
| modifiers | `kind`: string, `durationId`: relation→durations, `stat`: string, `operation`: string, `amount`: int, `effectGroupId`: relation→groups, `trigger`: string, `everyOccurrences`: int, `conditionId`: relation→conditions, `stackPolicy`: string, `maxStacks`: int, `blockedAction`: string, `tags`: strings, `priority`: int |
| upgrades | `effectGroupId`: relation→groups, `modifierId`: relation→modifiers, `requiredCardTag`: string, `companionId`: relation→companions, `attackBonus`: int, `maxAttachments`: int |
| monsters | `maxHp`: int, `attackGroupId`: relation→groups, `attack`: int, `rewardId`: relation→rewards, `finishRewardId`: relation→rewards, `tags`: strings, `repeatable`: bool, `maxHpValueId`: relation→values |
| encounters | `monsterIds`: relations→monsters, `rewardId`: relation→rewards, `nextEncounterId`: relation→encounters, `victoryOnClear`: bool, `enterGroupId`: relation→groups |
| rewards | `effectGroupId`: relation→groups |
| deckEntries | `deckId`: string, `cardId`: relation→cards, `count`: int, `order`: int |
| scenarios | `actorIds`: relations→actors, `deckId`: string, `encounterId`: relation→encounters, `companionIds`: relations→companions, `isSolo`: bool |
| sourceCards | `title`: string, `kind`: string, `page`: int, `abilityText`: string, `unknownStats`: strings, `issueIds`: strings, `executableReady`: bool |

작성 원본은 `Assets/_Project/Resources/bansheegz_database.bytes`다. `Window/BGDatabase` 또는 `Tools/Project16/Open Card Game Specs (BGDatabase)`의 Database 탭에서 행을 수정하고 Save한다. `Tools/Project16/Validate Saved Card Game Specs`는 저장된 bytes를 독립 repo로 읽어 전체 실행 규칙을 검증한다. 게임 모듈은 같은 에셋을 참조하며 다음 App Scope 생성 때 새 값을 읽는다. BG 창의 미저장 값이나 플레이 중 수정은 이미 구성한 Spec snapshot에 반영되지 않는다.

`Assets/_Project/SpecAuthoring/CardGame.tables.json`은 초기 샘플/테스트 seed다. `Tools/Project16/Create Missing Card Game Specs from JSON`은 DB가 없을 때만 검증 후 생성하며, 기존 BG 파일에는 쓰지 않는다. BG 편집과 JSON 사이의 자동 동기화는 없다. BG 창은 자체 전역 repo로 작성하지만 게임 Data/Domain/Application은 계속 독립 repo에서 복사한 불변 Spec만 사용한다.

샘플: `sleep`과 `deep-sleep`은 효과의 duration 참조만 달라 시전자 다음/두 번째 턴 시작까지 공격을 막는다. `add-regeneration`, `add-team-protection`은 기존 카드에 별도 효과 그룹을 부착한다. `regeneration-every-two`는 `TurnStart` 매 두 번째 일치 사건마다 회복한다. 이 카드들은 엔진 검증용 모바일 프로토타입 정의이며 원문 카드 전체의 확정 밸런스가 아니다.
