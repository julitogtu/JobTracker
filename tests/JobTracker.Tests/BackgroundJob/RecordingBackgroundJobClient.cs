using Hangfire;
using Hangfire.States;
using HangfireJob = Hangfire.Common.Job;

namespace JobTracker.Tests.BackgroundJob;

internal sealed class RecordingBackgroundJobClient : IBackgroundJobClient
{
    private readonly List<EnqueuedJob> enqueued = [];

    public IReadOnlyList<EnqueuedJob> Enqueued => enqueued;

    public string Create(HangfireJob job, IState state)
    {
        var id = Guid.CreateVersion7().ToString();
        enqueued.Add(new EnqueuedJob(id, job, state));

        return id;
    }

    public bool ChangeState(string jobId, IState state, string expectedState) => true;

    public TArgument SingleArgumentFor<T, TArgument>()
    {
        var job = enqueued.Single(entry => entry.Job.Type == typeof(T));

        return job.Job.Args.OfType<TArgument>().Single();
    }

    internal sealed record EnqueuedJob(string Id, HangfireJob Job, IState State);
}
