using System.Buffers.Binary;
using KodiSeekPreviewGenerator.Core;

string root = Path.Combine(Path.GetTempPath(), "KodiSeekPreviewGeneratorTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    string first = Path.Combine(root, "00000000.jpg");
    string second = Path.Combine(root, "00000001.jpg");
    await File.WriteAllBytesAsync(first, [0xff, 0xd8, 0x01, 0xff, 0xd9]);
    await File.WriteAllBytesAsync(second, [0xff, 0xd8, 0x02, 0xff, 0xd9]);

    string bif = Path.Combine(root, "episode.bif");
    await BifFile.WriteAsync(bif, [first, second], CancellationToken.None);
    Assert(BifFile.IsValid(bif), "The generated BIF must validate.");

    byte[] bytes = await File.ReadAllBytesAsync(bif);
    Assert(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12, 4)) == 2,
        "The BIF image count must be two.");
    Assert(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16, 4)) == 10_000,
        "The timestamp multiplier must represent ten seconds.");
    Assert(bytes.AsSpan(20, 5).SequenceEqual("KSPG2"u8),
        "The BIF must contain the exact-frame generator revision marker.");
    Assert(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(64, 4)) == 0,
        "The first image timestamp must be zero.");
    Assert(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(72, 4)) == 1,
        "The second image timestamp must be one ten-second unit.");
    Assert(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(80, 4)) == uint.MaxValue,
        "The final index entry must be the BIF sentinel.");

    bytes.AsSpan(20, 5).Clear();
    await File.WriteAllBytesAsync(bif, bytes);
    Assert(!BifFile.IsValid(bif),
        "A BIF from the older midpoint-frame generator must be regenerated.");

    "KSPG2"u8.CopyTo(bytes.AsSpan(20, 5));
    bytes[0] = 0;
    await File.WriteAllBytesAsync(bif, bytes);
    Assert(!BifFile.IsValid(bif), "A corrupted BIF magic value must be rejected.");
}
finally
{
    Directory.Delete(root, recursive: true);
}

Console.WriteLine("BIF smoke tests passed.");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
