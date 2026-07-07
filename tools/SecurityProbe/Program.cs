using System.Text.RegularExpressions;
using Core.Security;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Require(!SessionPermissions.For(SessionRole.Guest, GuestAccessLevel.Spectator).CanControl,
    "Spectators must not control input.");
Require(SessionPermissions.For(SessionRole.Guest, GuestAccessLevel.Control).CanControl,
    "Control guests must control ordinary input.");
Require(!SessionPermissions.For(SessionRole.Guest, GuestAccessLevel.Control).CanUseSystemKeys,
    "Control guests must not use system keys.");
Require(SessionPermissions.For(SessionRole.Guest, GuestAccessLevel.Full).CanTransferFiles,
    "Full guests must be allowed file transfer.");
Require(!SessionPermissions.For(SessionRole.Guest, GuestAccessLevel.Full).CanManageSessions,
    "Full guests must remain guests, not owners.");

var invites = new GuestInviteStore();
var invite = invites.Create(GuestAccessLevel.Control, TimeSpan.FromMinutes(10));
Require(Regex.IsMatch(invite.Code, "^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$"),
    "Invite code format is invalid.");
Require(invites.TryRedeem(invite.Code.ToLowerInvariant().Replace("-", " "), out var redeemed)
        && redeemed.Id == invite.Id,
    "Normalized invite redemption failed.");

var sessions = new SessionStore(TimeSpan.FromMinutes(15), invites.IsActive);
string ownerToken = sessions.CreateOwner("127.0.0.1");
string guestToken = sessions.CreateGuest("127.0.0.1", invite.Id, invite.AccessLevel);
Require(sessions.TryGet(ownerToken, out var owner) && owner.Role == SessionRole.Owner,
    "Owner session creation failed.");
Require(sessions.TryGet(guestToken, out var guest)
        && guest.Role == SessionRole.Guest
        && guest.GuestAccessLevel == GuestAccessLevel.Control,
    "Guest role was not preserved in the session.");

invites.Revoke(invite.Id);
Require(!sessions.Validate(guestToken), "Revoked invite still validates its guest session.");
Require(sessions.Validate(ownerToken), "Revoking a guest invite affected the owner session.");

var expiring = invites.Create(GuestAccessLevel.Spectator, TimeSpan.FromTicks(1));
Thread.Sleep(2);
Require(!invites.TryRedeem(expiring.Code, out _), "Expired invite was accepted.");

var assembly = typeof(SessionStore).Assembly;
foreach (string resourceName in new[]
{
    "Core.Assets.app.ico",
    "Core.Assets.tray-idle-black.ico",
    "Core.Assets.tray-idle-white.ico",
    "Core.Assets.tray-on-black.ico",
    "Core.Assets.tray-on-white.ico"
})
{
    using Stream stream = assembly.GetManifestResourceStream(resourceName)
        ?? throw new InvalidOperationException($"Missing icon resource: {resourceName}");
    using var icon = new System.Drawing.Icon(stream, new System.Drawing.Size(32, 32));
    Require(icon.Width == 32 && icon.Height == 32, $"Invalid embedded icon: {resourceName}");
}

if (args.Length == 1)
{
    using var executableIcon = System.Drawing.Icon.ExtractAssociatedIcon(Path.GetFullPath(args[0]));
    Require(executableIcon is not null, "The built executable has no Windows application icon.");
}

Console.WriteLine("Security probe OK: roles, permissions, codes, expiry, revocation, and icons.");
