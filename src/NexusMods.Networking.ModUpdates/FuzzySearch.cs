using System.Text.RegularExpressions;
namespace NexusMods.Networking.ModUpdates;

/// <summary>
/// This class has all the code around the 'fuzzy search' feature documented in
/// `0019-updating-mods.md` page of the wiki.
/// </summary>
public static class FuzzySearch
{
    private static readonly string[] KnownFileExtensions =
    [
        ".rar", ".zip", ".7z", ".exe", ".omod", ".nx",
    ];
    
    /// <summary>
    /// Normalizes a filename by:
    /// 1. Stripping all possible version number variants
    /// 2. Replacing underscores with spaces
    /// 3. Stripping file extensions
    /// 4. Converting to lowercase for case-insensitive comparison
    /// </summary>
    /// <param name="fileName">The filename to normalize</param>
    /// <param name="version">The version to strip from the filename</param>
    /// <returns>The normalized filename</returns>
    public static string NormalizeFileName(string? fileName, string? version)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;

        var result = fileName;
        
        // Strip all possible version number variants
        if (!string.IsNullOrWhiteSpace(version))
        {
            // Sort by length, to ensure we don't match a substring containing another string
            var versionVariants = GetVersionPermutations(version).OrderByDescending(x => x.Length);
            foreach (var variant in versionVariants)
                result = result.Replace(variant, string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        // Replace underscores with spaces
        result = result.Replace('_', ' ');
        
        // Strip extensions
        result = StripFileExtension(result);
        
        // Normalize whitespace (trim and reduce multiple spaces to single)
        result = string.Join(" ", result.Split([' '], 
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            
        // Convert to lowercase for case-insensitive comparison
        return result.ToLowerInvariant();
    }

    // Tokens that look like a version tag: "v2", "v1.3", "v1_3b", "2.0.1", "2.31.". Bare numbers such as "2" are
    // kept, so that "Part 1" and "Part 2" stay different files.
    private static readonly Regex VersionLikeToken = new(@"^(v\d+([._]\d+)*[a-z]?|\d+([._]\d+)+[a-z]?)\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "for 2.50", "for game 2.3a", "for v2.1": tags that authors add to a file name after a game update.
    private static readonly Regex GameVersionTag = new(@"\bfor\s+(game\s+)?v?\d+([._]\d+)*[a-z]?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// A looser normalization than <see cref="NormalizeFileName"/> for the same file renamed between two uploads. On top
    /// of it, this drops version-like tags ("V2", "v1.3"), game version tags ("for 2.50") and punctuation, so that
    /// "Foo - Bar" and "Foo - Bar V2" end up the same.
    /// </summary>
    public static string NormalizeFileNameLoose(string? fileName, string? version)
    {
        var strict = NormalizeFileName(fileName, version);
        strict = GameVersionTag.Replace(strict, " ");

        var tokens = strict
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            // A lone "v" is what is left of "v1.2.3" once the version number itself was stripped
            .Where(t => !VersionLikeToken.IsMatch(t) && !t.Equals("v", StringComparison.OrdinalIgnoreCase))
            .Select(t => new string(t.Where(char.IsLetterOrDigit).ToArray()))
            .Where(t => t.Length > 0);
        return string.Join(" ", tokens);
    }

    /// <summary>
    /// Whether two files of the same mod page are versions of the same file: their normalized names are equal or, when
    /// their version numbers differ, their loosely normalized names are equal (the file was renamed, as in
    /// "Foo" then "Foo V2").
    /// </summary>
    public static bool IsSameFileLineage(string? nameA, string? versionA, string? nameB, string? versionB)
    {
        if (NormalizeFileName(nameA, versionA).Equals(NormalizeFileName(nameB, versionB), StringComparison.OrdinalIgnoreCase))
            return true;

        // The loose match is only trusted when the version numbers differ: two files with the same version number and
        // names that only differ by a tag ("Foo V1", "Foo V2") are different files, not a rename.
        if (string.Equals(versionA?.Trim(), versionB?.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        var looseA = NormalizeFileNameLoose(nameA, versionA);
        return looseA.Length > 0 && looseA.Equals(NormalizeFileNameLoose(nameB, versionB), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether <paramref name="newName"/> is a later version of <paramref name="oldName"/>. A renamed file (one that only
    /// matches loosely) also needs a strictly higher version number.
    /// </summary>
    public static bool IsNewerFileInSameLineage(string? oldName, string? oldVersion, string? newName, string? newVersion)
    {
        if (NormalizeFileName(oldName, oldVersion).Equals(NormalizeFileName(newName, newVersion), StringComparison.OrdinalIgnoreCase))
            return true;

        return IsSameFileLineage(oldName, oldVersion, newName, newVersion) && VersionIsGreater(newVersion, oldVersion);
    }

    /// <summary>
    /// Compares the numeric parts of two version strings: "1.2.10" is greater than "1.2.9", and "2" is not greater than
    /// "2.0". A version without any number is never greater.
    /// </summary>
    public static bool VersionIsGreater(string? candidate, string? baseline)
    {
        static List<long> Parts(string? v) => Regex.Matches(v ?? "", @"\d+").Select(m => long.TryParse(m.Value, out var n) ? n : 0).ToList();

        var a = Parts(candidate);
        var b = Parts(baseline);
        if (a.Count == 0) return false;

        for (var i = 0; i < Math.Max(a.Count, b.Count); i++)
        {
            var x = i < a.Count ? a[i] : 0;
            var y = i < b.Count ? b[i] : 0;
            if (x != y) return x > y;
        }

        return false;
    }

    /// <summary>
    /// Given an existing version string, return all possible variations that
    /// a user may place inside the file name on a mod page.
    ///
    /// Refer to tests of this for examples.
    /// </summary>
    /// <param name="version">The original version string attached</param>
    public static string[] GetVersionPermutations(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return [];

        var results = new HashSet<string>(8);
        void AddIfValid(string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                results.Add(value);
        }

        void AddVariants(string input)
        {
            AddIfValid(input);
            AddIfValid(input.Replace(".", "_"));
        }

        // Add original version and its variants
        AddVariants(version);

        // Remove version suffix.
        var withoutTextSuffix = version;
        for (var x = version.Length - 1; x >= 0; x--)
        {
            if (!char.IsNumber(version[x])) continue;
            withoutTextSuffix = version.Substring(0, x + 1);
            break;
        }
        
        if (withoutTextSuffix != version)
            AddVariants(withoutTextSuffix);
            
        // Handle Single letter version prefixes variants ('v' for version, 'a' for 'alpha', 'b' for 'beta', etc.)
        if (version.Length > 0 && char.IsLetter(version[0]))
        {
            var withoutPrefix = version.Substring(1);
            AddVariants(withoutPrefix);
            
            // Also add variants without both prefix and suffix
            if (withoutTextSuffix != version)
                AddVariants(withoutPrefix.Substring(0, withoutTextSuffix.Length - 1));
        }

        return results.ToArray();
    }
    
    /// <summary>
    /// Strips known file extensions from the end of a filename
    /// </summary>
    /// <param name="fileName">The filename to process</param>
    /// <returns>Filename with any known extension removed</returns>
    public static string StripFileExtension(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return fileName;
            
        foreach (var ext in KnownFileExtensions)
        {
            if (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return fileName[..^ext.Length];
        }
        
        return fileName;
    }
}
