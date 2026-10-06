# 현장 취소의 Command 이동 검증 기록

## 결과와 변경 범위

2026-10-04 사용자 승인에 따라 현장 취소를 StateApply에서 Command로 옮겼다. Command가 취소를 기록하고 EndCommand가 기존 실물의 월드 반환과 현장/요청 삭제를 확정한다. 같은 틱 철거/취소와 재배치는 갱신 전 공간 인덱스의 점유를 기준으로 건설 요청을 거부한다. Synchronization 이후 새 요청부터 변경된 점유를 검증한다.

- 최종 Unity 재컴파일: completed, failed:false, 오류 0.
- 관련 기존 EditMode 4개 필터: **19/19 통과**, Failed/Skipped/Inconclusive 0.
- 종료 시 Editor ready, compiling:false, domainReloadInProgress:false, Play Mode stopped. 런타임·Editor 어셈블리 모두 수정된 소스보다 최신이었다.
- 기존 완공 Job 본문은 스테이징된 작업 시작 상태와 동일하다. 공간 인덱스의 조기 갱신이나 Command 중간 ECB 재생을 추가하지 않았다.
- git diff --check와 변경 문서의 로컬 링크 검사를 통과했고 새 소스의 .meta GUID와 공백도 확인했다.

| 위치 | 변경 |
| --- | --- |
| [ConstructionCancelCommandSystem.cs](../../../../Assets/Scripts/Systems/1_Command/ConstructionCancelCommandSystem.cs) | 기존 취소 Job을 Command 전용 시스템으로 옮기고 EndCommand producer를 등록했다. 새 파일의 .meta도 추가했다. |
| [ConstructionLifecycleApplySystem.cs](../../../../Assets/Scripts/Systems/5_StateApply/ConstructionLifecycleApplySystem.cs) | 취소 쿼리·Job·Lookup·연결을 제거하고 완공 Job만 남겼다. Stored Lookup은 읽기 전용으로 변경했다. |
| [ConstructionRequests.cs](../../../../Assets/Scripts/Components/Construction/ConstructionRequests.cs) | Consumer, 요청 실체화/소비 경계와 삭제 시점을 현재 Command 계약으로 갱신했다. |
| [Phase7ConstructionCancelTests.cs](../../../../Assets/Editor/Tests/Phase7ConstructionCancelTests.cs) | 기존 3건을 새 Command 소비 경계로 옮겼다. 충족 현장 취소에서 EndCommand 직후 Owner·렌더·좌표·동일 실물 보존과 후속 완공 차단을 확인한다. |
| [Phase7EndToEndConstructionPipelineTests.cs](../../../../Assets/Editor/Tests/Phase7EndToEndConstructionPipelineTests.cs) | 실제 그룹에 취소 시스템을 등록하고 기존 취소 사례를 같은 틱 재배치 거부→동기화 후 새 요청 승인·바닥 정리 대기까지 확장했다. |
| [TestSimulationDriver.cs](../../../../Assets/Editor/Tests/TestSupport/TestSimulationDriver.cs) | 실제 Crafter 그룹의 Command에 취소 시스템을 등록했다. |

새 테스트를 추가하지 않고 기존 핵심 회귀에 필요한 검증을 더했다. 취소의 Null/현장 부재/중복 보호, 완공 건물 보호, 활성 Destroy 자재의 반환 제외를 유지했다. 기존 실물을 반환하며 미도착 자재의 신규 환급은 하지 않는다. 건물 철거 요청의 이름·책임 정리와 새 드론 작업/예약은 이번 범위에 포함하지 않았다. 작업 시작 시 스테이징된 변경은 수정·취소하지 않았다.

## 처리와 데이터 경계

