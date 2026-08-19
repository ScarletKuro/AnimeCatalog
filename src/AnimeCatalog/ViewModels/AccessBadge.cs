namespace AnimeCatalog.ViewModels;

/// <summary>
/// Glyph shown by an <c>AccessState</c> card. The two states it distinguishes are not
/// interchangeable: a padlock means "not signed in", a barred shield means "signed in and still
/// not permitted", which is the difference between a sign-in prompt and a dead end.
/// </summary>
public enum AccessBadge
{
    Lock,
    Shield
}
