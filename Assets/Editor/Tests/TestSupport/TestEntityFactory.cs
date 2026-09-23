using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace PlanetMiner.Tests
{
    /// <summary>
    /// 테스트에서 사용하는 대표 ECS Entity 구성을 한 곳에서 관리.
    ///
    /// 기능 컴포넌트가 추가/삭제될 때 각 테스트 파일의 Create* 헬퍼를 반복 수정하지 않고
    /// 이 Factory만 갱신하는 것이 목적.
    /// </summary>
    public sealed class TestEntityFactory
    {
        private readonly EntityManager _entityManager;

        public TestEntityFactory(EntityManager entityManager)
        {
            _entityManager = entityManager;
        }

        public Entity CreateResourceNode(
            int2 position,
            ItemTypeEnum resourceType,
            int amount)
        {
            var entity = _entityManager.CreateEntity(
                typeof(ResourceNode),
                typeof(GridPosition));

            _entityManager.SetComponentData(entity, new ResourceNode(resourceType, amount));
            _entityManager.SetComponentData(entity, new GridPosition(position));
            return entity;
        }

        public Entity CreateBelt(
            int2 position,
            DirectionEnum direction,
            float speed = 2.0f)
        {
            var entity = _entityManager.CreateEntity(
                typeof(GridPosition),
                typeof(Direction),
                typeof(BeltComponent));

            _entityManager.SetComponentData(entity, new GridPosition(position));
            _entityManager.SetComponentData(entity, new Direction(direction));
            _entityManager.SetComponentData(entity, new BeltComponent(speed));
            return entity;
        }

        /// <summary>
        /// 벨트 위 WorldItem의 공통 테스트 구성을 생성.
        /// 실제 Item lifecycle에 가까운 이동/입고/소유권 관련 컴포넌트를 함께 보유하며,
        /// 이동 상태만 활성화하고 일회성 요청/입고 Decision은 비활성 상태로 시작.
        /// </summary>
        public Entity CreateBeltItem(
            int2 position,
            DirectionEnum direction,
            float progress,
            float plannedProgress = 0.0f,
            ItemTypeEnum itemType = ItemTypeEnum.Iron_Ore)
        {
            var entity = _entityManager.CreateEntity(
                typeof(ItemIdentity),
                typeof(ItemOwnership),
                typeof(GridPosition),
                typeof(Direction),
                typeof(LocalTransform),
                typeof(BeltMovementState),
                typeof(BeltMovementDecision),
                typeof(BuildingItemInputDecision),
                typeof(TransferOwnershipRequest));

            _entityManager.SetComponentData(entity, new ItemIdentity(itemType));
            _entityManager.SetComponentData(entity, ItemOwnership.WorldItem);
            _entityManager.SetComponentData(entity, new GridPosition(position));
            _entityManager.SetComponentData(entity, new Direction(direction));
            _entityManager.SetComponentData(
                entity,
                LocalTransform.FromPosition(new float3(position.x, position.y, 0f)));
            _entityManager.SetComponentData(entity, new BeltMovementState(progress));
            _entityManager.SetComponentData(entity, new BeltMovementDecision(plannedProgress, false));
            _entityManager.SetComponentData(
                entity,
                new BuildingItemInputDecision(Entity.Null, false, -1));
            _entityManager.SetComponentData(
                entity,
                new TransferOwnershipRequest(Entity.Null));

            _entityManager.SetComponentEnabled<BeltMovementState>(entity, true);
            _entityManager.SetComponentEnabled<BeltMovementDecision>(entity, true);
            _entityManager.SetComponentEnabled<BuildingItemInputDecision>(entity, false);
            _entityManager.SetComponentEnabled<TransferOwnershipRequest>(entity, false);

            return entity;
        }

        public Entity CreateStorage(
            int2 position,
            int2 size,
            DirectionEnum direction = DirectionEnum.Up,
            int slotCount = 8,
            StorageFilter? filter = null)
        {
            var entity = _entityManager.CreateEntity(
                typeof(BuildingType),
                typeof(BuildingFootprint),
                typeof(GridPosition),
                typeof(Direction),
                typeof(Storage),
                typeof(BuildingItemOutputDecision));

            _entityManager.SetComponentData(entity, new BuildingType(BuildingTypeEnum.Storage));
            _entityManager.SetComponentData(entity, new BuildingFootprint(size));
            _entityManager.SetComponentData(entity, new GridPosition(position));
            _entityManager.SetComponentData(entity, new Direction(direction));
            _entityManager.SetComponentData(entity, new Storage(slotCount));
            _entityManager.SetComponentData(
                entity,
                new BuildingItemOutputDecision(false, Entity.Null, int2.zero));
            _entityManager.SetComponentEnabled<BuildingItemOutputDecision>(entity, false);

            _entityManager.AddBuffer<StoredItemElement>(entity);

            if (filter.HasValue)
            {
                _entityManager.AddComponentData(entity, filter.Value);
            }

            return entity;
        }

        public Entity CreateMiner(
            int2 position,
            int2 size,
            DirectionEnum direction,
            float miningSpeed = 1.0f,
            float progress = 0.0f)
        {
            var entity = _entityManager.CreateEntity(
                typeof(BuildingType),
                typeof(BuildingFootprint),
                typeof(GridPosition),
                typeof(Direction),
                typeof(BuildingItemOutputDecision),
                typeof(MinerState),
                typeof(MinerDecision));

            _entityManager.SetComponentData(entity, new BuildingType(BuildingTypeEnum.Miner));
            _entityManager.SetComponentData(entity, new BuildingFootprint(size));
            _entityManager.SetComponentData(entity, new GridPosition(position));
            _entityManager.SetComponentData(entity, new Direction(direction));
            _entityManager.SetComponentData(
                entity,
                new BuildingItemOutputDecision(false, Entity.Null, int2.zero));
            _entityManager.SetComponentEnabled<BuildingItemOutputDecision>(entity, false);
            _entityManager.SetComponentData(entity, new MinerState(miningSpeed, progress));
            _entityManager.SetComponentData(entity, new MinerDecision(false, Entity.Null));
            _entityManager.SetComponentEnabled<MinerDecision>(entity, false);

            _entityManager.AddBuffer<ProductItemElement>(entity);
            _entityManager.AddBuffer<ProductResult>(entity);

            return entity;
        }

        public Entity CreateCrafter(
            int2 position,
            int recipeId = 1,
            float speed = 1.0f,
            int slotCount = 4,
            StorageFilter? filter = null)
        {
            var entity = _entityManager.CreateEntity(
                typeof(BuildingType),
                typeof(BuildingFootprint),
                typeof(GridPosition),
                typeof(Direction),
                typeof(CrafterState),
                typeof(CrafterDecision),
                typeof(CrafterStateDecision),
                typeof(Storage),
                typeof(StorageFilter));

            _entityManager.SetComponentData(entity, new BuildingType(BuildingTypeEnum.Crafter));
            _entityManager.SetComponentData(entity, new BuildingFootprint(new int2(1, 1)));
            _entityManager.SetComponentData(entity, new GridPosition(position));
            _entityManager.SetComponentData(entity, new Direction(DirectionEnum.Up));
            _entityManager.SetComponentData(entity, new CrafterState(recipeId, speed));
            _entityManager.SetComponentData(entity, new CrafterDecision(false, recipeId));
            _entityManager.SetComponentEnabled<CrafterDecision>(entity, false);
            _entityManager.SetComponentData(
                entity,
                new CrafterStateDecision(
                    recipeId > 0 ? CrafterStatusEnum.Idle : CrafterStatusEnum.NoRecipe));
            _entityManager.SetComponentEnabled<CrafterStateDecision>(entity, false);
            _entityManager.SetComponentData(entity, new Storage(slotCount));
            _entityManager.SetComponentData(
                entity,
                filter ?? new StorageFilter(StorageFilterMode.Whitelist));

            _entityManager.AddBuffer<StoredItemElement>(entity);
            _entityManager.AddBuffer<ProductItemElement>(entity);
            _entityManager.AddBuffer<ProductResult>(entity);

            return entity;
        }

        /// <summary>
        /// Storage/Crafter 등에 이미 보관된 Item을 만들고 StoredItemElement에도 함께 등록.
        /// ItemLifecycleApplySystem의 보관 Item 상태와 동일하게 이동/입고/요청 컴포넌트는 비활성화.
        /// </summary>
        public Entity CreateStoredItem(
            Entity owner,
            ItemTypeEnum itemType,
            int slotIndex = 0)
        {
            var itemEntity = _entityManager.CreateEntity(
                typeof(ItemIdentity),
                typeof(ItemOwnership),
                typeof(GridPosition),
                typeof(LocalTransform),
                typeof(BeltMovementState),
                typeof(BeltMovementDecision),
                typeof(BuildingItemInputDecision),
                typeof(DestroyItemRequest),
                typeof(TransferOwnershipRequest));

            _entityManager.SetComponentData(itemEntity, new ItemIdentity(itemType));
            _entityManager.SetComponentData(itemEntity, ItemOwnership.Stored(owner));
            _entityManager.SetComponentData(itemEntity, new GridPosition(int2.zero));
            _entityManager.SetComponentData(itemEntity, LocalTransform.FromPosition(float3.zero));

            _entityManager.SetComponentEnabled<BeltMovementState>(itemEntity, false);
            _entityManager.SetComponentEnabled<BeltMovementDecision>(itemEntity, false);
            _entityManager.SetComponentEnabled<BuildingItemInputDecision>(itemEntity, false);
            _entityManager.SetComponentEnabled<DestroyItemRequest>(itemEntity, false);
            _entityManager.SetComponentEnabled<TransferOwnershipRequest>(itemEntity, false);

            _entityManager
                .GetBuffer<StoredItemElement>(owner)
                .Add(new StoredItemElement(itemEntity, itemType, slotIndex));

            return itemEntity;
        }

        public Entity CreateBuilding(
            BuildingTypeEnum type,
            int2 position,
            int2 size,
            DirectionEnum direction = DirectionEnum.Up)
        {
            var entity = _entityManager.CreateEntity(
                typeof(BuildingType),
                typeof(BuildingFootprint),
                typeof(GridPosition),
                typeof(Direction));

            _entityManager.SetComponentData(entity, new BuildingType(type));
            _entityManager.SetComponentData(entity, new BuildingFootprint(size));
            _entityManager.SetComponentData(entity, new GridPosition(position));
            _entityManager.SetComponentData(entity, new Direction(direction));

            return entity;
        }
    }
}
