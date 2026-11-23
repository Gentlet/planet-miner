using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using SceneGuid = Unity.Entities.Hash128;

/// <summary>기존 JSON 슬롯을 별도 World에서 검증해 같은 디렉터리의 바이너리 슬롯으로 이전한다.</summary>
internal static class GameSaveMigration
{
    private const string MenuPath = "Tools/Planet Miner/Convert JSON Save to SAV";
    private const string PrefabScenePath = "Assets/Scenes/V2 Test Scene/sub.unity";
    private const double PreparationTimeout = 45;
    private static IEnumerator _conversion;

    [MenuItem(MenuPath, true)]
    private static bool CanConvert() => _conversion == null && !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem(MenuPath)]
    private static void SelectFolder()
    {
        string folder = EditorUtility.OpenFolderPanel("slot-01.json이 들어 있는 폴더",
            Application.persistentDataPath, "");
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }
        var sceneGuid = new SceneGuid(AssetDatabase.AssetPathToGUID(PrefabScenePath));
        _conversion = Convert(folder, sceneGuid, result =>
        {
            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("저장 파일 이전", Describe(result), "확인");
        });
        EditorUtility.DisplayProgressBar("저장 파일 이전", "저장 파일과 V2 월드를 확인하고 있습니다.", 0.5f);
        EditorApplication.update += Advance;
    }

    private static void Advance()
    {
        try
        {
            if (_conversion.MoveNext())
            {
                return;
            }
        }
        catch (Exception error)
        {
            EditorUtility.ClearProgressBar();
            UnityEngine.Debug.LogException(error);
            EditorUtility.DisplayDialog("저장 파일 이전", error.Message, "확인");
        }
        EditorApplication.update -= Advance;
        (_conversion as IDisposable)?.Dispose();
        _conversion = null;
        EditorUtility.ClearProgressBar();
    }

    internal static IEnumerator Convert(string folder, SceneGuid sceneGuid, Action<GameSaveStatusEnum> completed)
    {
        var slot = new GameSaveSlot(new DirectoryInfo(folder));
        if (File.Exists(Path.Combine(folder, GameSaveSlot.PrimaryName)))
        {
            completed(GameSaveStatusEnum.AlreadyExists);
            yield break;
        }
        string source = Path.Combine(folder, LegacyGameSaveFile.PrimaryName);
        var read = Task.Run(() => LegacyGameSaveFile.Read(source));
        while (!read.IsCompleted)
        {
            yield return null;
        }

        GameWorldReadResult legacy = read.GetAwaiter().GetResult();
        if (legacy.Status != GameSaveStatusEnum.Success)
        {
            completed(legacy.Status);
            yield break;
        }

        GameWorldReadResult converted = default;
        var candidate = ConvertCandidate(legacy, sceneGuid, result => converted = result);
        using (candidate as IDisposable)
        {
            while (candidate.MoveNext())
            {
                yield return candidate.Current;
            }
        }
        if (converted.Status != GameSaveStatusEnum.Success)
        {
            completed(converted.Status);
            yield break;
        }
        var writing = slot.ImportAsync(converted);
        while (!writing.IsCompleted)
        {
            yield return null;
        }
        completed(writing.GetAwaiter().GetResult());
    }

    private static IEnumerator ConvertCandidate(GameWorldReadResult legacy, SceneGuid guid, Action<GameWorldReadResult> completed)
    {
        GameWorldSnapshot snapshot = null;
        using (var preparation = new GameLoadWorldPreparation(guid))
        {
            foreach (var step in WaitForWorld(preparation))
            {
                yield return step;
            }
            try
            {
                snapshot = preparation.CaptureForMigration(legacy.Snapshot);
            }
            catch (InvalidDataException)
            {
                completed(new GameWorldReadResult(GameSaveStatusEnum.Corrupt));
            }
        }
        if (snapshot == null)
        {
            yield break;
        }

        // 현재 Reader로 다시 읽은 데이터도 새 World에서 검증한 뒤에만 최종 경로를 사용한다.
        using var verified = new GameLoadWorldPreparation(guid);
        foreach (var step in WaitForWorld(verified))
        {
            yield return step;
        }
        using var binary = new MemoryStream();
        GameWorldBinarySnapshot.Write(binary, snapshot, legacy.SavedAtUtc);
        binary.Position = 0;
        GameWorldSnapshot readBack = GameWorldBinarySnapshot.Read(binary, out DateTimeOffset savedAt);
        verified.VerifyMigrationSnapshot(readBack);
        completed(new GameWorldReadResult(GameSaveStatusEnum.Success, readBack, savedAt: savedAt));
    }

    private static IEnumerable WaitForWorld(GameLoadWorldPreparation preparation)
    {
        double deadline = EditorApplication.timeSinceStartup + PreparationTimeout;
        while (!preparation.IsReady)
        {
            preparation.AdvanceInitialization();
            if (preparation.HasFailed || EditorApplication.timeSinceStartup >= deadline)
            {
                throw new InvalidOperationException("이전 검증용 V2 월드 초기화에 실패했습니다.");
            }
            if (!preparation.IsReady)
            {
                yield return null;
            }
        }
    }

    private static string Describe(GameSaveStatusEnum status) => status switch
    {
        GameSaveStatusEnum.Success => "slot-01.sav로 이전했습니다.",
        GameSaveStatusEnum.AlreadyExists => "이 폴더에 이미 .sav 저장본이 있습니다.",
        GameSaveStatusEnum.Missing => "이전할 JSON 저장 파일이 없습니다.",
        GameSaveStatusEnum.Corrupt => "복원할 수 있는 JSON 저장 파일이 없습니다.",
        GameSaveStatusEnum.Incompatible => "지원하지 않는 저장 버전입니다.",
        _ => "저장 파일에 접근할 수 없습니다."
    };
}
