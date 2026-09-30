using Hangfire.Common;
using Hangfire.Server;

namespace JobPulse.Hangfire;

/// <summary>
/// Global Hangfire server filter that reports every job's start/success/failure
/// to JobPulse. Registered for you by <see cref="JobPulseHangfire.Register"/> —
/// call that once at startup instead of adding this attribute yourself.
///
/// Reporting calls are made synchronously (blocking the Hangfire worker thread
/// briefly) rather than fire-and-forget. This was not the original design —
/// fire-and-forget seemed obviously fine for "best-effort monitoring calls"
/// until a real end-to-end run against a live JobPulse instance showed the
/// "started" and "failed" events for the same run arriving at the server out
/// of order (both are independent async HTTP calls with no ordering
/// guarantee, and a job that runs in microseconds fires its terminal event
/// almost immediately after "started"). Whichever request's upsert commits
/// LAST wins regardless of send order, so the terminal status was silently
/// overwritten back to "started" — a monitoring tool that hides failures is
/// worse than one that adds a few milliseconds of latency. JobPulseClient
/// never throws (see SendAsync), so blocking here can't turn a JobPulse outage
/// into a Hangfire job failure; it can only add latency, bounded by
/// HttpClient's timeout (5s, see JobPulseHangfire.Register).
///
/// Known limitation (v0.1): the JobPulse run id is Hangfire's BackgroundJob.Id,
/// which stays the same across automatic retries. A job that fails and then
/// succeeds on retry will show only the final state, not each retry attempt.
/// Good enough to know "is this job healthy right now"; revisit if per-attempt
/// history is needed.
/// </summary>
public sealed class JobPulseFilterAttribute : JobFilterAttribute, IServerFilter
{
    private const string StartedAtKey = "JobPulse.StartedAt";

    private readonly JobPulseClient _client;

    public JobPulseFilterAttribute(JobPulseClient client)
    {
        _client = client;
    }

    public void OnPerforming(PerformingContext context)
    {
        var startedAt = DateTime.UtcNow;
        context.Items[StartedAtKey] = startedAt;

        _client.ReportStartedAsync(JobName(context.BackgroundJob.Job), context.BackgroundJob.Id, startedAt)
            .GetAwaiter().GetResult();
    }

    public void OnPerformed(PerformedContext context)
    {
        var startedAt = context.Items.TryGetValue(StartedAtKey, out var value) && value is DateTime dt
            ? dt
            : DateTime.UtcNow;
        var finishedAt = DateTime.UtcNow;
        var jobName = JobName(context.BackgroundJob.Job);

        if (context.Exception is not null && !context.ExceptionHandled)
        {
            // context.Exception is usually Hangfire's own JobPerformanceException, whose
            // .Message is the unhelpful generic "An exception occurred during performance
            // of the job." — the actual exception your job threw is its InnerException.
            // Found by checking the reported message against a real thrown exception
            // during end-to-end testing, not by reading Hangfire's source first.
            var reportedException = context.Exception.InnerException ?? context.Exception;
            _client.ReportFailedAsync(jobName, context.BackgroundJob.Id, startedAt, finishedAt, reportedException.Message)
                .GetAwaiter().GetResult();
        }
        else
        {
            _client.ReportSucceededAsync(jobName, context.BackgroundJob.Id, startedAt, finishedAt)
                .GetAwaiter().GetResult();
        }
    }

    private static string JobName(Job job) => $"{job.Type.Name}.{job.Method.Name}";
}
