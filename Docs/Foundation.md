# Project16 게임 개발 기반

## 시작

`Assets/_Project/Scenes/FoundationBoot.unity`를 열어 Play한다. 씬에는 `Starter`만 있으며 `SceneStarter`가 앱을 한 번 부팅하고 자신의 SceneScope를 만든다. 런타임 `GameStarter`는 씬 전환 후에도 유지된다. 다른 씬을 직접 실행하려면 `Assets/_Project/Prefabs/Composition/SceneStarter.prefab`을 씬에 배치한다.

설정은 `Assets/_Project/Resources/Project16/FoundationSettings.asset`이다. 기본 저장 간격은 30초, 로컬 프로필은 `default`다. Inspector의 Modules에는 프로젝트용 `FoundationModule` 에셋을 등록한다. 에셋이 없는 복사본은 `Tools > Project16 > Create Foundation Assets`로 생성할 수 있다. 이 메뉴는 기존 에셋·씬을 덮어쓰지 않는다.

Boot 씬은 부팅 검증과 새 게임 구현의 시작점이다. 특정 장르의 맵이나 메뉴로 자동 이동하지 않는다. 기존 SampleScene과 카메라·조명, Localization 및 Addressables 설정은 유지한다.

## 소유권과 등록

| 타입/대상 | 레이어 | 소유 Scope/소유자 | 생성 | 종료 |
| --- | --- | --- | --- | --- |
| GameStarter / GameRuntime | Unity 및 구성 경계 | 앱 | SceneStarter의 idempotent 부팅 | 앱 종료/host 파괴 |
| FoundationModule / Settings | 구성 입력 | 프로젝트 에셋 | Unity asset load | Unity |
| App 서비스 / ISpecTable<T> | 서비스별 해당 레이어 / Data | ApplicationScope | ConfigureApplication의 Bind | 앱 종료; Spec 값에는 별도 자원 없음 |
| UserPreferencesData | Data | LocalSessionScope | 로드된 snapshot을 FromFactory로 생성 | Scope Dispose |
| UserPreferencesDomain | DomainService<UserPreferencesData> | LocalSessionScope | KDI To | Scope Dispose |
| UserPreferencesApplication | ApplicationService | LocalSessionScope | KDI To | Scope Dispose |
| UserPreferencesViewModel | ViewModel | 사용할 화면의 Scope | 화면에서 Bind | 화면 종료 |
| LocalSaveSession / LocalFileSaveStore | 저장 인프라 | GameRuntime의 세션 소유 | 구성 경계에서 직접 생성 | 최종 flush 후 Abandon |
| ScopeHandle / ScopeCancellation | 구성 / 수명 인프라 | Scene·Screen·Feature | 부모 Build 후 생성 | 명시 종료 및 부모 cascade |
| BgSpecSource | 구성 인프라 | 로딩 코드의 using | 독립 BG 저장소 로드 | 매핑 후 Dispose |

`GameRuntime`, `ScopeHandle`, `FoundationModule`은 게임 서비스에 주입하지 않는다. 이들이 가진 Resolve 권한은 등록·진입·계층 주입에만 사용한다. 비즈니스 의존성은 `View → ViewModel → ApplicationService → DomainService → Data`의 private `[Inject]` 필드로 연결한다.

모듈 훅의 순서:

1. `ConfigureApplication(builder)` 후 ApplicationScope Build.
2. `ConfigureSession(builder, saves)`에서 저장 snapshot 로드 및 등록. 모든 등록 후 LocalSessionScope Build.
3. `OnSessionBuilt(scope, saves)`에서 해당 Data를 Resolve하고 저장 추적에 등록. 이 훅은 구성 경계이며 live Scope나 Data를 ScriptableObject 필드에 보관하지 않는다.
4. 씬 진입 때 `ConfigureScene(builder, sceneName)` 및 SceneStarter의 Configure 후 SceneScope Build.
5. 독립 상태가 필요한 화면은 `sceneScope.OpenChild(name, configure)`로 생성하고 종료 시 Dispose.

같은 Scope의 중복 Bind는 오류다. 부모의 Scoped 등록은 자식이 공유한다. 자식마다 독립 인스턴스가 필요하면 해당 자식에서 Bind한다. Singleton은 App root에서만 등록한다. 레이어 개수나 Canvas 개수로 Scope를 만들지 않는다.

