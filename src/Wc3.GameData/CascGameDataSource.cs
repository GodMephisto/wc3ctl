using CascLib.NET;
namespace Wc3.GameData;

public sealed class CascGameDataSource : IGameDataSource
{
    private readonly CascStorage _storage;
    private CascGameDataSource(CascStorage storage) => _storage = storage;

    public static bool TryOpen(string installDir, out CascGameDataSource? source, out string? error)
    {
        source = null; error = null;
        try { source = new CascGameDataSource(new CascStorage(installDir)); return true; }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    public byte[]? ReadFile(string name)
    {
        if (!_storage.TryOpenFile(name, out var stream) || stream is null) return null;
        using (stream)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
    }

    public void Dispose() => _storage.Dispose();
}
