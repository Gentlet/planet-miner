#if UNITY_EDITOR
using System;
using Unity.Profiling;
using Unity.Profiling.Editor;

[Serializable]
[ProfilerModuleMetadata("Frame JSON Export")]
public sealed class ProfilerFrameJsonModule : ProfilerModule
{
    // Dummy counter so the custom Profiler module can be registered.
    private static readonly ProfilerCounterDescriptor[] ChartCounters =
    {
        new ProfilerCounterDescriptor(
            "GC Allocated In Frame",
            ProfilerCategory.Memory),
    };

    public ProfilerFrameJsonModule() : base(ChartCounters)
    {
    }

    public override ProfilerModuleViewController CreateDetailsViewController()
    {
        return new ProfilerFrameJsonViewController(ProfilerWindow);
    }
}
#endif
