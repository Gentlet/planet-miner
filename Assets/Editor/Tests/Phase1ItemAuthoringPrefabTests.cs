using System.Text.RegularExpressions;
using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// 역할·목적: 아이템 Authoring/Resources 목록과 생성 실패 정책에 대한 NUnit EditMode 회귀 검증.
/// 입력·검사: 임시 Authoring과 ECS DB/요청으로 자산 참조·종류 일치/누락 거부를 검사한다. 실제 SubScene 베이킹/장면 렌더링은 범위 밖이다.
/// 수명: EcsWorldTestFixture가 각 사례의 독립 World를 준비하고 종료 시 해제한다.
/// </summary>
public class Phase1ItemAuthoringPrefabTests : EcsWorldTestFixture
{
    private SystemHandle _lifecycleHandle;
    private SystemHandle _ownershipHandle;
    private EndBuildingEntityCommandBufferSystem _endStateApplyEcb;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();

        _lifecycleHandle = _world.GetOrCreateSystem(typeof(ItemLifecycleApplySystem));
        _ownershipHandle = _world.GetOrCreateSystem(typeof(ItemOwnershipApplySystem));
        _endStateApplyEcb = _world.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>();
    }

    private void RunStateApplyPhase()
    {
        // ECB 재생 이후 실물 생성/렌더 태그를 검사하며 Authoring 목록 편집과 런타임 생성은 구분한다.
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

    [TestCase(false)]
    [TestCase(true)]
    public void Test06_SpawnItem_MissingPrefab_StrictFail_RejectsSpawning(bool missingDatabase)
    {
        LogAssert.Expect(LogType.Error, new Regex(".*Missing prefab for item type.*"));

        // 프리팹 DB 버퍼에 Stone이 등록되지 않은 상태
        if (!missingDatabase)
        {
            var dbEntity = _entityManager.CreateEntity(typeof(ItemPrefabDatabase));
            _entityManager.AddBuffer<ItemPrefabElement>(dbEntity);
        }

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

        // 검증: Strict Fail 정책에 따라 프리팹 누락 시 아이템이 생성되지 않고 스폰 거부됨
        var query = _entityManager.CreateEntityQuery(typeof(ItemIdentity), typeof(GridPosition), typeof(ItemOwnership));
        Assert.AreEqual(0, query.CalculateEntityCount(), "프리팹이 누락된 경우 아이템이 생성되지 않아야 함");

        // 요청 엔티티는 Consume-on-Apply로 정상 파괴됨
        Assert.IsFalse(_entityManager.Exists(reqEntity), "스폰 요청 엔티티는 소비되어 파괴되어야 함");
        using var errors = _entityManager.CreateEntityQuery(typeof(SimulationFatalError));
        Assert.AreEqual(1, errors.CalculateEntityCount());
    }

    [Test]
    public void ResourcesItemPrefabs_CoverEveryItemType_WithMatchingAuthoring()
    {
        var prefabs = Resources.LoadAll<GameObject>("Prefabs/Item");
        var registered = new System.Collections.Generic.HashSet<ItemTypeEnum>();
        foreach (var prefab in prefabs)
        {
            var authoring = prefab.GetComponent<ItemAuthoring>();
            Assert.IsNotNull(authoring, prefab.name);
            Assert.AreNotEqual(ItemTypeEnum.None, authoring.Type, prefab.name);
            Assert.IsTrue(registered.Add(authoring.Type), "Duplicate item authoring: " + prefab.name);
        }
        foreach (ItemTypeEnum type in System.Enum.GetValues(typeof(ItemTypeEnum)))
        {
            if (type == ItemTypeEnum.None)
            {
                continue;
            }
            Assert.IsTrue(registered.Contains(type), "Missing item prefab: " + type);
        }
    }
}
