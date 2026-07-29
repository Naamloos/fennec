namespace Dev.Naamloos.Fennec.App.Components;

public static class InAppDialogs
{
    public static async Task<string?> ChooseAsync(
        Page? page,
        string title,
        IEnumerable<string> actions,
        string? message = null
    )
    {
        if (page is null)
            return null;
        return await FloatingOverlay.ShowAsync(
            page,
            new FloatingActionMenu(title, actions, message)
        );
    }

    public static async Task<string?> PromptAsync(
        Page? page,
        string title,
        string message,
        string accept = "Continue",
        string? placeholder = null,
        string? initialValue = null,
        bool multiline = false,
        bool isPassword = false
    )
    {
        if (page is null)
            return null;
        return await FloatingOverlay.ShowAsync(
            page,
            new FloatingTextPrompt(
                title,
                message,
                accept,
                placeholder,
                initialValue,
                multiline,
                isPassword
            )
        );
    }

    public static Task<PollDraft?> ComposePollAsync(Page? page) =>
        FloatingOverlay.ShowAsync(page, new PollComposer());
}
