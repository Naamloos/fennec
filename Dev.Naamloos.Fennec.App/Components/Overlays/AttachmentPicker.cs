using CommunityToolkit.Maui.Extensions;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed record PickedAttachment(string FileName, string MimeType, byte[] Data);

public static class AttachmentPicker
{
    public static async Task<PickedAttachment?> PickConfirmedAsync(PickOptions? options = null)
    {
        var file = await FilePicker.Default.PickAsync(options);
        if (file is null || Shell.Current is not { } shell) return null;

        await using var input = await file.OpenReadAsync();
        using var data = new MemoryStream();
        await input.CopyToAsync(data);

        var attachment = new PickedAttachment(
            file.FileName,
            string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            data.ToArray());
        return await ConfirmAsync(attachment);
    }

    public static async Task<PickedAttachment?> ConfirmAsync(PickedAttachment attachment)
    {
        if (Shell.Current is not { } shell) return null;
        var result = await shell.ShowPopupAsync<bool>(new AttachmentPreviewPopup(attachment));
        return result.Result ? attachment : null;
    }
}
