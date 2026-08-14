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

        var result = new List<Tag>(requested.Count);
        foreach (var tag in requested)
        {
            if (existing.TryGetValue(tag.Slug, out var entity))
            {
                result.Add(entity);
                continue;
            }

            // Tag.Id is client-generated (Guid.NewGuid()), so EF can't infer "new" from
            // the key alone when this collection is merged into an already-tracked post's
            // navigation property (UpdatePost). Track it explicitly or SaveChanges silently
            // treats it as an existing row and the join-table insert fails its FK constraint.
            var newTag = new Tag { Name = tag.Name, Slug = tag.Slug };
            db.Tags.Add(newTag);
            result.Add(newTag);
        }

        return result;
    }
}
