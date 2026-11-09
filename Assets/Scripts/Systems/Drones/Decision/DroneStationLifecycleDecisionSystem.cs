using Unity.Burst;
using Unity.Entities;

/// <summary>EndBuilding 원본의 요청별 자격을 병렬 판단한다. 실물/슬롯을 선점하지 않는다.</summary>
[UpdateInGroup(typeof(DroneDecisionGroup))]
public partial struct DroneStationLifecycleDecisionSystem : ISystem
{
    private EntityQueryMask _stationMask;
    private EntityQueryMask _itemMask;
    private EntityQueryMask _workerMask;
    private DroneStationEligibility _eligibility;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        DroneStationEligibility.CreateMasks(ref state, out _stationMask, out _itemMask, out _workerMask);
        _eligibility = DroneStationEligibility.Create(ref state, _stationMask, _itemMask, _workerMask);
        state.RequireForUpdate<PrefabDatabaseReady>();
        state.RequireForUpdate<DroneBatteryConfig>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _eligibility.Update(ref state);
        state.Dependency = new DroneStationLifecycleDecisionJob
        {
            Eligibility = _eligibility,
            Automatic = SystemAPI.GetComponentLookup<DroneAutomaticStationRequest>(true),
            RelocationRequests = SystemAPI.GetComponentLookup<DroneStationRelocationRequest>(true),
            MaximumCharge = SystemAPI.GetSingleton<DroneBatteryConfig>().MaximumCharge
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
internal partial struct DroneStationLifecycleDecisionJob : IJobEntity
{
    [Unity.Collections.ReadOnly] public DroneStationEligibility Eligibility;
    [Unity.Collections.ReadOnly] public ComponentLookup<DroneAutomaticStationRequest> Automatic;
    [Unity.Collections.ReadOnly] public ComponentLookup<DroneStationRelocationRequest> RelocationRequests;
    public float MaximumCharge;

    public void Execute(Entity entity, in DroneStationRequest request, ref DroneStationDecision decision)
    {
        bool relocationRequest = RelocationRequests.HasComponent(entity);
        bool automaticRequest = Automatic.HasComponent(entity);
        var status = Eligibility.Validate(request, MaximumCharge, automaticRequest || relocationRequest, relocationRequest);
        decision = new DroneStationDecision
        {
            Eligible = status == DroneStationRequestStatus.Applied, Status = status, SlotIndex = -1,
            IsRelocationRequest = relocationRequest, IsAutomaticRequest = automaticRequest
        };
    }
}
