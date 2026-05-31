using Colossal.UI.Binding;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomChirps.Systems
{
    internal static class CustomChirpImageSourceRegistry
    {
        private const string TokenPrefix = "img_";
        private static readonly object s_Lock = new();
        private static readonly Dictionary<string, string> s_SourceByToken = new();
        private static readonly Dictionary<string, string> s_TokenBySource = new(StringComparer.Ordinal);
        private static int s_Version;

        public static int Version
        {
            get
            {
                lock (s_Lock)
                    return s_Version;
            }
        }

        public static string Register(string source)
        {
            source = source?.Trim();
            if (string.IsNullOrEmpty(source))
                return string.Empty;

            lock (s_Lock)
            {
                if (s_TokenBySource.TryGetValue(source, out string existingToken))
                    return existingToken;

                string token = $"{TokenPrefix}{Guid.NewGuid():N}";
                s_TokenBySource[source] = token;
                s_SourceByToken[token] = source;
                s_Version++;
                return token;
            }
        }

        public static void WriteSources(IJsonWriter writer)
        {
            KeyValuePair<string, string>[] snapshot;
            lock (s_Lock)
                snapshot = s_SourceByToken.ToArray();

            writer.ArrayBegin(snapshot.Length);
            for (int i = 0; i < snapshot.Length; i++)
            {
                writer.TypeBegin("CustomChirps.ImageSource");
                writer.PropertyName("token");
                writer.Write(snapshot[i].Key);
                writer.PropertyName("source");
                writer.Write(snapshot[i].Value);
                writer.TypeEnd();
            }
            writer.ArrayEnd();
        }

        public static void Clear()
        {
            lock (s_Lock)
            {
                s_SourceByToken.Clear();
                s_TokenBySource.Clear();
                s_Version++;
            }
        }
    }
}
