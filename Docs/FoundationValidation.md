# Foundation 검증 기록

2026-09-13, Unity 6000.3.10f1, 설치된 KDI 2.0.0 기준이다.

게임 모듈 통합 후에도 Foundation EditMode 74개와 PlayMode 5개를 다시 검증했다. 최종 통합 결과는 [GameDesign/Validation.md](GameDesign/Validation.md)에 기록했다.

| 구분 | 실행 | 결과 |
| --- | --- | --- |
| 컴파일 / KDI 분석기 | `uloop compile` | 오류 0, 경고 0 |
| EditMode | `uloop run-tests --test-mode EditMode --filter-type assembly --filter-value Project16.Foundation.Tests --unsaved-changes fail` | 74/74 통과 |
| PlayMode | `uloop run-tests --test-mode PlayMode --filter-type assembly --filter-value Project16.Foundation.PlayModeTests --unsaved-changes fail` | 5/5 통과 |
| 구조 audit | `.agents/skills/kdi-kylin-soul-injector/scripts/audit_kdi_architecture.py .` | Foundation 26개 파일, 오류/경고/advisory/UNKNOWN 0 |

EditMode는 저장 단위별 dirty revision, 성공한 revision만 확인 처리, 저장 실패 격리, checksum/backup 복구, 미지원 버전 보호, migration, 독립 Spec snapshot, BG global repo 격리, App/Session/Scene 소유권과 재생성, 자식 종료 후 최종 저장을 검증한다. PlayMode는 pause/focus 저장, timeScale=0에서 interval 저장, 종료 시 자식 해제, Starter 중복 방지, scene reload 비활성화 시 재연결을 검증한다.

처음 테스트 실행은 SampleScene의 미저장 변경을 감지해 중단했다. 사용자에게서 **현재 변경을 저장하고 테스트 진행** 승인을 받은 뒤 해당 씬만 저장했다. 테스트가 생성하는 저장 파일은 격리된 임시 경로를 사용한다.

플레이어/IL2CPP 빌드, 실기기 백그라운드 전환, OS 강제 종료, WebGL 저장은 실행하지 않았다. View Assets의 관리 프리팹/정책/생성 파일을 변경하지 않았으므로 이번 기반 작업에서 정책 Reconcile/CI validation은 실행하지 않았다. 모바일에서는 pause/focus 저장이 주된 보전 경로이며, 강제 종료 콜백 자체는 보장되지 않는다.
