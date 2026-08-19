namespace AnimeCatalog.ViewModels;

/// <summary>
/// One-line receipt for an action that stays on the page. Success and failure share the same
/// slot below the button that triggered them, so the colour is the only thing separating a
/// "Saved" from the reason it was not.
/// </summary>
/// <param name="Message">Text shown to the user.</param>
/// <param name="IsError">Renders in <c>--danger</c> instead of <c>--success</c>.</param>
public sealed record ActionFeedback(string Message, bool IsError)
{
    public static ActionFeedback Success(string message) => new(message, IsError: false);

    public static ActionFeedback Error(string message) => new(message, IsError: true);

    public string CssClass => IsError
        ? "action-feedback action-feedback--error"
        : "action-feedback action-feedback--success";
}
