using System;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

public sealed class TableTextureBuildValidator : IPreprocessBuildWithReport
{
    // 같은 빌드 보고서 전처리 콜백 중 텍스처 검사를 먼저 요청한다.
    public int callbackOrder => int.MinValue;

    public void OnPreprocessBuild(BuildReport report)
    {
        try
        {
            TableTextureSynchronizer.ValidateGeneratedTextures();
        }
        catch (Exception error)
        {
            throw new BuildFailedException($"Table texture validation failed. {error.Message}");
        }
    }
}
