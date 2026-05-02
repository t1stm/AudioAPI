using Result.Objects;

namespace Result;

public readonly struct Result<TOk, TError> : IEquatable<Result<TOk, TError>>
{
    public readonly TError? ErrorValue;
    public readonly TOk? OkValue;
    public readonly Status Status;

    /// <summary>
    ///     Creates a result object.
    /// </summary>
    /// <param name="okValue">Value when successfully returned.</param>
    /// <param name="errorValue">Value returned when failed.</param>
    /// <param name="status">The status code of the result.</param>
    public Result(TOk? okValue, TError? errorValue, Status status)
    {
        OkValue = okValue;
        ErrorValue = errorValue;
        Status = status;
    }

    /// <summary>
    ///     Gets the OK object when the status is OK.
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NullReferenceException">Thrown when the OK object is null. This ensures null safety.</exception>
    /// <exception cref="InvalidResultAccessException">Thrown when the status isn't Status.OK</exception>
    public TOk GetOk()
    {
        return this == Status.Ok
            ? OkValue ?? throw new NullReferenceException("OK value is null.")
            : throw new InvalidResultAccessException("Tried to get OK result when status is \'Error\'.");
    }

    /// <summary>
    ///     Gets the Error object when the status is Error.
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NullReferenceException">Thrown when the Error object is null. This ensures null safety.</exception>
    /// <exception cref="InvalidResultAccessException">Thrown when the status isn't Status.Error</exception>
    public TError GetError()
    {
        return this == Status.Error
            ? ErrorValue ?? throw new NullReferenceException("Error value is null.")
            : throw new InvalidResultAccessException("Tried to get Error result when status is \'OK\'.");
    }

    /// <summary>
    ///     Creates a new Result object with the status: Status.OK
    /// </summary>
    /// <param name="ok">The object to assign to T_OK</param>
    /// <returns>The created Result object.</returns>
    public static Result<TOk, TError> Success(TOk ok)
    {
        return new Result<TOk, TError>(ok, default, Status.Ok);
    }

    /// <summary>
    ///     Creates a new Result object with the status: Status.Error
    /// </summary>
    /// <param name="error">The object to assign to T_Error</param>
    /// <returns>The created Result object.</returns>
    public static Result<TOk, TError> Error(TError error)
    {
        return new Result<TOk, TError>(default, error, Status.Error);
    }

    /// <summary>
    ///     Compare the current result with a status code.
    /// </summary>
    /// <param name="source">The source Result object.</param>
    /// <param name="status">The status code to compare.</param>
    /// <returns>Whether the Result object has a matching status.</returns>
    public static bool operator ==(Result<TOk, TError> source, Status status)
    {
        return source.Status == status;
    }

    /// <summary>
    ///     Compare the current result with a status code.
    /// </summary>
    /// <param name="source">The source Result object.</param>
    /// <param name="status">The status code to compare.</param>
    /// <returns>Whether the Result object doesn't have a matching status.</returns>
    public static bool operator !=(Result<TOk, TError> source, Status status)
    {
        return !(source == status);
    }


    public bool Equals(Result<TOk, TError> other)
    {
        return
            EqualityComparer<TError?>.Default.Equals(ErrorValue, other.ErrorValue) &&
            EqualityComparer<TOk?>.Default.Equals(OkValue, other.OkValue) && Status == other.Status;
    }

    public override bool Equals(object? obj)
    {
        return obj is Result<TOk, TError> other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(ErrorValue, OkValue, (int)Status);
    }
}