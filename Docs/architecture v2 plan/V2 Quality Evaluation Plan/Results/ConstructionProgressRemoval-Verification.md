# ConstructionSite Progress 제거 검증 기록

후속 상태: 현장 취소는 [Command 이동 작업](ConstructionCancelCommand-Verification.md)에서 옮겼다. 이 문서의 실행 결과와 당시 후속 범위는 원래 검증 시점을 보존한다.

## 결과와 변경 범위

2026-10-04 사용자 승인에 따라 ConstructionSite.Progress 필드, 생성자 progress 인자와 대입을 제거했다. 새 생성자는 목표 건물 타입과 선택적인 ConstructionSiteFlags를 받는다. 자재 도착 비율을 보관하는 대체 필드는 추가하지 않았다.

- Unity 재컴파일: completed, failed:false, 오류 0.
- 기존 핵심 EditMode 3개 필터: **6/6 통과**, Failed/Skipped/Inconclusive 0.
- 현장 코드·테스트의 Progress 직접 참조와 이전 생성자 호출은 남아 있지 않다.
- 완공은 자재 요구량 충족과 취소/바닥 정리 대기 플래그를 판단한다. 벨트·채굴·제작 진행도는 별도 데이터이며 변경하지 않았다.

| 수정 위치 | 변경 |
| --- | --- |
| [ConstructionComponents.cs](../../../../Assets/Scripts/Components/Construction/ConstructionComponents.cs) | 필드·주석·생성자 인자·대입 제거 |
| [BuildingPlacementCommandSystem.cs](../../../../Assets/Scripts/Systems/1_Command/BuildingPlacementCommandSystem.cs) | 목표 타입과 flags로 현장 생성 |
| [Phase7ConstructionCompletionTests.cs](../../../../Assets/Editor/Tests/Phase7ConstructionCompletionTests.cs), [Phase7ConstructionCancelTests.cs](../../../../Assets/Editor/Tests/Phase7ConstructionCancelTests.cs) | 기존 현장 준비 생성자 호출 수정 |
| [Phase5CrafterInputPipelineTests.cs](../../../../Assets/Editor/Tests/Phase5CrafterInputPipelineTests.cs) | 공사 완료 Crafter 준비에서 진행도 인자 제거 |
| [Phase7PlacementCommandTests.cs](../../../../Assets/Editor/Tests/Phase7PlacementCommandTests.cs) | 삭제된 필드의 초기값 assertion 제거. 타입·위치·크기·Stamp 검증 유지 |

새 테스트는 추가하지 않았다. 생성자 호출의 타입/플래그 전달과 실제 공사 완료→제작 연결을 기존 회귀로 확인했다. 이전 운송 제거 변경과 스테이징된 문서는 보존했다.

## 실행 근거

연결된 Unity 6000.4.11f1 Editor에서 recompile --focus false의 최종 completed 결과를 확인한 뒤 아래 필터를 순차 실행하고 test_status의 completed 통계를 확인했다.

검증 종료 시 Editor는 ready, compiling:false, domainReloadInProgress:false, Play Mode stopped 상태였다. 런타임 및 Editor 어셈블리는 각각 변경된 소스보다 최신임을 확인했다.

| 필터 | Total | Passed | Failed | Skipped | Inconclusive |
| --- | ---: | ---: | ---: | ---: | ---: |
| Phase7PlacementCommandTests.Test01_SinglePlacementRequest_CreatesConstructionSiteWithMaterialsAndStamp | 1 | 1 | 0 | 0 | 0 |
| Phase7PlacementCommandTests.Test06_GroundItems_TagsAwaitingItemClearance | 1 | 1 | 0 | 0 | 0 |
| Phase5CrafterInputPipelineTests.SortedGroups_ActualCreation_Transport_Production_RecipeChange_AndClear | 4 | 4 | 0 | 0 | 0 |
| 합계 | 6 | 6 | 0 | 0 | 0 |

Crafter 통합 4개는 직접 Spawn/공사 완료와 기본/사용자 설정 테스트 프리팹의 조합이며 실제 정렬된 그룹의 제작·물류·레시피 변경을 검증한다. 삭제된 공사 자재 운송을 다시 구현하거나 검증하는 테스트가 아니다.

원본 CLI 응답은 Logs/Codex/ConstructionProgressRemoval-20261004/에 보존했다.

- [재컴파일](../../../../Logs/Codex/ConstructionProgressRemoval-20261004/recompile-status.json)
- [현장 생성](../../../../Logs/Codex/ConstructionProgressRemoval-20261004/placement-create-status.json)
- [바닥 정리 대기](../../../../Logs/Codex/ConstructionProgressRemoval-20261004/placement-clearance-status.json)
- [Crafter 통합](../../../../Logs/Codex/ConstructionProgressRemoval-20261004/crafter-pipeline-status.json)
- [최신성·Editor 상태](../../../../Logs/Codex/ConstructionProgressRemoval-20261004/assembly-freshness.json)
- [요약](../../../../Logs/Codex/ConstructionProgressRemoval-20261004/summary.json)

## 남은 범위와 한계

현장 취소의 Command 이동, 건물 파괴 요청 정리, 새 드론 공급·회수·예약 연결은 후속 작업이다. 전체 EditMode, Play Mode, 실제 SubScene 베이킹·프리팹, 입력·시각·성능 검증은 수행하지 않았다.

현재 계약은 [AGENTS.md](../../../../AGENTS.md), [공사 컴포넌트](../../../CodeMemory/Components/Construction.md), [합의 명세](../../../Specifications/ConstructionAndDroneSupply.md)를 따른다. [운송 제거 기록](ConstructionTransportRemoval-Verification.md)은 Progress가 아직 남아 있던 직전 단계의 결과이며 해당 실행 근거는 그대로 보존한다.
