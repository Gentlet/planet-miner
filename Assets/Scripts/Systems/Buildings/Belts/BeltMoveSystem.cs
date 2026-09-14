using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateAfter(typeof(ChunkMapSystem))]
[UpdateAfter(typeof(ItemTrackingSystem))]
[UpdateBefore(typeof(MiningSystem))]
[UpdateBefore(typeof(CrafterSystem))]
public partial class BeltMoveSystem : SystemBase
{
    private ChunkMapSystem _chunkMap;
    private readonly List<int2> _activeCellPositions = new();
    private readonly List<Entity> _itemsInCell = new();
    private readonly List<ActiveBeltCell> _activeCells = new();
    private readonly List<ItemSpatialEntry> _itemSnapshots = new();

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
        RequireForUpdate<ResearchConfig>();
    }

    protected override void OnUpdate()
    {
        if (_chunkMap == null)
        {
            _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
            if (_chunkMap == null)
                return;
        }

        DynamicBuffer<ResearchStatModifierElement> researchModifiers =
            SystemAPI.GetSingletonBuffer<ResearchStatModifierElement>(true);
        float beltSpeedMultiplier = researchModifiers.GetStatMultiplier(
            ResearchStatModifierTypeEnum.BeltSpeed);
        BuildActiveCellSnapshots(beltSpeedMultiplier);

        if (_activeCells.Count == 0)
            return;

        NativeArray<ActiveBeltCell> activeCells =
            new NativeArray<ActiveBeltCell>(_activeCells.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        NativeArray<ItemSpatialEntry> itemSnapshots =
            new NativeArray<ItemSpatialEntry>(_itemSnapshots.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        NativeArray<MoveResult> moveResults =
            new NativeArray<MoveResult>(_itemSnapshots.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

        for (int i = 0; i < _activeCells.Count; i++)
            activeCells[i] = _activeCells[i];

        for (int i = 0; i < _itemSnapshots.Count; i++)
            itemSnapshots[i] = _itemSnapshots[i];

        MoveActiveBeltCellsJob job = new MoveActiveBeltCellsJob
        {
            deltaTime = SystemAPI.Time.DeltaTime,
            itemSpacingSq = GameConstants.itemSpacing * GameConstants.itemSpacing,
            activeCells = activeCells,
            itemSnapshots = itemSnapshots,
            moveResults = moveResults,
            transforms = SystemAPI.GetComponentLookup<LocalTransform>(),
            cellChanged = SystemAPI.GetComponentLookup<ItemCellChanged>()
        };

        Dependency = job.Schedule(activeCells.Length, 1, Dependency);
        Dependency = activeCells.Dispose(Dependency);
        Dependency = itemSnapshots.Dispose(Dependency);
        Dependency = moveResults.Dispose(Dependency);
    }

    private void BuildActiveCellSnapshots(float speedMultiplier)
    {
        _activeCells.Clear();
        _itemSnapshots.Clear();
        _chunkMap.CopyActiveBeltCells(_activeCellPositions);

        for (int i = 0; i < _activeCellPositions.Count; i++)
        {
            int2 cell = _activeCellPositions[i];

            Entity beltEntity;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            bool hasIndexedBelt = _chunkMap.TryGetBelt(cell, out beltEntity);
            bool isValidBelt = hasIndexedBelt &&
                               EntityManager.Exists(beltEntity) &&
                               EntityManager.HasComponent<Belt>(beltEntity) &&
                               EntityManager.HasComponent<Direction>(beltEntity);

            UnityEngine.Assertions.Assert.IsTrue(
                isValidBelt,
                $"Active belt cell contains an invalid belt index. Cell: {cell}, Entity: {beltEntity}");

            if (!isValidBelt)
                continue;
#else
            _chunkMap.TryGetBelt(cell, out beltEntity);
#endif

            Belt belt = EntityManager.GetComponentData<Belt>(beltEntity);
            Direction direction = EntityManager.GetComponentData<Direction>(beltEntity);
            int currentStartIndex = _itemSnapshots.Count;
            AppendCellSnapshot(cell);
            int currentItemCount = _itemSnapshots.Count - currentStartIndex;

            if (currentItemCount == 0)
                continue;

            int2 nextCell = cell + direction.dir.ToInt2();
            int nextStartIndex = _itemSnapshots.Count;
            AppendCellSnapshot(nextCell);

            _activeCells.Add(new ActiveBeltCell
            {
                cell = cell,
                direction = direction.dir,
                speed = belt.speed * speedMultiplier,
                currentStartIndex = currentStartIndex,
                currentItemCount = currentItemCount,
                nextStartIndex = nextStartIndex,
                nextItemCount = _itemSnapshots.Count - nextStartIndex
            });
        }
    }

    private void AppendCellSnapshot(int2 cell)
    {
        _chunkMap.GetItems(cell, _itemsInCell);

        for (int i = 0; i < _itemsInCell.Count; i++)
        {
            Entity item = _itemsInCell[i];

            _itemSnapshots.Add(new ItemSpatialEntry
            {
                entity = item,
                position = EntityManager.GetComponentData<LocalTransform>(item).Position
            });
        }
    }
}
