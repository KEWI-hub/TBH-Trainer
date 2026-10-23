using System.Security.Cryptography;
using System.Text;

namespace TBH_Trainer;

/// <summary>
/// A local command channel so the log can be read and the buttons pressed without a human at
/// the keyboard. It exists to make a build testable end to end in one pass.
///
/// It is deliberately hard to turn on anywhere else: it only starts on the one machine whose
/// MachineGuid hash is baked in below, and it talks through files in this user's own temp
/// folder, so it is reachable only by this account on this machine and never over a network.
///
/// A file channel rather than a named pipe: the trainer runs elevated, which puts an elevated
/// pipe's default ACL out of reach of an ordinary shell, while a file in the user's temp stays
/// readable and writable from both sides because both run as the same user.
///
/// Protocol: the caller writes "req.&lt;id&gt;.txt" holding one line, and the answer appears as
/// "res.&lt;id&gt;.txt". Requests are deleted as they are picked up. Nothing here takes a file
/// path or a command line from the caller - only the fixed verbs in MainForm's dispatcher.
/// </summary>
internal sealed class DebugServer : IDisposable
{
    /// <summary>
    /// SHA-256 of HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid on the machine this build is
    /// for. The GUID itself is not stored, so the constant gives nothing away about the machine.
    /// </summary>
    private const string AllowedMachine =
        "c9d7a2606f1a73ca44a17d8523b6856c93adcea0a1973816d7f73a4606f8b6ff";

    public static string ChannelDir =>
        Path.Combine(Path.GetTempPath(), "KEWI_TBH.debug");

    private readonly Func<string, string> _handle;
    private CancellationTokenSource? _cts;

    public DebugServer(Func<string, string> handle) => _handle = handle;

    /// <summary>True only on the machine this build was made for.</summary>
    public static bool IsAllowedMachine()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine
                .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            if (key?.GetValue("MachineGuid") is not string guid) return false;
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(guid)))
                                 .ToLowerInvariant();
            return CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(AllowedMachine));
        }
        catch { return false; }
    }

    /// <summary>Starts listening. Returns false when this is not the machine for it.</summary>
    public bool Start()
    {
        if (!IsAllowedMachine()) return false;
        try
        {
            Directory.CreateDirectory(ChannelDir);
            foreach (string stale in Directory.GetFiles(ChannelDir))
                try { File.Delete(stale); } catch { }
        }
        catch { return false; }

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => LoopAsync(_cts.Token));
        return true;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                foreach (string req in Directory.GetFiles(ChannelDir, "req.*.txt"))
                {
                    string id = Path.GetFileName(req)[4..^4];
                    string line;
                    try { line = File.ReadAllText(req).Trim(); }
                    catch { continue; }          // still being written: pick it up next pass
                    try { File.Delete(req); } catch { }

                    string reply;
                    try { reply = _handle(line); }
                    catch (Exception ex) { reply = "ERROR " + ex.Message; }

                    // Written aside and moved, so the caller never reads a half-written answer.
                    string tmp = Path.Combine(ChannelDir, $"res.{id}.tmp");
                    string res = Path.Combine(ChannelDir, $"res.{id}.txt");
                    File.WriteAllText(tmp, reply, Encoding.UTF8);
                    File.Move(tmp, res, overwrite: true);
                }
            }
            catch { /* a transient IO error must not take the listener down */ }

            try { await Task.Delay(150, ct); } catch { return; }
        }
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        _cts?.Dispose();
        _cts = null;
    }
}
