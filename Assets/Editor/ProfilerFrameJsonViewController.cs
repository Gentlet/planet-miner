#if UNITY_EDITOR
using System;
using System.IO;
using Unity.Profiling.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class ProfilerFrameJsonViewController : ProfilerModuleViewController
{
    private Label _selectedFrameLabel;
    private Button _exportButton;
    private Toggle _includeMetadataToggle;
    private Toggle _includeCallstackToggle;
    private Toggle _includeFlowEventsToggle;

    public ProfilerFrameJsonViewController(ProfilerWindow profilerWindow)
        : base(profilerWindow)
    {
    }

    protected override VisualElement CreateView()
    {
        var root = new VisualElement
        {
            style =
            {
                paddingLeft = 8,
                paddingRight = 8,
                paddingTop = 8,
                paddingBottom = 8,
            }
        };

        var title = new Label("Selected Profiler Frame JSON Export")
        {
            style =
            {
                unityFontStyleAndWeight = FontStyle.Bold,
                marginBottom = 6,
            }
        };
        root.Add(title);

        _selectedFrameLabel = new Label();
        _selectedFrameLabel.style.marginBottom = 8;
        root.Add(_selectedFrameLabel);

        _includeMetadataToggle = new Toggle("Include sample metadata")
        {
            value = true
        };
        root.Add(_includeMetadataToggle);

        _includeCallstackToggle = new Toggle("Include recorded callstacks")
        {
            value = true
        };
        root.Add(_includeCallstackToggle);

        _includeFlowEventsToggle = new Toggle("Include flow events")
        {
            value = true
        };
        root.Add(_includeFlowEventsToggle);

        var note = new Label(
            "Exports the selected Profiler frame as hierarchical JSON. " +
            "All recorded CPU raw samples from every available thread are included.")
        {
            style =
            {
                whiteSpace = WhiteSpace.Normal,
                marginTop = 6,
                marginBottom = 8,
            }
        };
        root.Add(note);

        _exportButton = new Button(ExportSelectedFrame)
        {
            text = "Export Selected Frame to JSON"
        };
        _exportButton.style.height = 28;
        root.Add(_exportButton);

        ProfilerWindow.SelectedFrameIndexChanged += OnSelectedFrameIndexChanged;
        ReloadFrameLabel(ProfilerWindow.selectedFrameIndex);

        return root;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            ProfilerWindow.SelectedFrameIndexChanged -= OnSelectedFrameIndexChanged;

        base.Dispose(disposing);
    }

    private void OnSelectedFrameIndexChanged(long selectedFrameIndex)
    {
        ReloadFrameLabel(selectedFrameIndex);
    }

    private void ReloadFrameLabel(long selectedFrameIndex)
    {
        bool valid = selectedFrameIndex >= 0;

        if (_selectedFrameLabel != null)
        {
            _selectedFrameLabel.text = valid
                ? $"Selected frame: {selectedFrameIndex + 1} (internal index: {selectedFrameIndex})"
                : "Selected frame: none";
        }

        _exportButton?.SetEnabled(valid);
    }

    private void ExportSelectedFrame()
    {
        long selectedFrameIndex = ProfilerWindow.selectedFrameIndex;

        if (selectedFrameIndex < 0)
        {
            EditorUtility.DisplayDialog(
                "Profiler Frame JSON Export",
                "No Profiler frame is selected.",
                "OK");
            return;
        }

        int frameIndex;

        try
        {
            frameIndex = Convert.ToInt32(selectedFrameIndex);
        }
        catch (OverflowException)
        {
            EditorUtility.DisplayDialog(
                "Profiler Frame JSON Export",
                "The selected frame index is outside the supported Int32 range.",
                "OK");
            return;
        }

        string defaultDirectory = Path.GetDirectoryName(Application.dataPath);
        string defaultFileName =
            $"Profiler_Frame_{selectedFrameIndex + 1}_{DateTime.Now:yyyyMMdd_HHmmss}.json";

        string path = EditorUtility.SaveFilePanel(
            "Export Selected Profiler Frame",
            defaultDirectory,
            defaultFileName,
            "json");

        if (string.IsNullOrEmpty(path))
            return;

        var options = new ProfilerFrameJsonExporter.ExportOptions
        {
            IncludeMetadata = _includeMetadataToggle?.value ?? true,
            IncludeCallstacks = _includeCallstackToggle?.value ?? true,
            IncludeFlowEvents = _includeFlowEventsToggle?.value ?? true,
        };

        try
        {
            ProfilerFrameJsonExporter.Export(frameIndex, path, options);

            Debug.Log(
                $"[ProfilerFrameJsonExporter] Exported frame {selectedFrameIndex + 1} to:\n{path}");

            if (EditorUtility.DisplayDialog(
                    "Profiler Frame JSON Export",
                    $"Frame {selectedFrameIndex + 1} exported successfully.",
                    "Show File",
                    "Close"))
            {
                EditorUtility.RevealInFinder(path);
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog(
                "Profiler Frame JSON Export",
                $"Export failed:\n{exception.Message}",
                "OK");
        }
    }
}
#endif
