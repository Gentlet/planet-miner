# 메인 스테이션 드론 작업 범위 좌우 비대칭 문제

## 1. 개요
- **구분**: 버그 / 알려진 문제 (Bug / Known Issue)
- **상태**: 분석 완료 및 수정 대기 (Open)
- **관련 시스템**: `DroneStationNetworkSystem`, `DroneStationRangeUtility`, `MainFacilityBootstrapSystem`

---

## 2. 현상 및 문제점
- 메인 스테이션(Main Station / Main Facility)의 드론 작업 가능 범위(커버리지)가 **우측 방향은 길고, 좌측 방향은 짧게 비대칭**으로 적용되는 현상이 발생합니다.
- 사용자가 체감하기에 중앙을 기준으로 대칭적인 범위를 가져야 하나 편향되어 작동합니다.

---

## 3. 원인 분석
1. **청크(Chunk) 기반 범위 스냅 및 바운드 계산**:
   - [`DroneStationRangeUtility.cs`](file:///c:/Projects/unity/PlanetMiner/planet%20miner/Assets/Scripts/Components/Drones/DroneStationRangeUtility.cs)에서 작업 범위(`GridBounds`)를 계산할 때, 시설의 Anchor 위치가 속한 단일 청크(`stationChunk`)를 기준으로 `[-range, +range]` 청크를 포함하도록 계산합니다.
     ```csharp
     int2 stationChunk = ChunkUtility.ToChunkPosition(stationCell);
     int2 minimumChunk = stationChunk - normalizedRange;
     int2 maximumChunk = stationChunk + normalizedRange;
     ```
2. **다중 타일 건물 크기 및 앵커 오프셋 편향**:
   - 메인 시설은 크기가 다중 셀(예: 3x3 등)을 차지할 수 있으나, 기준 좌표(`stationCell` / 앵커)가 좌하단(Min)에 위치하는 경우:
     - 앵커가 청크 내 좌측/하단 쪽에 걸치게 되면 우측 청크로 넘어가는 경계에 따라 실제 시설 중심 대비 우측으로 더 많은 청크/셀이 포함될 수 있습니다.
3. **셀 단위 바운드 변환 오차**:
   - `maximumCell = (maximumChunk + new int2(1)) * GameConstants.chunkSize - new int2(1);` 계산 시 청크 단위 정렬로 인해 시설 중심점 대비 비대칭 마진이 발생합니다.

---

## 4. 관련 코드 위치
- [`DroneStationRangeUtility.cs`](file:///c:/Projects/unity/PlanetMiner/planet%20miner/Assets/Scripts/Components/Drones/DroneStationRangeUtility.cs)
  - `GetActivityBounds(int2 stationCell, int2 activityRangeInChunks)`
- [`DroneStationNetworkSystem.Registration.cs`](file:///c:/Projects/unity/PlanetMiner/planet%20miner/Assets/Scripts/Systems/Drones/DroneStationNetworkSystem.Registration.cs)
  - `SynchronizeStationRegistration()`
- [`MainFacilityBootstrapSystem.cs`](file:///c:/Projects/unity/PlanetMiner/planet%20miner/Assets/Scripts/Systems/Power/MainFacilityBootstrapSystem.cs)
  - 메인 스테이션 생성 및 `GridPosition`, `DroneStation` 컴포넌트 초기화

---

## 5. 해결 방안 (검토)
1. **중심점 기반 범위 계산 도입**:
   - 스테이션의 앵커 대신 시설의 중앙 타일 좌표(또는 건물 크기(`BuildingFootprint` 등)를 고려한 중심 셀)를 기준으로 청크/셀 범위를 계산하도록 보정.
2. **청크 단위 스냅 vs 셀 반경 방식 검토**:
   - 청크 정렬이 필수적인 경우 중심 셀이 속한 청크 기준으로 정렬하거나, 셀 단위 대칭 반경을 청크 바운드에 대칭적으로 포함하도록 보정.
