# Project16

Unity 6와 Kylin DI를 사용하는 모바일 카드게임의 공통 기반입니다. 현재 Data / Domain / Application 레이어, 데이터로 조합하는 카드 효과, 로컬 MessagePack 저장까지 구현되어 있습니다. 게임 UI와 전체 원문 카드팩은 개발 중입니다.

## 함께 개발하기

1. [kjk1360/Project16](https://github.com/kjk1360/Project16)에서 **Fork**를 누릅니다.
2. 자기 계정의 fork를 clone합니다.
3. 이 저장소를 upstream으로 연결하고 기능 브랜치에서 작업한 뒤 Pull Request를 보냅니다.

```sh
git clone https://github.com/YOUR_ACCOUNT/Project16.git
cd Project16
git remote add upstream https://github.com/kjk1360/Project16.git
git switch -c feature/my-feature
```

저장소 루트가 Unity 프로젝트 루트입니다. Unity Hub에서 이 폴더를 엽니다.

## 최초 설치

- **Unity 6000.3.10f1**을 사용합니다. 정확한 버전은 [ProjectVersion.txt](ProjectSettings/ProjectVersion.txt)에 고정되어 있습니다.
- Git을 설치하고 UPM 패키지 복원이 끝날 때까지 기다립니다. KDI 7개 패키지는 공개 저장소의 commit으로 고정되어 있으며 ToolStorage 계정 권한은 필요하지 않습니다. 이 게임 프로젝트의 원격 저장소는 `kjk1360/Project16`입니다.
- **BGDatabase 1.9.5**를 본인이 사용할 수 있는 라이선스로 설치합니다. [BGDatabase 설치 안내](https://www.bansheegz.com/BGDatabase/Setup/)를 참고하여 `Assets/BansheeGz/BGDatabase`에 임포트합니다. 유료 패키지의 원본 DLL/Editor 파일은 이 공개 저장소에 포함하지 않습니다.
- Unity 메뉴 **NuGet → Restore Packages**를 실행합니다. MessagePack 3.1.8과 의존성은 [Assets/packages.config](Assets/packages.config) 및 [Assets/NuGet.config](Assets/NuGet.config)를 기준으로 복원합니다. `Assets/Packages`는 생성된 패키지 폴더이므로 Git에서 제외합니다.
- 의존성이 설치되기 전에는 BGDatabase/MessagePack 관련 컴파일 오류가 날 수 있습니다. 패키지 임포트와 복원 후 컴파일을 완료합니다.
- `Assets/_Project/Scenes/FoundationBoot.unity`를 엽니다. `Starter`가 App → LocalSession → Scene Scope를 구성합니다. 아직 게임 화면은 없으며 새 run은 Application API에서 시작합니다.

## Spec 작성과 저장

**Window → BGDatabase** 또는 **Tools → Project16 → Open Card Game Specs (BGDatabase)**를 엽니다. **Database** 탭에서 20개 테이블의 행을 수정하고 BG 창의 **Save Repo**로 저장합니다. 작성 원본은 [bansheegz_database.bytes](Assets/_Project/Resources/bansheegz_database.bytes)이며 게임 모듈도 이 에셋을 직접 참조합니다. 저장 후 **Tools → Project16 → Validate Saved Card Game Specs**로 규칙과 참조를 검증하고 게임을 다시 시작하면 변경값으로 App Scope를 구성합니다. 실행 중인 Spec snapshot은 자동 갱신하지 않습니다.

[CardGame.tables.json](Assets/_Project/SpecAuthoring/CardGame.tables.json)은 초기 샘플과 테스트용 seed입니다. **Create Missing Card Game Specs from JSON**은 DB 파일이 없을 때만 생성하며 기존 BG 편집 내용을 덮어쓰지 않습니다. 생성 전에 BG 창이 이미 열려 있었다면 **Reload**를 누릅니다. 일반 데이터 작업에서는 BG bytes와 `.meta`를 보존하여 커밋합니다. 공개 샘플의 `sourceCards`는 빈 근거 테이블입니다. 룰북/카드 원문과 원문 전사 파일은 별도로 보관하고 이 저장소에 업로드하지 않습니다.

UserData는 `Application.persistentDataPath/UserData/<profile>`에 저장됩니다. dirty 단위만 30초 간격과 백그라운드/종료 시 MessagePack으로 저장하며 서버 저장은 없습니다.

## 작업 기준과 검증

[AGENTS.md](AGENTS.md)를 읽고 기존 에셋의 `.meta`를 함께 보존합니다. Unity의 Force Text / Visible Meta Files 설정이 적용되어 있습니다. `Library`, `Temp`, 로컬 설정, 빌드, uLoop 출력은 커밋하지 않습니다. Git LFS는 현재 필요하지 않습니다.

Unity Editor를 연 뒤 프로젝트 루트에서 설치된 uLoop CLI로 실행합니다. 테스트는 Unity Test Runner에서도 실행할 수 있습니다.

```sh
uloop compile
uloop run-tests --test-mode EditMode --filter-type assembly --filter-value Project16.Foundation.Tests --unsaved-changes fail
uloop run-tests --test-mode EditMode --filter-type assembly --filter-value Project16.CardGame.Tests --unsaved-changes fail
uloop run-tests --test-mode PlayMode --filter-type assembly --filter-value Project16.Foundation.PlayModeTests --unsaved-changes fail
```

- [기반 구성과 Scope 소유권](Docs/Foundation.md)
- [Spec 설계와 사용법](Docs/GameDesign/SpecTableDesign.md)
- [테이블별 열](Docs/GameDesign/ImplementedTables.md)
- [구현 범위와 남은 규칙](Docs/GameDesign/SourceEffectCoverage.md)
- [검증 기록](Docs/GameDesign/Validation.md)
- [공개 저장소 구성](Docs/RepositorySetup.md)
