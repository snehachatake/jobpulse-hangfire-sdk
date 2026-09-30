// Real integration check for JobPulse.Hangfire: a genuine Hangfire server
// (in-memory storage, no SQL/Redis needed) running a job that succeeds and a
// job that throws, with JobPulseHangfire.Register wired in exactly the way a
// real consumer would. Run this against a live `jobpulse` API instance
// (see ../../jobpulse) and then check `GET /v1/jobs` to confirm both runs
// were reported correctly — that's the actual test; this program alone only
// proves nothing crashed.
using Hangfire;
using Hangfire.InMemory;
using JobPulse.Hangfire;

var baseUrl = Environment.GetEnvironmentVariable("JOBPULSE_BASE_URL") ?? "http://localhost:8080";
var apiKey = Environment.GetEnvironmentVariable("JOBPULSE_API_KEY") ?? "test-key-123";

Console.WriteLine($"Reporting to {baseUrl} with key {apiKey}");

GlobalConfiguration.Configuration.UseInMemoryStorage();
JobPulseHangfire.Register(baseUrl, apiKey, onError: ex => Console.WriteLine($"[JobPulse reporting error] {ex}"));

using (var server = new BackgroundJobServer())
{
    var successId = BackgroundJob.Enqueue(() => DemoJobs.SucceedsQuickly());
    var failId = BackgroundJob.Enqueue(() => DemoJobs.AlwaysFails());
    var retryId = BackgroundJob.Enqueue(() => DemoJobs.FailsOnceThenSucceeds());
    Console.WriteLine($"Enqueued success job {successId}, failing job {failId}, retry-then-succeed job {retryId}");

    // Give the server time to pick up and run both jobs (and the failing one
    // to exhaust Hangfire's default automatic retries) before shutting down.
    Console.WriteLine("Waiting 20s for jobs to run...");
    await Task.Delay(TimeSpan.FromSeconds(20));
}

// One extra pause so any in-flight fire-and-forget JobPulse HTTP calls land
// before the process exits (JobPulseClient doesn't await/track them from the
// filter's synchronous Hangfire hook, by design — see JobPulseFilterAttribute).
await Task.Delay(TimeSpan.FromSeconds(2));
Console.WriteLine("Done.");

public static class DemoJobs
{
    public static void SucceedsQuickly()
    {
        Console.WriteLine("DemoJobs.SucceedsQuickly running...");
    }

    [AutomaticRetry(Attempts = 0)] // keep the test run short and the JobPulse history simple
    public static void AlwaysFails()
    {
        Console.WriteLine("DemoJobs.AlwaysFails running (about to throw)...");
        throw new InvalidOperationException("Simulated failure for JobPulse SDK verification.");
    }

    private static int _retryAttempt;

    // Verifies the documented "retries share one run id" limitation directly,
    // rather than just asserting it in a doc comment: this should end up
    // reported as a single 'succeeded' run, not a 'failed' run followed by a
    // separate one.
    [AutomaticRetry(Attempts = 1, DelaysInSeconds = new[] { 1 })]
    public static void FailsOnceThenSucceeds()
    {
        var attempt = Interlocked.Increment(ref _retryAttempt);
        Console.WriteLine($"DemoJobs.FailsOnceThenSucceeds attempt {attempt}...");
        if (attempt == 1)
        {
            throw new InvalidOperationException("Simulated first-attempt failure (should be retried).");
        }
    }
}
