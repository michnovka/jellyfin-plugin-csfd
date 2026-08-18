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

public sealed record TestMatchResultDto(bool Matched, string? CsfdId, int? Percent, int? Votes, string? Url);

/// <summary>
/// Backs the "unmatched items" report and the match-preview tool on the
/// plugin configuration page.
/// </summary>
[ApiController]
[Route("Plugins/Csfd")]
[Produces(MediaTypeNames.Application.Json)]
public class CsfdController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly Csfd.CsfdResolver _resolver;

    public CsfdController(ILibraryManager libraryManager, Csfd.CsfdResolver resolver)
    {
        _libraryManager = libraryManager;
        _resolver = resolver;
    }

    /// <summary>Dry-runs the matcher for a title/year without touching any item.</summary>
    [HttpGet("TestMatch")]
    [Authorize(Policy = "RequiresElevation")]
    public async Task<ActionResult<TestMatchResultDto>> TestMatch(
        [FromQuery] string title,
        [FromQuery] string? originalTitle,
        [FromQuery] int? year,
        [FromQuery] bool series,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return BadRequest("title is required");
        }

        var resolution = await _resolver.ResolveAsync(title, originalTitle, year, series, cancellationToken).ConfigureAwait(false);
        return resolution is null
            ? new TestMatchResultDto(false, null, null, null, null)
            : new TestMatchResultDto(true, resolution.Id, resolution.Percent, resolution.Votes, $"https://www.csfd.cz/film/{resolution.Id}/prehled/");
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
