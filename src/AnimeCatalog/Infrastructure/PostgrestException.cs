using AnimeCatalog.Models;

namespace AnimeCatalog.Infrastructure;

public sealed class PostgrestException : Exception
{
    public PostgrestException(PostgrestError error, int statusCode)
        : base(error.Message)
    {
        Error = error;
        StatusCode = statusCode;
    }

    public PostgrestError Error { get; }

    public int StatusCode { get; }
}
