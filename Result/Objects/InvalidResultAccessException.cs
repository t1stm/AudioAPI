namespace Result.Objects;

public class InvalidResultAccessException : Exception
{
    /// <summary>
    /// Exception used in the Result class.
    /// </summary>
    /// <param name="message">Exception message.</param>
    public InvalidResultAccessException(string message) : base(message)
    {
    }
}