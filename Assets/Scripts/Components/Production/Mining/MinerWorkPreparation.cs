using Unity.Entities;

/// <summary>Preparation만 작성하는 전력 외 작업 자격. enable은 이번 틱 준비 완료다.</summary>
public struct MinerWorkPreparation : IComponentData, IEnableableComponent
{
    public bool CanMine;
    public Entity TargetResource;
}
