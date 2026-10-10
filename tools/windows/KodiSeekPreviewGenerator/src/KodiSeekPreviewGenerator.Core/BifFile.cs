using System.Buffers.Binary;

namespace KodiSeekPreviewGenerator.Core;

public static class BifFile
{
    public const uint TimestampMultiplierMilliseconds = 10_000;

    private const int HeaderSize = 64;
    private const int GeneratorMarkerOffset = 20;
    private const uint SentinelTimestamp = uint.MaxValue;
    private static readonly byte[] Magic = [0x89, 0x42, 0x49, 0x46, 0x0d, 0x0a, 0x1a, 0x0a];
    private static readonly byte[] GeneratorMarker = "KSPG2"u8.ToArray();

    public static bool IsValid(string path)
    {
        try
        {
            using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length < HeaderSize)
                return false;

            Span<byte> header = stackalloc byte[HeaderSize];
            stream.ReadExactly(header);
            if (!header[..Magic.Length].SequenceEqual(Magic))
                return false;

            uint version = BinaryPrimitives.ReadUInt32LittleEndian(header[8..12]);
            uint imageCount = BinaryPrimitives.ReadUInt32LittleEndian(header[12..16]);
            uint multiplier = BinaryPrimitives.ReadUInt32LittleEndian(header[16..20]);
            if (version != 0 || imageCount == 0 ||
                multiplier != TimestampMultiplierMilliseconds ||
                !header.Slice(GeneratorMarkerOffset, GeneratorMarker.Length)
                    .SequenceEqual(GeneratorMarker))
                return false;

            long indexSize = checked(((long)imageCount + 1) * 8);
            if (HeaderSize + indexSize > stream.Length)
                return false;

            stream.Position = HeaderSize + ((long)imageCount * 8);
            Span<byte> sentinel = stackalloc byte[8];
            stream.ReadExactly(sentinel);
            uint timestamp = BinaryPrimitives.ReadUInt32LittleEndian(sentinel[..4]);
            uint endOffset = BinaryPrimitives.ReadUInt32LittleEndian(sentinel[4..]);
            return timestamp == SentinelTimestamp && endOffset == stream.Length;
        }
        catch
        {
            return false;
        }
    }

    public static async Task WriteAsync(
        string outputPath,
        IReadOnlyList<string> jpegPaths,
        CancellationToken cancellationToken)
    {
        if (jpegPaths.Count == 0)
            throw new InvalidOperationException("O FFmpeg não produziu imagens para este vídeo.");

        long indexSize = checked(((long)jpegPaths.Count + 1) * 8);
        long nextOffset = HeaderSize + indexSize;
        var entries = new List<(uint Timestamp, uint Offset)>(jpegPaths.Count + 1);

        for (int index = 0; index < jpegPaths.Count; index++)
        {
            long jpegLength = new FileInfo(jpegPaths[index]).Length;
            if (jpegLength <= 0)
                throw new InvalidDataException($"A imagem {jpegPaths[index]} está vazia.");
            if (nextOffset > uint.MaxValue || nextOffset + jpegLength > uint.MaxValue)
                throw new InvalidDataException("O ficheiro BIF excederia o limite de 4 GiB.");

            entries.Add((checked((uint)index), checked((uint)nextOffset)));
            nextOffset += jpegLength;
        }
        entries.Add((SentinelTimestamp, checked((uint)nextOffset)));

        await using FileStream output = new(
            outputPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1024 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        byte[] header = new byte[HeaderSize];
        Magic.CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12, 4), checked((uint)jpegPaths.Count));
        BinaryPrimitives.WriteUInt32LittleEndian(
            header.AsSpan(16, 4), TimestampMultiplierMilliseconds);
        GeneratorMarker.CopyTo(header, GeneratorMarkerOffset);
        await output.WriteAsync(header, cancellationToken);

        byte[] entryBytes = new byte[8];
        foreach ((uint timestamp, uint offset) in entries)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(entryBytes.AsSpan(0, 4), timestamp);
            BinaryPrimitives.WriteUInt32LittleEndian(entryBytes.AsSpan(4, 4), offset);
            await output.WriteAsync(entryBytes, cancellationToken);
        }

        foreach (string jpegPath in jpegPaths)
        {
            await using FileStream jpeg = new(
                jpegPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1024 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);
            await jpeg.CopyToAsync(output, cancellationToken);
        }

        await output.FlushAsync(cancellationToken);
    }
}
