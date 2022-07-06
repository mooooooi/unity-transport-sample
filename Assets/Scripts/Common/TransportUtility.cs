using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Error;

[BurstCompatible]
public static class TransportUtility
{
    public static FixedString32Bytes GetReasonString(DisconnectReason reason)
    {
        switch (reason)
        {
            case DisconnectReason.MaxConnectionAttempts:
                return nameof(DisconnectReason.MaxConnectionAttempts);
            case DisconnectReason.Timeout:
                return nameof(DisconnectReason.Timeout);
            case DisconnectReason.ClosedByRemote:
                return nameof(DisconnectReason.ClosedByRemote);
            default:
                return nameof(DisconnectReason.Default);
        }
    }

    public unsafe static bool Write<T>(this DataStreamWriter writer, ref T t) where T : unmanaged
    {
        return writer.WriteBytes((byte*)UnsafeUtility.AddressOf(ref t), UnsafeUtility.SizeOf<T>());
    }
}