1. 소비 시스템 실행 전에 실체화된 요청은 현재 Command에서 처리한다. 이후 생성된 요청은 다음 Command에서 대상 유효성을 재검증하며 이미 완공/삭제된 대상은 요청만 소비한다.
2. 단일 워커가 Cancelled를 즉시 설정해 같은 틱 중복 반환을 막는다. Stored 버퍼는 읽기만 하고 반환과 삭제는 ECB에 기록한다.
3. EndCommand(OrderLast)에서 Owner, DisableRendering, GridPosition/LocalTransform, 현장/요청 삭제가 확정된다. StateApply의 완공 쿼리에 취소 현장이 남지 않는다.
4. 건물/아이템 공간 인덱스는 기존 Synchronization에서 갱신한다. 배치 검증은 기존 맵을 읽으므로 같은 틱 재배치는 시스템 나열 순서에 의존해 점유를 우회하지 않는다. 거부된 배치는 소비되며 자동 재시도하지 않는다.
5. 기존 완공 조건과 공통 Spawn 실패 시 자재/현장 보존 계약은 유지한다. StateApply에서 완공 성공 이후만 자재/현장을 삭제한다.

## 실행 근거

연결된 Unity 6000.4.11f1 Editor에서 recompile --focus false의 최종 상태를 확인한 뒤 아래 필터를 순차 실행하고 test_status의 completed 통계를 확인했다.

| 필터 | Total | Passed | Failed | Skipped | Inconclusive |
| --- | ---: | ---: | ---: | ---: | ---: |
| Phase7ConstructionCancelTests | 3 | 3 | 0 | 0 | 0 |
| Phase7EndToEndConstructionPipelineTests | 4 | 4 | 0 | 0 | 0 |
| Phase7ConstructionCompletionTests | 8 | 8 | 0 | 0 | 0 |
| Phase5CrafterInputPipelineTests.SortedGroups_ActualCreation_Transport_Production_RecipeChange_AndClear | 4 | 4 | 0 | 0 | 0 |
| 합계 | 19 | 19 | 0 | 0 | 0 |

첫 재컴파일에서는 분리 파일의 ReadOnly Attribute에 필요한 Unity.Collections using 누락으로 6개 오류가 발생했다. 해당 참조를 보완한 후 재컴파일이 오류 없이 완료됐다. 첫 실패 응답과 최종 응답을 모두 보존했다. 테스트 실패는 없었다. Crafter 통합의 Transport는 일반 물류이며 삭제한 공사 운송을 구현·검증한 것이 아니다.

원본 CLI 응답은 Logs/Codex/ConstructionCancelCommand-20261004/에 보존했다.

- [최종 재컴파일](../../../../Logs/Codex/ConstructionCancelCommand-20261004/recompile-status.json), [수정 전 첫 실패](../../../../Logs/Codex/ConstructionCancelCommand-20261004/recompile-first-failure.json)
- [취소 회귀](../../../../Logs/Codex/ConstructionCancelCommand-20261004/cancel-status.json), [공사 통합](../../../../Logs/Codex/ConstructionCancelCommand-20261004/pipeline-status.json)
- [기존 완공](../../../../Logs/Codex/ConstructionCancelCommand-20261004/completion-status.json), [Crafter 통합](../../../../Logs/Codex/ConstructionCancelCommand-20261004/crafter-status.json)
- [어셈블리 최신성·Editor 상태](../../../../Logs/Codex/ConstructionCancelCommand-20261004/assembly-freshness.json), [완공 Job 보존 정적 확인](../../../../Logs/Codex/ConstructionCancelCommand-20261004/static-contract-check.json), [요약](../../../../Logs/Codex/ConstructionCancelCommand-20261004/summary.json)

## 남은 범위와 한계

건물 파괴 요청 이름·의존 축소와 새 드론 공급/회수·예약 연결은 후속 작업이다. 전체 EditMode, Play Mode, 실제 SubScene 베이킹·프리팹, 입력·시각·성능 검증은 수행하지 않았다. 향후 드론의 취소/재배정 동작도 이번 실행으로 검증하지 않았다.

현재 계약은 [AGENTS.md](../../../../AGENTS.md), [공사 컴포넌트](../../../CodeMemory/Components/Construction.md), [합의 명세](../../../Specifications/ConstructionAndDroneSupply.md)를 따른다. [운송 제거](ConstructionTransportRemoval-Verification.md)와 [Progress 제거](ConstructionProgressRemoval-Verification.md)는 취소가 StateApply에 있던 이전 단계의 실행 근거이며 원래 결과를 보존한다.
