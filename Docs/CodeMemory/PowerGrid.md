# 전력망

## 목적과 책임

전력 시스템은 JSON 설정을 ECS 데이터로 게시하고, 전신주의 연결 토폴로지와 공급 범위를 관리하며, 연결된 발전기·소비자의 수요와 공급을 집계한다. 석탄 발전기의 입력과 실제 연료 소모도 이 영역에 포함된다.

## 주요 스크립트와 역할

- `PowerConfigLoadSystem`, `PowerConfigParser.*`: `PowerConfig.json` 전체를 검증하고 전신주, 발전기, 소비자, 석탄 설정을 게시한다.
- `PowerGridSystem`과 partial 파일들:
  - `.Registration`: 새 전신주의 stable ID와 공급 셀 범위 등록, 제거.
  - `.Topology`: connection range로 connected component를 만들고 `PowerGrid` 엔티티와 pole connection을 다시 만든다.
  - `.Participants`: 소비자·발전기의 가장 가까운 전신주 연결, 수요/공급 집계, `PowerGridState` 게시.
  - `.Generation`: 일반/석탄 발전 가능량 집계와 실제 부하에 따른 발전 dispatch.
  - `.Queries`: 셀의 nearest pole, pole의 grid, 연결 가능한 pole 조회.
- `CoalGeneratorInputSystem`: footprint 전체에서 Coal 월드 아이템을 찾아 `ItemStorageSystem`으로 연료 저장 버퍼에 넣는다.
- `CoalGeneratorFuelSystem`: 현재 배정 발전량에 필요한 에너지만큼 stored Coal을 소비하고 `remainingFuelEnergy`를 감소시킨다.
- `CoalGeneratorPowerUtility`: 연료 기반 가능 출력, 활성 비율, 소비 에너지 계산.
- `PowerProductionUtility`: `PowerConsumer.supplyRatio`를 생산 진행 delta time에 적용한다.
- `MainFacilityBootstrapSystem`: 주 시설의 기초 발전기와 정거장/저장 상태, 그리고 전력망 루트 역할의 가상 `PowerPole`을 만든다.
- `BuildingPlacementPreview.Power`: 전신주 설치 시 공급·연결 범위와 연결 후보를 시각화한다.

## 데이터 모델

- `PowerPole`: 등록 순서를 나타내는 stable ID. 일반 전신주와 메인 시설의 가상 전신주가 모두 이 토폴로지에 참가한다.
- `PowerGridConnection`: 참가자가 선택한 pole과 grid entity.
- `PowerGrid`: connected component의 stable ID.
- `PowerGridState`: available generation, maximum demand, actual consumption, spare power, supply ratio, connected building count.
- `PowerGenerator`: 발전기 종류, 최대/현재 발전량.
- `PowerConsumer`: 최대/현재 소비량과 공급 비율.
- `CoalGenerator`: item과 별개로 남은 연료 에너지를 보관한다.

## 토폴로지와 공급 범위

전신주는 두 개의 서로 다른 사각 범위를 가진다.

- supply range: 건물이 이 전신주를 통해 전력망에 참가할 수 있는 셀 범위. `ChunkMapSystem` 각 셀에 pole entity를 등록한다.
- connection range: 두 전신주 중심이 서로의 연결 범위 안에 있을 때 같은 전력망으로 병합한다.

메인 시설은 전신주와 같은 connection range를 갖는 가상 전신주로 등록된다. supply range는 자기 셀만 포함하므로 전신주 없이도 메인 시설의 발전·소비·드론 충전은 작동하지만, 주변 건물에는 전력을 공급하지 않는다. 실제 전신주가 connection range 안에 설치되면 메인 시설의 전력망에 병합되어 공급 범위를 확장한다.

공급 범위가 겹치는 것만으로 망을 병합하지 않는다. 한 셀을 여러 pole이 덮으면 `PowerGridSystem.TryGetNearestPowerPole`이 거리 제곱이 가장 작은 pole을 선택하고, 동률은 낮은 stable ID로 결정한다.

