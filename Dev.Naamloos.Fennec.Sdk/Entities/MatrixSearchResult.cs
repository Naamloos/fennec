namespace Dev.Naamloos.Fennec.Sdk.Entities;

public sealed record MatrixSearchResult(
    string RoomId,
    string EventId,
    string SenderName,
    string SenderId,
    string Body,
    string Timestamp
);