ScopeHandle은 종료 시작에 Token을 취소하고, 자식을 역순 종료한 뒤 자신을 Dispose한다. 취소 요청 자체로 모든 비동기 작업이 끝나는 것은 아니므로 게임 서비스도 await 이후 취소/수명 확인을 해야 한다. 저장 세션을 종료할 때는 Scene·Feature를 먼저 종료해 UI 정리 중 발생한 마지막 변경까지 반영하고, 저장한 뒤 Session과 App을 정리한다. `TryEndSession()`의 저장 실패는 세션을 유지해 재시도할 수 있도록 한다. 이미 닫힌 Scene은 재진입할 때 다시 생성한다.

씬에 배치한 LifetimeScope는 GameRuntime의 C# Scope를 자동으로 부모로 찾지 않는다. 그 부모를 연결하는 public API도 설치본에는 없다. 씬 배치에는 SceneStarter의 주입 루트를 사용한다. LifetimeScope가 들어 있는 일반 프리팹은 해당 Scope의 IInstantiator로 생성하면 런타임 부모가 준비된다.

## 로컬 UserData

파일 위치는 `Application.persistentDataPath/UserData/<profile>/<key>.mpk`다. 각 key가 저장 단위다. `preferences`는 음악/효과음 볼륨과 언어 식별자를 가진 작동 예제이며, Audio/Localization 시스템에 값을 적용하는 제품 기능까지 포함하지 않는다.

저장 경로:

`Domain의 OwnerOnly 변경 → Data.MarkDirty → immutable MessagePack snapshot → temp Flush(true) → atomic replace → 해당 revision ack`

- interval은 timeScale과 무관한 unscaled 시간이다. 기본 30초이며 누적된 여러 interval은 저장 한 번으로 합친다.
- `OnApplicationPause(true)`, `OnApplicationFocus(false)`, 정상 종료 및 host 파괴에서 dirty 단위만 저장한다.
- 저장은 생성 스레드에서 동기로 실행한다. 주기와 종료 저장이 겹쳐 파일 기록이 경쟁하지 않으며 재진입도 차단한다. 큰 데이터에서 프레임 지연이 생기면 메인 스레드 snapshot 후 작업 큐를 도입하되 revision ack와 종료 drain 계약을 유지해야 한다.
- 변경 없는 setter는 dirty를 만들지 않는다. 예제 Data는 변경 직전에 MarkDirty하여 Reaction의 지연 알림 여부에 저장 추적이 영향받지 않게 한다.
- 새 단위는 기본값을 dirty로 시작한다. 저장 성공 후에만 깨끗해진다. 저장 중 더 큰 revision이 생기면 다음 저장 대상으로 남는다.
- 한 단위의 실패는 다른 단위 저장을 막지 않는다. 실패한 단위는 dirty 유지, 오류는 SaveReport에 key와 예외로 남는다.
- envelope에 형식 버전·단위 key·schema·revision·payload와 checksum이 있다. checksum은 비밀키 없는 손상 검출이며 변조 방지나 암호화가 아니다.
- 기존 정상 파일은 `.mpk.bak`에 보존한다. primary 손상 시 검증된 backup을 읽고 복구 저장 대상으로 표시한다. 복구 첫 저장은 손상 primary로 정상 backup을 덮어쓰지 않는다.
- 미래 schema, 지원하지 않는 과거 schema, migration 실패, 두 파일 모두 손상, 읽기 권한 오류는 부팅 실패로 처리한다. 기본값으로 조용히 덮어쓰지 않는다. 실제 서비스의 복구 UI/사용자 선택은 게임에서 추가한다.
- `Load<T>(..., migration)`으로 이전 payload를 현재 snapshot으로 변환한다. migration은 전달된 이전 버전을 명시적으로 검사하고 지원하지 않으면 실패시킨다.
- 원자성은 단위 파일 하나에만 적용된다. 재화 차감과 구매 결과처럼 함께 커밋되어야 하는 값은 같은 저장 단위에 둔다.
- 현재 파일 최대 크기는 envelope 포함 8 MiB다. 대상 파일시스템이 `File.Replace`와 durable flush를 지원해야 한다. 미지원 시 파일을 삭제해 대체하는 방식으로 우회하지 않고 오류를 반환한다.
- OS 강제 종료·크래시·배터리 소진에는 종료 callback이 보장되지 않는다. 주기 저장이 마지막 성공 이후 손실 범위를 줄이며, 구매 완료 등 중요한 경계에서는 구성 측의 명시적 저장을 추가할 수 있다. WebGL의 브라우저 영속화 어댑터는 아직 없다.

