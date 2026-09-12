# BGDatabase와 KDI Spec Tables

BGDatabase는 유지한다. 설치된 구현은 독립 `BGRepo`를 지원하므로 전역 데이터 접근을 게임 코드에 확산하지 않고, KDI의 읽기 전용 Data 계약으로 연결할 수 있다. 현재 프로젝트에서 작성된 BG 데이터 파일은 발견되지 않았으며, 게임 테이블이나 예시 에셋을 임의로 만들지 않았다.

## 확인한 설치본

- 실행 DLL: `Assets/BansheeGz/BGDatabase/Scripts/BGDatabase.dll`.
- DLL metadata/reflection으로 `BGRepo.Version == "1.9.5"`, `BGRepo` 생성자와 읽기 API를 확인했다. DLL의 assembly version은 `1.0.0.0`이므로 제품 버전과 혼동하지 않는다.
- 함께 배포된 `BGDatabaseSourceCode.unitypackage`의 런타임 소스는 제품 버전 `1.9.5`, 빌드 `2026.06.18`이다. `readme.txt`의 `1.8.x` 문구보다 DLL과 소스를 우선했다.
- 소스 아카이브는 시스템 임시 디렉터리에만 풀어 검토했다. Unity에 import하거나 공급자 파일을 수정하지 않았다.
- DLL의 직접 assembly 참조는 `netstandard 2.1.0.0`, `UnityEngine`이다. 게임의 KDI 레이어 타입에 의존하지 않는다.

아래 소스 경로는 아카이브 안의 `Assets/BansheeGz/BGDatabase/Scripts/` 이후 경로다.

| 설치 소스 | 확인한 계약 |
| --- | --- |
| `Database/Repo/BGRepo.cs:26` | 제품 버전 1.9.5. |
| `Database/Repo/BGRepo.cs:35` | `BGRepo.I`는 필요하면 기본 DB를 로드하는 전역 편의 접근자. 사용이 필수인 API는 아니다. |
| `Database/Repo/BGRepo.cs:167` | `new BGRepo()`, `new BGRepo(byte[])`, `new BGRepo(string)`, 복제 생성자 지원. |
| `Database/Repo/BGRepo.cs:320` | 인스턴스 `Load(byte[])`, `Load(string)` 지원. `Save()`는 351행, `GetMeta(string)`는 535행, `Clear()`는 952행. |
| `Database/Repo/Binary/BGRepoBinary.cs:21` | `new BGRepoBinary().Read(bytes)`로 독립 repo를 직접 읽을 수 있다. 이 어댑터가 쓰는 경로다. |
| `Database/Repo/Binary/V8/BGRepoBinaryV8.cs:31` | reader가 새 repo를 생성한다. |
| `Database/Core/Objects/BGEntity.cs:212` | `Get<T>(BGField)`가 repo, table, 값 타입을 검사한다. `Get<T>(string)`는 229행. |
| `Database/Field/Relation/BGFieldRelationSA.cs:20` | relation의 대상 table을 `Meta.Repo.GetMeta(toId)`로 찾는다. 전역 repo를 요구하지 않는다. |
| `Database/Field/Relation/BGFieldRelationSingle.cs:64` | 관련 row는 해당 repo의 대상 table에서 ID로 찾는다. |

