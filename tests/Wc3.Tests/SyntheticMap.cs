// tests/Wc3.Tests/SyntheticMap.cs
using War3Net.IO.Mpq;
namespace Wc3.Tests;

public static class SyntheticMap
{
    public static byte[] Build(IReadOnlyDictionary<string, byte[]> files)
    {
        var builder = new MpqArchiveBuilder();
        foreach (var (name, data) in files)
            builder.AddFile(MpqFile.New(new MemoryStream(data), name));

        using var mpq = new MemoryStream();
        builder.SaveTo(mpq, leaveOpen: true);

        using var outStream = new MemoryStream();
        var header = new byte[0x200];
        header[0] = (byte)'H'; header[1] = (byte)'M'; header[2] = (byte)'3'; header[3] = (byte)'W';
        outStream.Write(header, 0, header.Length);
        mpq.Position = 0;
        mpq.CopyTo(outStream);
        return outStream.ToArray();
    }
}
