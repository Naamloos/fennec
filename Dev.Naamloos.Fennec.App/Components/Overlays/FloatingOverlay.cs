namespace Dev.Naamloos.Fennec.App.Components;

public abstract class FloatingOverlay<TResult> : ContentView
{
    private readonly TaskCompletionSource<TResult?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task<TResult?> Completion => _completion.Task;

    protected void Complete(TResult? result) => _completion.TrySetResult(result);

    protected TapGestureRecognizer DismissGesture() => new() { Command = new Command(() => Complete(default)) };
}

public static class FloatingOverlay
{
    public static async Task<TResult?> ShowAsync<TResult>(Page? page, FloatingOverlay<TResult> overlay)
    {
        var contentPage = page as ContentPage ?? (page as Shell)?.CurrentPage as ContentPage;
        if (contentPage?.Content is not Grid host) return default;

        overlay.ZIndex = int.MaxValue;
        host.Children.Add(overlay);
        try
        {
            return await overlay.Completion;
        }
        finally
        {
            host.Children.Remove(overlay);
        }
    }
}
