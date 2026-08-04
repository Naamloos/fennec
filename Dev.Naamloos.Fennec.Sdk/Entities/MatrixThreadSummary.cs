namespace Dev.Naamloos.Fennec.Sdk.Entities;

public sealed record MatrixThreadSummary(
    string RootEventId,
    string Sender,
    string? SenderAvatarUrl,
    string Body,
    string LatestBody,
    uint ReplyCount
);
