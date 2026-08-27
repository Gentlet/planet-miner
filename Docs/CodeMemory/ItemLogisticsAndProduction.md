# 아이템 물류와 생산

## 목적과 책임

이 영역은 월드 아이템의 셀 소유권과 벨트 이동, 건물 소유 아이템의 저장·복원, splitter/merger 분배, storage 입출력, miner/crafter 생산을 연결한다.

## 아이템의 두 상태

### 월드 아이템

월드에 존재하는 운반 가능한 아이템은 `Item`, `LocalTransform`, `GridPosition`, enableable `ItemCellChanged`를 가진다. `ChunkMapSystem` 셀 목록과 `Entity -> cell` 역방향 인덱스에도 등록되어야 한다.

### 건물 소유 아이템

건물 안의 아이템 엔티티는 `StoredItem { owner }`와 `Disabled`를 가지며 월드 공간 인덱스에서 빠진다. owner는 다음 버퍼 중 하나로 엔티티를 보유한다.

- `StoredItemElement`: 입력/일반 저장품
- `ProducedItemElement`: miner/crafter 생산 출력 대기품

수량은 별도 숫자만 올리는 방식이 아니라 실제 item entity 개수로 표현한다.

## 주요 시스템

- `ItemTrackingSystem`: `ItemCellChanged`를 소비하여 transform과 `GridPosition`, `ChunkMapSystem` 소유권을 동기화한다. same-frame용 register/move/apply API도 제공한다.
- `ItemStorageSystem` + `.Transfer`: 월드 아이템 저장, 소유 아이템 복원/이관/소비, owner buffer와 `StoredItem`/`Disabled` 구조 변경을 원자적으로 처리한다.
- `ItemSpawnSystem`: miner/crafter의 `ItemSpawnRequest`를 disabled stored item으로 만들고 `ProducedItemElement`에 넣는다. 시작 아이템은 `StartingItemSpawnRequest`에서 `StoredItemElement`로 들어간다.
- `WorldItemSpawnSystem`: 파괴/취소 반환 요청을 실제 월드 아이템으로 만들고 즉시 공간 등록하며 필요하면 회수 작업 요청을 만든다.
- `BeltMoveSystem` + `.Job`: 활성 벨트 셀을 기준으로 아이템 transform을 이동하고 셀 경계를 넘는 항목에 `ItemCellChanged`를 enable한다.
- `BuildingUI.Belt`: 선택 벨트 셀의 `ChunkMapSystem` 월드 아이템을 종류별로 집계하고, 실제 이동 계산에 사용되는 `Belt.speed`를 최대 이동 속도(칸/초)로 표시한다.
- `SplitterSystem`, `MergerSystem`: 자체 저장 버퍼 없이 셀에 이미 존재하는 월드 아이템을 immediate move한다.
- `StorageSystem`: footprint 전체에서 월드 아이템을 받아 FIFO `StoredItemElement`로 저장하고 바깥 방향 벨트로 oldest item을 복원한다.
- `MiningSystem`: footprint 자원을 선택·소모하고 생산 요청을 만들며 `ProducedItemElement`를 벨트로 출력한다.
- `CrafterSystem`: 레시피 입력을 저장하고, 진행도를 갱신하고, 재료를 소비한 뒤 출력 item 요청을 만들며 생산 버퍼를 벨트로 출력한다.
- `CrafterRecipeChangeSystem`: 레시피 변경, 진행도 취소, 상태 재평가, 새 레시피에 맞지 않는 저장품의 드론 제거 요청을 처리한다.

## 월드 물류 실행 흐름

1. 이전 시스템이 item transform을 옮기고 `ItemCellChanged`를 enable한다.
2. `ItemTrackingSystem`이 이전 셀 소유권을 해제하고 새 셀에 등록한 뒤 `GridPosition`을 갱신한다.
3. `BeltMoveSystem`이 셀 내 전방 아이템부터 spacing과 다음 셀 조건을 고려해 이동한다.
4. `SplitterSystem`은 pending belt move를 flush한 뒤 `forward -> right -> left`의 상대 순서에서 가능한 출구로 보낸다. 성공할 때만 cursor를 진행한다.
5. `MergerSystem`은 `back -> left -> right` 상대 입력 순서에서 가장 가까운 item을 forward 벨트로 보낸다. 성공할 때만 cursor를 진행한다.
6. `StorageSystem`, `CoalGeneratorInputSystem`, `MiningSystem`, `CrafterSystem` 등 건물 시스템은 pending item changes를 적용하고 footprint 셀에서 입력을 수집한다.

