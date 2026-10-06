using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;

namespace PlanetMiner.Tests
{
    /// <summary>
    /// 역할·목적: 테스트 자원/벨트/보관품/건물의 공통 컴포넌트 구성을 직접 준비한다.
    /// 호출·입출력: 테스트 EntityManager와 위치/종류/수량으로 엔티티를 즉시 생성하여 반환하고 Owner/버퍼·enable 상태를 맞춘다.
    /// 수명·범위: 엔티티는 호출 테스트 World가 해제한다. 제품 Authoring/요청/생성 phase를 실행하는 Factory는 아니다.
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
            StorageFilter? filter = null)
        {
            // 선택 레시피의 슬롯/Whitelist fixture다. 실제 신규 제작기의 0슬롯·미선택 초기화 경로와 구분한다.
            var slots = CalculateCrafterInputSlots(recipeId);
            var entity = _entityManager.CreateEntity(
                typeof(BuildingType),
                typeof(BuildingFootprint),
                typeof(GridPosition),
                typeof(Direction),
                typeof(CrafterState),
                typeof(CrafterDecision),
                typeof(CrafterStateDecision),
                typeof(BuildingItemOutputDecision),
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
            _entityManager.SetComponentEnabled<BuildingItemOutputDecision>(entity, false);
            _entityManager.SetComponentData(entity, new Storage(slots.Length));
            var recipeFilter = new StorageFilter(StorageFilterMode.Whitelist);
            for (int i = 0; i < slots.Length; i++)
            {
                recipeFilter.Mask.Set((byte)slots[i].ItemType, true);
            }
            _entityManager.SetComponentData(entity, filter ?? recipeFilter);

            _entityManager.AddBuffer<StoredItemElement>(entity);
            _entityManager.AddBuffer<ProductItemElement>(entity);
            _entityManager.AddBuffer<ProductResult>(entity);
            var inputSlots = _entityManager.AddBuffer<BuildingInputSlotElement>(entity);
            for (int i = 0; i < slots.Length; i++)
            {
                inputSlots.Add(slots[i]);
            }

            return entity;
        }

        private FixedList512Bytes<BuildingInputSlotElement> CalculateCrafterInputSlots(int recipeId)
        {
            if (recipeId <= 0)
            {
                return default;
            }

            using var recipeQuery = _entityManager.CreateEntityQuery(typeof(RecipeRegistry));
            using var itemQuery = _entityManager.CreateEntityQuery(typeof(ItemRegistry));
            if (recipeQuery.IsEmptyIgnoreFilter || itemQuery.IsEmptyIgnoreFilter)
            {
                throw new System.InvalidOperationException("Crafter fixtures require RecipeRegistry and ItemRegistry.");
            }

            Entity recipeEntity = recipeQuery.GetSingletonEntity();
            Entity itemEntity = itemQuery.GetSingletonEntity();
            var recipes = _entityManager.GetBuffer<RecipeConfigElement>(recipeEntity, true);
            var ingredients = _entityManager.GetBuffer<RecipeIngredientElement>(recipeEntity, true);
            var itemRegistry = itemQuery.GetSingleton<ItemRegistry>();
            var items = _entityManager.GetBuffer<ItemConfigElement>(itemEntity, true);
            if (!RecipeConfigLookupUtility.TryGetRecipeIndex(recipes, recipeId, out int recipeIndex))
            {
                throw new System.InvalidOperationException($"Unknown fixture recipe {recipeId}.");
            }

            var recipe = recipes[recipeIndex];
            if (!BuildingInputSlotUtility.TryCalculate(ingredients, recipe.IngredientStart, recipe.IngredientCount,
                    itemRegistry, items, out var slots, out var error))
            {
                throw new System.InvalidOperationException($"Invalid fixture input slots: {error}.");
            }

            return slots;
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
            // 수납 fixture는 Owner와 소유자의 StoredItemElement를 함께 준비하여 같은 실물 참조를 유지한다.
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
