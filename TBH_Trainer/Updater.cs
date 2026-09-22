using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace TBH_Trainer;

internal static class AppInfo
{
    /// <summary>Bump together with the GitHub release tag (v&lt;Version&gt;).</summary>
    public const string Version = "1.4.1";
}

/// <summary>
/// Checks the private GitHub repo for a newer release and installs it.
/// Each user authenticates with their own GitHub login: git's stored credential if
/// present, otherwise a personal access token saved in Windows Credential Manager.
/// Nothing is ever embedded in the exe.
/// </summary>
internal static class Updater
{
    private const string Repo = "KEWI-hub/TBH-Trainer";
    private const string CredentialTarget = "TBH Trainer/GitHub";
    public const string ReleasesUrl = "https://github.com/KEWI-hub/TBH-Trainer/releases";
    private const string NotInvitedHint =
        "Automatic updates are only for invited users. Download new releases from " + ReleasesUrl + " (or ask the owner).";

    private sealed record Release(string Tag, Version Version, string Notes, string AssetApiUrl, string AssetName);

    /// <summary>
    /// Startup (<paramref name="interactive"/> = false): only users whose GitHub login can see
    /// the private repo (invited collaborators) are updated; everyone else just gets a hint to
    /// download new releases themselves — no prompts. Interactive (tray menu) additionally
    /// asks invited users without a git login for their own token and reports "up to date".
    /// </summary>
    public static async Task CheckAsync(Form owner, Action<string> log, bool interactive)
    {
        string? token = GitCredentialToken() ?? ReadStoredToken();
        if (token == null)
        {
            if (!interactive) { log(NotInvitedHint); return; }
            token = PromptForToken(owner);
            if (token == null) return;
            WriteStoredToken(token);
        }

        Release? latest;
        try
        {
            latest = await GetLatestReleaseAsync(token);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Unauthorized
                                                 or System.Net.HttpStatusCode.NotFound)
        {
            log("Update check: this GitHub login has no access to the repo. " + NotInvitedHint);
            if (interactive)
            {
                if (ReadStoredToken() != null) DeleteStoredToken();
                MessageBox.Show(owner, "This GitHub login can't access the trainer repo (invite not accepted, or wrong token).\n\n" +
                                       NotInvitedHint, "Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return;
        }
        catch (Exception ex)
        {
            log($"Update check failed: {ex.Message}");
            return;
        }

        var current = Version.Parse(AppInfo.Version);
        if (latest == null || latest.Version <= current)
        {
            log($"Trainer is up to date (v{AppInfo.Version}).");
            if (interactive)
                MessageBox.Show(owner, $"TBH Trainer v{AppInfo.Version} is the latest version.",
                    "Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        log($"Update available: {latest.Tag} (current v{AppInfo.Version}).");
        string notes = latest.Notes.Length > 600 ? latest.Notes[..600] + " …" : latest.Notes;
        if (MessageBox.Show(owner,
                $"TBH Trainer {latest.Tag} is available (you have v{AppInfo.Version}).\n\n{notes}\n\nDownload and install now?",
                "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
            return;

        // TBHHook.dll stays loaded (and locked) inside the game after injection.
        if (Process.GetProcessesByName("TaskBarHero").Length > 0)
        {
            MessageBox.Show(owner, "Close Taskbar Hero first — TBHHook.dll is locked while the game is running.\n" +
                                   "Then reopen the trainer to install the update.",
                "Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            log($"Downloading {latest.AssetName}…");
            string staging = await DownloadAndExtractAsync(token, latest);
            log("Installing update — the trainer will restart.");
            LaunchInstaller(staging);
            Application.Exit();
        }
        catch (Exception ex)
        {
            log($"Update failed: {ex.Message}");
            MessageBox.Show(owner, $"Update failed:\n{ex.Message}", "Update", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ---------------------------------------------------------------- GitHub API

    private static HttpClient CreateClient(string token)
    {
        // Authorization is dropped automatically when the asset download redirects to storage.
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"TBH-Trainer/{AppInfo.Version}");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return http;
    }

    private static async Task<Release?> GetLatestReleaseAsync(string token)
    {
        using var http = CreateClient(token);
        http.Timeout = TimeSpan.FromSeconds(15);
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? "";
            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                return new Release(tag, version, root.GetProperty("body").GetString() ?? "",
                    asset.GetProperty("url").GetString()!, name);
        }
        return null; // release without a zip: nothing to install
    }

    private static async Task<string> DownloadAndExtractAsync(string token, Release release)
    {
        string staging = Path.Combine(Path.GetTempPath(), "TBHTrainerUpdate");
        TryDelete(staging);
        Directory.CreateDirectory(staging);
        string zipPath = Path.Combine(staging, release.AssetName);

        using (var http = CreateClient(token))
        using (var req = new HttpRequestMessage(HttpMethod.Get, release.AssetApiUrl))
        {
            req.Headers.Accept.ParseAdd("application/octet-stream");
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            await using var file = File.Create(zipPath);
            await resp.Content.CopyToAsync(file);
        }

        string extracted = Path.Combine(staging, "files");
        ZipFile.ExtractToDirectory(zipPath, extracted);
        if (!File.Exists(Path.Combine(extracted, "TBH Trainer.exe")))
            throw new InvalidDataException("the release zip does not contain TBH Trainer.exe");
        return extracted;
    }

    /// <summary>
    /// The running exe can't overwrite itself: a hidden cmd waits for this process to exit,
    /// copies the new files over the install folder and starts the new trainer.
    /// </summary>
    private static void LaunchInstaller(string extracted)
    {
        string target = AppContext.BaseDirectory.TrimEnd('\\');
        string exe = Path.Combine(target, "TBH Trainer.exe");
        string script = Path.Combine(Path.GetTempPath(), "TBHTrainerUpdate.cmd");
        int pid = Environment.ProcessId;
        File.WriteAllText(script,
            "@echo off\r\n" +
            ":wait\r\n" +
            $"tasklist /FI \"PID eq {pid}\" | find \"{pid}\" >nul && (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
            $"xcopy /Y /Q \"{extracted}\\*\" \"{target}\\\" >nul\r\n" +
            $"start \"\" \"{exe}\"\r\n" +
            $"rmdir /S /Q \"{Path.GetDirectoryName(extracted)}\"\r\n" +
            "del \"%~f0\"\r\n", Encoding.ASCII);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }

    // ---------------------------------------------------------------- credentials

    /// <summary>Reuses a GitHub login already saved for git on this PC, if any.</summary>
    private static string? GitCredentialToken()
    {
        try
        {
            var psi = new ProcessStartInfo("git", "credential fill")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
            psi.Environment["GCM_INTERACTIVE"] = "never";
            using var p = Process.Start(psi);
            if (p == null) return null;
            p.StandardInput.Write("protocol=https\nhost=github.com\n\n");
            p.StandardInput.Close();
            string output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(10_000)) { try { p.Kill(); } catch { } return null; }
            foreach (string line in output.Split('\n'))
                if (line.StartsWith("password=")) return line[9..].Trim();
        }
        catch { /* git not installed */ }
        return null;
    }

    private static string? PromptForToken(IWin32Window owner)
    {
        using var form = new Form
        {
            Text = "TBH Trainer — GitHub login for updates",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false,
            ClientSize = new Size(460, 190),
        };
        var info = new Label
        {
            Text = "Updates come from the private GitHub repo KEWI-hub/TBH-Trainer.\n" +
                   "Paste a GitHub personal access token from your own account\n" +
                   "(fine-grained, read-only \"Contents\" on that repo). It is saved in\n" +
                   "Windows Credential Manager on this PC only. Only invited users can update this way.",
            Location = new Point(12, 12), Size = new Size(436, 80),
        };
        var box = new TextBox { Location = new Point(12, 100), Width = 436, UseSystemPasswordChar = true };
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Location = new Point(292, 145), Width = 75 };
        var skip = new Button { Text = "Skip", DialogResult = DialogResult.Cancel, Location = new Point(373, 145), Width = 75 };
        form.Controls.AddRange(new Control[] { info, box, ok, skip });
        form.AcceptButton = ok;
        form.CancelButton = skip;
        return form.ShowDialog(owner) == DialogResult.OK && box.Text.Trim().Length > 0 ? box.Text.Trim() : null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    private const int CRED_TYPE_GENERIC = 1;
    private const int CRED_PERSIST_LOCAL_MACHINE = 2;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref CREDENTIAL credential, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    private static string? ReadStoredToken()
    {
        if (!CredRead(CredentialTarget, CRED_TYPE_GENERIC, 0, out var ptr)) return null;
        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
            return cred.CredentialBlobSize > 0
                ? Marshal.PtrToStringUni(cred.CredentialBlob, cred.CredentialBlobSize / 2)
                : null;
        }
        finally { CredFree(ptr); }
    }

    private static void WriteStoredToken(string token)
    {
        byte[] blob = Encoding.Unicode.GetBytes(token);
        IntPtr mem = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, mem, blob.Length);
            var cred = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = CredentialTarget,
                CredentialBlobSize = blob.Length,
                CredentialBlob = mem,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                UserName = "github-token",
            };
            CredWrite(ref cred, 0);
        }
        finally { Marshal.FreeHGlobal(mem); }
    }

    private static void DeleteStoredToken() => CredDelete(CredentialTarget, CRED_TYPE_GENERIC, 0);

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }
}
