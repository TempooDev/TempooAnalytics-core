namespace TempooAnalytics.Api.Infrastructure.Common;

public record Result
{
    public bool IsSuccess { get; }
    public string? ErrorMessage { get; }
    public ResultErrorType ErrorType { get; }

    protected Result(bool success, string? errorMessage, ResultErrorType errorType)
    {
        IsSuccess = success;
        ErrorMessage = errorMessage;
        ErrorType = errorType;
    }

    public static Result Success() => new(true, null, ResultErrorType.None);
    public static Result Failure(string message, ResultErrorType type = ResultErrorType.Failure) => new(false, message, type);
    
    public static Result<T> Success<T>(T value) => Result<T>.Success(value);
    public static Result<T> Failure<T>(string message, ResultErrorType type = ResultErrorType.Failure) => Result<T>.Failure(message, type);
}

public record Result<T> : Result
{
    public T? Value { get; }

    private Result(bool success, T? value, string? errorMessage, ResultErrorType errorType) 
        : base(success, errorMessage, errorType)
    {
        Value = value;
    }

    public static Result<T> Success(T value) => new(true, value, null, ResultErrorType.None);
    public new static Result<T> Failure(string message, ResultErrorType type = ResultErrorType.Failure) => new(false, default, message, type);
}

public enum ResultErrorType
{
    None,
    Failure,
    NotFound,
    Unauthorized,
    Forbidden,
    Conflict,
    Validation
}
