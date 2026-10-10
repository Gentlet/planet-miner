using System;
using Unity.Entities;

/// <summary>
/// 역할·목적: 배치 묶음·직접 생성 요청을 주 스레드에서 접수하고 기존 World 번호 원본으로 설치 순서를 발급한다.
/// 입력·출력: 사전 확인한 후보와 요청 정책을 같은 엔티티에 준비한다. 최종 승인과 점유 중재는 기존 배치 Command가 담당한다.
/// 수명: 번호/Tick 원본은 World 동안 유지한다. 배치 요청은 EndCommand, 직접 생성 요청은 EndBuilding에서 소비하며 건물의 표식은 보존한다.
/// </summary>
public static class BuildingPlacementRequestUtility
{
    public static Entity Submit(
        EntityManager manager,
        in BuildingPlacementRequest request,
        ReadOnlySpan<PlacementRequestCandidateElement> candidates)
    {
        Entity sequenceEntity = GetOrCreateReceiptSequence(manager);
        var sequence = manager.GetComponentData<BuildingPlacementReceiptSequence>(sequenceEntity);
        ValidateSequence(sequence);

        var accepted = request;
        accepted.ReceiptSequence = sequence.NextValue;
        Entity entity = manager.CreateEntity(typeof(BuildingPlacementRequest), typeof(PlacementRequestCandidateElement));
        try
        {
            manager.SetComponentData(entity, accepted);
            var buffer = manager.GetBuffer<PlacementRequestCandidateElement>(entity);
            for (int i = 0; i < candidates.Length; i++)
            {
                buffer.Add(candidates[i]);
            }

            sequence.NextValue++;
            manager.SetComponentData(sequenceEntity, sequence);
            return entity;
        }
        catch
        {
            manager.DestroyEntity(entity);
            throw;
        }
    }

    /// <summary>직접 생성의 실제 접수 경계. 생략한 표식만 발급하고 명시한 표식(0/0 포함)은 그대로 보존한다.</summary>
    public static Entity SubmitSpawn(EntityManager manager, in SpawnBuildingRequest request)
    {
        var accepted = request;
        bool needsStamp = !accepted.HasPlacementStamp && accepted.Stamp.IsDefault;
        Entity sequenceEntity = Entity.Null;
        BuildingPlacementReceiptSequence sequence = default;
        if (needsStamp)
        {
            sequenceEntity = GetOrCreateReceiptSequence(manager);
            sequence = manager.GetComponentData<BuildingPlacementReceiptSequence>(sequenceEntity);
            ValidateSequence(sequence);
            accepted.Stamp = new PlacementStamp(sequence.CurrentTick, 0, sequence.NextValue);
        }
        accepted.HasPlacementStamp = true;

        Entity entity = manager.CreateEntity(typeof(SpawnBuildingRequest));
        try
        {
            manager.SetComponentData(entity, accepted);
            if (needsStamp)
            {
                sequence.NextValue++;
                manager.SetComponentData(sequenceEntity, sequence);
            }
            return entity;
        }
        catch
        {
            manager.DestroyEntity(entity);
            throw;
        }
    }

    /// <summary>
    /// 접수 API를 거치지 않은 직접 요청의 생략값을 처리 직전에 준비한다. 실제 도착 순서는 복원하지 않는다.
    /// 같은 처리 묶음에는 같은 표식을 부여해 최종 좌표 동률 규칙을 사용하고 쿼리 순서로 번호를 나누지 않는다.
    /// 구조 변경 가능성이 있으므로 호출자는 Job을 완료하고 준비 뒤 Lookup을 갱신한다.
    /// </summary>
    public static void PrepareSpawnStamps(EntityManager manager, EntityQuery requests)
    {
        using var entities = requests.ToEntityArray(Unity.Collections.Allocator.Temp);
        PlacementStamp batchStamp = default;
        for (int i = 0; i < entities.Length; i++)
        {
            var request = manager.GetComponentData<SpawnBuildingRequest>(entities[i]);
            if (request.HasPlacementStamp) continue;

            if (request.Stamp.IsDefault)
            {
                if (batchStamp.IsDefault)
                {
                    Entity sequenceEntity = GetOrCreateReceiptSequence(manager);
                    var sequence = manager.GetComponentData<BuildingPlacementReceiptSequence>(sequenceEntity);
                    ValidateSequence(sequence);
                    batchStamp = new PlacementStamp(sequence.CurrentTick, 0, sequence.NextValue++);
                    manager.SetComponentData(sequenceEntity, sequence);
                }
                request.Stamp = batchStamp;
            }
            request.HasPlacementStamp = true;
            manager.SetComponentData(entities[i], request);
        }
    }

    public static void AdvanceTick(EntityManager manager)
    {
        Entity entity = GetOrCreateReceiptSequence(manager);
        var sequence = manager.GetComponentData<BuildingPlacementReceiptSequence>(entity);
        if (sequence.CurrentTick == 0 || sequence.CurrentTick == ulong.MaxValue)
        {
            throw new InvalidOperationException("Building placement tick is exhausted or invalid.");
        }
        sequence.CurrentTick++;
        manager.SetComponentData(entity, sequence);
    }

    private static void ValidateSequence(in BuildingPlacementReceiptSequence sequence)
    {
        if (sequence.NextValue == 0 || sequence.NextValue == ulong.MaxValue || sequence.CurrentTick == 0)
        {
            throw new InvalidOperationException("Building placement receipt sequence or tick is exhausted or invalid.");
        }
    }

    public static Entity GetOrCreateReceiptSequence(EntityManager manager)
    {
        using var query = manager.CreateEntityQuery(ComponentType.ReadWrite<BuildingPlacementReceiptSequence>());
        if (!query.IsEmptyIgnoreFilter)
        {
            return query.GetSingletonEntity();
        }

        Entity entity = manager.CreateEntity(typeof(BuildingPlacementReceiptSequence));
        manager.SetComponentData(entity, new BuildingPlacementReceiptSequence { NextValue = 1, CurrentTick = 1 });
        return entity;
    }
}
