using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

[assembly: RegisterGenericJobType(typeof(ClearSpatialMapJob<int2, BeltInfo>))]
[assembly: RegisterGenericJobType(typeof(ClearSpatialMapJob<int2, BuildingInfo>))]
[assembly: RegisterGenericJobType(typeof(ClearSpatialMapJob<int2, Entity>))]
[assembly: RegisterGenericJobType(typeof(ClearSpatialMapJob<Entity, PowerGridConnection>))]
[assembly: RegisterGenericJobType(typeof(ClearSpatialMapJob<Entity, DroneStationCoverage>))]

/// <summary>호출자가 이전 Reader/Writer 뒤에 예약하는 공간 HashMap 초기화 작업.</summary>
[BurstCompile]
internal struct ClearSpatialMapJob<TKey, TValue> : IJob
    where TKey : unmanaged, IEquatable<TKey>
    where TValue : unmanaged
{
    public NativeParallelHashMap<TKey, TValue> Map;

    public void Execute()
    {
        Map.Clear();
    }
}
