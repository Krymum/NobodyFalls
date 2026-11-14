namespace NobodyFalls.Domain.Common;

/// <summary>Outcome of a rule check that carries no value.</summary>
public sealed class Result
{
    private static readonly Result SuccessInstance = new(null);

    private Result(DomainError? error) => Error = error;

    public DomainError? Error { get; }

    public bool IsSuccess => Error is null;

    public bool IsFailure => !IsSuccess;

    public static Result Success() => SuccessInstance;

    public static Result Failure(DomainError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(error);
    }

    public static Result<T> Success<T>(T value) => Result<T>.FromValue(value);

    public static Result<T> Failure<T>(DomainError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return Result<T>.FromError(error);
    }

    public static implicit operator Result(DomainError error) => Failure(error);
}

/// <summary>Outcome of a rule check that produces a value on success.</summary>
public sealed class Result<T>
{
    private readonly T? _value;

    private Result(T? value, DomainError? error)
    {
        _value = value;
        Error = error;
    }

    public DomainError? Error { get; }

    public bool IsSuccess => Error is null;

    public bool IsFailure => !IsSuccess;

    /// <summary>The value. Throws when the result is a failure, to surface programming errors early.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot read the value of a failed result ({Error}).");

    public static implicit operator Result<T>(DomainError error) => Result.Failure<T>(error);

    internal static Result<T> FromValue(T value) => new(value, null);

    internal static Result<T> FromError(DomainError error) => new(default, error);
}
