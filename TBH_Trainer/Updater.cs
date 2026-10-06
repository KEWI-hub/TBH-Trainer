using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace TBH_Trainer;

internal static class AppInfo
{
    /// <summary>Bump together with the GitHub release tag (v&lt;Version&gt;).</summary>
    public const string Version = "2.1.0";
}

/// <summary>
/// Checks the GitHub repo for a newer release and installs it.
/// The repo is public, so the release list and the zip are fetched without any login:
/// nothing is stored, nothing is prompted for, and no GitHub account is needed.
/// </summary>
internal static class Updater
{
    private const string Repo = "KEWI-hub/TBH-Trainer";
    public const string ReleasesUrl = "https://github.com/KEWI-hub/TBH-Trainer/releases";

    /// <summary>Credential left behind by the versions that needed a token; removed on first run.</summary>
    private const string OldCredentialTarget = "TBH Trainer/GitHub";

    private sealed record Release(string Tag, Version Version, string Notes, string AssetApiUrl, string AssetName);

    /// <summary>
    /// Looks for a newer release. <paramref name="interactive"/> = false is the startup check and
    /// stays quiet unless there is something to install; the tray menu passes true and also
    /// reports "up to date" or why the check failed.
    /// </summary>
    public static async Task CheckAsync(Form owner, Action<string> log, bool interactive)
    {
        DeleteOldToken();

        Release? latest;
        try
        {
            latest = await GetLatestReleaseAsync();
        }
        catch (Exception ex)
        {
            // No network, GitHub down, or the anonymous rate limit (60 requests per hour per IP).
            log($"Update check failed: {ex.Message}");
            if (interactive)
                MessageBox.Show(owner, $"Could not reach GitHub to check for updates:\n{ex.Message}\n\n" +
                                       "You can always download the latest release from\n" + ReleasesUrl,
                    "Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        string notes = latest.Notes.Length > 600 ? latest.Notes[..600] + " â€¦" : latest.Notes;
        if (MessageBox.Show(owner,
                $"TBH Trainer {latest.Tag} is available (you have v{AppInfo.Version}).\n\n{notes}\n\nDownload and install now?",
                "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
            return;

        // TBHHook.dll stays loaded (and locked) inside the game after injection.
        if (Process.GetProcessesByName("TaskBarHero").Length > 0)
        {
            MessageBox.Show(owner, "Close Taskbar Hero first â€” TBHHook.dll is locked while the game is running.\n" +
                                   "Then reopen the trainer to install the update.",
                "Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            log($"Downloading {latest.AssetName}â€¦");
            string staging = await DownloadAndExtractAsync(latest);
            log("Installing update â€” the trainer will restart.");
            LaunchInstaller(staging);
            Application.Exit();
        }
        catch (Exception ex)
        {
            log($"Update failed: {ex.Message}");
            MessageBox.Show(owner, $"Update failed:\n{ex.Message}\n\nDownload it manually from\n" + ReleasesUrl,
                "Update", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ---------------------------------------------------------------- GitHub API

    private static HttpClient CreateClient()
    {
        // No Authorization header: the repo is public, so GitHub serves this to anyone.
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"TBH-Trainer/{AppInfo.Version}");
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return http;
    }

    private static async Task<Release?> GetLatestReleaseAsync()
    {
        using var http = CreateClient();
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

    private static async Task<string> DownloadAndExtractAsync(Release release)
    {
        string staging = Path.Combine(Path.GetTempPath(), "TBHTrainerUpdate");
        TryDelete(staging);
        Directory.CreateDirectory(staging);
        string zipPath = Path.Combine(staging, release.AssetName);

        using (var http = CreateClient())
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

    // ---------------------------------------------------------------- leftovers

    /// <summary>
    /// Updates used to need a personal access token, kept in Windows Credential Manager.
    /// It is useless now that the repo is public, so do not leave the user's token on disk.
    /// </summary>
    private static void DeleteOldToken()
    {
        try { CredDelete(OldCredentialTarget, CRED_TYPE_GENERIC, 0); } catch { }
    }

    private const int CRED_TYPE_GENERIC = 1;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

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
