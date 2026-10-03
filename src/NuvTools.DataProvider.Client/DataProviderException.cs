namespace NuvTools.DataProvider.Client;

/// <summary>
/// Thrown by <see cref="DataProviderResponseExtensions.EnsureSuccessAsync"/> when the platform
/// refused a call.
/// </summary>
/// <remarks>
/// Carries the <see cref="Error"/> rather than flattening it into a message, so a caller catching it
/// can still branch on <see cref="DataProviderError.Type"/> and read <c>Retry-After</c> — which is
/// the whole reason the refusal was classified in the first place.
/// </remarks>
public class DataProviderException : Exception
{
    public DataProviderException(DataProviderError error)
        : base(error?.ToString() ?? throw new ArgumentNullException(nameof(error)))
        => Error = error;

    public DataProviderException() : base() => Error = Empty;

    public DataProviderException(string message) : base(message) => Error = Empty;

    public DataProviderException(string message, Exception innerException)
        : base(message, innerException) => Error = Empty;

    /// <summary>What the platform said.</summary>
    public DataProviderError Error { get; }

    /// <inheritdoc cref="DataProviderError.Type" />
    public DataProviderErrorType Type => Error.Type;

    private static DataProviderError Empty { get; } = new(
        DataProviderErrorType.Unknown, 0, null, null, null, null, null);
}
