using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>현재 건물 종료 원본으로 경로와 진행 중 이동 의도를 준비한다. 두 Job은 같은 임시 정거장/현장 자료만 빌린다.</summary>
[UpdateInGroup(typeof(DroneDecisionGroup))]
[UpdateAfter(typeof(DroneStationNetworkDecisionSystem))]
[UpdateBefore(typeof(DroneTaskDecisionSystem))]
public partial struct DroneFlightDecisionSystem : ISystem
{
    private EntityQuery _stations;
    private EntityQuery _sites;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _stations = SystemAPI.QueryBuilder().WithAll<DroneStation, DroneStationNetworkDecision, GridPosition>().Build();
        _sites = SystemAPI.QueryBuilder().WithAll<ConstructionSite, GridPosition, BuildingFootprint, Direction>().Build();
        state.RequireForUpdate<DroneMovementConfig>();
        state.RequireForUpdate<DroneBatteryConfig>();
    }

    // 스케줄링은 managed 경계에 두어 OnUpdate의 Burst 트램펄린에 Job 레이아웃을 내장하지 않는다.
    // 정거장 자료 준비/경로 평가/이동 판단의 실제 계산 Job은 모두 Burst로 유지한다.
    public void OnUpdate(ref SystemState state)
    {
        var stationOwners = _stations.ToEntityListAsync(Allocator.TempJob, state.Dependency, out var stationOwnersReady);
        var siteOwners = _sites.ToEntityListAsync(Allocator.TempJob, state.Dependency, out var siteOwnersReady);
        var stations = new NativeList<DroneFlightStation>(Allocator.TempJob);
        var sites = new NativeList<DroneFlightSite>(Allocator.TempJob);
        var build = new BuildFlightGeometryJob
        {
            StationOwners = stationOwners.AsDeferredJobArray(), SiteOwners = siteOwners.AsDeferredJobArray(),
            Stations = stations, Sites = sites,
            Positions = state.GetComponentLookup<GridPosition>(true),
            Footprints = state.GetComponentLookup<BuildingFootprint>(true),
            Directions = state.GetComponentLookup<Direction>(true),
            Networks = state.GetComponentLookup<DroneStationNetworkDecision>(true),
            Demolitions = state.GetComponentLookup<PendingBuildingDemolition>(true),
            Stamps = state.GetComponentLookup<PlacementStamp>(true),
            Construction = state.GetComponentLookup<ConstructionSite>(true),
            Storages = state.GetComponentLookup<Storage>(true),
            Items = state.GetBufferLookup<StoredItemElement>(true), Docks = state.GetBufferLookup<DockedDroneElement>(true)
        }.Schedule(JobHandle.CombineDependencies(state.Dependency, stationOwnersReady, siteOwnersReady));
        var config = SystemAPI.GetSingleton<DroneMovementConfig>();
        float maximum = SystemAPI.GetSingleton<DroneBatteryConfig>().MaximumCharge;
        var routes = new EvaluateFlightRoutesJob
        {
            Stations = stations, Sites = sites, Config = config, Maximum = maximum,
            Positions = state.GetComponentLookup<GridPosition>(true),
            Footprints = state.GetComponentLookup<BuildingFootprint>(true),
            Directions = state.GetComponentLookup<Direction>(true),
            Batteries = state.GetComponentLookup<DroneBatteryState>(true)
        }.ScheduleParallel(build);
        var movements = new PrepareMovementJob
        {
            Stations = stations, Sites = sites, Config = config, Maximum = maximum,
            Positions = state.GetComponentLookup<GridPosition>(true),
            Footprints = state.GetComponentLookup<BuildingFootprint>(true),
            Directions = state.GetComponentLookup<Direction>(true),
            Assignments = state.GetComponentLookup<DroneTaskAssignment>(true)
        }.ScheduleParallel(build);
        var readers = JobHandle.CombineDependencies(routes, movements);
        var ownerDisposal = JobHandle.CombineDependencies(stationOwners.Dispose(build), siteOwners.Dispose(build));
        state.Dependency = JobHandle.CombineDependencies(stations.Dispose(readers), sites.Dispose(readers), ownerDisposal);
    }

    [BurstCompile]
    private struct BuildFlightGeometryJob : IJob
    {
        [ReadOnly] public NativeArray<Entity> StationOwners;
        [ReadOnly] public NativeArray<Entity> SiteOwners;
        public NativeList<DroneFlightStation> Stations;
        public NativeList<DroneFlightSite> Sites;
        [ReadOnly] public ComponentLookup<GridPosition> Positions;
        [ReadOnly] public ComponentLookup<BuildingFootprint> Footprints;
        [ReadOnly] public ComponentLookup<Direction> Directions;
        [ReadOnly] public ComponentLookup<DroneStationNetworkDecision> Networks;
        [ReadOnly] public ComponentLookup<PendingBuildingDemolition> Demolitions;
        [ReadOnly] public ComponentLookup<PlacementStamp> Stamps;
        [ReadOnly] public ComponentLookup<ConstructionSite> Construction;
        [ReadOnly] public ComponentLookup<Storage> Storages;
        [ReadOnly] public BufferLookup<StoredItemElement> Items;
        [ReadOnly] public BufferLookup<DockedDroneElement> Docks;

        public void Execute()
        {
            for (int i = 0; i < StationOwners.Length; i++)
            {
                Entity entity = StationOwners[i];
                if (Demolitions.HasComponent(entity))
                {
                    continue;
                }
                float2 position = Positions[entity].Value;
                if (Footprints.HasComponent(entity) && Directions.HasComponent(entity))
                {
                    position += (float2)Footprints[entity].GetEffectiveSize(Directions[entity].dir) * 0.5f;
                }
                bool canDock = false;
                if (Storages.HasComponent(entity) && Items.HasBuffer(entity) && Docks.HasBuffer(entity))
                {
                    var items = Items[entity];
                    var docks = Docks[entity];
                    for (int slot = 0; slot < Storages[entity].SlotCount; slot++)
                    {
                        bool occupied = false;
                        for (int item = 0; item < items.Length; item++)
                        {
                            occupied |= items[item].SlotIndex == slot;
                        }
                        for (int dock = 0; dock < docks.Length; dock++)
                        {
                            occupied |= docks[dock].SlotIndex == slot;
                        }
                        if (!occupied)
                        {
                            canDock = true;
                            break;
                        }
                    }
                }
                Stations.Add(new DroneFlightStation
                {
                    Entity = entity, Network = Networks[entity].RepresentativeStation,
                    Bounds = Networks[entity].ActivityBounds, Position = position, Anchor = Positions[entity].Value,
                    HasStamp = Stamps.HasComponent(entity), Stamp = Stamps.HasComponent(entity) ? Stamps[entity] : default,
                    CanDock = canDock
                });
            }
            for (int i = 0; i < SiteOwners.Length; i++)
            {
                Entity entity = SiteOwners[i];
                if ((Construction[entity].Flags & ConstructionSiteFlags.Cancelled) != 0)
                {
                    continue;
                }
                Sites.Add(new DroneFlightSite
                {
                    Anchor = Positions[entity].Value,
                    Size = Footprints[entity].GetEffectiveSize(Directions[entity].dir)
                });
            }
        }
    }

    [BurstCompile]
    private partial struct EvaluateFlightRoutesJob : IJobEntity
    {
        [ReadOnly] public NativeList<DroneFlightStation> Stations;
        [ReadOnly] public NativeList<DroneFlightSite> Sites;
        public DroneMovementConfig Config;
        public float Maximum;
        [ReadOnly] public ComponentLookup<GridPosition> Positions;
        [ReadOnly] public ComponentLookup<BuildingFootprint> Footprints;
        [ReadOnly] public ComponentLookup<Direction> Directions;
        [ReadOnly] public ComponentLookup<DroneBatteryState> Batteries;

        private void Execute(in DroneRouteEvaluationRequest request, ref DroneRouteEvaluationDecision decision)
        {
            decision = default;
            if (!Batteries.HasComponent(request.Worker))
            {
                return;
            }
            var result = new DroneRouteEvaluationResult
            {
                EvaluationRevision = request.EvaluationRevision,
                Status = DroneRouteEvaluationStatusEnum.Unreachable
            };
            float2 destination;
            if (request.IsDropPositionSearch)
            {
                if (!DroneFlightPlanningUtility.TryFindDrop(request.OriginPosition.xy, Stations, Sites, out var drop))
                {
                    decision = new DroneRouteEvaluationDecision { Ready = true, Result = result };
                    return;
                }
                destination = drop;
                result.HasDropPosition = true;
                result.DropPosition = drop;
            }
            else if (!TryPoint(request.Destination, Positions, Footprints, Directions, out destination))
            {
                decision = new DroneRouteEvaluationDecision { Ready = true, Result = result };
                return;
            }
            float charge = Batteries[request.Worker].CurrentCharge;
            DroneFlightPlan plan;
            if (request.Kind == DroneRouteKindEnum.ViaSource)
            {
                if (!TryPoint(request.Source, Positions, Footprints, Directions, out var source))
                {
                    decision = new DroneRouteEvaluationDecision { Ready = true, Result = result };
                    return;
                }
                plan = DroneFlightPlanningUtility.PlanViaSource(request.OriginPosition.xy, source, destination,
                    charge, Maximum, Config.EnergyPerDistance, Stations);
            }
            else
            {
                plan = DroneFlightPlanningUtility.Plan(request.OriginPosition.xy, destination,
                    charge, Maximum, Config.EnergyPerDistance, Stations);
            }
            result.Status = plan.Status;
            result.TotalDistance = plan.Distance;
            decision = new DroneRouteEvaluationDecision { Ready = true, Result = result };
        }
    }

    [BurstCompile]
    [WithNone(typeof(DroneDocked))]
    private partial struct PrepareMovementJob : IJobEntity
    {
        [ReadOnly] public NativeList<DroneFlightStation> Stations;
        [ReadOnly] public NativeList<DroneFlightSite> Sites;
        public DroneMovementConfig Config;
        public float Maximum;
        [ReadOnly] public ComponentLookup<GridPosition> Positions;
        [ReadOnly] public ComponentLookup<BuildingFootprint> Footprints;
        [ReadOnly] public ComponentLookup<Direction> Directions;
        [ReadOnly] public ComponentLookup<DroneTaskAssignment> Assignments;

        private void Execute(in DroneWorker worker, in LocalTransform transform, in DroneBatteryState battery,
            in DroneWorkerAssignment link, in DroneMovementState movement, in DroneMovementResult previous,
            ref DroneMovementDecision decision)
        {
            decision = new DroneMovementDecision { Ready = true, Target = transform.Position.xy };
            bool hasAssignment = Assignments.HasComponent(link.Assignment);
            DroneTaskAssignment assignment = hasAssignment ? Assignments[link.Assignment] : default;
            bool failureSettled = !hasAssignment || assignment.Revision != previous.AssignmentRevision ||
                assignment.State == DroneTaskAssignmentStateEnum.Retargeting || DroneSchedulingUtility.IsTerminal(assignment.State);
            decision.ClearFailure = failureSettled;
            if (hasAssignment)
            {
                if (assignment.State == DroneTaskAssignmentStateEnum.AwaitingCollection ||
                    assignment.State == DroneTaskAssignmentStateEnum.AwaitingDelivery)
                {
                    decision.Assignment = link.Assignment;
                    decision.AssignmentRevision = assignment.Revision;
                    decision.Action = assignment.NextAction;
                    if (assignment.State == DroneTaskAssignmentStateEnum.AwaitingCollection)
                    {
                        // 도착의 공개와 픽업 시작은 다르다. 0 배터리는 새 픽업 대신 기존 중단/귀환 정산으로 보낸다.
                        if (battery.CurrentCharge <= 0f)
                        {
                            decision.Failure = DroneMovementFailureEnum.EnergyUnavailable;
                            PrepareReturn(transform.Position.xy, battery.CurrentCharge, link.Assignment, assignment, previous, ref decision);
                            return;
                        }
                        if (!TryPoint(assignment.Source, Positions, Footprints, Directions, out var source) ||
                            math.any(transform.Position.xy != source))
                        {
                            decision.Failure = DroneMovementFailureEnum.InvalidGoal;
                            PrepareReturn(transform.Position.xy, battery.CurrentCharge, link.Assignment, assignment, previous, ref decision);
                            return;
                        }
                    }
                    decision.Purpose = DroneFlightPurposeEnum.AwaitingAction;
                    return;
                }
            }
            bool needsReturn = movement.ReturnStation != Entity.Null ||
                (previous.Failure != DroneMovementFailureEnum.None && !failureSettled) || battery.CurrentCharge <= 0f;
            bool chargingStationActive = false;
            if (movement.ChargingStation != Entity.Null)
            {
                for (int i = 0; i < Stations.Length; i++)
                {
                    if (Stations[i].Entity == movement.ChargingStation)
                    {
                        chargingStationActive = true;
                        if (battery.CurrentCharge <= 0f || (previous.Failure != DroneMovementFailureEnum.None && !failureSettled))
                        {
                            decision.Purpose = DroneFlightPurposeEnum.WaitingForCharge;
                            decision.Station = movement.ChargingStation;
                            return;
                        }
                        if (!hasAssignment)
                        {
                            return;
                        }
                        if (assignment.State == DroneTaskAssignmentStateEnum.Retargeting ||
                            DroneSchedulingUtility.IsTerminal(assignment.State))
                        {
                            return;
                        }
                        needsReturn = false;
                        break;
                    }
                }
            }
            if (needsReturn)
            {
                PrepareReturn(transform.Position.xy, battery.CurrentCharge, link.Assignment, assignment, previous, ref decision);
                return;
            }
            if (!hasAssignment)
            {
                return;
            }
            if (assignment.State == DroneTaskAssignmentStateEnum.Retargeting ||
                DroneSchedulingUtility.IsTerminal(assignment.State))
            {
                return;
            }
            decision.Assignment = link.Assignment;
            decision.AssignmentRevision = assignment.Revision;
            decision.Action = assignment.NextAction;
            Entity goal = assignment.NextAction == DroneActionKindEnum.CollectFromStorage ||
                assignment.NextAction == DroneActionKindEnum.RecoverWorldItem ? assignment.Source : assignment.Destination;
            float2 destination;
            bool valid;
            if (assignment.NextAction == DroneActionKindEnum.DropCargo)
            {
                destination = assignment.DropPosition;
                valid = !DroneFlightPlanningUtility.IsInsideSite(Sites, assignment.DropPosition);
            }
            else
            {
                valid = TryPoint(goal, Positions, Footprints, Directions, out destination);
            }
            if (!valid)
            {
                decision.Failure = DroneMovementFailureEnum.InvalidGoal;
                PrepareReturn(transform.Position.xy, battery.CurrentCharge, link.Assignment, assignment, previous, ref decision);
                return;
            }
            DroneFlightPlan plan;
            bool collecting = assignment.NextAction == DroneActionKindEnum.CollectFromStorage ||
                assignment.NextAction == DroneActionKindEnum.RecoverWorldItem;
            if (collecting && TryPoint(assignment.Destination, Positions, Footprints, Directions, out var delivery))
            {
                plan = DroneFlightPlanningUtility.PlanViaSource(transform.Position.xy, destination, delivery,
                    battery.CurrentCharge, Maximum, Config.EnergyPerDistance, Stations);
            }
            else
            {
                plan = DroneFlightPlanningUtility.Plan(transform.Position.xy, destination,
                    battery.CurrentCharge, Maximum, Config.EnergyPerDistance, Stations);
            }
            if (plan.Status != DroneRouteEvaluationStatusEnum.Reachable)
            {
                if (chargingStationActive)
                {
                    // 실제 접속 전 슬롯/충전 공급 대기는 작업 중단이 아니다. 정거장 소실과 구분한다.
                    decision.Purpose = DroneFlightPurposeEnum.WaitingForCharge;
                    decision.Station = movement.ChargingStation;
                    return;
                }
                decision.Failure = DroneMovementFailureEnum.EnergyUnavailable;
                PrepareReturn(transform.Position.xy, battery.CurrentCharge, link.Assignment, assignment, previous, ref decision);
                return;
            }
            decision.Target = plan.NextPosition;
            decision.Station = plan.ChargingStation;
            decision.Purpose = plan.ChargingStation == Entity.Null ? DroneFlightPurposeEnum.Task : DroneFlightPurposeEnum.ChargingStop;
        }

        private void PrepareReturn(float2 origin, float charge, Entity assignmentEntity, in DroneTaskAssignment assignment,
            in DroneMovementResult previous, ref DroneMovementDecision decision)
        {
            if (assignmentEntity != Entity.Null)
            {
                if (decision.Failure == DroneMovementFailureEnum.None && previous.Failure == DroneMovementFailureEnum.None)
                {
                    decision.Failure = DroneMovementFailureEnum.EnergyUnavailable;
                }
                decision.Assignment = assignmentEntity;
                decision.AssignmentRevision = assignment.Revision;
                decision.Action = assignment.NextAction;
            }
            if (charge > 0f)
            {
                // 양수 배터리의 작업 중단은 현재 위치에서 기존 Lifecycle 정산을 기다린다.
                // 정상 귀환 선택을 앞당겨 다음 작업/적재 재배정의 우선권을 빼앗지 않는다.
                decision.Purpose = DroneFlightPurposeEnum.None;
                return;
            }
            int station = DroneFlightPlanningUtility.FindReturnStation(origin, Stations);
            if (station < 0)
            {
                decision.Purpose = DroneFlightPurposeEnum.WaitingForStation;
                return;
            }
            decision.Purpose = DroneFlightPurposeEnum.EmergencyReturn;
            decision.Station = Stations[station].Entity;
            decision.Target = Stations[station].Position;
        }
    }

    private static bool TryPoint(Entity entity, ComponentLookup<GridPosition> positions,
        ComponentLookup<BuildingFootprint> footprints, ComponentLookup<Direction> directions, out float2 point)
    {
        return DroneFlightPlanningUtility.TryGetTargetPoint(entity, positions, footprints, directions, out point);
    }
}
