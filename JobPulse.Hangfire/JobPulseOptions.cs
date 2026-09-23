namespace JobPulse.Hangfire;

public class JobPulseOptions
{
    /// <summary>Base URL of the JobPulse API, e.g. "https://api.jobpulse.dev".</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Your JobPulse customer API key.</summary>
    public string ApiKey { get; set; } = string.Empty;
}
