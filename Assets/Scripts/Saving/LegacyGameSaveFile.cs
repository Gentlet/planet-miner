#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>JSON 형식 1~3을 이전 도구에서만 읽는다.</summary>
internal static class LegacyGameSaveFile
{
    internal const int InitialVersion = 1;
    internal const int WorkVersion = 2;
    internal const int DroneVersion = 3;
    internal const string PrimaryName = "slot-01.json";

    private static readonly Encoding SaveEncoding = new UTF8Encoding(false, true);
    private static readonly string[] TimestampFormats = { "O", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'" };

    public static GameWorldReadResult Read(string path)
    {
        try
        {
            using var input = new StreamReader(path, SaveEncoding);
            using var reader = new JsonTextReader(input) { DateParseHandling = DateParseHandling.None };
            JObject document = JObject.Load(reader, new JsonLoadSettings
            {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
            });
            if (reader.Read())
            {
                return new GameWorldReadResult(GameSaveStatusEnum.Corrupt);
            }
            return ReadDocument(document);
        }
        catch (FileNotFoundException)
        {
            return new GameWorldReadResult(GameSaveStatusEnum.Missing);
        }
        catch (DirectoryNotFoundException)
        {
            return new GameWorldReadResult(GameSaveStatusEnum.Missing);
        }
        catch (IOException)
        {
            return new GameWorldReadResult(GameSaveStatusEnum.IoError);
        }
        catch (UnauthorizedAccessException)
        {
            return new GameWorldReadResult(GameSaveStatusEnum.IoError);
        }
        catch (JsonException)
        {
            return new GameWorldReadResult(GameSaveStatusEnum.Corrupt);
        }
        catch (DecoderFallbackException)
        {
            return new GameWorldReadResult(GameSaveStatusEnum.Corrupt);
        }
    }

    internal static GameWorldReadResult ReadDocument(JObject document)
    {
        if (document["formatVersion"]?.Type != JTokenType.Integer ||
            document["worldGeneratorVersion"]?.Type != JTokenType.Integer ||
            document["contentVersion"]?.Type != JTokenType.String)
        {
            return new GameWorldReadResult(GameSaveStatusEnum.Corrupt);
        }

        JToken version = document["formatVersion"];
        bool supportedFormat = JToken.DeepEquals(version, new JValue(InitialVersion)) ||
            JToken.DeepEquals(version, new JValue(WorkVersion)) || JToken.DeepEquals(version, new JValue(DroneVersion));
        if (!supportedFormat || !JToken.DeepEquals(document["worldGeneratorVersion"], new JValue(GameSaveSlot.WorldGeneratorVersion)) ||
            (string)document["contentVersion"] != GameSaveSlot.ContentVersion)
        {
            return new GameWorldReadResult(GameSaveStatusEnum.Incompatible);
        }
        if (document["world"] is not JObject || document["savedAtUtc"]?.Type != JTokenType.String ||
            !DateTimeOffset.TryParseExact((string)document["savedAtUtc"], TimestampFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var savedAt) || savedAt.Offset != TimeSpan.Zero)
        {
            return new GameWorldReadResult(GameSaveStatusEnum.Corrupt);
        }
        try
        {
            return new GameWorldReadResult(GameSaveStatusEnum.Success, GameWorldSnapshot.FromJson(document), savedAt: savedAt);
        }
        catch (NotSupportedException)
        {
            return new GameWorldReadResult(GameSaveStatusEnum.Incompatible);
        }
        catch (JsonException)
        {
            return new GameWorldReadResult(GameSaveStatusEnum.Corrupt);
        }
    }
}
#endif
