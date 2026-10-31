using Unity.Collections;
using Unity.Entities;

namespace PlanetMiner.Tests
{
    /// <summary>
    /// 건물/전력/기존 물류의 격리 테스트에 이미 공개된 드론 확인을 입력으로 제공한다.
    /// 실제 드론 수행 검증에는 설치하지 않으며 승인 검증을 통과한 일반 건물만 입력으로 사용한다.
    /// </summary>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(BuildingStateApplyGroup), OrderFirst = true)]
    public partial class ConfirmedBuildingDemolitionTestSystem : SystemBase
    {
        protected override void OnUpdate() => ConfirmApproved(EntityManager);

        public static void ConfirmApproved(EntityManager manager)
        {
            using var query = manager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PendingBuildingDemolition>() },
                Options = EntityQueryOptions.IncludeDisabledEntities
            });
            using var buildings = query.ToEntityArray(Allocator.Temp);
            foreach (Entity building in buildings)
            {
                var pending = manager.GetComponentData<PendingBuildingDemolition>(building);
                if (pending.Task == Entity.Null)
                {
                    Entity counter = DroneTaskCreationUtility.GetOrCreateSequence(manager);
                    var sequence = manager.GetComponentData<DroneTaskSequence>(counter);
                    using var ecb = new EntityCommandBuffer(Allocator.Temp);
                    DroneTaskCreationUtility.CreateBuildingDemolitionTask(ecb, building, ref sequence);
                    ecb.Playback(manager);
                    manager.SetComponentData(counter, sequence);
                    pending = manager.GetComponentData<PendingBuildingDemolition>(building);
                }
                if (manager.HasComponent<BuildingDemolitionExecutionRequest>(pending.Task)) continue;
                var task = manager.GetComponentData<DroneLogisticsTask>(pending.Task);
                task.State = DroneLogisticsTaskStateEnum.ExecutionConfirmed;
                manager.SetComponentData(pending.Task, task);
                manager.AddComponent<BuildingDemolitionExecutionRequest>(pending.Task);
            }
        }
    }
}
