using BlogIt.Shared.Data;
using BlogIt.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Shared.Helpers;

public static class TagResolver
{
    /// <summary>
    /// Reports a requested tag name too long for its column, under the <c>tagNames</c> key.
    /// </summary>
    /// <remarks>
    /// One key for the whole list rather than one per index: the admin edits tags as a single field,
    /// so an error attached to <c>tagNames[3]</c> has nowhere to be displayed.
    /// </remarks>
    public static void Validate(Dictionary<string, string[]> errors, IEnumerable<string>? tagNames)
    {
        if (tagNames is null)
            return;

        if (tagNames.Any(name => name?.Trim().Length > ContentLimits.TagNameLength))
        {
            errors["tagNames"] =
                [$"Each tag name must be {ContentLimits.TagNameLength} characters or fewer."];
        }
    }

    public static async Task<ICollection<Tag>> ResolveAsync(
        BlogItDbContext db,
        IEnumerable<string> tagNames,
        CancellationToken cancellationToken = default)
    {
        var requested = tagNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => new { Name = name.Trim(), Slug = SlugHelper.Slugify(name) })
            .Where(tag => tag.Slug.Length > 0)
            // Dropped rather than truncated, and dropped here rather than only in the API validation
            // that answers a person with a 400: the AI export path feeds this whatever the model
            // produced for "Tags:", and there is nobody to hand a validation error to. Consistent
            // with how blank and unslugifiable names are already handled a line above.
            .Where(tag => tag.Name.Length <= ContentLimits.TagNameLength)
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
