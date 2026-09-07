namespace JobTracker.Application.Common.Results;

public class Result
{
    protected Result(
        bool isSuccess,
        Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (isSuccess && error != Error.None)
        {
            throw new ArgumentException(
                "A successful result cannot contain an error.",
                nameof(error));
        }

        if (!isSuccess && error == Error.None)
        {
            throw new ArgumentException(
                "A failed result must contain an error.",
                nameof(error));
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() =>
        new(true, Error.None);

    public static Result Failure(Error error) =>
        new(false, error);
}

public sealed class Result<T> : Result
{
    private readonly T? value;

    private Result(
        T? value,
        bool isSuccess,
        Error error)
        : base(isSuccess, error)
    {
        this.value = value;
    }

    public T Value =>
        IsSuccess
            ? value!
            : throw new InvalidOperationException(
                "The value of a failed result cannot be accessed.");

    public static Result<T> Success(T value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return new Result<T>(
            value,
            true,
            Error.None);
    }

    public static new Result<T> Failure(Error error) =>
        new(
            default,
            false,
            error);

    public static implicit operator Result<T>(T value) =>
        Success(value);
}