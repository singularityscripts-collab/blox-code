using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace BloxCode.Core;

public sealed class ClientVersion
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("clientVersionUpload")] public string VersionGuid { get; set; } = "";
    [JsonPropertyName("bootstrapperVersion")] public string BootstrapperVersion { get; set; } = "";
}

public sealed class InvalidChannelException(string channel, HttpStatusCode? status)
    : Exception($"Channel '{channel}' is not available ({status?.ToString() ?? "no response"})");

/// <summary>
/// Talks to Roblox's deployment infrastructure. Endpoints and the mirror probe were
/// recovered from Bloxstrap.RobloxInterfaces.Deployment (see docs/REVERSE_ENGINEERING.md).
/// </summary>
public static class Deployment
{
    public const string DefaultChannel = Settings.DefaultChannel;
    public const string BinaryType = "WindowsPlayer";
    public const string StudioBinaryType = "WindowsStudio64";

    // Best first for big downloads (measured Nov 2026: Akamai 5.7 MB/s, Roblox's CDN 4.0-4.6, S3 3.3).
    // cachefly is gone: it still answers /versionStudio but returns 404 for every package.
    private static readonly string[] Mirrors =
    {
        "https://setup-ak.rbxcdn.com",
        "https://setup.rbxcdn.com",
        "https://setup-aws.rbxcdn.com",
        "https://s3.amazonaws.com/setup.roblox.com",
    };

    // How long a better-ranked mirror may trail the first one to answer and still be picked.
    private static readonly TimeSpan PreferenceGrace = TimeSpan.FromMilliseconds(750);

    // /versionStudio on every mirror returns this frozen value, which makes it a cheap health probe.
    private const string VersionStudioProbe = "version-012732894899482c";

    // The shared HttpClient allows 10 minutes for big downloads; these small calls must fail fast.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan InfoTimeout = TimeSpan.FromSeconds(15);

    public static string BaseUrl { get; private set; } = Mirrors[0];

    public static string GetLocation(string resource) => $"{BaseUrl}/channel/common{resource}";

    public static async Task InitializeConnectivity(HttpClient http, CancellationToken ct)
    {
        const string ident = "Deployment::InitializeConnectivity";
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pending = Mirrors.Select(m => TestConnection(http, m, cts.Token)).ToList();
        var errors = new List<Exception>();

        // Losing probes keep running after we pick a winner; observe their faults so they aren't left unobserved.
        foreach (var probe in pending)
            _ = probe.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);

        var probes = pending.ToArray();
        while (pending.Count > 0)
        {
            var finished = await Task.WhenAny(pending);
            pending.Remove(finished);
            try
            {
                string first = await finished;

                // Answering a tiny probe first says little about download speed: S3 often wins the
                // race and then downloads slowest. Better-ranked mirrors get a short moment to catch up.
                int rank = Array.IndexOf(probes, finished);
                if (rank > 0)
                {
                    var better = Task.WhenAll(probes.Take(rank).Select(p => p.ContinueWith(_ => { }, TaskScheduler.Default)));
                    await Task.WhenAny(better, Task.Delay(PreferenceGrace, ct));
                }
                BaseUrl = probes.FirstOrDefault(p => p.IsCompletedSuccessfully)?.Result ?? first;

                Logger.Info(ident, BaseUrl == first ? $"Using mirror {BaseUrl}" : $"Using mirror {BaseUrl} (answered after {first})");
                cts.Cancel();
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                errors.Add(ex);
            }
        }

        ct.ThrowIfCancellationRequested();
        throw new AggregateException("Could not reach any Roblox deployment mirror", errors);
    }

    private static async Task<string> TestConnection(HttpClient http, string mirror, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ProbeTimeout);
        string body;
        try
        {
            body = await http.GetStringAsync($"{mirror}/versionStudio", timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"{mirror} did not answer within {ProbeTimeout.TotalSeconds:0}s");
        }
        if (body.Trim() != VersionStudioProbe)
            throw new InvalidDataException($"{mirror}/versionStudio returned an unexpected body");
        return mirror;
    }

    public static async Task<ClientVersion> GetInfo(HttpClient http, string channel, CancellationToken ct, string binaryType = BinaryType)
    {
        const string ident = "Deployment::GetInfo";
        // Production has no channel segment; ".../channel/production" answers 401 like an unknown channel.
        string path = channel == DefaultChannel
            ? $"/v2/client-version/{binaryType}"
            : $"/v2/client-version/{binaryType}/channel/{channel}";
        HttpStatusCode? lastStatus = null;
        Exception? lastError = null;

        foreach (var host in new[] { "https://clientsettingscdn.roblox.com", "https://clientsettings.roblox.com" })
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(InfoTimeout);
                using var response = await http.GetAsync(host + path, timeout.Token);
                lastStatus = response.StatusCode;
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                    throw new InvalidChannelException(channel, response.StatusCode);
                response.EnsureSuccessStatusCode();

                var info = await response.Content.ReadFromJsonAsync<ClientVersion>(timeout.Token);
                if (info is null || !info.VersionGuid.StartsWith("version-", StringComparison.Ordinal))
                    throw new InvalidDataException("Unexpected client-version response");
                Logger.Info(ident, $"{channel}: {info.Version} ({info.VersionGuid}) via {host}");
                return info;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or InvalidDataException
                                       && !ct.IsCancellationRequested)
            {
                lastError = ex;
                Logger.Warn(ident, $"{host} failed: {ex.Message}");
            }
        }

        // Only a real HTTP answer means the channel is bad; otherwise we simply couldn't reach Roblox.
        if (lastStatus is null || (int)lastStatus >= 500)
            throw new HttpRequestException($"Couldn't get the latest Roblox version ({lastError?.Message ?? "no response"})", lastError);
        throw new InvalidChannelException(channel, lastStatus);
    }
}
