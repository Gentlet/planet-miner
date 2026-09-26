using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

public class Phase1ItemAuthoringPrefabTests : EcsWorldTestFixture
{
    private SystemHandle _lifecycleHandle;
    private SystemHandle _ownershipHandle;
    private EndStateApplyEntityCommandBufferSystem _endStateApplyEcb;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();

        _lifecycleHandle = _world.GetOrCreateSystem(typeof(ItemLifecycleApplySystem));
        _ownershipHandle = _world.GetOrCreateSystem(typeof(ItemOwnershipApplySystem));
        _endStateApplyEcb = _world.GetOrCreateSystemManaged<EndStateApplyEntityCommandBufferSystem>();
    }

    private void RunStateApplyPhase()
    {
        _lifecycleHandle.Update(_world.Unmanaged);
        _ownershipHandle.Update(_world.Unmanaged);
        _endStateApplyEcb.Update();
    }

    [Test]
    public void Test01_ItemPrefabDatabaseAuthoring_PopulateFromResources_LoadsPrefabs()
    {
        // Authoring GameObject 생성
        var go = new GameObject("TestItemPrefabAuthoring");
        var authoring = go.AddComponent<ItemPrefabDatabaseAuthoring>();

        try
        {
            // Resources/Prefabs/Item 에서 프리팹 자동 수집 실행
            authoring.PopulateFromResources();

            // Resources에 존재하는 Iron_Ore, Copper_Ore, Coal 등이 리스트에 채워졌는지 확인
            Assert.Greater(authoring.Prefabs.Count, 0, "Resources/Prefabs/Item 폴더에서 프리팹이 검색되어 채워져야 함");

            bool foundIronOre = false;
            for (int i = 0; i < authoring.Prefabs.Count; i++)
            {
                if (authoring.Prefabs[i].Type == ItemTypeEnum.Iron_Ore)
                {
                    foundIronOre = true;
                    Assert.IsNotNull(authoring.Prefabs[i].Prefab, "프리팹 에셋 참조가 null이 아니어야 함");
                }
            }
            Assert.IsTrue(foundIronOre, "Iron_Ore Item 프리팹이 성공적으로 매핑되어야 함");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void Test02_SpawnWorldItem_WithPrefab_InstantiatesPrefabAndExcludesDisableRendering()
    {
        // 1. 프리팹 및 프리팹 데이터베이스 엔티티 준비 (베이킹된 프리팹은 LocalTransform을 보유)
        var mockPrefab = _entityManager.CreateEntity(typeof(Prefab), typeof(ItemIdentity), typeof(LocalTransform));
        _entityManager.SetComponentData(mockPrefab, new ItemIdentity(ItemTypeEnum.Iron_Ore));

        var dbEntity = _entityManager.CreateEntity(typeof(ItemPrefabDatabase));
        var buffer = _entityManager.AddBuffer<ItemPrefabElement>(dbEntity);
        buffer.Add(new ItemPrefabElement(ItemTypeEnum.Iron_Ore, mockPrefab));

        // 2. 월드 아이템 스폰 요청 생성
        var reqEntity = _entityManager.CreateEntity(typeof(SpawnItemRequest));
        _entityManager.SetComponentData(reqEntity, new SpawnItemRequest
        {
            ItemType = ItemTypeEnum.Iron_Ore,
            Position = new int2(10, 20),
            Destination = ItemSpawnDestination.World,
            TargetOwner = Entity.Null
        });

        // 3. StateApply 실행
        RunStateApplyPhase();

        // 4. 검증: 월드 아이템 엔티티 생성, DisableRendering 미부착(렌더링 활성), 정확한 좌표 설정 확인
        var query = _entityManager.CreateEntityQuery(typeof(ItemIdentity), typeof(GridPosition), typeof(ItemOwnership));
        Assert.AreEqual(1, query.CalculateEntityCount(), "아이템 엔티티가 1개 생성되어야 함");

        var spawnedItem = query.GetSingletonEntity();
        Assert.AreEqual(ItemTypeEnum.Iron_Ore, _entityManager.GetComponentData<ItemIdentity>(spawnedItem).Type);
        Assert.AreEqual(new int2(10, 20), _entityManager.GetComponentData<GridPosition>(spawnedItem).Value);
        Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(spawnedItem).IsWorldItem);
        Assert.IsFalse(_entityManager.HasComponent<DisableRendering>(spawnedItem), "월드 아이템은 DisableRendering 컴포넌트가 없어야 함 (화면 렌더링)");
    }

    [Test]
    public void Test03_SpawnStoredItem_WithPrefab_AddsDisableRendering()
    {
        // 1. 모의 프리팹 및 프리팹 DB (베이킹된 프리팹은 LocalTransform을 보유)
        var mockPrefab = _entityManager.CreateEntity(typeof(Prefab), typeof(ItemIdentity), typeof(LocalTransform));
        _entityManager.SetComponentData(mockPrefab, new ItemIdentity(ItemTypeEnum.Coal));

        var dbEntity = _entityManager.CreateEntity(typeof(ItemPrefabDatabase));
        var buffer = _entityManager.AddBuffer<ItemPrefabElement>(dbEntity);
        buffer.Add(new ItemPrefabElement(ItemTypeEnum.Coal, mockPrefab));

        // 2. 수납 대상 창고 엔티티
        var storageEntity = _entityManager.CreateEntity();
        _entityManager.AddBuffer<StoredItemElement>(storageEntity);

        // 3. 수납 스폰 요청
        var reqEntity = _entityManager.CreateEntity(typeof(SpawnItemRequest));
        _entityManager.SetComponentData(reqEntity, new SpawnItemRequest
        {
            ItemType = ItemTypeEnum.Coal,
            Position = new int2(5, 5),
            Destination = ItemSpawnDestination.Storage,
            TargetOwner = storageEntity,
            TargetSlotIndex = 0
        });

        // 4. StateApply 실행
        RunStateApplyPhase();

        // 5. 검증: Stored 상태 및 DisableRendering 부착 확인
        var storedBuffer = _entityManager.GetBuffer<StoredItemElement>(storageEntity);
        Assert.AreEqual(1, storedBuffer.Length, "창고 버퍼에 아이템이 보관되어야 함");
        var itemEntity = storedBuffer[0].ItemEntity;

        Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(itemEntity).IsStored);
        Assert.IsTrue(_entityManager.HasComponent<DisableRendering>(itemEntity), "수납된 아이템은 DisableRendering 컴포넌트가 부착되어 렌더링이 비활성화되어야 함");
    }

    [Test]
    public void Test04_TransferOwnership_StoredToWorld_RemovesDisableRendering()
    {
        // 1. 수납된 아이템 엔티티 생성 (초기 상태: Stored + DisableRendering)
        var storage = _entityManager.CreateEntity();
        var item = _entityManager.CreateEntity(
            typeof(ItemIdentity),
            typeof(ItemOwnership),
            typeof(GridPosition),
            typeof(DisableRendering),
            typeof(TransferOwnershipRequest)
        );
        _entityManager.SetComponentData(item, ItemOwnership.Stored(storage));
        _entityManager.SetComponentData(item, new TransferOwnershipRequest(Entity.Null));
        _entityManager.SetComponentEnabled<TransferOwnershipRequest>(item, true);

        // 2. StateApply 실행 (소유권 이전)
        RunStateApplyPhase();

        // 3. 검증: 월드 아이템으로 전환되고 DisableRendering이 제거되어 렌더링 활성화됨
        Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(item).IsWorldItem);
        Assert.IsFalse(_entityManager.HasComponent<DisableRendering>(item), "월드로 방출 시 DisableRendering이 제거되어야 함");
        Assert.IsFalse(_entityManager.IsComponentEnabled<TransferOwnershipRequest>(item), "요청은 소비되어 비활성화되어야 함");
    }

    [Test]
    public void Test05_TransferOwnership_WorldToStored_AddsDisableRendering()
    {
        // 1. 월드 아이템 엔티티 생성 (초기 상태: WorldItem, DisableRendering 없음)
        var storage = _entityManager.CreateEntity();
        var item = _entityManager.CreateEntity(
            typeof(ItemIdentity),
            typeof(ItemOwnership),
            typeof(GridPosition),
            typeof(TransferOwnershipRequest)
        );
        _entityManager.SetComponentData(item, ItemOwnership.WorldItem);
        _entityManager.SetComponentData(item, new TransferOwnershipRequest(storage));
        _entityManager.SetComponentEnabled<TransferOwnershipRequest>(item, true);

        // 2. StateApply 실행 (소유권 이전)
        RunStateApplyPhase();

        // 3. 검증: Stored로 전환되고 DisableRendering이 부착되어 렌더링 비활성화됨
        Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(item).IsStored);
        Assert.IsTrue(_entityManager.HasComponent<DisableRendering>(item), "시설 수납 시 DisableRendering이 추가되어야 함");
        Assert.IsFalse(_entityManager.IsComponentEnabled<TransferOwnershipRequest>(item), "요청은 소비되어 비활성화되어야 함");
    }

    [Test]
    public void Test06_SpawnItem_MissingPrefab_FallsBackToSimulationArchetype()
    {
        // 프리팹 DB 버퍼에 Stone이 등록되지 않은 상태
        var dbEntity = _entityManager.CreateEntity(typeof(ItemPrefabDatabase));
        _entityManager.AddBuffer<ItemPrefabElement>(dbEntity);

        var reqEntity = _entityManager.CreateEntity(typeof(SpawnItemRequest));
        _entityManager.SetComponentData(reqEntity, new SpawnItemRequest
        {
            ItemType = ItemTypeEnum.Stone,
            Position = new int2(3, 4),
            Destination = ItemSpawnDestination.World,
            TargetOwner = Entity.Null
        });

        // StateApply 실행
        RunStateApplyPhase();

        // 검증: 프리팹이 없어도 Safe Fallback 아키타입으로 엔티티가 정상 생성되어 시뮬레이션 연속성 보장
        var query = _entityManager.CreateEntityQuery(typeof(ItemIdentity), typeof(GridPosition), typeof(ItemOwnership));
        Assert.AreEqual(1, query.CalculateEntityCount(), "Safe Fallback으로 아이템이 1개 생성되어야 함");

        var spawnedItem = query.GetSingletonEntity();
        Assert.AreEqual(ItemTypeEnum.Stone, _entityManager.GetComponentData<ItemIdentity>(spawnedItem).Type);
        Assert.AreEqual(new int2(3, 4), _entityManager.GetComponentData<GridPosition>(spawnedItem).Value);
    }
}
