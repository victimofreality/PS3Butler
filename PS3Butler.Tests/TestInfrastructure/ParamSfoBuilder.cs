using System.Text;

namespace PS3Butler.Tests.TestInfrastructure;

internal static class ParamSfoBuilder
{
    public static byte[] Create(
        string titleId,
        string title)
    {
        var entries = new[]
        {
            new Entry("TITLE_ID", titleId),
            new Entry("TITLE", title)
        };

        const int headerSize = 20;
        const int entrySize = 16;

        var keyBytes = entries
            .SelectMany(entry =>
                Encoding.UTF8.GetBytes(entry.Key + "\0"))
            .ToArray();

        var keyTableOffset =
            headerSize + (entries.Length * entrySize);

        var dataTableOffset = Align(keyTableOffset + keyBytes.Length, 4);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(0x46535000u);
        writer.Write(0x00000101u);
        writer.Write((uint)keyTableOffset);
        writer.Write((uint)dataTableOffset);
        writer.Write((uint)entries.Length);

        ushort keyOffset = 0;
        uint dataOffset = 0;

        foreach (var entry in entries)
        {
            var valueBytes = Encoding.UTF8.GetBytes(entry.Value + "\0");

            writer.Write(keyOffset);
            writer.Write((ushort)0x0204);
            writer.Write((uint)valueBytes.Length);
            writer.Write((uint)valueBytes.Length);
            writer.Write(dataOffset);

            keyOffset += (ushort)Encoding.UTF8.GetByteCount(entry.Key + "\0");
            dataOffset += (uint)valueBytes.Length;
        }

        writer.Write(keyBytes);

        while (stream.Position < dataTableOffset)
        {
            writer.Write((byte)0);
        }

        foreach (var entry in entries)
        {
            writer.Write(Encoding.UTF8.GetBytes(entry.Value + "\0"));
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static int Align(int value, int alignment) =>
        (value + alignment - 1) / alignment * alignment;

    private sealed record Entry(string Key, string Value);
}
