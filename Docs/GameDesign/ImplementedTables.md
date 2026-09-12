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

작성본: `Assets/_Project/SpecAuthoring/CardGame.tables.json`. Unity 메뉴 `Tools/Project16/Import Card Game Specs (JSON to BG)`는 전체를 검증한 뒤 `Assets/_Project/Resources/Project16/CardGameSpecs.bytes`만 갱신한다. BGDatabase 전역 repo나 기존 벤더 자료는 변경하지 않는다. JSON을 다시 임포트하면 이 생성 bytes에 한 직접 수정은 덮어쓰므로 작성 기준은 JSON으로 통일한다. 향후 BG 에디터를 작성 기준으로 정할 때는 임포트 메뉴를 사용하지 않고 동일 스키마의 BG bytes를 모듈에 지정한다.

샘플: `sleep`과 `deep-sleep`은 효과의 duration 참조만 달라 시전자 다음/두 번째 턴 시작까지 공격을 막는다. `add-regeneration`, `add-team-protection`은 기존 카드에 별도 효과 그룹을 부착한다. `regeneration-every-two`는 `TurnStart` 매 두 번째 일치 사건마다 회복한다. 이 카드들은 엔진 검증용 모바일 프로토타입 정의이며 원문 카드 전체의 확정 밸런스가 아니다.
