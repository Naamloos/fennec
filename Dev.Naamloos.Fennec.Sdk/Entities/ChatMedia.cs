namespace Dev.Naamloos.Fennec.Sdk.Entities;

public enum ChatMediaKind
{
    None,
    Image,
    Video,
    Audio,
    File,
}

public sealed class ChatMedia : ObservableModel
{
    private byte[]? _fullImageData;
    private string? _videoPath;
    private bool _isLoading;

    public ChatMedia(
        ChatMediaKind kind,
        string sourceJson,
        string filename,
        string? mimeType,
        string? thumbnailSourceJson = null
    )
    {
        Kind = kind;
        SourceJson = sourceJson;
        Filename = filename;
        MimeType = mimeType;
        ThumbnailSourceJson = thumbnailSourceJson;
    }

    public ChatMediaKind Kind { get; }

    public string SourceJson { get; }

    public string Filename { get; }

    public string? MimeType { get; }

    public string? ThumbnailSourceJson { get; }

    public bool IsAnimatedGif =>
        Kind == ChatMediaKind.Image
        && (
            string.Equals(MimeType, "image/gif", StringComparison.OrdinalIgnoreCase)
            || Filename.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
        );

    public bool HasPreview =>
        Kind != ChatMediaKind.Video || !string.IsNullOrWhiteSpace(ThumbnailSourceJson);

    public byte[]? FullImageData
    {
        get => _fullImageData;
        private set => Set(ref _fullImageData, value);
    }

    public string? VideoPath
    {
        get => _videoPath;
        private set => Set(ref _videoPath, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => Set(ref _isLoading, value);
    }

    public async Task<byte[]?> LoadPreviewAsync(
        ManagedMatrixClient client,
        CancellationToken cancellationToken = default
    )
    {
        if (!HasPreview)
        {
            return null;
        }

        IsLoading = true;

        try
        {
            if (IsAnimatedGif)
            {
                return await client.GetMediaContentAsync(SourceJson);
            }

            return await client.GetRoomImageThumbnailAsync(
                ThumbnailSourceJson ?? SourceJson,
                480,
                480,
                cancellationToken: cancellationToken
            );
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadFullAsync(ManagedMatrixClient client)
    {
        IsLoading = true;

        try
        {
            if (Kind == ChatMediaKind.Video && VideoPath is null)
            {
                VideoPath = await client.GetVideoFileAsync(
                    SourceJson,
                    Filename,
                    MimeType ?? "video/mp4"
                );
            }
            else if (Kind == ChatMediaKind.Image && FullImageData is null)
            {
                FullImageData = await client.GetMediaContentAsync(SourceJson);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }
}
