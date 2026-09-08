namespace JobTracker.Application.Common.Correlation;

public sealed class NullCorrelationIdAccessor : ICorrelationIdAccessor
{
    public string CorrelationId => ICorrelationIdAccessor.None;
}
