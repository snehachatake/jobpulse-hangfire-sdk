using Hangfire.Common;
using Hangfire.Server;

namespace JobPulse.Hangfire;

/// <summary>
/// Global Hangfire server filter that reports every job's start/success/failure
/// to JobPulse. Register once at startup via <c>GlobalJobFilters.Filters.Add(...)</c>
/// (see ServiceCollectionExtensions.AddJobPulse) — no per-job attribute needed.
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

        _ = _client.ReportStartedAsync(JobName(context.BackgroundJob.Job), context.BackgroundJob.Id, startedAt);
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
            _ = _client.ReportFailedAsync(jobName, context.BackgroundJob.Id, startedAt, finishedAt, context.Exception.Message);
        }
        else
        {
            _ = _client.ReportSucceededAsync(jobName, context.BackgroundJob.Id, startedAt, finishedAt);
        }
    }

    private static string JobName(Job job) => $"{job.Type.Name}.{job.Method.Name}";
}
