using System.Net.Sockets;

namespace KRocketDocumentScanner.App;

public static class SingleInstance
{
    private const string Name = "krocketdocumentscanner-single-instance";

    private static Socket? _claim;

    public static bool TryAcquire()
    {
        if (!OperatingSystem.IsLinux()) return true;

        try
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
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
