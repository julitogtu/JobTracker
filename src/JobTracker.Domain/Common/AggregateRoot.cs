namespace JobTracker.Domain.Common;

public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> domainEvents = [];

    protected AggregateRoot(Guid id)
        : base(id) { }

    protected AggregateRoot() { }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => domainEvents.AsReadOnly();

    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        domainEvents.Add(domainEvent);
    }

    public void ClearDomainEvents() => domainEvents.Clear();
}
