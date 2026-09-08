namespace JobTracker.Application.Jobs.Queries;

public sealed record PagedList<T>(IReadOnlyList<T> Items, string? NextCursor, int PageSize)
{
    public bool HasNextPage => NextCursor is not null;
}
