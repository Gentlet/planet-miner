using Unity.Entities;

/// <summary>
/// 건물 Execution 첫 단계에서 필요한 기존 석탄을 잔여 에너지로 전환한 뒤 이번 틱 공급 예산을 한 번 게시할 경계.
/// 아직 빈 그룹이다. 전환은 연소와 구분하며 미사용 에너지는 PowerSettlementGroup에서도 차감하지 않는다.
/// </summary>
[UpdateInGroup(typeof(BuildingExecutionGroup), OrderFirst = true)]
public partial class PowerSupplyExecutionGroup : ComponentSystemGroup { }
