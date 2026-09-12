# Project16 기반 구성

설정일: 2026-09-12. Unity 6000.3.10f1.

## 설치 패키지

| 패키지 | UPM ID | 버전 |
|---|---|---|
| KDI | com.kylin.di | 2.0.0 |
| KDI-Subscribable | com.kylin.subscribable | 2.0.0 |
| KDI-Layered | com.kylin.di.layered | 2.0.0 |
| KDI-MessagePack | com.kylin.di.messagepack | 2.0.0 |
| KDI-ViewAssets | com.kylin.di.view-assets | 1.0.0 |
| KDI-ViewAssets-Addressables | com.kylin.di.view-assets.addressables | 1.0.0 |
| KDI-ViewAssets-Layered | com.kylin.di.view-assets.layered | 1.0.0 |

KDI-Skill-Manifestation은 사용자 요청에 따라 제외했다. KDI 7개는 ToolStorage의 각 Git 저장소에서 특정 커밋을 고정해 설치했다. 전체 URL과 commit은 `Packages/manifest.json`, 실제 해석 결과는 `Packages/packages-lock.json`에 기록되어 있다. 로컬 개발 저장소의 수정은 이 프로젝트에 자동 반영되지 않는다.

Addressables 연동 패키지의 선언된 의존성에 맞춰 Addressables를 2.9.0에서 2.11.2로 변경했다. Scriptable Build Pipeline은 3.0.3으로 해석됐다. 기존 Localization 1.5.13과 NuGet MessagePack 3.1.8을 사용한다.

## KylinSoulInjector와 Codex

- 프로젝트 전용 설치: `.agents/skills/kdi-kylin-soul-injector`.
- 원본: `https://github.com/ToolStorage/Kylin-Soul-Injector`, commit `a11a3779bb059ac74ba9110dcff7b91e10252fd4`.
- 스킬/감사 도구 기준: 2.0.0, 분석기 계약: 2.0-preview.1.
- 루트 `AGENTS.md`는 KDI 관련 구현·리뷰·진단 시 이 스킬을 읽고 적용하도록 지정한다. 설치된 KDI 코드가 스킬 기준 자료보다 우선한다.
- Unity 제어는 기존 Unity CLI Loop 3.6.3과 `.agents/skills/uloop-*`를 사용한다.
- 별도의 전역 플러그인 설치 없이 이 폴더의 프로젝트 전용 스킬로 사용할 수 있다.

Codex에서 `Ctrl+O`로 `C:\KDI\KDIBaseProjects\Project15\Project16`을 열고 해당 폴더를 기본 작업 폴더로 삼아 새 작업을 시작한다. 다른 프로젝트의 보조 폴더로만 첨부하는 것과 다르다. 이번 작업에서는 Codex 사이드바의 저장된 프로젝트 등록을 자동 수행하지 않았다.

## View Assets 초기 정책

정책 파일: `Assets/_Project/Settings/AddressablesViewAssetPolicy.asset`.

| 항목 | 설정 |
|---|---|
| 관리 폴더 | Assets/_Project/AddressableResources/Views |
| 그룹 | KDI Views Local |
| 템플릿 | Packed Assets |
| 주소 접두사 | kdi/views |
| 관리 라벨 | kdi.view-assets.managed |
| 폴더 규칙 라벨 | kdi.view-assets.views |
| Pool Mode | Transient |
| 고아 관리 항목 제거 | 비활성화 |
| 생성 클래스 | Project16.ViewAssets.ProjectViewAssets |
| 생성 코드 | Assets/_Project/Generated/ViewAssets/ProjectViewAssets.g.cs |
| 전용 어셈블리 | Project16.ViewAssets |

정책은 관리하는 KDI View 프리팹이 없는 상태로 검증했다. 초기 Reconcile은 빈 typed manifest 생성만 수행했고 기존 그룹/라벨/에셋을 재배치하지 않았다. 규칙의 그룹과 라벨은 첫 유효 프리팹을 넣고 Reconcile할 때 생성된다.

