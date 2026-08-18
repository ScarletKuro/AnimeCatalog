namespace AnimeCatalog.Infrastructure;

public enum CatalogAccessState
{
    Available,
    NotConfigured,
    Private,
    Error
}

public sealed class CatalogAccessDeniedException : Exception
{
    public CatalogAccessDeniedException()
        : base(CatalogAccess.PrivateMessage)
    {
    }
}

public static class CatalogAccess
{
    public const string PrivateTitle = "Private catalog";
    public const string PrivateMessage = "This catalog is private. Sign in with an approved account to continue.";

    public static bool IsPrivateAccessDenied(Exception exception)
    {
        if (exception is CatalogAccessDeniedException)
        {
            return true;
        }

        if (exception is PostgrestException { StatusCode: 401 or 403 })
        {
            return true;
        }

        if (exception is AggregateException aggregateException)
        {
            return aggregateException.InnerExceptions.Any(IsPrivateAccessDenied);
        }

        return exception.InnerException is not null && IsPrivateAccessDenied(exception.InnerException);
    }
}
