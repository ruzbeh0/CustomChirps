using System;
using System.Collections.Generic;

namespace CustomChirps.Utils
{
    internal static class RuntimeChirpLocalization
    {
        private const int WindowSize = 144;

        private static readonly object s_lock = new object();
        private static readonly Dictionary<string, string> s_entries = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly LinkedList<string> s_keys = new LinkedList<string>();

        public static void Add(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key cannot be null or empty", nameof(key));

            lock (s_lock)
            {
                if (s_entries.Remove(key))
                    s_keys.Remove(key);

                while (s_keys.Count >= WindowSize)
                {
                    var oldest = s_keys.First;
                    if (oldest == null)
                        break;

                    s_entries.Remove(oldest.Value);
                    s_keys.RemoveFirst();
                }

                s_entries[key] = value;
                s_keys.AddLast(key);
            }
        }

        public static bool TryGetValue(string key, out string value)
        {
            lock (s_lock)
            {
                return s_entries.TryGetValue(key, out value);
            }
        }

        public static void Clear()
        {
            lock (s_lock)
            {
                s_entries.Clear();
                s_keys.Clear();
            }
        }
    }
}
