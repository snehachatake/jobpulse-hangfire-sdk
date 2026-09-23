using Hangfire;

namespace JobPulse.Hangfire;

/// <summary>
/// Entry point for wiring JobPulse into Hangfire without needing a DI container.
/// Call once at startup, after Hangfire itself is configured.
/// </summary>
public static class JobPulseHangfire
{
    /// <param name="baseUrl">JobPulse API base URL, e.g. "https://api.jobpulse.dev".</param>
    /// <param name="apiKey">Your JobPulse customer API key.</param>
    /// <param name="onError">Optional callback for transport failures (e.g. log a warning). Never throws into your app.</param>
    public static void Register(string baseUrl, string apiKey, Action<Exception>? onError = null)
    {
        var options = new JobPulseOptions { BaseUrl = baseUrl, ApiKey = apiKey };
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var client = new JobPulseClient(httpClient, options, onError);

        GlobalJobFilters.Filters.Add(new JobPulseFilterAttribute(client));
    }
}
