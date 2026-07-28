namespace Dev.Naamloos.Fennec.Sdk.Entities;

public sealed record MatrixSearchResult(
    string RoomId,
    string EventId,
    string SenderId,
    string Body,
    string Timestamp);
