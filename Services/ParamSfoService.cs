using PS3Butler.Models;
using System;
using System.IO;
using System.Text;

namespace PS3Butler.Services;

public class ParamSfoService
{
    public Ps3GameMetadata? Read(string paramSfoPath)
    {
        if (!File.Exists(paramSfoPath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(paramSfoPath);
            using var reader = new BinaryReader(stream);

            var magic = reader.ReadUInt32();

            if (magic != 0x46535000)
            {
                return null;
            }

            var version = reader.ReadUInt32();
            var keyTableOffset = reader.ReadUInt32();
            var dataTableOffset = reader.ReadUInt32();
            var entryCount = reader.ReadUInt32();

            string titleId = string.Empty;
            string title = string.Empty;

            for (var i = 0; i < entryCount; i++)
            {
                stream.Position = 20 + (i * 16);

                var keyOffset = reader.ReadUInt16();
                var dataFormat = reader.ReadUInt16();
                var dataLength = reader.ReadUInt32();
                var dataMaxLength = reader.ReadUInt32();
                var dataOffset = reader.ReadUInt32();

                var key = ReadNullTerminatedString(
                    stream,
                    keyTableOffset + keyOffset);

                if (key != "TITLE_ID" && key != "TITLE")
                {
                    continue;
                }

                stream.Position = dataTableOffset + dataOffset;

                var data = reader.ReadBytes((int)dataLength);

                var value = Encoding.UTF8
                    .GetString(data)
                    .TrimEnd('\0')
                    .Trim();

                if (key == "TITLE_ID")
                {
                    titleId = value;
                }
                else if (key == "TITLE")
                {
                    title = value;
                }
            }

            if (string.IsNullOrWhiteSpace(titleId) &&
                string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            return new Ps3GameMetadata
            {
                TitleId = titleId,
                Title = title
            };
        }
        catch
        {
            return null;
        }
    }

    private static string ReadNullTerminatedString(
        Stream stream,
        long position)
    {
        stream.Position = position;

        using var memory = new MemoryStream();

        while (true)
        {
            var value = stream.ReadByte();

            if (value == -1 || value == 0)
            {
                break;
            }

            memory.WriteByte((byte)value);
        }

        return Encoding.UTF8.GetString(memory.ToArray());
    }
}