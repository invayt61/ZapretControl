using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using ZapretControl.Models;

namespace ZapretControl.Services;

public sealed class AppUpdateService
{
    private const string ApiUrl = "https://api.github.com/repos/invayt61/ZapretControl/releases/latest";
    private static readonly HttpClient Client = CreateClient();

    public sealed record PreparedUpdate(string WorkDirectory, string ExecutablePath);

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
        var version = root.GetProperty("tag_name").GetString()?.TrimStart('v') ?? throw new InvalidDataException("GitHub не вернул версию приложения.");
        var asset = root.GetProperty("assets").EnumerateArray().FirstOrDefault(x =>
            x.GetProperty("name").GetString()?.Equals("ZapretControl-win-x64.zip", StringComparison.OrdinalIgnoreCase) == true);
        if (asset.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("В релизе нет ZapretControl-win-x64.zip.");
        var digest = asset.TryGetProperty("digest", out var digestElement) ? digestElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GitHub не предоставил SHA-256 приложения. Обновление отменено.");
        return new ReleaseInfo
        {
            Version = version,
            Notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
            PageUrl = root.GetProperty("html_url").GetString() ?? "https://github.com/invayt61/ZapretControl/releases/latest",
            DownloadUrl = asset.GetProperty("browser_download_url").GetString() ?? throw new InvalidDataException("Не найдена ссылка на приложение."),
            Sha256 = digest[7..],
            Size = asset.GetProperty("size").GetInt64(),
            PublishedAt = root.GetProperty("published_at").GetDateTime()
        };
    }

    public async Task<PreparedUpdate> PrepareAsync(ReleaseInfo release, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var work = Path.Combine(Path.GetTempPath(), "ZapretControl-app-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var zipPath = Path.Combine(work, "release.zip");
        var executablePath = Path.Combine(work, "ZapretControl.exe");
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
                    if (release.Size > 0) progress?.Report(Math.Min(90, total * 90.0 / release.Size));
                }
            }
            string actualHash;
            using (var sha = SHA256.Create())
            {
                await using var hashStream = File.OpenRead(zipPath);
                actualHash = Convert.ToHexString(await Task.Run(() => sha.ComputeHash(hashStream), cancellationToken));
            }
            if (!actualHash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SHA-256 обновления приложения не совпадает. Обновление отменено.");
            progress?.Report(95);
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entry = archive.Entries.FirstOrDefault(x => x.FullName.Equals("ZapretControl.exe", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException("В архиве не найден ZapretControl.exe.");
                entry.ExtractToFile(executablePath, true);
            }
            using (var executable = File.OpenRead(executablePath))
            {
                if (executable.ReadByte() != 'M' || executable.ReadByte() != 'Z') throw new InvalidDataException("Обновление не содержит исполняемый файл Windows.");
            }
            progress?.Report(100);
            return new PreparedUpdate(work, executablePath);
        }
        catch
        {
            try { Directory.Delete(work, true); } catch { }
            throw;
        }
    }

    public void StartUpdater(PreparedUpdate update)
    {
        var target = Environment.ProcessPath ?? throw new InvalidOperationException("Не удалось определить путь приложения.");
        if (!Path.GetFileName(target).Equals("ZapretControl.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Обновление доступно только для установленного ZapretControl.exe.");
        var script = Path.Combine(update.WorkDirectory, "install.ps1");
        File.WriteAllText(script, @"
            param([int]$ProcessId, [string]$Source, [string]$Target)
            $ErrorActionPreference = 'Stop'
            try {
                while (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 300 }
                Copy-Item -LiteralPath $Source -Destination $Target -Force
                Start-Process -FilePath $Target -WorkingDirectory (Split-Path -Parent $Target)
            } finally {
                Remove-Item -LiteralPath $PSScriptRoot -Recurse -Force -ErrorAction SilentlyContinue
            }
        ");
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-WindowStyle");
        startInfo.ArgumentList.Add("Hidden");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add("-ProcessId");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add("-Source");
        startInfo.ArgumentList.Add(update.ExecutablePath);
        startInfo.ArgumentList.Add("-Target");
        startInfo.ArgumentList.Add(target);
        Process.Start(startInfo);
    }
}
