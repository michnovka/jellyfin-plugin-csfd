using System.Net.Mime;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Csfd.Api;

public sealed record UnmatchedItemDto(Guid Id, string Name, int? Year, string Type, string CsfdSearchUrl);

/// <summary>
/// Backs the "unmatched items" report on the plugin configuration page.
/// </summary>
[ApiController]
[Route("Plugins/Csfd")]
[Produces(MediaTypeNames.Application.Json)]
public class CsfdController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;

    public CsfdController(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <summary>Movies and series that have no ČSFD id yet.</summary>
    [HttpGet("Unmatched")]
    [Authorize(Policy = "RequiresElevation")]
    public ActionResult<IEnumerable<UnmatchedItemDto>> GetUnmatched()
    {
        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
            IsVirtualItem = false,
            Recursive = true,
        });

        var unmatched = items
            .Where(i => string.IsNullOrEmpty(i.GetProviderId("Csfd")))
            .OrderBy(i => i.SortName, StringComparer.OrdinalIgnoreCase)
            .Select(i => new UnmatchedItemDto(
                i.Id,
                i.Name,
                i.ProductionYear,
                i is Series ? "Series" : "Movie",
                $"https://www.csfd.cz/hledat/?q={Uri.EscapeDataString(i.Name)}"))
            .ToList();

        return unmatched;
    }
}
