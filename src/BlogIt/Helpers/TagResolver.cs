using BlogIt.Shared.Data;
using BlogIt.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Shared.Helpers;

public static class TagResolver
{
    public static async Task<ICollection<Tag>> ResolveAsync(
        BlogItDbContext db,
        IEnumerable<string> tagNames,
        CancellationToken cancellationToken = default)
    {
        var requested = tagNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => new { Name = name.Trim(), Slug = SlugHelper.Slugify(name) })
            .Where(tag => tag.Slug.Length > 0)
            .DistinctBy(tag => tag.Slug, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var slugs = requested.Select(tag => tag.Slug).ToList();
        var existing = await db.Tags
            .Where(tag => slugs.Contains(tag.Slug))
            .ToDictionaryAsync(tag => tag.Slug, StringComparer.OrdinalIgnoreCase, cancellationToken);

        return requested
            .Select(tag => existing.TryGetValue(tag.Slug, out var entity)
                ? entity
                : new Tag { Name = tag.Name, Slug = tag.Slug })
            .ToList();
    }
}
