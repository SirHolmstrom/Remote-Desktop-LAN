namespace Core.Security;

public enum SessionRole
{
    Owner,
    Guest
}

public enum GuestAccessLevel
{
    Spectator = 0,
    Control = 1,
    Full = 2
}

public sealed record SessionPermissions(
    bool CanControl,
    bool CanUseSystemKeys,
    bool CanTransferFiles,
    bool CanManageSessions)
{
    public static SessionPermissions For(SessionRole role, GuestAccessLevel? guestAccessLevel)
    {
        if (role == SessionRole.Owner)
            return new(true, true, true, true);

        return guestAccessLevel switch
        {
            GuestAccessLevel.Full => new(true, true, true, false),
            GuestAccessLevel.Control => new(true, false, false, false),
            _ => new(false, false, false, false)
        };
    }
}
