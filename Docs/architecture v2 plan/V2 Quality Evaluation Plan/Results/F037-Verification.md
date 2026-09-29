# F-037 실제 Crafter 생성·입력 슬롯 연결 검증

완료일: 2026-09-29. 대상: Unity 6000.4.11f1, 연결된 Editor의 EditMode.

직접 Spawn과 공사 완료로 생성한 Crafter가 레시피 선택부터 입고·재료 선소비·생산·후속 출고까지 실행되는 것을 확인했다. F-037의 생성 구성 누락은 완료 처리한다. 최초 평가 [Q21](Q21.md)은 당시 기록으로 보존한다.

## 확정된 계약

- 같은 품목의 레시피 요구량을 합산하고 `ceil(요구량 / MaxStack)`개의 품목 전용 슬롯을 만든다. 슬롯당 최대 스택까지 비축하며, 제작 1회분만 수령하는 제한은 아니다.
- `StoredItemElement`는 실제 아이템 참조와 보관 슬롯을, `BuildingInputSlotElement`는 슬롯별 허용 품목을 보관한다. 슬롯 버퍼 길이와 `Storage.SlotCount`는 같다.
- 생성 직후와 레시피 해제 시 입력은 0슬롯/빈 Whitelist다. 계산 실패 시 기존 상태를 보존하고, 설정 미게시 시 레시피 선택 요청을 유지한다.
- 레시피 변경은 진행을 초기화하고 남은 입력을 기존 스택 구분을 유지한 채 출력 버퍼로 옮긴다. 남은 출력이 배출되기 전 입고·새 제작을 막는다.
- 레시피 Command는 `CrafterDecision`/`CrafterStateDecision`을 읽거나 쓰지 않는다. 뒤의 Decision 단계가 변경된 원본 상태에서 프레임 결정을 작성한다. 생성 시 초기 컴포넌트 구성은 별도 책임이다.

## 변경 위치

- `Assets/Scripts/Common/BuildingLifecycleUtility.cs`: 직접 생성/완공의 공통 Crafter 구성.
- `Assets/Scripts/Common/BuildingInputSlotUtility.cs`, `Assets/Scripts/Components/Buildings/BuildingInputSlotElement.cs`: 슬롯 계산과 공통 데이터.
- `Assets/Scripts/Systems/1_Command/CrafterRecipeCommandSystem.cs`: 계산 검증·입력 구성 갱신·잔여물 이동.
- `Assets/Scripts/Systems/2_Decision/BuildingItemInputDecisionSystem.cs`, `Assets/Scripts/Systems/3_Reservation/BuildingStorageInputReservationSystem.cs`: 0슬롯 차단과 품목별 예약.
- `Assets/Scripts/Validation/WorldInvariantValidationSystem.cs`: 정상 0슬롯 Crafter 및 입력 슬롯 품목 대응 검사.
- `Assets/Editor/Tests/Phase5CrafterInputSlotTests.cs`, `Phase5CrafterInputPipelineTests.cs`, `TestSupport/TestSimulationDriver.cs`: 계산·단계 경계·실제 정렬 그룹 검증. 기존 Factory와 제작/레시피 변경/생성 테스트도 새 입력 계약에 맞췄다.

## 이번 통합 실행 결과

Unity 재컴파일은 `completed`, 오류 0이었다. 런타임/테스트 어셈블리 최신성을 확인한 뒤 `Phase5CrafterInputPipelineTests` 19개가 모두 통과했다. 실패·생략·Inconclusive·CLI 경고는 0이었다.

새 통합 테스트 `SortedGroups_ActualCreation_Transport_Production_RecipeChange_AndClear`는 다음 네 조합을 각각 실행한다.

- 직접 Spawn, DB 없는 fallback.
- 공사 완료, DB 없는 fallback.
- 직접 Spawn, DB의 ECS 테스트 프리팹.
- 공사 완료, DB의 ECS 테스트 프리팹.

