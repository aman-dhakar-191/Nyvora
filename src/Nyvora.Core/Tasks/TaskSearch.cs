namespace Nyvora.Core.Tasks;

/// <summary>
/// The task list's search. Every word in the query must match somewhere (case-insensitive, "contains"): in the title,
/// the status name or a tag name. A word starting with # matches tag names only, so "#work" finds tagged tasks without
/// matching titles that merely mention work; a word starting with ! matches the priority only ("!high").
/// </summary>
public static class TaskSearch
{
    public static bool IsActive(string? query) => !string.IsNullOrWhiteSpace(query);

    public static bool Matches(string? query, string title, string status, IEnumerable<string> tags, string? priority = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var tagList = tags.ToList();
        foreach (var word in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.StartsWith('!'))
            {
                var p = word[1..];
                if (p.Length > 0 && !(priority ?? "").StartsWith(p, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                continue; // a lone "!" is just the user starting to type a priority
            }

            if (word.StartsWith('#'))
            {
                var tag = word[1..];
                if (tag.Length > 0 && !tagList.Any(t => t.Contains(tag, StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }

                continue; // a lone "#" is just the user starting to type a tag
            }

            var found = title.Contains(word, StringComparison.OrdinalIgnoreCase)
                || status.Contains(word, StringComparison.OrdinalIgnoreCase)
                || tagList.Any(t => t.Contains(word, StringComparison.OrdinalIgnoreCase));
            if (!found)
            {
                return false;
            }
        }

        return true;
    }
}
