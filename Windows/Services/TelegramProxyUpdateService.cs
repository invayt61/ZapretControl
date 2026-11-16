using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using ZapretControl.Models;

namespace ZapretControl.Services;

public sealed class TelegramProxyUpdateService
{
    private const string UpstreamApiUrl = "https://api.github.com/repos/Flowseal/tg-ws-proxy/releases/latest";
    private const string IntegratedRepository = "invayt61/ZapretControl";
    private static readonly HttpClient Client = CreateClient();
    private readonly TelegramProxyService _proxy;

    public TelegramProxyUpdateService(TelegramProxyService proxy)
    {
        _proxy = proxy;
    }

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
        using var upstreamResponse = await Client.GetAsync(UpstreamApiUrl, cancellationToken);
        upstreamResponse.EnsureSuccessStatusCode();
        await using var upstreamStream = await upstreamResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var upstream = await JsonDocument.ParseAsync(upstreamStream, cancellationToken: cancellationToken);
        var upstreamRoot = upstream.RootElement;
        var version = upstreamRoot.GetProperty("tag_name").GetString()?.TrimStart('v') ?? throw new InvalidDataException("GitHub не вернул версию Telegram-прокси.");
        var releaseTag = "tg-ws-proxy-v" + version;
        using var integratedResponse = await Client.GetAsync($"https://api.github.com/repos/{IntegratedRepository}/releases/tags/{releaseTag}", cancellationToken);
        if (!integratedResponse.IsSuccessStatusCode)
            throw new InvalidDataException($"Интегрированный движок {version} ещё собирается. Попробуйте позже.");
        await using var integratedStream = await integratedResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var integrated = await JsonDocument.ParseAsync(integratedStream, cancellationToken: cancellationToken);
        var asset = integrated.RootElement.GetProperty("assets").EnumerateArray().FirstOrDefault(x =>
            x.GetProperty("name").GetString()?.Equals("TgWsProxy.exe", StringComparison.OrdinalIgnoreCase) == true);
        if (asset.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("В релизе нет интегрированного движка TgWsProxy.exe.");
        var digest = asset.TryGetProperty("digest", out var digestElement) ? digestElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GitHub не предоставил SHA-256 Telegram-прокси. Установка отменена.");

        return new ReleaseInfo
        {
            Version = version,
            Notes = upstreamRoot.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
            PageUrl = upstreamRoot.GetProperty("html_url").GetString() ?? "https://github.com/Flowseal/tg-ws-proxy/releases/latest",
            DownloadUrl = asset.GetProperty("browser_download_url").GetString() ?? throw new InvalidDataException("Не найдена ссылка на Telegram-прокси."),
            Sha256 = digest[7..],
            Size = asset.GetProperty("size").GetInt64(),
            PublishedAt = upstreamRoot.GetProperty("published_at").GetDateTime()
        };
    }

    public async Task InstallAsync(ReleaseInfo release, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_proxy.IsRunning) throw new InvalidOperationException("Перед обновлением остановите Telegram-прокси.");
        Directory.CreateDirectory(_proxy.RootPath);
        var suffix = Guid.NewGuid().ToString("N");
        var download = Path.Combine(_proxy.RootPath, ".download-" + suffix + ".tmp");
        var newExecutable = _proxy.ExecutablePath + ".new-" + suffix;
        var backupExecutable = _proxy.ExecutablePath + ".backup-" + suffix;
        try
        {
            progress?.Report(1);
            using (var response = await Client.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var target = File.Create(download);
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
                await using var hashStream = File.OpenRead(download);
                actualHash = Convert.ToHexString(await Task.Run(() => sha.ComputeHash(hashStream), cancellationToken));
            }
            if (!actualHash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SHA-256 скачанного Telegram-прокси не совпадает. Установка отменена.");
            progress?.Report(95);

            File.Move(download, newExecutable);
            if (File.Exists(_proxy.ExecutablePath)) File.Move(_proxy.ExecutablePath, backupExecutable);
            try { File.Move(newExecutable, _proxy.ExecutablePath); }
            catch
            {
                if (File.Exists(backupExecutable) && !File.Exists(_proxy.ExecutablePath)) File.Move(backupExecutable, _proxy.ExecutablePath);
                throw;
            }
            var versionTemporary = _proxy.VersionPath + ".tmp";
            await File.WriteAllTextAsync(versionTemporary, release.Version + Environment.NewLine, cancellationToken);
            File.Move(versionTemporary, _proxy.VersionPath, true);
            await File.WriteAllTextAsync(_proxy.IntegratedMarkerPath, release.Version + Environment.NewLine, cancellationToken);
            try { if (File.Exists(backupExecutable)) File.Delete(backupExecutable); } catch { }
            progress?.Report(100);
        }
        finally
        {
            try { if (File.Exists(download)) File.Delete(download); } catch { }
            try { if (File.Exists(newExecutable)) File.Delete(newExecutable); } catch { }
        }
    }
}
