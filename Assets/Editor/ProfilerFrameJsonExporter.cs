#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

public static partial class ProfilerFrameJsonExporter
{
    public static void Export(int frameIndex, string path, ExportOptions options)
    {
        var root = new ExportFileData
        {
            exportedAtUtc = DateTime.UtcNow.ToString("o"),
            frameIndex = frameIndex,
            frameDisplayIndex = frameIndex + 1,
        };

        bool foundAnyThread = false;
        bool filledFrameSummary = false;

        // Enumerate all available threads for the selected frame.
        for (int threadIndex = 0; ; threadIndex++)
        {
            using (RawFrameDataView frameData =
                   ProfilerDriver.GetRawFrameDataView(frameIndex, threadIndex))
            {
                if (frameData == null || !frameData.valid)
                    break;

                foundAnyThread = true;

                if (!filledFrameSummary)
                {
                    root.frameTimeMs = frameData.frameTimeMs;
                    root.frameFps = frameData.frameFps;
                    root.frameGpuTimeMs = frameData.frameGpuTimeMs;
                    filledFrameSummary = true;
                }

                root.threads.Add(BuildThreadData(frameData, threadIndex, options));
            }
        }

        if (!foundAnyThread)
        {
            throw new InvalidOperationException(
                $"No raw Profiler data is available for frame index {frameIndex}. " +
                "Make sure the frame is still loaded in the Profiler.");
        }

        string json = JsonUtility.ToJson(root, true);
        File.WriteAllText(path, json, new UTF8Encoding(true));
    }

    private static ThreadData BuildThreadData(
        RawFrameDataView frameData,
        int threadIndex,
        ExportOptions options)
    {
        var threadData = new ThreadData
        {
            threadIndex = threadIndex,
            threadId = SafeToString(frameData.threadId),
            threadGroupName = frameData.threadGroupName,
            threadName = frameData.threadName,
            totalSampleCount = frameData.sampleCount,
        };

        int sampleCount = frameData.sampleCount;
        int gcAllocMarkerId = frameData.GetMarkerId("GC.Alloc");

        var flatSamples = new List<SampleData>(sampleCount);
        var parentStack = new Stack<ParentState>();

        var callstackBuffer = options.IncludeCallstacks
            ? new List<ulong>(32)
            : null;

        var flowEventBuffer = options.IncludeFlowEvents
            ? new List<RawFrameDataView.FlowEvent>(8)
            : null;

        for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            while (parentStack.Count > 0 &&
                   parentStack.Peek().RemainingChildren <= 0)
            {
                parentStack.Pop();
            }

            SampleData parentSample = null;
            int parentSampleIndex = -1;
            int depth = parentStack.Count;
            string parentPath = string.Empty;

            if (parentStack.Count > 0)
            {
                ParentState parent = parentStack.Peek();
                parent.RemainingChildren--;
                parentSample = parent.Sample;
                parentSampleIndex = parentSample.sampleIndex;
                parentPath = parentSample.path;
            }

            string sampleName = SafeGetSampleName(frameData, sampleIndex);
            string samplePath = string.IsNullOrEmpty(parentPath)
                ? sampleName
                : parentPath + "/" + sampleName;

            int childCount = frameData.GetSampleChildrenCount(sampleIndex);
            float durationMs = (float)frameData.GetSampleTimeMs(sampleIndex);
            float startTimeMs = (float)frameData.GetSampleStartTimeMs(sampleIndex);

            int markerId = frameData.GetSampleMarkerId(sampleIndex);
            ushort categoryId = frameData.GetSampleCategoryIndex(sampleIndex);

            var sampleData = new SampleData
            {
                sampleIndex = sampleIndex,
                parentSampleIndex = parentSampleIndex,
                depth = depth,
                childCount = childCount,

                markerId = markerId,
                categoryId = categoryId,
                categoryName = SafeGetCategoryName(frameData, categoryId),
                sampleName = sampleName,
                path = samplePath,
                markerFlags = SafeGetMarkerFlags(frameData, sampleIndex),

                startTimeMs = startTimeMs,
                startFromFrameMs = startTimeMs - (float)frameData.frameStartTimeMs,
                durationMs = durationMs,
            };

            if (markerId == gcAllocMarkerId)
            {
                sampleData.directGcAllocBytes =
                    SafeGetGcAllocBytes(frameData, sampleIndex);
            }

            if (options.IncludeMetadata)
            {
                FillMetadata(frameData, sampleIndex, sampleData);
            }

            if (options.IncludeCallstacks)
            {
                FillCallstack(frameData, sampleIndex, sampleData, callstackBuffer);
            }

            if (options.IncludeFlowEvents)
            {
                FillFlowEvents(frameData, sampleIndex, sampleData, flowEventBuffer);
            }

            if (parentSample != null)
            {
                parentSample.children.Add(sampleData);
                parentSample.directChildrenTimeMs += durationMs;
            }
            else
            {
                threadData.rootSamples.Add(sampleData);
            }

            flatSamples.Add(sampleData);

            if (childCount > 0)
            {
                parentStack.Push(new ParentState
                {
                    Sample = sampleData,
                    RemainingChildren = childCount,
                });
            }
        }

