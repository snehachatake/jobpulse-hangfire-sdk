# JobPulse.Hangfire

Reports every Hangfire job's start/success/failure to [JobPulse](../jobpulse) automatically — no changes to your
existing job methods.

## ⚠️ Not yet build-verified
This was written without a local .NET SDK (installing it needed sudo, which wasn't available in this session)
so it has **not been compiled or run yet** — treat it as a first draft to build and fix, not working code.
Before relying on it: `dotnet build`, then run it against a real Hangfire app pointed at a local JobPulse instance
and confirm events actually show up in `GET /v1/jobs`.

## Usage
```csharp
using Hangfire;
using JobPulse.Hangfire;

// After GlobalConfiguration.Configuration.UseSqlServerStorage(...) etc:
JobPulseHangfire.Register(
    baseUrl: "https://api.jobpulse.dev",
    apiKey: "your-jobpulse-api-key",
    onError: ex => Log.Warning(ex, "JobPulse reporting failed")
);
```

That's it — every job enqueued after this runs through the filter and reports its lifecycle automatically.

## Known limitation (v0.1)
Automatic Hangfire retries reuse the same job id, which this SDK uses as the JobPulse run id. A job that fails
then succeeds on retry will show only the final state in JobPulse, not each attempt. Fine for "is this job
healthy" alerting; would need a tweak (append retry count to the run id) if per-attempt history matters later.