각 사례는 제작·물류에 필요한 시스템을 실제 `GameSimulationGroup`의 여섯 Phase 그룹에 등록하고 `SortSystems()`로 정렬한다. 실제 그룹 안의 EndCommand/EndStateApply ECB만 사용하고, 중간에 수동 Playback을 추가하지 않는다. 공사 현장·요청 외의 공급/수신 창고와 벨트는 테스트 Factory로 구성한다.

검증 흐름은 미선택 입고 차단 → 레시피 선택 → 공급 창고 출고 → 벨트 입고 → 제작 재료 버퍼/Owner 일치 → 다음 프레임 선소비 및 엔티티 삭제 → 생산품 버퍼/Owner 일치 → 출력 벨트 → 목적지 창고다. 이후 두 번째 입고 직후 레시피를 변경하여 선소비 전 잔여 재료가 정상 출력 경로로 배출되는지, 새 레시피 입력 대기와 해제가 정상 처리되는지도 확인한다. 제작 시간과 벨트 속도에서 대기 프레임 상한을 계산하며, 모든 완료 프레임에서 `WorldInvariantValidationSystem.TotalViolationCount == 0`을 검사한다.

실행 중 Editor 재로드로 CLI 재컴파일 응답 연결이 한 번 끊겼다. `recompile_status`에서 완료·오류 없음을 확인한 뒤 어셈블리 최신성을 검사하고 테스트를 이어서 실행했다. 제출만으로 컴파일 완료를 판단하지 않았다.

최종 원본 요약: [통합 실행 로그](../../../../Logs/Codex/F037-stage3-integration-verification.json).

## 이전 단계의 검증과 구분

- 순수 슬롯 계산/Burst 호출: 18/18 통과. [1단계 로그](../../../../Logs/Codex/F037-building-input-slots-verification.json).
- 런타임 연결 단계: 관련 사례 59개가 각 최종 실행에서 통과했다. [CodeMemory의 단계별 기록](../../CodeMemory/README.md)을 참고한다. [2단계 최종 입력 검증](../../../../Logs/Codex/F037-stage2-input-verification.json)은 15/15 통과이며, `F037-stage2-verification.json`에는 보정 전 테스트 준비 오류와 당시 통과한 기존 테스트 결과가 남아 있다.
- Command/Decision 책임 경계 수정: 입력 연결 15개와 레시피 변경 2개 통과. [책임 경계 로그](../../../../Logs/Codex/F037-command-decision-boundary-verification.json).
- 이전에 통과한 변경 없는 테스트는 이번 결과를 만들기 위해 반복 실행하지 않았다. 위 수치를 이번 한 번의 전체 테스트 실행 결과로 합산하지 않는다.

## 검증 범위의 한계

- 프리팹 경로는 ECS 테스트 프리팹이다. 실제 SubScene baked prefab, 렌더링/LinkedEntityGroup, 실제 장면 로딩은 검증하지 않았다.
- 공사 완료는 자재 요구가 충족된 현장에서 시작한다. 배치 요청부터 건설 자재 운송까지 전체 공사 과정을 검증한 것은 아니다.
- 제작·물류 관련 6단계 그룹을 실행했으며, 모든 게임 시스템을 등록한 월드나 전체 EditMode 실행은 아니다. Play Mode·입력·UI·시각 결과는 실행하지 않았다.
- 슬롯 계산의 중복 품목 합산은 제작 Decision의 중복 재료 행 소비 문제(F-014) 해결을 뜻하지 않는다. 일반 창고의 빈 슬롯 예약 문제(F-031), 다른 생산/물류 이슈, 연구·발전 건물의 입력 정책은 변경하지 않았다.

사용자 수동 확인이 필요한 범위는 실제 V2 테스트 장면의 베이킹된 Crafter로 생성·레시피 변경·입출고와 표시 결과를 확인하는 것이다. 이는 완료된 EditMode 결과와 별도로 기록한다.
