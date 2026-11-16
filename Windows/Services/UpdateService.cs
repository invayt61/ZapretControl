using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using ZapretControl.Models;

namespace ZapretControl.Services;

public sealed class UpdateService
{
    private const string ApiUrl = "https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest";
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ZapretControl/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        return client;
    }

    public async Task<ReleaseInfo> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync(ApiUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var version = root.GetProperty("tag_name").GetString()?.TrimStart('v') ?? throw new InvalidDataException("GitHub не вернул версию релиза.");
        var asset = root.GetProperty("assets").EnumerateArray().FirstOrDefault(x =>
            x.GetProperty("name").GetString()?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true);
        if (asset.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("В релизе нет ZIP-архива.");
        var digest = asset.TryGetProperty("digest", out var digestElement) ? digestElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GitHub не предоставил SHA-256 архива. Обновление отменено.");

        return new ReleaseInfo
        {
            Version = version,
            Notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
            PageUrl = root.GetProperty("html_url").GetString() ?? "https://github.com/Flowseal/zapret-discord-youtube/releases/latest",
            DownloadUrl = asset.GetProperty("browser_download_url").GetString() ?? throw new InvalidDataException("Не найдена ссылка на архив."),
            Sha256 = digest[7..],
            Size = asset.GetProperty("size").GetInt64(),
            PublishedAt = root.GetProperty("published_at").GetDateTime()
        };
    }

    public static bool IsNewer(string candidate, string current)
    {
        if (Version.TryParse(candidate, out var next) && Version.TryParse(current, out var installed)) return next > installed;
        return !candidate.Equals(current, StringComparison.OrdinalIgnoreCase);
    }

    public async Task InstallAsync(ReleaseInfo release, string enginePath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var updatesRoot = Path.Combine(AppContext.BaseDirectory, ".updates");
        var work = Path.Combine(updatesRoot, Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(work, "release.zip");
        var extractPath = Path.Combine(work, "extracted");
        var newEngine = enginePath + ".new-" + Guid.NewGuid().ToString("N");
        var backupEngine = enginePath + ".backup-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(work);
        try
        {
            progress?.Report(1);
            using (var response = await Client.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var target = File.Create(zipPath);
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    total += read;
                    if (release.Size > 0) progress?.Report(Math.Min(85, total * 85.0 / release.Size));
                }
            }

            progress?.Report(88);
            string actualHash;
            using (var sha = SHA256.Create())
            {
                await using var hashStream = File.OpenRead(zipPath);
                actualHash = Convert.ToHexString(await Task.Run(() => sha.ComputeHash(hashStream), cancellationToken));
            }
            if (!actualHash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SHA-256 скачанного архива не совпадает. Файлы не изменены.");

            Directory.CreateDirectory(extractPath);
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var root = Path.GetFullPath(extractPath) + Path.DirectorySeparatorChar;
                foreach (var entry in archive.Entries)
                {
                    var destination = Path.GetFullPath(Path.Combine(extractPath, entry.FullName));
                    if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Архив содержит небезопасный путь.");
                    if (string.IsNullOrEmpty(entry.Name)) Directory.CreateDirectory(destination);
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        entry.ExtractToFile(destination, true);
                    }
                }
            }

            var sourceEngine = FindEngineRoot(extractPath);
            CopyDirectory(sourceEngine, newEngine);
            PreserveUserFiles(enginePath, newEngine);
            if (!File.Exists(Path.Combine(newEngine, "bin", "winws.exe")) || Directory.GetFiles(newEngine, "general*.bat").Length == 0)
                throw new InvalidDataException("Архив не содержит корректное ядро zapret.");
            progress?.Report(94);

            if (Directory.Exists(enginePath)) Directory.Move(enginePath, backupEngine);
            try { Directory.Move(newEngine, enginePath); }
            catch
            {
                if (Directory.Exists(backupEngine) && !Directory.Exists(enginePath)) Directory.Move(backupEngine, enginePath);
                throw;
            }
            try { if (Directory.Exists(backupEngine)) Directory.Delete(backupEngine, true); } catch { }
            progress?.Report(100);
        }
        finally
        {
            try { if (Directory.Exists(newEngine)) Directory.Delete(newEngine, true); } catch { }
            try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch { }
            try { if (Directory.Exists(updatesRoot) && !Directory.EnumerateFileSystemEntries(updatesRoot).Any()) Directory.Delete(updatesRoot); } catch { }
        }
    }

    private static string FindEngineRoot(string extractPath)
    {
        if (File.Exists(Path.Combine(extractPath, "bin", "winws.exe"))) return extractPath;
        return Directory.EnumerateDirectories(extractPath, "*", SearchOption.AllDirectories)
            .FirstOrDefault(x => File.Exists(Path.Combine(x, "bin", "winws.exe")))
            ?? throw new InvalidDataException("winws.exe не найден в скачанном архиве.");
    }

    private static void PreserveUserFiles(string oldEngine, string newEngine)
    {
        string[] files = { "list-general-user.txt", "list-exclude-user.txt", "ipset-exclude-user.txt" };
        foreach (var file in files)
        {
            var source = Path.Combine(oldEngine, "lists", file);
            if (!File.Exists(source)) continue;
            var destination = Path.Combine(newEngine, "lists", file);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, true);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var directory in Directory.EnumerateDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
