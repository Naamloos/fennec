namespace Dev.Naamloos.Fennec.App.Components;

public sealed class ChatMessageView : ContentView
{
    public ChatMessageView(Func<View> renderer)
    {
        Content = renderer();
    }
}
