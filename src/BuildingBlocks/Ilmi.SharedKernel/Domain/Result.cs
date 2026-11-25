namespace Ilmi.SharedKernel.Domain;

public enum ErrorType { Validation, NotFound, Conflict, Unauthorized, Forbidden, Locked, Failure }

/// <summary>A business or validation failure. <see cref="Code"/> is stable and machine readable.</summary>
public sealed record Error(string Code, string Message, ErrorType Type = ErrorType.Failure)
{
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);
    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);
    public static Error Locked(string code, string message) => new(code, message, ErrorType.Locked);
}

public class Result
{
    protected Result(bool ok, Error? error) { IsSuccess = ok; Error = error; }
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error? Error { get; }

    public static Result Success() => new(true, null);
    public static Result Failure(Error error) => new(false, error);
    public static Result<T> Success<T>(T value) => new(value, true, null);
    public static Result<T> Failure<T>(Error error) => new(default, false, error);

    public static implicit operator Result(Error error) => Failure(error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;
    internal Result(T? value, bool ok, Error? error) : base(ok, error) => _value = value;
    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("A failed result has no value.");

    public static implicit operator Result<T>(Error error) => Failure<T>(error);
    public static implicit operator Result<T>(T value) => Success(value);
}
