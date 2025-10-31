using System;
using System.Collections.Generic;

namespace CustomChirps.Utils
{
    internal static class ChirpFilter
    {
        // Exact keys (with index)
        private static readonly HashSet<string> s_blockExact = new(StringComparer.Ordinal)
        {
            // "Chirper.CITY_SERVICE_ELECTRICITY_IMPORT:0",
        };

        // Prefixes (match any index)
        private static readonly HashSet<string> s_blockPrefixes = new(StringComparer.Ordinal)
        {
            "Chirper.CITY_SERVICE_ELECTRICITY_IMPORT",
            // "Chirper.GOOD_EDUCATION_SERVICE",
        };

        public static bool IsBlocked(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (s_blockExact.Contains(key)) return true;

            var colon = key.LastIndexOf(':');
            var id = colon >= 0 ? key.Substring(0, colon) : key;
            return s_blockPrefixes.Contains(id);
        }
    }
}
