# JobPulse.Hangfire

Reports every Hangfire job's start/success/failure to [JobPulse](../jobpulse) automatically — no changes to your
existing job methods.

## Build-verified end to end
Previously blocked on not having a local .NET SDK (Homebrew's cask needed sudo, unavailable in that session).
Installed via Microsoft's `dotnet-install.sh` instead, which installs to `~/.dotnet` with no root needed —
that unblocks this on any machine where the cask install isn't an option. From there: `dotnet build` and
`dotnet pack` both succeed with zero warnings, and `TestHarness/` is a real Hangfire server (in-memory storage,
no SQL/Redis needed) that runs a job that succeeds, a job that always fails, and a job that fails once then
succeeds on retry — checked against a real running JobPulse instance's `GET /v1/jobs` and `.../runs`, not just
"it compiled". Run it yourself:
```
cd TestHarness
JOBPULSE_BASE_URL=http://localhost:8080 JOBPULSE_API_KEY=test-key-123 dotnet run
```
(point those at a real `jobpulse` instance — see `../jobpulse/README.md`)

**Two real bugs found this way, both fixed:**
1. **Race condition, not a hypothetical one.** `OnPerforming`'s "started" report and `OnPerformed`'s
   success/failure report were both fire-and-forget HTTP calls with no ordering guarantee between them. A job
   that runs in microseconds fires its terminal event almost immediately after "started", and the two requests
   can arrive at the server out of order — whichever one's upsert commits *last* wins, regardless of send order.
   First run of the test harness reproduced this directly: a job that threw an exception was reported to
   JobPulse as `"started"` forever, never `"failed"`. Fixed by making both reporting calls synchronous
   (`.GetAwaiter().GetResult()`) so there's a strict happens-before relationship between them — confirmed fixed
   across multiple reruns with job execution order varying between them. `JobPulseClient` never throws, so this
   can only add latency (bounded by the 5s `HttpClient` timeout), never turn a JobPulse outage into a Hangfire
   job failure.
2. **Wrong error message.** `context.Exception` is Hangfire's own `JobPerformanceException`, whose `.Message` is
   the generic "An exception occurred during performance of the job." — not what your job actually threw. The
   real exception is `.InnerException`. Fixed to report that instead; confirmed the real message
   ("Simulated failure for JobPulse SDK verification.") now shows up in `GET /v1/jobs/{id}/runs`.

Also removed two unused package references (`Microsoft.Extensions.Http`, `.DependencyInjection.Abstractions`) —
dead weight in every consumer's dependency tree, since `Register()` builds its own `HttpClient` and never touches
a DI container — and fixed a doc comment pointing at a `ServiceCollectionExtensions.AddJobPulse` class that was
never actually built.

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
then succeeds on retry will show only the final state in JobPulse, not each attempt — verified directly in
`TestHarness/DemoJobs.FailsOnceThenSucceeds`: a job that throws on attempt 1 and succeeds on attempt 2 ends up
as a single `"succeeded"` run in JobPulse, with the failed attempt's `error_message` and timing gone. Fine for
"is this job healthy" alerting; would need a tweak (append retry count to the run id) if per-attempt history
matters later.
