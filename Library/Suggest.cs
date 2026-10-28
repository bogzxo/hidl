namespace Horizon.HIDL.Library;

/// <summary>
/// "Did you mean" for the errors. A name a letter or two off one there is was nine times in ten meant to be that one.
/// </summary>
public static class Suggest
{
    /// <summary>" Did you mean x?" with the nearest of some names, empty when none is near enough.</summary>
    public static string Nearest(string name, IEnumerable<string> known)
    {
        string? best = null;
        int closest = int.MaxValue;
        int most = name.Length <= 4 ? 1 : 2;

        foreach (string candidate in known)
        {
            if (candidate == name || Math.Abs(candidate.Length - name.Length) > most)
                continue;

            int distance = Distance(name, candidate);
            if (distance <= most && distance < closest)
            {
                closest = distance;
                best = candidate;
            }
        }

        return best is null ? string.Empty : $" Did you mean {best}?";
    }

    /// <summary>Names as a sentence, "a, b and c".</summary>
    public static string Listed(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "nothing",
        1 => names[0],
        _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}"
    };

    /// <summary>How many letters have to change to turn one word into another (Levenshtein).</summary>
    public static int Distance(string a, string b)
    {
        Span<int> previous = b.Length < 128 ? stackalloc int[b.Length + 1] : new int[b.Length + 1];
        Span<int> current = b.Length < 128 ? stackalloc int[b.Length + 1] : new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) previous[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            current.CopyTo(previous);
        }

        return previous[b.Length];
    }
}
