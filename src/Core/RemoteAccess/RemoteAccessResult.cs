namespace Core.RemoteAccess;

public enum RemoteAccessState
{
    Closed,
    Opening,
    Active,
    ManualSetupRequired,
    Failed
}

public sealed record RemoteAccessResult(
    bool Success,
    RemoteAccessState State,
    string Message,
    string? RemoteUrl = null)
{
    public static RemoteAccessResult Active(string url) =>
        new(true, RemoteAccessState.Active, "Remote access is active.", url);

    public static RemoteAccessResult Manual(string message) =>
        new(false, RemoteAccessState.ManualSetupRequired, message);

    public static RemoteAccessResult Failure(string message) =>
        new(false, RemoteAccessState.Failed, message);
}
