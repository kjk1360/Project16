# Project16 작업 지침

## 작업 기준

- 이 디렉터리가 Unity 프로젝트 루트다. Unity 버전은 `ProjectSettings/ProjectVersion.txt`, 설치 패키지는 `Packages/manifest.json`과 `Packages/packages-lock.json`을 기준으로 확인한다.
- KDI 관련 구현·리뷰·진단을 시작할 때 반드시 `.agents/skills/kdi-kylin-soul-injector/SKILL.md`를 읽고 적용한다. 그 스킬의 `references/kdi-contract.md`를 함께 읽으며, 실제 설치된 런타임 소스가 문서와 스킬의 기준 자료보다 우선한다.
- 프로젝트 전용 스킬은 `.agents/skills`에 있다. Unity 제어에는 설치된 uLoop CLI와 해당 명령의 `uloop-*` 스킬을 사용한다.
- 사용자가 작업 중인 씬, 프리팹, 에셋, 설정 변경을 보존한다. 조회 목적으로 씬을 저장하거나 Play Mode를 재시작하지 않는다.

## KDI 구성

- 설치 대상은 KDI, KDI-Subscribable, KDI-Layered, KDI-MessagePack, KDI-ViewAssets, KDI-ViewAssets-Addressables, KDI-ViewAssets-Layered다.
- 사용자 결정에 따라 `KDI-Skill-Manifestation`은 제외한다. 별도 요청 없이 추가하지 않는다.
- KDI 패키지는 Git commit으로 고정되어 있다. `C:/KDI`의 개발 저장소를 수정해도 이 소비 프로젝트에 자동 반영되지 않는다. 버전 변경은 명시적으로 수행하고 의존 패키지 호환성을 확인한다.
- `View → ViewModel → ApplicationService → DomainService → Data` 방향, Scope 소유권, 생성·해제 경계는 인젝터와 실제 KDI 계약을 따른다.
- MessagePack 옵션은 애플리케이션이 소유한 옵션에 `WithKDI()`로 구성한다. 패키지 설치만을 이유로 전역 직렬화 옵션을 바꾸지 않는다.

## View Assets와 Addressables

- 정책: `Assets/_Project/Settings/AddressablesViewAssetPolicy.asset`.
- KDI View 프리팹 관리 폴더: `Assets/_Project/AddressableResources/Views`.
- 생성 코드: `Assets/_Project/Generated/ViewAssets/ProjectViewAssets.g.cs`. 이 파일은 직접 수정하지 않고 정책의 Reconcile로 생성한다.
- 관리 폴더 `Assets/_Project/AddressableResources/Views`에는 패키지의 `KdiView<TArgs>` 프리팹 계약을 만족하는 에셋을 둔다. 일반 이미지·오디오와 Localization 에셋까지 규칙 범위를 넓히지 않는다.
- 구체적 View/Args 타입은 생성 매니페스트 어셈블리 `Project16.ViewAssets`가 참조할 수 있는 어셈블리에 둔다. 사용자 정의 asmdef에서 기본 `Assembly-CSharp`를 참조할 수 없으므로, 타입을 추가할 때 같은 View 어셈블리 또는 명시적 어셈블리 참조를 함께 구성한다.
- 그룹·라벨·주소 변경은 `Tools/Kylin/KDI View Assets/Addressables Reconcile`의 Preview 결과를 확인한 뒤 Apply한다. 파일 임포트만으로 자동 적용되지 않는다.
- Unity Auto Group Generator를 이 정책의 관리 항목에 함께 적용하지 않는다. 그룹·주소·라벨의 기준은 KDI 정책이다.
- 기존 그룹 템플릿이나 Localization 설정을 임의로 재작성하지 않는다.

## 게임 기반과 데이터

- 기반 구현은 `Assets/_Project/Foundation`, 진입 씬은 `Assets/_Project/Scenes/FoundationBoot.unity`다. 사용법·소유권은 `Docs/Foundation.md`, BGDatabase 판단은 `Docs/BGDatabaseAssessment.md`를 따른다.
- Scope는 App → LocalSession → Scene → 필요한 Screen/Feature 수명으로 구성한다. `FoundationModule`과 `ScopeHandle`은 구성 경계이며 비즈니스 서비스에 주입하거나 모듈 에셋에 live Scope/서비스를 보관하지 않는다.
- 새 UserData 단위는 안정적인 key/schema, detached MessagePack snapshot, `UserDataUnit<T>` 및 typed Domain owner로 추가한다. 실제 mutation 시 MarkDirty하고 성공한 저장 revision만 ack한다. 저장 key/기존 wire schema 변경에는 명시적인 migration을 함께 구현한다.
- 저장은 `LocalSaveSession`을 사용하며 앱 소유 serializer 옵션을 유지한다. Scope 폐기 전에 자식 작업을 종료하고 저장한다. 손상·미지원 버전의 로컬 파일을 기본값으로 덮어쓰지 않는다.
- BGDatabase는 Spec 작성 공급자로 유지한다. `BgSpecSource`의 독립 repo에서 불변 `ISpecTable<T> : IDataLayer`로 변환한다. 게임 레이어에서 `BGRepo.I`나 BG 가변 row/field를 직접 사용하지 않는다. relation은 키 값으로 전달한다.
- Foundation 컴파일 및 EditMode 테스트: `uloop run-tests --test-mode EditMode --filter-type assembly --filter-value Project16.Foundation.Tests --unsaved-changes fail`. PlayMode 수명 검증은 `Project16.Foundation.PlayModeTests` 어셈블리로 별도 실행한다.

