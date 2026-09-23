using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Storage 소유권 이전 및 상태 전이 연동 테스트.
/// 벨트 아이템 -> 창고 적재(버퍼 추가, 소유권 전환, 벨트 비활성화, 공간 인덱스 제외),
/// 창고 아이템 -> 벨트 방출(버퍼 제거, 소유권 해제, 벨트 활성화 Progress=0, 공간 인덱스 복원),
/// 슬롯 예약 경합(Reservation) 및 스택 병합 규칙을 종합 검증.
/// </summary>
public class Phase3StorageOwnershipTests : EcsWorldTestFixture
{
    private SystemHandle _beltSpatialSyncHandle;
    private SystemHandle _itemSpatialSyncHandle;
    private SystemHandle _buildingSpatialSyncHandle;

    private SystemHandle _buildingInputDecisionHandle;
    private SystemHandle _buildingOutputDecisionHandle;
    private SystemHandle _storageReservationHandle;
    private SystemHandle _storageApplyHandle;
    private SystemHandle _ownershipApplyHandle;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();

        _beltSpatialSyncHandle = _world.GetOrCreateSystem(typeof(BeltSpatialSyncSystem));
        _itemSpatialSyncHandle = _world.GetOrCreateSystem(typeof(ItemSpatialSyncSystem));
        _buildingSpatialSyncHandle = _world.GetOrCreateSystem(typeof(BuildingSpatialSyncSystem));

