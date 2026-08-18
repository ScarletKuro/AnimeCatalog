using AnimeCatalog.Infrastructure;
using System.Text.Json.Serialization;

namespace AnimeCatalog.Models;

[JsonConverter(typeof(CatalogStatusJsonConverter))]
public enum CatalogStatus
{
    Planned,
    Watching,
    Completed,
    OnHold,
    Dropped
}
