using System.Globalization;
using System.Text;

namespace AetherShell.Server.Services
{
    /// <summary>
    /// Читаемый сегмент URL панели: /panel/{slug}.
    /// Кириллица транслитерируется, пробелы и спецсимволы схлопываются в дефисы.
    /// </summary>
    public static class ClubSlug
    {
        private static readonly Dictionary<char, string> Map = new()
        {
            ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d",
            ['е'] = "e", ['ё'] = "e", ['ж'] = "zh", ['з'] = "z", ['и'] = "i",
            ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n",
            ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t",
            ['у'] = "u", ['ф'] = "f", ['х'] = "h", ['ц'] = "ts", ['ч'] = "ch",
            ['ш'] = "sh", ['щ'] = "sch", ['ъ'] = "", ['ы'] = "y", ['ь'] = "",
            ['э'] = "e", ['ю'] = "yu", ['я'] = "ya"
        };

        public static string FromName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "club";

            var sb = new StringBuilder(name.Length * 2);
            foreach (var ch in name.Trim().Normalize(NormalizationForm.FormD))
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (category == UnicodeCategory.NonSpacingMark) continue;

                var lower = char.ToLowerInvariant(ch);
                if (Map.TryGetValue(lower, out var latin))
                {
                    sb.Append(latin);
                    continue;
                }

                if (char.IsAsciiLetterOrDigit(lower))
                {
                    sb.Append(lower);
                    continue;
                }

                if (sb.Length > 0 && sb[^1] != '-')
                    sb.Append('-');
            }

            var slug = sb.ToString().Trim('-');
            while (slug.Contains("--", StringComparison.Ordinal))
                slug = slug.Replace("--", "-", StringComparison.Ordinal);

            return string.IsNullOrEmpty(slug) ? "club" : slug;
        }

        /// <summary>Подбирает уникальный slug среди уже занятых.</summary>
        public static string EnsureUnique(string baseSlug, IEnumerable<string> taken)
        {
            var set = new HashSet<string>(taken.Select(s => s.ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);
            if (!set.Contains(baseSlug)) return baseSlug;

            for (var i = 2; i < 1000; i++)
            {
                var candidate = $"{baseSlug}-{i}";
                if (!set.Contains(candidate)) return candidate;
            }

            return $"{baseSlug}-{Guid.NewGuid():N}"[..12];
        }
    }
}