## 카드게임 규칙 기반

- 구현은 `Assets/_Project/CardGame`, 설계는 `Docs/GameDesign/SpecTableDesign.md`, 지원 범위는 `Docs/GameDesign/SourceEffectCoverage.md`를 따른다. Data/Domain/Application 코어는 UnityEngine을 참조하지 않는다.
- BG 작성 원본은 `Assets/_Project/Resources/bansheegz_database.bytes`다. `Window/BGDatabase`에서 편집·Save 후 `Tools/Project16/Validate Saved Card Game Specs`로 검증한다. 기본 BG Resources 로더가 찾는 파일명과 위치, 기존 `.meta` GUID를 보존한다. 런타임은 이 에셋을 독립 repo로 읽는다.
- `Assets/_Project/SpecAuthoring/CardGame.tables.json`은 초기 샘플/테스트 seed다. `Tools/Project16/Create Missing Card Game Specs from JSON`은 파일이 없을 때만 생성한다. 기존 BG 작성본을 이 seed로 덮어쓰거나 자동 동기화하지 않는다.
- `sourceCards`는 원문 근거다. 번역 시트에 없는 공격력/가격/HP를 추정하여 정식 Spec으로 승격하지 않는다. 현재 실행 데이터는 `prototype-balance`로 구별한다.
- 카드 이름/ID별 Domain 분기 대신 EffectOperation/Target/Value/Condition/Duration/Modifier/Upgrade를 조합한다. 새로운 의미는 primitive와 테스트를 추가한다. enum만 선언하고 처리하지 않는 효과를 유효하다고 받아들이지 않는다.
- `GameRulesDomain`만 GameData를 Commit한다. 명령은 detached 상태에서 실행하고 선택 누락/거절 시 RNG·비용·카운터를 포함한 전체 상태가 불변이어야 한다. 지속시간은 전체 라운드와 개인턴의 기준 identity를 구분한다.
- GameState 필드를 추가하면 Clone/Validate/GameSnapshot/명시적 GameStateFormatter와 세이브 호환성 테스트를 함께 갱신한다. 기존 wire 슬롯의 의미를 바꾸지 않는다.
- 테스트: `uloop run-tests --test-mode EditMode --filter-type assembly --filter-value Project16.CardGame.Tests --unsaved-changes fail`.

## 공개 저장소

- 원격은 `kjk1360/Project16`이며 KDI 패키지의 기존 ToolStorage URL은 유지한다.
- `.gitignore`의 BGDatabase, NuGet 복원 폴더, 원문 룰북·카드 전문, `.uloop` 제외 규칙을 유지한다. 의존성 복원과 공개 데이터 구성은 `README.md`, `Docs/RepositorySetup.md`를 따른다.
- 공개 `CardGame.tables.json`의 `sourceCards.rows`는 비어 있어야 한다. 로컬 원문 포함 백업을 그대로 복사하거나 그 자료로 생성한 binary를 공개 커밋에 넣지 않는다.

## 검증 명령과 기준

- CLI가 다른 Unity 인스턴스를 제어하지 않도록 프로젝트 루트에서 실행하거나 `--project-path`를 명시한다.
- C# 또는 패키지 변경 후 일반 `uloop compile`을 실행한다. 특별한 근거 없이 `--force-recompile`을 사용하지 않는다.
- 콘솔 확인: `uloop get-logs --log-type Error --max-count 20`.
- KDI 구조 변경 시 `python .agents/skills/kdi-kylin-soul-injector/scripts/audit_kdi_architecture.py .`를 실행한다. 결과는 advisory이며 UNKNOWN은 검토한다. Python 3 실행 경로는 현재 환경에서 확인한다.
- View Assets 정책/관리 프리팹/생성 파일 변경 후 `Kylin.DI.ViewAssets.Addressables.Editor.AddressablesViewAssetCi.Validate()`를 Unity Editor에서 실행한다.
- 테스트는 변경된 계약과 수명 경계에 맞게 실행한다. uLoop 테스트 명령은 동시에 실행하지 않으며, 저장되지 않은 사용자 변경을 보호하려면 `--unsaved-changes fail`을 사용한다.
- 컴파일, 분석기, 정책 검증, 런타임 테스트, 플레이어 빌드 결과를 구분하여 보고한다. 실행하지 않은 검증을 통과했다고 표현하지 않는다.

프로젝트 구성과 설치 출처는 `Docs/ProjectSetup.md`를 참고한다.
