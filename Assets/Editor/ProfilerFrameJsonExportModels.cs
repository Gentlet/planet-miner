#if UNITY_EDITOR
using System;
using System.Collections.Generic;

public static partial class ProfilerFrameJsonExporter
{
    [Serializable]
    public struct ExportOptions
    {
        public bool IncludeMetadata;
        public bool IncludeCallstacks;
        public bool IncludeFlowEvents;
    }

    [Serializable]
    private sealed class ExportFileData
    {
        public string exporter = "ProfilerFrameJsonExporter";
        public string exportedAtUtc;
        public int frameIndex;
        public int frameDisplayIndex;
        public float frameTimeMs;
        public float frameFps;
        public float frameGpuTimeMs;
        public List<ThreadData> threads = new List<ThreadData>();
    }

    [Serializable]
    private sealed class ThreadData
    {
        public int threadIndex;
        public string threadId;
        public string threadGroupName;
        public string threadName;
        public int totalSampleCount;
        public List<SampleData> rootSamples = new List<SampleData>();
    }

    [Serializable]
    private sealed class SampleData
    {
        public int sampleIndex;
        public int parentSampleIndex;
        public int depth;
        public int childCount;

        public int markerId;
        public int categoryId;
        public string categoryName;
        public string sampleName;
        public string path;
        public string markerFlags;

        public float startTimeMs;
        public float startFromFrameMs;
        public float durationMs;
        public float directChildrenTimeMs;
        public float selfTimeMs;

        public long directGcAllocBytes;
        public long inclusiveGcAllocBytes;

        public List<MetadataEntry> metadata = new List<MetadataEntry>();
        public List<string> callstack = new List<string>();
        public List<FlowEventEntry> flowEvents = new List<FlowEventEntry>();
        public List<SampleData> children = new List<SampleData>();
    }

    [Serializable]
    private sealed class MetadataEntry
    {
        public int index;
        public string value;
    }

    [Serializable]
    private sealed class FlowEventEntry
    {
        public string flowEventType;
        public string flowId;
        public int parentSampleIndex;
    }

    private sealed class ParentState
    {
        public SampleData Sample;
        public int RemainingChildren;
    }
}
#endif
