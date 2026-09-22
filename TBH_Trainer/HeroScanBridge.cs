using System.IO.MemoryMappedFiles;
using System.Text;

namespace TBH_Trainer;

internal sealed class HeroScanBridge : IDisposable
{
    private const string MapName = "TBHTrainerHeroes";
    private const int MapSize = 524288;
    private const int Magic = 0x48524854;

    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _view;
    private const long OffCommand = 16;
    private const long OffHeroIndex = 20;
    private const long OffValue = 24;
    private const long OffText = 32;

    public bool TryConnect(int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                _mmf = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.ReadWrite);
                _view = _mmf.CreateViewAccessor(0, MapSize, MemoryMappedFileAccess.ReadWrite);
                if (_view.ReadInt32(0) == Magic)
                    return true;
                Dispose();
            }
            catch { }
            Thread.Sleep(80);
        }
        return false;
    }

    public string? RunScan(int timeoutMs = 10000)
    {
        if (_view == null) return null;
        _view.Write(OffCommand, 0);
        return RunRequest(timeoutMs);
    }

    /// <summary>Command 7: metadata-only dump of hero class layouts (safe on unknown builds).</summary>
    public string? RunLayoutDump(int timeoutMs = 10000)
    {
        if (_view == null) return null;
        _view.Write(OffCommand, 7);
        return RunRequest(timeoutMs);
    }

    public string? WriteValue(int command, int heroIndex, float value, int timeoutMs = 5000)
    {
        if (_view == null) return null;
        _view.Write(OffCommand, command);
        _view.Write(OffHeroIndex, heroIndex);
        _view.Write(OffValue, value);
        return RunRequest(timeoutMs);
    }

    private string? RunRequest(int timeoutMs)
    {
        if (_view == null) return null;
        _view.Write(8, 0);
        _view.Write(12, 0);
        _view.Write(4, _view.ReadInt32(4) + 1);

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            int done = _view.ReadInt32(8);
            if (done is 1 or < 0)
            {
                int len = Math.Clamp(_view.ReadInt32(12), 0, MapSize - (int)OffText - 1);
                byte[] buf = new byte[len];
                _view.ReadArray(OffText, buf, 0, len);
                return Encoding.UTF8.GetString(buf);
            }
            Thread.Sleep(40);
        }
        return null;
    }

    public void Dispose()
    {
        _view?.Dispose();
        _mmf?.Dispose();
        _view = null;
        _mmf = null;
    }
}
