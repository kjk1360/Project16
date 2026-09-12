# 공개 저장소 구성

원격 저장소: https://github.com/kjk1360/Project16 — 기본 브랜치 `main`.

Unity 프로젝트의 `Assets`와 `.meta`, `Packages/manifest.json`, `Packages/packages-lock.json`, `ProjectSettings`, 자체 코드·설계 문서·프로젝트 스킬을 포함한다. IDE 생성 파일, Unity 캐시, 빌드, 개인 설정, `.uloop` 출력은 제외한다. `.gitattributes`는 소스의 줄바꿈과 `.bytes` 등의 바이너리를 구분한다.

## 별도로 준비할 항목

| 항목 | 복원 방법 |
| --- | --- |
| BGDatabase | 각 협업자가 라이선스를 갖고 1.9.5를 설치. `Assets/BansheeGz`는 Git 제외 |
| MessagePack 등 NuGet 패키지 | `NuGet → Restore Packages`. `Assets/packages.config`와 `Assets/NuGet.config` 유지 |
| 룰북·원본 카드와 전문 전사 | 필요한 협업자에게 별도로 제공. 공개 Git에는 포함하지 않음 |
| 세이브·IDE 상태·캐시 | 각 컴퓨터에서 생성. 공유하지 않음 |

KDI Git 의존성은 공개 ToolStorage 저장소의 기존 commit을 유지한다. 게임 저장소를 kjk1360에 생성하는 것과 패키지 배포 출처는 별개이며, 기존 패키지 버전을 임의로 바꾸지 않았다.

## 원문 데이터 보존

공개 준비 전의 원문 포함 `CardGame.tables.json`과 `CardGameSpecs.bytes`는 로컬의 `Docs/GameRuleBook/PrivateSpecSnapshots`에 복사해 보존했다. 기존 룰북과 `Docs/GameDesign/CardSourceInventory.md`, `.json`, `RulebookCombatNotes.md`도 삭제하지 않았다. 이 파일들은 `.gitignore`로 제외한다.

공개 작성본은 `sourceCards.rows`만 비운다. 해당 테이블은 근거 기록이며 런타임 규칙 엔진에서 참조하지 않는다. 실행용 Spec 124행을 그대로 유지하고 binary를 다시 생성했다. 원본 전문 100행이 JSON뿐 아니라 binary에도 남지 않도록 같은 공개 작성본에서 생성했다.

BG 창 연결 수정 후의 작성 원본은 `Assets/_Project/Resources/bansheegz_database.bytes`다. 초기 공개 bytes를 GUID와 함께 이동했고, BG 창과 게임 모듈이 같은 파일을 사용한다. JSON은 초기 샘플/테스트 seed로 유지하며 기존 BG 파일을 덮어쓰지 않는다. BG가 만드는 `bansheegz_database_settings.json`과 `.meta`는 개인 편집기 설정으로 Git에서 제외한다. 데이터 bytes와 그 `.meta`는 계속 공유한다.

## 브랜치와 에셋 충돌

씬·프리팹의 동일 에셋을 동시에 편집하는 일을 줄이고 각 기능 브랜치로 PR을 보낸다. `.meta`를 새로 생성해 기존 GUID를 바꾸지 않는다. Addressables 그룹/라벨/주소는 AGENTS.md의 KDI 정책을 따른다.

이 업로드 작업에서는 새 게임 로직이나 패키지 버전을 바꾸지 않았다. 공개 샘플 검증의 원문 행 개수 기대값과 문서 링크만 공개 구성에 맞췄다. BGDatabase가 제외되므로 새 clone에서 의존성 설치 없이 즉시 컴파일된다고 가정하지 않는다.
