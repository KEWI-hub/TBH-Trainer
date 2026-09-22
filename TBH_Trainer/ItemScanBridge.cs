using System.IO.MemoryMappedFiles;
using System.Text;

namespace TBH_Trainer;

/// <summary>
/// Secondary channel: hook writes scan/catalog text (UTF-8).
/// Layout must match ScanState in TBHHook/dllmain.cpp.
/// </summary>
internal sealed class ItemScanBridge : IDisposable
{
    private const string MapName = "TBHTrainerScan";
    private const int MapSize = 524288;
    private const int Magic = 0x53424854; // "TBHS"

    private const long OffMagic = 0x00;
    private const long OffRequest = 0x04;
    private const long OffDone = 0x08;
    private const long OffLength = 0x0C;
    private const long OffText = 0x10;

    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _view;

    public bool TryConnect(int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                _mmf = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.ReadWrite);
                _view = _mmf.CreateViewAccessor(0, MapSize, MemoryMappedFileAccess.ReadWrite);
                if (_view.ReadInt32(OffMagic) == Magic)
                    return true;
                Dispose();
            }
            catch { /* hook not ready */ }
            Thread.Sleep(80);
        }
        return false;
    }

    /// <summary>Stash + inventory scan (even request id).</summary>
    public string? RunScan(int timeoutMs = 10000) =>
        RunInternal(ensureOdd: false, timeoutMs);

    /// <summary>Full game item catalog from yq (odd request id).</summary>
    public string? RunCatalogDump(int timeoutMs = 15000) =>
        RunInternal(ensureOdd: true, timeoutMs);

    private string? RunInternal(bool ensureOdd, int timeoutMs)
    {
        if (_view == null) return null;

        _view.Write(OffDone, 0);
        _view.Write(OffLength, 0);

        int prev = _view.ReadInt32(OffRequest);
        int next = prev + 1;
        if (ensureOdd)
        {
            if ((next & 1) == 0) next++;
        }
        else
        {
            if ((next & 1) != 0) next++;
        }
        _view.Write(OffRequest, next);

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            int done = _view.ReadInt32(OffDone);
            if (done == 1 || done == 2)
            {
                int len = _view.ReadInt32(OffLength);
                if (len <= 0) return "(empty)";
                len = Math.Min(len, MapSize - (int)OffText - 1);
                byte[] buf = new byte[len];
                _view.ReadArray(OffText, buf, 0, len);
                string text = Encoding.UTF8.GetString(buf);
                if (done == 2)
                    text += "\r\n[Note: list was truncated — partial catalog written.]";
                return text;
            }
            if (done < 0) return null;
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
