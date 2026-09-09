using JobTracker.Application.Common.Correlation;

namespace JobTracker.BackgroundJob.Correlation;

public sealed class AmbientCorrelationIdAccessor : ICorrelationIdAccessor
{
    private static readonly AsyncLocal<string?> Current = new();

    public string CorrelationId => Current.Value ?? ICorrelationIdAccessor.None;

    public static IDisposable Push(string? correlationId)
    {
        var previous = Current.Value;
        Current.Value = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId;

        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            Current.Value = previous;
        }
    }
}