        _buildingInputDecisionHandle = _world.GetOrCreateSystem(typeof(BuildingItemInputDecisionSystem));
        _buildingOutputDecisionHandle = _world.GetOrCreateSystem(typeof(StorageItemOutputDecisionSystem));
        _storageReservationHandle = _world.GetOrCreateSystem(typeof(BuildingStorageInputReservationSystem));
        _storageApplyHandle = _world.GetOrCreateSystem(typeof(BuildingItemStorageApplySystem));
        _ownershipApplyHandle = _world.GetOrCreateSystem(typeof(ItemOwnershipApplySystem));
    }

    private Entity CreateStorage(int2 position, int2 size, int slotCount = 8, StorageFilter? filter = null)
        => Entities.CreateStorage(position, size, DirectionEnum.Up, slotCount, filter);

    private Entity CreateBelt(int2 position, DirectionEnum direction, float speed = 2.0f)
        => Entities.CreateBelt(position, direction, speed);

    private Entity CreateBeltItem(int2 position, DirectionEnum direction, float progress, ItemTypeEnum itemType = ItemTypeEnum.Iron_Ore)
        => Entities.CreateBeltItem(position, direction, progress, 0.0f, itemType);

    private void SyncAllSpatialIndices()
    {
        _beltSpatialSyncHandle.Update(_world.Unmanaged);
        var beltFence = _world.EntityManager.CreateEntityQuery(typeof(BeltSpatialIndexFence)).GetSingletonRW<BeltSpatialIndexFence>();
        beltFence.ValueRW.Complete();

        _itemSpatialSyncHandle.Update(_world.Unmanaged);
        var itemFence = _world.EntityManager.CreateEntityQuery(typeof(ItemSpatialIndexFence)).GetSingletonRW<ItemSpatialIndexFence>();
        itemFence.ValueRW.Complete();

        _buildingSpatialSyncHandle.Update(_world.Unmanaged);
        var buildingFence = _world.EntityManager.CreateEntityQuery(typeof(BuildingSpatialIndexFence)).GetSingletonRW<BuildingSpatialIndexFence>();
        buildingFence.ValueRW.Complete();
    }

    [Test]
    public void Test01_BeltItem_DepositToStorage_SuccessfulOwnershipTransfer()
    {
        // [시나리오]
        // (0,0)에 우향 벨트, (1,0)에 1x1 창고(SlotCount=4) 배치.
        // 벨트 끝(Progress = 0.999f)에 도달한 아이템이 존재할 때 전체 파이프라인 실행:
        // InputDecision -> Reservation -> StorageApply -> OwnershipApply -> SpatialSync
        var belt = CreateBelt(new int2(0, 0), DirectionEnum.Right);
        var storage = CreateStorage(new int2(1, 0), new int2(1, 1), slotCount: 4);
        var item = CreateBeltItem(new int2(0, 0), DirectionEnum.Right, 0.999f, ItemTypeEnum.Iron_Ore);

        SyncAllSpatialIndices();

        // 1. DecisionGroup: BuildingItemInputDecisionSystem 실행
        _buildingInputDecisionHandle.Update(_world.Unmanaged);

        var decision = _entityManager.GetComponentData<BuildingItemInputDecision>(item);
        Assert.IsTrue(_entityManager.IsComponentEnabled<BuildingItemInputDecision>(item), "입고 판정 컴포넌트 활성화 필요");
        Assert.IsTrue(decision.CanDeposit, "입고 적합 판정이어야 함");
        Assert.AreEqual(storage, decision.TargetBuilding, "대상 창고 일치");
        Assert.AreEqual(-1, decision.TargetSlotIndex, "Reservation 전에는 -1이어야 함");

        // 2. ReservationGroup: BuildingStorageInputReservationSystem 실행
        _storageReservationHandle.Update(_world.Unmanaged);

        decision = _entityManager.GetComponentData<BuildingItemInputDecision>(item);
        Assert.AreEqual(0, decision.TargetSlotIndex, "빈 창고이므로 0번 슬롯 배정 성공");

        // 3. StateApplyGroup: BuildingItemStorageApplySystem 실행
        _storageApplyHandle.Update(_world.Unmanaged);

        // 창고 버퍼 확인
        var buffer = _entityManager.GetBuffer<StoredItemElement>(storage);
        Assert.AreEqual(1, buffer.Length, "창고 버퍼에 아이템이 적재되어야 함");
        Assert.AreEqual(item, buffer[0].ItemEntity);
        Assert.AreEqual(0, buffer[0].SlotIndex);

        // 벨트 이동 상태 비활성화 확인
        Assert.IsFalse(_entityManager.IsComponentEnabled<BeltMovementState>(item), "보관 상태 진입 시 BeltMovementState 비활성화");
        Assert.IsFalse(_entityManager.IsComponentEnabled<BuildingItemInputDecision>(item), "입고 완료 후 Decision 비활성화");

        // TransferOwnershipRequest 발행 확인
        Assert.IsTrue(_entityManager.IsComponentEnabled<TransferOwnershipRequest>(item), "TransferOwnershipRequest 활성화 필요");
        var req = _entityManager.GetComponentData<TransferOwnershipRequest>(item);
        Assert.AreEqual(storage, req.TargetOwner, "창고 엔티티로 소유권 이전 요청");

        // 4. StateApplyGroup: ItemOwnershipApplySystem 실행
        _ownershipApplyHandle.Update(_world.Unmanaged);

        Assert.IsFalse(_entityManager.IsComponentEnabled<TransferOwnershipRequest>(item), "Consume-on-Apply로 요청 비활성화");
        var ownership = _entityManager.GetComponentData<ItemOwnership>(item);
        Assert.IsFalse(ownership.IsWorldItem, "보관 아이템이므로 IsWorldItem = false");
        Assert.AreEqual(storage, ownership.Owner, "소유자가 창고 엔티티로 변경됨");

        // 5. SynchronizationGroup: 공간 인덱스 동기화 실행
        SyncAllSpatialIndices();

        var itemSpatialIndex = _entityManager.CreateEntityQuery(typeof(ItemSpatialIndex)).GetSingleton<ItemSpatialIndex>();
        bool foundInSpatial = itemSpatialIndex.Map.ContainsKey(new int2(0, 0));
        Assert.IsFalse(foundInSpatial, "창고에 들어간 아이템은 공간 인덱스(ItemSpatialIndex)에서 완전히 제외되어야 함");
    }

    [Test]
    public void Test02_StorageItem_OutputToBelt_SuccessfulOwnershipRelease()
    {
        // [시나리오]
        // (1,0)에 1x1 창고, (2,0)에 우향 외향 벨트 배치.
        // 창고 버퍼에 보관된 아이템(Slot 0)이 존재하고, 외향 벨트가 비어있을 때:
        // OutputDecision -> StorageApply -> OwnershipApply -> SpatialSync
        var storage = CreateStorage(new int2(1, 0), new int2(1, 1), slotCount: 4);
        var belt = CreateBelt(new int2(2, 0), DirectionEnum.Right);

        // 창고 보관 아이템 생성 (초기 상태: Stored)
        var item = _entityManager.CreateEntity(
            typeof(ItemIdentity),
            typeof(ItemOwnership),
            typeof(GridPosition),
            typeof(Direction),
            typeof(LocalTransform),
            typeof(BeltMovementState),
            typeof(BeltMovementDecision),
            typeof(BuildingItemInputDecision),
            typeof(TransferOwnershipRequest));

        _entityManager.SetComponentData(item, new ItemIdentity(ItemTypeEnum.Copper_Ore));
        _entityManager.SetComponentData(item, ItemOwnership.Stored(storage));
        _entityManager.SetComponentData(item, new GridPosition(new int2(1, 0)));
        _entityManager.SetComponentData(item, new Direction(DirectionEnum.Right));
        _entityManager.SetComponentData(item, LocalTransform.FromPosition(new float3(1f, 0f, 0f)));
        _entityManager.SetComponentData(item, new BeltMovementState(0f));
        _entityManager.SetComponentData(item, new BeltMovementDecision(0f, false));
        _entityManager.SetComponentData(item, new BuildingItemInputDecision(Entity.Null, false, -1));
        _entityManager.SetComponentData(item, new TransferOwnershipRequest(Entity.Null));

        _entityManager.SetComponentEnabled<BeltMovementState>(item, false);
        _entityManager.SetComponentEnabled<BuildingItemInputDecision>(item, false);
        _entityManager.SetComponentEnabled<TransferOwnershipRequest>(item, false);

        var buffer = _entityManager.GetBuffer<StoredItemElement>(storage);
        buffer.Add(new StoredItemElement(item, ItemTypeEnum.Copper_Ore, slotIndex: 0));

        SyncAllSpatialIndices();

        // 1. DecisionGroup: BuildingItemOutputDecisionSystem 실행
        _buildingOutputDecisionHandle.Update(_world.Unmanaged);

        var outputDecision = _entityManager.GetComponentData<BuildingItemOutputDecision>(storage);
        Assert.IsTrue(_entityManager.IsComponentEnabled<BuildingItemOutputDecision>(storage), "출고 의사결정 활성화 필요");
        Assert.IsTrue(outputDecision.CanOutput, "외향 벨트 여유가 있으므로 CanOutput = true");
        Assert.AreEqual(item, outputDecision.ItemToOutput, "방출 대상은 버퍼의 0번 아이템");
        Assert.AreEqual(new int2(2, 0), outputDecision.TargetBeltPosition, "대상 벨트 위치 일치");

        // 2. StateApplyGroup: BuildingItemStorageApplySystem 실행
        _storageApplyHandle.Update(_world.Unmanaged);

        // 창고 버퍼에서 제거 확인
        buffer = _entityManager.GetBuffer<StoredItemElement>(storage);
        Assert.AreEqual(0, buffer.Length, "방출 후 창고 버퍼에서 아이템이 제거되어야 함");

        // 아이템의 월드 컴포넌트 복원 확인
        var gridPos = _entityManager.GetComponentData<GridPosition>(item);
        Assert.AreEqual(new int2(2, 0), gridPos.Value, "외향 벨트 좌표로 GridPosition 복원");

        var beltState = _entityManager.GetComponentData<BeltMovementState>(item);
        Assert.AreEqual(0.0f, beltState.Progress, "벨트 시작점(Progress = 0.0f)으로 복원");
        Assert.IsTrue(_entityManager.IsComponentEnabled<BeltMovementState>(item), "BeltMovementState 컴포넌트 활성화");

        // TransferOwnershipRequest 발행 확인 (TargetOwner = Null: 월드 아이템 전환)
        Assert.IsTrue(_entityManager.IsComponentEnabled<TransferOwnershipRequest>(item));
        var req = _entityManager.GetComponentData<TransferOwnershipRequest>(item);
        Assert.AreEqual(Entity.Null, req.TargetOwner);

        // 3. StateApplyGroup: ItemOwnershipApplySystem 실행
        _ownershipApplyHandle.Update(_world.Unmanaged);

        var ownership = _entityManager.GetComponentData<ItemOwnership>(item);
        Assert.IsTrue(ownership.IsWorldItem, "WorldItem으로 전환 완료");
        Assert.AreEqual(Entity.Null, ownership.Owner);

        // 4. SynchronizationGroup: 공간 인덱스 동기화 실행
        SyncAllSpatialIndices();

        var itemSpatialIndex = _entityManager.CreateEntityQuery(typeof(ItemSpatialIndex)).GetSingleton<ItemSpatialIndex>();
        bool foundInSpatial = itemSpatialIndex.Map.ContainsKey(new int2(2, 0));
        Assert.IsTrue(foundInSpatial, "방출된 아이템이 벨트 위치(2,0)의 공간 인덱스(ItemSpatialIndex)에 정상 등록되어야 함");
    }

    [Test]
    public void Test03_MultipleBeltItems_ContentionOnSingleRemainingSlot_OnlyOneReserved()
    {
        // [시나리오]
        // 잔여 슬롯이 1개(SlotCount = 1)뿐인 창고에 2개의 벨트 아이템(좌/우 벨트 끝)이 동시에 입고를 시도.
        // Reservation 시스템이 슬롯 경합을 직렬화하여 1개만 슬롯 0을 선점하고, 나머지 1개는 만석으로 거부(대기)되어야 함.
        var storage = CreateStorage(new int2(1, 1), new int2(1, 1), slotCount: 1);

        // 왼쪽 벨트(0,1 -> 우향)와 아래쪽 벨트(1,0 -> 상향)
        CreateBelt(new int2(0, 1), DirectionEnum.Right);
        CreateBelt(new int2(1, 0), DirectionEnum.Up);

        var item1 = CreateBeltItem(new int2(0, 1), DirectionEnum.Right, 0.999f, ItemTypeEnum.Iron_Ore);
        var item2 = CreateBeltItem(new int2(1, 0), DirectionEnum.Up, 0.999f, ItemTypeEnum.Copper_Ore);

        SyncAllSpatialIndices();

        // 1. DecisionGroup 실행: 둘 다 입고 가능 판정 (TargetSlotIndex = -1)
        _buildingInputDecisionHandle.Update(_world.Unmanaged);

        Assert.IsTrue(_entityManager.GetComponentData<BuildingItemInputDecision>(item1).CanDeposit);
        Assert.IsTrue(_entityManager.GetComponentData<BuildingItemInputDecision>(item2).CanDeposit);

        // 2. ReservationGroup 실행: 슬롯 경합 해결
        _storageReservationHandle.Update(_world.Unmanaged);

        var d1 = _entityManager.GetComponentData<BuildingItemInputDecision>(item1);
        var d2 = _entityManager.GetComponentData<BuildingItemInputDecision>(item2);
        bool e1 = _entityManager.IsComponentEnabled<BuildingItemInputDecision>(item1);
        bool e2 = _entityManager.IsComponentEnabled<BuildingItemInputDecision>(item2);

        // 정확히 하나만 배정 성공(Slot 0)하고, 다른 하나는 실패(CanDeposit=false, Enabled=false)해야 함
        int successCount = 0;
        int failCount = 0;

        if (e1 && d1.CanDeposit && d1.TargetSlotIndex == 0) successCount++;
        else failCount++;

        if (e2 && d2.CanDeposit && d2.TargetSlotIndex == 0) successCount++;
        else failCount++;

        Assert.AreEqual(1, successCount, "정확히 1개의 아이템만 슬롯 0을 선점해야 함");
        Assert.AreEqual(1, failCount, "경합에서 밀린 1개의 아이템은 배정 실패 처리되어야 함");

        // 3. StateApplyGroup 실행
        _storageApplyHandle.Update(_world.Unmanaged);
        _ownershipApplyHandle.Update(_world.Unmanaged);

        var buffer = _entityManager.GetBuffer<StoredItemElement>(storage);
        Assert.AreEqual(1, buffer.Length, "창고 버퍼에는 선점된 1개의 아이템만 수납되어야 함");
    }

    [Test]
    public void Test04_StackMerging_And_NewSlotAllocation()
    {
        // [시나리오]
        // 창고 SlotCount = 2, ItemConfig.DefaultMaxStack = 50.
        // Slot 0에 이미 Iron_Ore 1개가 있는 상태.
        // 추가로 Iron_Ore 1개(item1)와 Copper_Ore 1개(item2)가 동시에 입고를 시도:
        // - item1(Iron_Ore)은 Slot 0의 기존 스택(여유 있음)에 합쳐져 Slot 0 배정.
        // - item2(Copper_Ore)는 타입이 다르므로 비어 있는 Slot 1에 신규 배정.
        var storage = CreateStorage(new int2(1, 0), new int2(1, 1), slotCount: 2);
        var belt = CreateBelt(new int2(0, 0), DirectionEnum.Right);

        // 기존 보관 아이템 생성 (Slot 0, Iron_Ore)
        var existingItem = _entityManager.CreateEntity(typeof(ItemIdentity), typeof(ItemOwnership));
        _entityManager.SetComponentData(existingItem, new ItemIdentity(ItemTypeEnum.Iron_Ore));
        _entityManager.SetComponentData(existingItem, ItemOwnership.Stored(storage));
        var buffer = _entityManager.GetBuffer<StoredItemElement>(storage);
        buffer.Add(new StoredItemElement(existingItem, ItemTypeEnum.Iron_Ore, slotIndex: 0));

        // 유입 아이템 2개 (item1: Iron_Ore, item2: Copper_Ore)
        var item1 = CreateBeltItem(new int2(0, 0), DirectionEnum.Right, 0.999f, ItemTypeEnum.Iron_Ore);
        var item2 = CreateBeltItem(new int2(0, 0), DirectionEnum.Right, 0.999f, ItemTypeEnum.Copper_Ore);

        // 직접 Decision을 CanDeposit = true, TargetBuilding = storage로 세팅
        _entityManager.SetComponentData(item1, new BuildingItemInputDecision(storage, true, -1));
        _entityManager.SetComponentEnabled<BuildingItemInputDecision>(item1, true);

        _entityManager.SetComponentData(item2, new BuildingItemInputDecision(storage, true, -1));
        _entityManager.SetComponentEnabled<BuildingItemInputDecision>(item2, true);

        // Reservation 실행
        _storageReservationHandle.Update(_world.Unmanaged);

        var d1 = _entityManager.GetComponentData<BuildingItemInputDecision>(item1);
        var d2 = _entityManager.GetComponentData<BuildingItemInputDecision>(item2);

        Assert.AreEqual(0, d1.TargetSlotIndex, "Iron_Ore는 기존 Iron_Ore가 있는 Slot 0에 스택 병합 배정");
        Assert.AreEqual(1, d2.TargetSlotIndex, "Copper_Ore는 새로운 빈 슬롯인 Slot 1에 신규 배정");

        // Apply 실행
        _storageApplyHandle.Update(_world.Unmanaged);
        _ownershipApplyHandle.Update(_world.Unmanaged);

        buffer = _entityManager.GetBuffer<StoredItemElement>(storage);
        Assert.AreEqual(3, buffer.Length, "기존 1개 + 신규 2개 = 총 3개 아이템 보관");
        Assert.AreEqual(0, buffer[1].SlotIndex, "item1은 Slot 0");
        Assert.AreEqual(1, buffer[2].SlotIndex, "item2는 Slot 1");
    }
}
