using System.Buffers.Binary;

namespace Ilmi.SharedKernel.Domain;

/// <summary>
/// Creates GUIDs that sort in creation order under SQL Server's uniqueidentifier ordering
/// (the last six bytes are the most significant). Same layout as EF Core's SequentialGuidValueGenerator.
/// Do not use Guid.CreateVersion7() for clustered keys in SQL Server: it does not sort sequentially there.
/// </summary>
public static class SequentialGuid
{
    private static long _counter = DateTime.UtcNow.Ticks;

    public static Guid Create()
    {
        var bytes = Guid.NewGuid().ToByteArray();
        var counter = Interlocked.Increment(ref _counter);
        Span<byte> c = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(c, counter);
        bytes[08] = c[6]; bytes[09] = c[7];
        bytes[10] = c[0]; bytes[11] = c[1]; bytes[12] = c[2]; bytes[13] = c[3]; bytes[14] = c[4]; bytes[15] = c[5];
        return new Guid(bytes);
    }
}