새 저장 단위 추가:

1. detached snapshot과 MessagePack formatter를 만든다. 예제는 명시적 formatter를 사용한다. IL2CPP에서는 게임이 추가한 DTO/formatter와 KDI generic formatter의 AOT 등록을 검증한다.
2. `UserDataUnit<TSnapshot>`을 상속한다. 생성자는 `SaveLoadResult<TSnapshot>`의 Snapshot으로 초기화한다. 실제 변경 메서드에 `[OwnerOnly]`를 붙이고 MarkDirty를 호출한다. `CaptureSnapshot`은 서비스·Unity 오브젝트·변경 가능한 공유 참조를 반환하지 않는다.
3. `IDomainServiceLayer<해당Data>`에서만 mutation을 수행한다. 외부에는 읽기 전용 Data 인터페이스를 노출한다.
4. ConfigureSession에서 `saves.Load` 후 `FromFactory(() => new MyData(loaded))`로 Bind한다. OnSessionBuilt에서 `saves.Track(scope.Resolve<MyData>())`를 호출한다.
5. 변경·재로드·부분 실패·migration 테스트를 추가한다. Key는 배포 후 변경하지 않는다.

KDI-Subscribable의 컬렉션 변경 알림은 중첩 객체 내부 변경을 자동으로 추적하지 않는다. 명시적 mutation 경계의 MarkDirty를 기본으로 사용한다. 직접 mutable property/collection을 외부에 노출하지 않는다.

MessagePack 옵션은 LocalSaveSession이 소유하며 WithKDI를 적용한다. `MessagePackSerializer.DefaultOptions`는 변경하지 않는다. 서버 sync, 전역 dirty bus, 게임별 데이터 registry 조회는 추가하지 않았다.

## Spec Tables

BGDatabase 1.9.5는 유지한다. 상세 소스 판단은 [BGDatabaseAssessment.md](BGDatabaseAssessment.md)를 따른다. BG 에셋을 `BgSpecSource.Load`로 읽고, 필요한 테이블을 `readonly struct : ISpecRow`로 매핑하고 검증한다. source를 닫은 뒤 결과를 AppScope의 `ISpecTable<T>`로 Bind한다. 테이블 간 relation은 키 값으로 변환하며 Domain에서 필요한 테이블들을 각각 주입받는다.

기본 Foundation assembly에는 BGDatabase DLL 참조가 없다. BG 접근은 별도 `Project16.Foundation.BGDatabase` assembly의 구성 어댑터에 있다. 사업 코드에서 BGRepo.I, BGEntity, BG mutable field를 직접 사용하지 않는다. 실제 기획 스키마가 없으므로 제품용 Spec DB 파일이나 아이템·재화 테이블은 아직 만들지 않았다. SO 공급자로 바꾸어도 동일한 SpecTable 계약을 사용할 수 있다.

## View Assets 확장

기존 `ProjectViewAssets` 매니페스트는 아직 비어 있다. 첫 View를 구현할 때 View/Args를 참조 가능한 assembly에 만들고 정책 Reconcile의 Preview/Apply로 생성한다. 해당 View Host가 속한 Scope의 Configure에서 `builder.AddViewAssets(host, catalog)`를 호출하고, Build 후 host를 그 Scope로 주입한다.

관리 View 프리팹 내부에는 LifetimeScope를 넣지 않는다. 풀의 장기 주입과 화면마다 달라지는 ViewModel 수명을 분리하고 per-rent ViewModel은 Args로 전달한다. ViewLease 종료는 ViewModel Dispose를 대신하지 않는다. 현재 기반은 장르별 UI나 빈 Canvas/팝업 매니저를 임의로 생성하지 않는다.

## 검증

일반 `uloop compile`, `uloop run-tests --test-mode EditMode --filter-type assembly --filter-value Project16.Foundation.Tests --unsaved-changes fail`, 구조 감사를 사용한다. `.kdi-audit.json`은 실제 구성 경계만 지정하며 차단 정책을 추가하지 않는다. 테스트는 임시 폴더와 독립 BG 저장소를 사용한다.

최종 실행 결과는 `Docs/FoundationValidation.md`에 기록한다. 플레이어 빌드, 모바일 실제 background/강제종료, 특정 게임 데이터의 스키마·성능은 별도의 검증 대상이다.
