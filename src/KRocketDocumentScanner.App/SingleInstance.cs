using System.Net.Sockets;

namespace KRocketDocumentScanner.App;

/// <summary>
/// Lets only one copy of the app run per user, in either mode (scan window or --manage).
///
/// The first copy claims a name in Linux's "abstract" socket namespace. There is no file
/// behind it, so nothing can be deleted by hand to get around it, and the kernel releases
/// the name when the process ends, including after a crash. Any failure other than "the
/// name is taken" lets the app start: this check must never stop it.
/// </summary>
public static class SingleInstance
{
    // Neutral on purpose (not the product name) so a rename does not change it.
    private const string Name = "krocketdocumentscanner-single-instance";

    // Kept for the life of the process; closing it would release the name.
    private static Socket? _claim;

    /// <summary>True if this is the only copy (or the check is not possible here); false if
    /// another copy of this user's app is already running.</summary>
    public static bool TryAcquire()
    {
        if (!OperatingSystem.IsLinux()) return true;

        try
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                // Leading NUL byte = abstract namespace. The user name keeps two users on one
                // machine from blocking each other.
                socket.Bind(new UnixDomainSocketEndPoint($"\0{Name}-{Environment.UserName}"));
            }
            catch
            {
                socket.Dispose();
                throw;
            }
            _claim = socket;
            return true;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            return false;
        }
        catch
        {
            return true;
        }
    }
}
