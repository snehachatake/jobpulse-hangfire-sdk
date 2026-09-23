using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace JobPulse.Hangfire;

internal sealed class EventRequest
{
    [JsonPropertyName("job_name")]
    public string JobName { get; set; } = string.Empty;

    [JsonPropertyName("run_id")]
    public string RunId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("started_at")]
    public DateTime StartedAt { get; set; }

    [JsonPropertyName("finished_at")]
    public DateTime? FinishedAt { get; set; }

    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Thin HTTP client for the JobPulse ingestion API. Never throws — a JobPulse
/// outage or misconfiguration must never fail the underlying Hangfire job.
/// </summary>
public sealed class JobPulseClient
{
    private readonly HttpClient _httpClient;
    private readonly JobPulseOptions _options;
    private readonly Action<Exception>? _onError;

    public JobPulseClient(HttpClient httpClient, JobPulseOptions options, Action<Exception>? onError = null)
    {
        _httpClient = httpClient;
        _options = options;
        _onError = onError;
    }

    public Task ReportStartedAsync(string jobName, string runId, DateTime startedAt)
        => SendAsync(new EventRequest
        {
            JobName = jobName,
            RunId = runId,
            Status = "started",
            StartedAt = startedAt,
        });

    public Task ReportSucceededAsync(string jobName, string runId, DateTime startedAt, DateTime finishedAt)
        => SendAsync(new EventRequest
        {
            JobName = jobName,
            RunId = runId,
            Status = "succeeded",
            StartedAt = startedAt,
            FinishedAt = finishedAt,
        });

    public Task ReportFailedAsync(string jobName, string runId, DateTime startedAt, DateTime finishedAt, string errorMessage)
        => SendAsync(new EventRequest
        {
            JobName = jobName,
            RunId = runId,
            Status = "failed",
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            ErrorMessage = errorMessage,
        });

    private async Task SendAsync(EventRequest evt)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/v1/events")
            {
                Content = JsonContent.Create(evt),
            };
            request.Headers.Add("X-API-Key", _options.ApiKey);

            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            // Intentionally not throwing on non-2xx: monitoring must be best-effort.
        }
        catch (Exception ex)
        {
            _onError?.Invoke(ex);
        }
    }
}
