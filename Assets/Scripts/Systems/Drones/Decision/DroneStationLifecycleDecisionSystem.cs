using Unity.Burst;
using Unity.Entities;

/// <summary>EndBuilding 원본의 요청별 자격을 병렬 판단한다. 실물/슬롯을 선점하지 않는다.</summary>
[UpdateInGroup(typeof(DroneDecisionGroup))]
public partial struct DroneStationLifecycleDecisionSystem : ISystem
{
    private EntityQueryMask _stationMask;
    private EntityQueryMask _itemMask;
    private EntityQueryMask _workerMask;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        DroneStationEligibility.CreateMasks(ref state, out _stationMask, out _itemMask, out _workerMask);
        state.RequireForUpdate<PrefabDatabaseReady>();
        state.RequireForUpdate<DroneBatteryConfig>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new DroneStationLifecycleDecisionJob
        {
            Eligibility = DroneStationEligibility.Create(ref state, _stationMask, _itemMask, _workerMask),
            Automatic = SystemAPI.GetComponentLookup<DroneAutomaticStationRequest>(true),
            MaximumCharge = SystemAPI.GetSingleton<DroneBatteryConfig>().MaximumCharge
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
internal partial struct DroneStationLifecycleDecisionJob : IJobEntity
{
    [Unity.Collections.ReadOnly] public DroneStationEligibility Eligibility;
    [Unity.Collections.ReadOnly] public ComponentLookup<DroneAutomaticStationRequest> Automatic;
    public float MaximumCharge;

    public void Execute(Entity entity, in DroneStationRequest request, ref DroneStationDecision decision)
    {
        var status = Eligibility.Validate(request, MaximumCharge, Automatic.HasComponent(entity));
        decision = new DroneStationDecision
        {
            Eligible = status == DroneStationRequestStatus.Applied, Status = status, SlotIndex = -1
        };
    }
}