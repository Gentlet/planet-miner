using System;
using UnityEngine.UIElements;

/// <summary>설명과 실행 버튼을 가진 재사용 행. 버튼은 클릭 시점에 지정된 Action을 호출한다.</summary>
internal sealed class WorldUiActionRow
{
    public readonly VisualElement Root = new VisualElement();
    public readonly Label Label = new Label();
    public readonly Button Button;
    public Action Click;

    internal WorldUiActionRow(string buttonText = "")
    {
        Root.style.flexDirection = FlexDirection.Row;
        Label.style.flexGrow = 1;
        Label.style.whiteSpace = WhiteSpace.Normal;
        Root.Add(Label);
        Button = new Button(() => Click?.Invoke()) { text = buttonText };
        Root.Add(Button);
    }
}
