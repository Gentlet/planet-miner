# 드론 스테이션 철거 시 보관 드론 처리 개선

## 1. 개요
- **구분**: 기능 개선 (Feedback / Improvement)
- **상태**: 검토 및 구현 대기 (Open)
- **관련 시스템**: `DroneIdentityConversionSystem`, `BuildingDestroySystem`, `DroneRecoverySystem`

---

## 2. 현상 및 문제점
- 현재 드론 스테이션을 철거(Demolition)할 경우, 해당 스테이션에 보관 중이던 드론(`StoredDroneElement`)이 `ItemTypeEnum.Drone` 형태의 **월드 아이템으로 변환되어 바닥에 드롭**되고, 이후 회수 작업(Recovery Task)을 통해 수거되도록 구현되어 있습니다.
- 이로 인해 철거 즉시 바닥에 아이템이 쏟아져 나와 공간을 차지하고, 드론들이 다시 회수하러 가는 번거로운 과정이 발생합니다.

---

## 3. 요구사항 (피드백 내용)
- 드론 스테이션 철거 시 보관 중이던 드론은 **아이템 형태로 바닥에 떨어지지 않고, 주변의 다른 드론 스테이션(또는 메인 스테이션)으로 이동 및 재배치**되도록 변경합니다.

---

## 4. 관련 코드 위치
1. **드론 엔티티 변환 및 드롭 처리**:
   - [`DroneIdentityConversionSystem.Recovery.cs`](file:///c:/Projects/unity/PlanetMiner/planet%20miner/Assets/Scripts/Systems/Drones/DroneIdentityConversionSystem.Recovery.cs)
     - `TryRestoreStoredDronesForStationDestruction()`: 철거 시 드론을 아이템 형태로 변환 및 드롭 위치 검색
     - `TryConvertStoredDroneToWorldItem()`: 활성 드론 컴포넌트를 제거하고 `Item` 컴포넌트 부여
2. **건물 파괴 및 철거 처리**:
   - [`BuildingDestroySystem.cs`](file:///c:/Projects/unity/PlanetMiner/planet%20miner/Assets/Scripts/Systems/Buildings/BuildingDestroySystem.cs)
     - 라인 132~142: `_droneIdentityConversion.TryRestoreStoredDronesForStationDestruction()` 호출
3. **드론 스테이션 용량 및 보관 유틸리티**:
   - [`DroneStationStorageUtility.cs`](file:///c:/Projects/unity/PlanetMiner/planet%20miner/Assets/Scripts/Components/Drones/DroneStationStorageUtility.cs)

---

## 5. 개선 방향 및 구현 방안 (예시)
1. **타깃 스테이션 탐색**:
   - 철거되는 스테이션 반경 내에서 **여유 슬롯이 있는 드론 스테이션(메인 스테이션 포함)**을 검색.
2. **이동 상태 전환**:
   - 보관 중이던 드론을 활성 상태(`ActiveDrone`)로 전환하고 목적지 스테이션으로 비행 이동(`DroneStateEnum.MovingToStation` 등).
3. **예외 처리 (대체 스테이션 부재 / 슬롯 부족 시)**:
   - 주변에 수용 가능한 스테이션이 전혀 없는 경우의 Fallback 정책 결정 필요 (예: 메인 스테이션 강제 이동 or 임시 오버플로우 or 기존 방식대로 아이템 드롭).
