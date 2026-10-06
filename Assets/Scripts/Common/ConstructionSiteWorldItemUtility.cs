using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 현장 footprint 내부의 새 월드 생성/드론 방출을 같은 기준으로 거부한다.
/// 입력·출력: 현재 활성·비취소 현장의 위치·방향·기본 크기로 회전 영역을 계산해 위치 허용 여부를 반환한다.
/// 이용: ItemSpawnAdmissionDecisionSystem, ItemLifecycleApplySystem, 드론 배정/인계 검증이 호출한다. 완공 건물/벨트는 이 금지 범위에 포함하지 않는다.
/// 수명·소유권: 원본은 현장 ECS 상태다. 캡처한 배열은 호출자/Job이 Dispose하는 한 번의 읽기 자료이며 영속 공간 인덱스나 이동 위치 원본이 아니다.
/// </summary>
public static class ConstructionSiteWorldItemUtility
{
    public struct Footprint
    {
        public int2 Origin;
        public int2 Size;
    }

    /// <summary>단건 최종 검사용. 여러 셀은 footprint를 한 번 캡처한 배열 API로 검사한다.</summary>
    public static bool IsWorldPositionAllowed(EntityManager manager, int2 position)
    {
        using var footprints = CaptureFootprints(manager, Allocator.Temp);
        return IsWorldPositionAllowed(footprints, position);
    }

    /// <summary>호출 시점의 회전된 현장 영역만 복사한다. 반환 배열은 호출자/Job이 Dispose한다.</summary>
    public static NativeArray<Footprint> CaptureFootprints(EntityManager manager, Allocator allocator)
    {
        using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<ConstructionSite>(),
            ComponentType.ReadOnly<GridPosition>(), ComponentType.ReadOnly<BuildingFootprint>(),
            ComponentType.ReadOnly<Direction>());
        using var sites = query.ToEntityArray(Allocator.Temp);
        using var footprints = new NativeList<Footprint>(sites.Length, Allocator.Temp);
        for (int i = 0; i < sites.Length; i++)
        {
            Entity site = sites[i];
            if ((manager.GetComponentData<ConstructionSite>(site).Flags & ConstructionSiteFlags.Cancelled) != 0)
            {
                continue;
            }

            footprints.Add(new Footprint
            {
                Origin = manager.GetComponentData<GridPosition>(site).Value,
                Size = BuildingFootprintUtility.GetEffectiveSize(
                    manager.GetComponentData<BuildingFootprint>(site).Size,
                    manager.GetComponentData<Direction>(site).dir)
            });
        }

        return new NativeArray<Footprint>(footprints.AsArray(), allocator);
    }

    /// <summary>Job/Burst용 같은 위치 검사. 원본이나 공간 인덱스를 변경하지 않는다.</summary>
    public static bool IsWorldPositionAllowed(NativeArray<Footprint> footprints, int2 position)
    {
        for (int i = 0; i < footprints.Length; i++)
        {
            var footprint = footprints[i];
            if (math.all(position >= footprint.Origin) && math.all(position < footprint.Origin + footprint.Size))
            {
                return false;
            }
        }

        return true;
    }
}
