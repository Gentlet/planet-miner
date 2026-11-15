using Unity.Collections;

/// <summary>첫 전력 검증 실패의 메시지 기록.</summary>
internal static class PowerValidationUtility
{

    public static void Fail(NativeReference<PowerValidationResult> validation, FixedString128Bytes message)
    {
        if (!validation.Value.HasError)
        {
            validation.Value = new PowerValidationResult { Message = message };
        }
    }
}