작업 메뉴는 `Tools > Kylin > KDI View Assets > Addressables Reconcile`이다. 폴더 규칙을 수정한 뒤 Preview를 확인하고 Apply한다. 임포트 시 자동 적용되는 기능이 아니다.

관리 프리팹은 정확히 하나의 루트 `KdiView<TArgs>` 등 패키지 계약을 만족해야 한다. 일반 이미지/오디오 또는 Localization 폴더를 이 규칙에 포함하지 않는다. Pooled 모드로 변경할 때는 `KdiPooledView<TArgs>` 계약을 확인한다.

첫 구체적 View/Args 타입을 추가할 때 `Project16.ViewAssets`가 해당 타입의 어셈블리를 참조할 수 있도록 구성한다. 같은 View/composition 어셈블리에 타입을 두거나 별도 View 어셈블리를 명시적으로 참조한다. 기본 `Assembly-CSharp`에만 둔 타입은 이 사용자 정의 asmdef에서 참조할 수 없다.

생성된 `ProjectViewAssets`는 아직 빈 카탈로그 선언용 타입이며 게임 씬에서 생성·등록하지 않았다. 2026-09-13에 App/LocalSession/Scene/Feature Scope, 부트스트랩, 로컬 MessagePack 저장, BGDatabase Spec 어댑터를 추가했다. 사용법과 소유권은 [Foundation.md](Foundation.md), 검증 기록은 [FoundationValidation.md](FoundationValidation.md)를 따른다. 실제 View Host와 게임 View 프리팹은 첫 UI 기능을 구현할 때 해당 화면의 수명에 맞게 구성한다.

## 카드게임 규칙 기반 (2026-09-13)

BGDatabase는 Spec 전용으로 유지했다. `Assets/_Project/CardGame`에 Data/Domain/Application, `Assets/_Project/Resources/bansheegz_database.bytes`에 BG 창에서 편집하는 테이블을 추가했다. `Assets/_Project/SpecAuthoring/CardGame.tables.json`은 초기 샘플/테스트 seed다. CardGameModule은 같은 BG 에셋을 참조하며 FoundationSettings에 연결되어 있고, 진행 상태는 독립 MessagePack 저장 단위로 관리한다. 화면 구현과 원문 카드 전체의 확정 팩은 포함하지 않는다. 설계/사용법은 [SpecTableDesign.md](GameDesign/SpecTableDesign.md), 원문 대비 미비사항은 [SourceEffectCoverage.md](GameDesign/SourceEffectCoverage.md), 검증은 [Validation.md](GameDesign/Validation.md)를 따른다.

## 초기 설치 검증 기록 (2026-09-12)

- Unity의 등록 패키지 목록에서 KDI 7개와 Addressables 2.11.2 확인.
- 일반 `uloop compile`: 오류 0, 경고 0.
- 생성 매니페스트 어셈블리의 컴파일 응답 파일에서 KDI Layered와 ViewAssets Layered 분석기 DLL 로드 확인.
- `AddressablesViewAssetCi.Validate()`: 관리 프리팹 0개, 오류 없음, drift 없음. 플레이어 빌드 게이트와 같은 검증 엔진을 직접 호출했다.
- `uloop get-logs --log-type Error`: 콘솔 오류 0개.
- 인젝터 구조 감사 실행: 직접 작성한 검사 대상 C# 파일이 아직 없어 0개 스캔. 생성 코드는 감사 도구가 제외한다. `KSI001 UNKNOWN` 1건은 KDI 사용을 판단할 게임 코드가 없다는 의미이며 구조 검증 통과로 간주하지 않는다.
- 게임 코드와 실제 Scope 구성을 추가하지 않았으므로 게임플레이/수명 주기 테스트 및 전체 플레이어 빌드는 실행하지 않았다.

Python 감사 실행 예시(사용 가능한 Python 3 실행 파일 사용):

```powershell
python .agents/skills/kdi-kylin-soul-injector/scripts/audit_kdi_architecture.py .
```

이번 환경에서는 PATH의 Python 별칭 대신 Codex의 번들 Python 3 실행 파일을 사용했다.