건물 참가자는 회전된 footprint의 모든 점유 셀을 검사한다. 각 셀을 실제로 덮으며 유효한 grid가 있는 pole 후보에 대해 셀과 pole 중심 사이 거리 제곱을 비교하고, 전체 후보 중 최소 거리, 동률이면 낮은 stable ID를 선택한다. 첫 번째로 덮인 셀에서 검색을 끝내거나 anchor/시각 중심 거리로 순위를 정하지 않는다. 단일 셀은 기존 nearest 규칙과 같다.

전신주 추가/제거로 topology dirty가 되면 기존 grid entity와 pole connection을 제거하고, stable ID 순서로 connected component를 다시 만든다. 각 component의 grid stable ID는 포함 pole의 최소 stable ID다.

## 프레임 실행 흐름

1. `CoalGeneratorInputSystem`이 storage 처리 뒤, grid 계산 전에 Coal 입력을 수집한다.
2. `PowerGridSystem`이 새 전신주 등록과 사라진 전신주 정리를 동기화한다.
3. 토폴로지가 변했으면 grid entity와 pole connection을 재구축한다.
4. `GridPosition`, `BuildingFootprint`, `Direction`, `BuildingOccupant`가 있는 소비자와 발전기를 점유 셀 전체에서 선택한 pole/grid에 연결하거나 연결을 제거한다.
5. grid별 maximum demand와 가용 발전량을 집계한다. 석탄 발전기는 남은 에너지와 stored Coal로 현재 프레임의 가능 출력을 제한한다.
6. `supplyRatio = min(1, availableGeneration / maximumDemand)`를 계산하고 소비자 상태를 갱신한다.
7. 실제 소비량을 우선 기초 발전으로 충당하고, 부족분을 석탄 발전에 배정한다.
8. `CoalGeneratorFuelSystem`이 배정된 실제 발전 에너지만 소비한다. 연료가 부족하면 실제 `currentGeneration`을 가능한 값으로 낮춘다.
9. `DroneChargingSystem`, `MiningSystem`, `CrafterSystem`, `ResearchSystem`이 게시된 공급 비율을 사용한다.

## 다른 시스템과의 의존 관계

- 전신주 공급 셀 인덱스: `ChunkMapSystem`
- 건물 생성/파괴: `BuildingSpawnSystem`이 설정 기반 power component를 추가하고, `BuildingDestroySystem`이 pole 등록을 해제한다.
- 석탄 소유권: `ItemStorageSystem`
- 생산과 연구: miner/crafter/research building이 `PowerProductionUtility`를 사용한다. 연구건물은 0 전력에서 이미 시작한 로컬 주기의 진행도를 보존한다.
- 드론 충전: 정거장의 `PowerConsumer` 수요를 `DroneChargingDemandSystem`이 계산하고, 충전량은 실제 공급 비율을 사용한다.
- UI: `BuildingUI.Power`가 connection과 grid state를 표시한다.

## 수정 시 함께 확인할 영역

- pole 범위/연결 변경: parser, `PowerGridRangeUtility`, `ChunkMapSystem.PowerPoles`, placement preview, `PowerGridTopologyTests`를 함께 확인한다.
- 발전 배분 변경: grid state, coal dispatch, fuel consumption, drone charging demand, `CoalGeneratorPowerTests`, `CoalGeneratorFuelAndRestoreTests`를 확인한다.
- 소비 건물 추가: `PowerConfig.json`, spawn component 부착, 생산/행동 시스템의 supply ratio 사용, UI를 확인한다.
- pole 파괴 변경: 공급 범위 해제, topology dirty, participant reconnection을 확인한다.

## 구현상 주의사항

- topology/grid aggregate의 소유자는 `PowerGridSystem`, 셀 coverage의 소유자는 `ChunkMapSystem`이다.
- 모든 건물에 global grid ID를 공간 데이터로 기록하지 않는다. 참가자 connection은 현재 nearest pole 선택 결과다.
- 무전력 상태에서 miner/crafter의 progress와 입력/출력 버퍼를 지우지 않는다. progress 증가량만 0에 가까워진다.
- 석탄 발전기는 수요 추종 방식이며 최대 출력을 항상 연소하지 않는다.
- `PowerGridState`는 재구축 가능한 집계 데이터다. 영속 원본처럼 다른 시스템에서 임의 수정하지 않는다.
