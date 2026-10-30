using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>검증된 한 이동 구간의 실제 거리만 소비한다. 위치/격자/렌더와 배터리는 이 Job이 함께 갱신한다.</summary>
[UpdateInGroup(typeof(DroneExecutionGroup))]
public partial struct DroneMovementExecutionSystem : ISystem
{
    private EntityQuery _candidates;
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _candidates = SystemAPI.QueryBuilder().WithAll<DroneTaskInvalidationDecisionElement>().Build();
        state.RequireForUpdate(_candidates);
        state.RequireForUpdate<DroneMovementConfig>();
        state.RequireForUpdate<DroneBatteryConfig>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new MoveDronesJob
        {
            Config = SystemAPI.GetSingleton<DroneMovementConfig>(), DeltaTime = SystemAPI.Time.DeltaTime,
            MaximumCharge = SystemAPI.GetSingleton<DroneBatteryConfig>().MaximumCharge,
            Candidates = _candidates.GetSingletonEntity(),
            Invalidations = state.GetBufferLookup<DroneTaskInvalidationDecisionElement>(true),
            ReturnReservations = state.GetBufferLookup<DroneReturnReservationElement>(true),
            PostTransforms = state.GetComponentLookup<PostTransformMatrix>(true)
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    [WithNone(typeof(DroneDocked))]
    private partial struct MoveDronesJob : IJobEntity
    {
        public DroneMovementConfig Config;
        public float DeltaTime;
        public float MaximumCharge;
        public Entity Candidates;
        [ReadOnly] public BufferLookup<DroneTaskInvalidationDecisionElement> Invalidations;
        [ReadOnly] public BufferLookup<DroneReturnReservationElement> ReturnReservations;
        [ReadOnly] public ComponentLookup<PostTransformMatrix> PostTransforms;

        private void Execute(Entity entity, in DroneWorker worker, in DroneMovementDecision decision,
            ref LocalTransform transform, ref GridPosition position, ref LocalToWorld world,
            ref DroneBatteryState battery, ref DroneActivityState activity,
            ref DroneMovementState movement, ref DroneMovementResult result)
        {
            if (!decision.Ready)
            {
                return;
            }
            if (ReturnReservations.HasBuffer(Candidates))
            {
                var reservations = ReturnReservations[Candidates];
                for (int i = 0; i < reservations.Length; i++)
                {
                    if (reservations[i].Worker != entity)
                    {
                        continue;
                    }
                    if (!reservations[i].PreserveArrival)
                    {
                        movement = default;
                        activity.Activity = DroneActivityKind.Idle;
                        if (decision.ClearFailure) result = default;
                    }
                    return;
                }
            }
            if (decision.Purpose == DroneFlightPurposeEnum.Task || decision.Purpose == DroneFlightPurposeEnum.ChargingStop)
            {
                var invalidations = Invalidations[Candidates];
                for (int i = 0; i < invalidations.Length; i++)
                {
                    var invalidation = invalidations[i];
                    if (invalidation.Kind == DroneTaskInvalidationDecisionKindEnum.InvalidateAssignment &&
                        invalidation.Target == decision.Assignment && invalidation.AssignmentRevision == decision.AssignmentRevision)
                    {
                        // 같은 틱 Task Decision의 무효화 의도를 재사용한다. 철거/비활성/소유권 검사를 이동 Job에 복제하지 않는다.
                        activity.Activity = DroneActivityKind.Idle;
                        return;
                    }
                }
            }
            if (decision.ClearFailure)
            {
                result = default;
            }
            if (decision.Failure != DroneMovementFailureEnum.None)
            {
                result = new DroneMovementResult
                {
                    Assignment = decision.Assignment, AssignmentRevision = decision.AssignmentRevision,
                    Action = decision.Action, Failure = decision.Failure
                };
            }
            if (decision.Purpose == DroneFlightPurposeEnum.None)
            {
                movement = default;
                activity.Activity = DroneActivityKind.Idle;
                return;
            }
            if (decision.Purpose == DroneFlightPurposeEnum.AwaitingAction)
            {
                activity.Activity = DroneActivityKind.Acting;
                return;
            }
            if (decision.Purpose == DroneFlightPurposeEnum.WaitingForCharge)
            {
                movement.ChargingStation = decision.Station;
                activity.Activity = DroneActivityKind.WaitingForCharge;
                return;
            }
            if (decision.Purpose == DroneFlightPurposeEnum.WaitingForStation)
            {
                movement = default;
                activity.Activity = DroneActivityKind.Idle;
                return;
            }
            bool emergency = decision.Purpose == DroneFlightPurposeEnum.EmergencyReturn;
            bool normalReturn = decision.Purpose == DroneFlightPurposeEnum.NormalReturn;
            movement.IsNormalReturn = normalReturn;
            movement.ReturnStation = emergency || normalReturn ? decision.Station : Entity.Null;
            movement.ChargingStation = Entity.Null;
            float2 origin = transform.Position.xy;
            float distance = math.distance(origin, decision.Target);
            float speed = emergency && battery.CurrentCharge <= 0f ? Config.EmergencySpeed : Config.Speed;
            float step = math.min(distance, math.max(0f, DeltaTime) * speed);
            bool consumesEnergy = !emergency || battery.CurrentCharge > 0f;
            if (consumesEnergy)
            {
                step = math.min(step, math.max(0f, battery.CurrentCharge) / Config.EnergyPerDistance);
            }
            float2 next = distance <= step ? decision.Target : origin + (decision.Target - origin) * (step / distance);
            double cellX = math.floor((double)next.x);
            double cellY = math.floor((double)next.y);
            if (cellX < int.MinValue || cellX > int.MaxValue || cellY < int.MinValue || cellY > int.MaxValue)
            {
                result = new DroneMovementResult
                {
                    Assignment = decision.Assignment, AssignmentRevision = decision.AssignmentRevision,
                    Action = decision.Action, Failure = DroneMovementFailureEnum.InvalidGoal
                };
                return;
            }
            float moved = math.distance(origin, next);
            if (consumesEnergy)
            {
                battery.CurrentCharge = math.max(0f, battery.CurrentCharge - moved * Config.EnergyPerDistance);
            }
            transform.Position = new float3(next, transform.Position.z);
            position.Value = new int2((int)cellX, (int)cellY);
            var post = PostTransforms.HasComponent(entity) ? PostTransforms[entity].Value : float4x4.identity;
            world.Value = math.mul(transform.ToMatrix(), post);
            activity.Activity = DroneActivityKind.Moving;
            if (math.all(next == decision.Target))
            {
                if (decision.Purpose == DroneFlightPurposeEnum.Task)
                {
                    result = new DroneMovementResult
                    {
                        Assignment = decision.Assignment, AssignmentRevision = decision.AssignmentRevision,
                        Action = decision.Action, Arrived = true
                    };
                    activity.Activity = DroneActivityKind.Acting;
                }
                else
                {
                    // 도착만 기록한다. 정박/수납 요청은 다음 Decision이 기존 정거장 소유자에게 전달한다.
                    movement.ChargingStation = decision.Station;
                    movement.ReturnStation = Entity.Null;
                    activity.Activity = normalReturn && battery.CurrentCharge >= MaximumCharge
                        ? DroneActivityKind.Idle : DroneActivityKind.WaitingForCharge;
                }
            }
        }
    }
}
