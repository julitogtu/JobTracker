namespace JobTracker.Application.Common.Correlation;

public interface ICorrelationIdAccessor
{
    const string None = "none";

    string CorrelationId { get; }
}
