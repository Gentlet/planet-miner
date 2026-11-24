using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

/// <summary>전력 검증 자료의 수명과 접근 JobHandle을 소유한다. 초기화/소비는 명시적인 경계 API를 사용한다.</summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class PowerValidationInitializationSystem : SystemBase
{
    private NativeReference<PowerValidationResult> _validation;
    private JobHandle _jobs;
    private bool _failurePublished;

    protected override void OnCreate()
    {
        base.OnCreate();
        _validation = new NativeReference<PowerValidationResult>(Allocator.Persistent);
    }

    internal NativeReference<PowerValidationResult> Validation => _validation;
    internal JobHandle GetDependency(JobHandle dependency) => JobHandle.CombineDependencies(dependency, _jobs);
    internal void Register(JobHandle jobs) => _jobs = jobs;

    internal void PrepareFrame()
    {
        _failurePublished = false;
        _jobs = new ResetPowerValidationJob { Validation = _validation }.Schedule(_jobs);
    }

    internal bool CompleteAndTryConsumeFailure(out FixedString128Bytes message, out bool supplyCommitted)
    {
        _jobs.Complete();
        _jobs = default;
        var result = _validation.Value;
        supplyCommitted = result.SupplyCommitted && !result.HasError;
        if (result.HasError && !_failurePublished)
        {
            message = result.Message;
            _failurePublished = true;
            return true;
        }
        message = default;
        return false;
    }

    protected override void OnUpdate()
    {
        // 자료는 World 수명으로 유지하고 매 틱 Reset은 Preparation이 요청한다.
        Enabled = false;
    }

    protected override void OnDestroy()
    {
        _jobs.Complete();
        if (_validation.IsCreated)
        {
            _validation.Dispose();
        }
        base.OnDestroy();
    }
}

internal struct PowerValidationResult
{
    public FixedString128Bytes Message;
    // 공급 확정 Job이 연료와 소비를 기록한 뒤 설정하며 EndPower가 Job 완료 후 읽는다.
    public bool SupplyCommitted;
    public bool HasError => Message.Length > 0;
}

[BurstCompile]
internal struct ResetPowerValidationJob : IJob
{
    public NativeReference<PowerValidationResult> Validation;
    public void Execute() => Validation.Value = default;
}