        for (int i = 0; i < threadData.rootSamples.Count; i++)
        {
            ComputeDerivedValues(threadData.rootSamples[i]);
        }

        return threadData;
    }

    private static void ComputeDerivedValues(SampleData sample)
    {
        long childInclusiveGc = 0;
        float directChildDurationTotal = 0f;

        for (int i = 0; i < sample.children.Count; i++)
        {
            SampleData child = sample.children[i];
            ComputeDerivedValues(child);
            childInclusiveGc += child.inclusiveGcAllocBytes;
            directChildDurationTotal += child.durationMs;
        }

        // Recalculate from child list to stay correct even if hierarchy handling changes.
        sample.directChildrenTimeMs = directChildDurationTotal;
        sample.selfTimeMs = Mathf.Max(0f, sample.durationMs - directChildDurationTotal);
        sample.inclusiveGcAllocBytes = sample.directGcAllocBytes + childInclusiveGc;
    }

    private static void FillMetadata(
        RawFrameDataView frameData,
        int sampleIndex,
        SampleData sampleData)
    {
        int metadataCount = 0;

        try
        {
            metadataCount = frameData.GetSampleMetadataCount(sampleIndex);
        }
        catch
        {
            metadataCount = 0;
        }

        if (metadataCount <= 0)
            return;

        for (int metadataIndex = 0; metadataIndex < metadataCount; metadataIndex++)
        {
            string value;

            try
            {
                value = frameData.GetSampleMetadataAsString(sampleIndex, metadataIndex);
            }
            catch
            {
                value = "<unavailable>";
            }

            sampleData.metadata.Add(new MetadataEntry
            {
                index = metadataIndex,
                value = value,
            });
        }
    }

    private static void FillCallstack(
        RawFrameDataView frameData,
        int sampleIndex,
        SampleData sampleData,
        List<ulong> buffer)
    {
        if (buffer == null)
            return;

        buffer.Clear();

        try
        {
            frameData.GetSampleCallstack(sampleIndex, buffer);
        }
        catch
        {
            return;
        }

        for (int i = 0; i < buffer.Count; i++)
        {
            ulong address = buffer[i];

            try
            {
                FrameDataView.MethodInfo method =
                    frameData.ResolveMethodInfo(address);

                if (!string.IsNullOrEmpty(method.methodName))
                {
                    string entry = method.methodName;

                    if (!string.IsNullOrEmpty(method.sourceFileName))
                    {
                        entry += " [" + method.sourceFileName;

                        if (method.sourceFileLine > 0)
                            entry += ":" + method.sourceFileLine;

                        entry += "]";
                    }

                    sampleData.callstack.Add(entry);
                }
                else
                {
                    sampleData.callstack.Add("0x" + address.ToString("X"));
                }
            }
            catch
            {
                sampleData.callstack.Add("0x" + address.ToString("X"));
            }
        }
    }

    private static void FillFlowEvents(
        RawFrameDataView frameData,
        int sampleIndex,
        SampleData sampleData,
        List<RawFrameDataView.FlowEvent> buffer)
    {
        if (buffer == null)
            return;

        buffer.Clear();

        try
        {
            frameData.GetSampleFlowEvents(sampleIndex, buffer);
        }
        catch
        {
            return;
        }

        for (int i = 0; i < buffer.Count; i++)
        {
            RawFrameDataView.FlowEvent flow = buffer[i];

            sampleData.flowEvents.Add(new FlowEventEntry
            {
                flowEventType = flow.FlowEventType.ToString(),
                flowId = SafeToString(flow.FlowId),
                parentSampleIndex = flow.ParentSampleIndex,
            });
        }
    }

    private static string SafeGetSampleName(
        RawFrameDataView frameData,
        int sampleIndex)
    {
        try
        {
            return frameData.GetSampleName(sampleIndex) ?? string.Empty;
        }
        catch
        {
            int markerId = frameData.GetSampleMarkerId(sampleIndex);

            try
            {
                return frameData.GetMarkerName(markerId) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    private static string SafeGetCategoryName(
        RawFrameDataView frameData,
        ushort categoryId)
    {
        try
        {
            return frameData.GetCategoryInfo(categoryId).name ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string SafeGetMarkerFlags(
        RawFrameDataView frameData,
        int sampleIndex)
    {
        try
        {
            return frameData.GetSampleFlags(sampleIndex).ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static long SafeGetGcAllocBytes(
        RawFrameDataView frameData,
        int sampleIndex)
    {
        try
        {
            return frameData.GetSampleMetadataAsLong(sampleIndex, 0);
        }
        catch
        {
            try
            {
                return frameData.GetSampleMetadataAsInt(sampleIndex, 0);
            }
            catch
            {
                return 0;
            }
        }
    }

    private static string SafeToString(object value)
    {
        return value != null ? value.ToString() : string.Empty;
    }
}
#endif