공식 [BGDatabase 안내](https://www.bansheegz.com/BGDatabase/)도 Unity용 메모리 DB 및 relation 기능을 설명한다. 이번 설계 판단과 API 선택은 설치본을 기준으로 했다.

## 구현 경계

```text
작성된 BG 데이터 bytes / TextAsset
    → BgSpecSource의 독립 BGRepo
    → 명시적 row mapper + 스키마/참조 검증
    → SpecTable<T> : ISpecTable<T> : IDataLayer
    → Domain / Application / ViewModel의 [Inject] 필드
```

- `Assets/_Project/Foundation/Specs/`에는 공급자에 독립적인 `ISpecRow`, `ISpecTable<T>`, `SpecTable<T>`가 있다. `ISpecTable<T>`는 `IDataLayer`이며 테이블 조회만 제공한다.
- `Assets/_Project/Foundation/BGDatabase/`는 별도 assembly로 격리한 composition 어댑터다. `BGDatabase.dll`을 이 assembly에서 명시적으로 참조한다. 기반 `Project16.Foundation` assembly는 BG 타입을 참조하지 않는다.
- `BgSpecSource.Load(byte[])`와 `Load(TextAsset)`는 기본 repo나 전역 `BGRepo.Reader`를 설정하지 않는다. 인스턴스 reader를 호출하고 private repo를 보유한다. 기본 repo의 addon 활성화 경로도 호출하지 않는다.
- `ReadTable<T>(tableName, map)`는 모든 row를 매핑하고 키 검증이 끝난 후 snapshot 하나를 반환한다. 중복 키와 공백 키는 실패한다. 키 비교는 ordinal, 대소문자를 구분한다.
- `BgSpecRowReader`는 문자열, 정수, 실수, bool 및 필수 단일 relation의 대상 키를 읽는다. public API로 `BGRepo`, `BGEntity`, `BGField`, Unity asset 참조를 반환하지 않는다.
- `ReadRequiredRelationKey`는 대상 존재, 예상 table, 같은 repo, 비어 있지 않은 키를 검증한다. 대상 키에 Name을 사용할 수도 있고 명시적인 문자열 key field를 지정할 수도 있다. 참조되는 테이블도 함께 매핑하여 그 테이블의 키 중복 검증을 수행한다.
- source를 Dispose하면 private repo가 정리된다. 완성된 snapshots는 값을 복사했으므로 이후에도 사용할 수 있다. 보관한 mapper reader를 Dispose 이후 사용하면 예외가 발생한다.

`ISpecRow`는 `readonly struct`로 구현하고 문자열/숫자/값 식별자처럼 불변인 값만 보관한다. `where T : struct`는 행을 값으로 전달하지만, 구조체 내부의 mutable list까지 자동으로 복제하지는 않는다. mutable collection, `BgSpecRowReader`, BG row, Unity object, 다른 Data 서비스는 행에 넣지 않는 것이 이 계약이다. 이 규칙을 넘는 배열/중첩 데이터가 필요하면 별도의 불변 값 타입과 복사 규칙을 먼저 정의한다.

## Scope와 Bind

Specs가 앱 실행 동안 고정이면 AppScope에 바인딩한다. 소스 읽기와 mapper는 부트스트랩/구성 경계에서 실행하고, 모든 테이블 검증에 성공한 다음 Scope를 Build한다. 각 테이블은 예를 들어 `builder.Bind<ISpecTable<MySpec>>().FromInstance(snapshot)`로 연결한다. `MySpec`과 실제 table/field 이름은 게임 스키마를 작성할 때 정한다.

snapshot에는 정리해야 할 Unity 자원이나 구독이 없으므로 불변 `FromInstance` 등록이 적합하다. 사전 로딩 source는 별도의 `using`으로 종료한다. 테이블을 런타임 동안 BG repo에 연결해 두거나 BG 기본 repo의 이벤트를 구독하지 않는다. 실패한 로딩에서 부분 snapshot을 AppScope에 공개하지 않는다.

테이블 사이의 relation은 데이터의 외래 키다. 이를 `IDataLayer → IDataLayer` 주입으로 표현할 필요가 없다. 예를 들어 필요한 Domain이 두 `ISpecTable<T>`를 각각 주입받고 키로 조회하면 레이어 의존 방향이 명시된다. row 값 자체는 KDI Data 서비스가 아니다.

가변 플레이 상태와 저장 상태는 별도 Data/Domain 및 persistence 경계에서 관리한다. Specs를 플레이 중 변경하거나 BGDatabase 저장 기능을 플레이어 저장 시스템으로 연결하지 않는다. UI의 기존 KDI View Assets/Addressables 정책도 변경하지 않는다.

## 검증과 현재 범위

`Assets/_Project/Foundation/Tests/Editor/SpecTests.cs`는 게임 스키마와 무관한 lookup/reference 테이블을 독립 `BGRepo`에서 메모리로 만든다. 다음 계약을 검증한다.

- 기본 repo 로드 상태/에러/경로를 바꾸지 않는 로딩과 relation 매핑.
- 원본 repo 변경/정리와 source Dispose 이후 snapshot 보존, reader 접근 차단.
- 누락된 필수 relation, 예상과 다른 대상 table, 중복 키, 누락 table 및 잘못된 field 타입의 실패.
- 원본 배열 변경과 외부 list 쓰기로부터 table 구조 보호.
- 빈/짧은 입력을 조용히 빈 Specs로 받아들이지 않음.

이는 scalar와 필수 단일 relation을 사용한 foundation 검증이다. 아직 없는 실제 게임 DB의 스키마, 모든 BG addon, 사용자 정의 field, live update, Unity asset 로딩까지 검증한 것은 아니다. 테이블의 값 범위나 게임 규칙은 각 mapper/row 값 타입에서 추가 검증한다. 공급자가 일부 손상된 field 값을 로그로만 보고하는 경로도 있으므로, 실제 authoring 데이터의 품질 검증을 임의의 bytes에 대한 완전한 무결성 검사와 동일하게 취급하지 않는다.

컴파일/테스트 실행 결과는 작업 완료 보고에서 별도로 기록한다. 이 문서의 테스트 목록은 테스트 통과를 대신하지 않는다.