`BuildingInputCollectionUtility`는 회전 footprint 전체의 item을 중복 제거하고 실제 source cell과 거리 정보를 유지한다. `BuildingBeltConnectionUtility`는 footprint 바깥을 향하는 벨트만 출력 연결로 인정하고, `BuildingOutputCursor`로 성공한 출력 위치를 순환한다.

## 저장 용량

`Storage.capacity`은 item entity 수가 아니라 stack slot 수다. `ItemStorageLimitElement`가 종류별 한 slot의 최대 개수를 정의한다. `StorageCapacityUtility`는 기존 partial stack, 새 stack 필요량, 드론 예약 용량, 정거장 stored drone 수를 함께 계산한다.

Storage는 buffer append 순서를 FIFO로 사용하며 `StoredItemElement[0]`부터 출력한다. 정거장도 같은 Storage/StoredItem 구조를 사용하지만 belt output은 하지 않는다.

## 생산 흐름

### Miner

1. 회전 footprint의 자원 후보를 읽는다.
2. 출력 대기 버퍼가 비어 있을 때 유효 자원을 선택하고 전력 `supplyRatio`가 반영된 delta time으로 timer를 진행한다.
3. 자원량을 감소시키고 고갈 시 공간 인덱스와 자원 엔티티를 제거한다.
4. `ItemSpawnRequest`를 만들어 대응 item을 `ProducedItemElement`에 넣는다.
5. 바깥 방향 벨트가 비어 있으면 `ItemStorageSystem.TryRestoreItemImmediate<ProducedItemElement>`로 월드에 출력한다.

### Crafter

1. 선택 레시피의 ingredient만 footprint에서 저장한다.
2. 예외 item, 재료 부족, 출력 대기 상태를 `CrafterStateEnum`으로 표시한다.
3. 전력 비율이 반영된 시간으로 progress를 진행한다.
4. 완성 시 owned ingredient를 `ItemStorageSystem`으로 소비하고 `ItemSpawnRequest`를 만든다.
5. 이미 만들어진 output은 레시피가 바뀌어도 `ProducedItemElement`에서 계속 출력된다.

새 crafter는 기본적으로 `ItemTypeEnum.None`, `CrafterStateEnum.NoRecipe`이며 UI에서 첫 레시피를 명시적으로 선택한다.

## 다른 시스템과의 의존 관계

- 공간과 벨트 조회: `ChunkMapSystem`
- footprint와 경계: `BuildingFootprintUtility`, `BuildingInputCollectionUtility`, `BuildingBeltConnectionUtility`
- 전력 속도: `PowerProductionUtility`
- 레시피/stack 한도: `CrafterConfig` 버퍼
- 공사·드론: 같은 `ItemStorageSystem` API와 reservation marker를 공유한다.

## 수정 시 함께 확인할 영역

- item ownership 변경: owner buffer, `StoredItem`, `Disabled`, 공간 역방향 인덱스, ECB 재생 시점을 함께 확인한다.
- belt 이동 변경: 활성 벨트 set, 셀 내 정렬, spacing, `ItemCellChanged`, splitter/merger flush 순서를 확인한다.
- 저장 용량 변경: 일반 Storage, DroneStation, 드론 destination reservation, BuildingUI slot 렌더링을 함께 확인한다.
- 레시피 변경: parser, ingredient acceptance, 예외 item 상태, 드론 제거 요청, UI 버튼과 표시를 함께 확인한다.
- 생산 속도 변경: `PowerProductionUtility`, progress 보존, output blocked 상태를 함께 확인한다.

## 구현상 주의사항

- 구조 변경 후 기존 `DynamicBuffer` handle은 무효가 될 수 있다. item을 하나 저장/복원할 때마다 owner buffer를 다시 얻는다.
- 건물 시스템에서 `GridPosition`과 `ChunkMapSystem`을 따로 직접 변경하지 않는다. immediate API가 둘을 함께 갱신한다.
- 월드 셀의 item 개수는 고정 제한이 없고, 실제 배치 가능 여부는 `GameConstants.itemSpacing`으로 판단한다.
- splitter/merger는 inventory가 아니다. 해당 셀의 월드 아이템을 직접 라우팅한다.
- 생산 결과는 spawn 요청을 거치므로 요청 생성 프레임과 실제 `ProducedItemElement` 추가 프레임을 동일하게 가정하지 않는다.
