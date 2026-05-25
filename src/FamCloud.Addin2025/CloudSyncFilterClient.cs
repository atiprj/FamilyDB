using System;
using System.Collections.Generic;
using System.Text;

namespace FamCloud.Addin2025
{
    internal sealed class UnchangedFilterResult
    {
        public HashSet<string> UnchangedRfaPaths { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> NeedsPreviewOnlyRfaPaths { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    internal static class CloudSyncFilterClient
    {
        public static UnchangedFilterResult FilterUnchanged(IReadOnlyList<FamilyUploadItem> items)
        {
            var result = new UnchangedFilterResult();
            if (items == null || items.Count == 0)
            {
                return result;
            }

            const int chunkSize = 80;
            for (var offset = 0; offset < items.Count; offset += chunkSize)
            {
                var take = Math.Min(chunkSize, items.Count - offset);
                var json = BuildFilterJson(items, offset, take);
                var response = CloudApiClient.PostJsonDetailed(
                    "/api/families/unchanged-filter",
                    json,
                    includeApiKey: true);

                if (response.StatusCode < 200 || response.StatusCode >= 300)
                {
                    continue;
                }

                ParseFilterResponse(response.Body, result);
            }

            return result;
        }

        private static string BuildFilterJson(IReadOnlyList<FamilyUploadItem> items, int start, int take)
        {
            var sb = new StringBuilder(4096);
            sb.Append("{\"items\":[");
            for (var i = 0; i < take; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                var family = items[start + i].Family;
                sb.Append("{\"rfaPath\":\"").Append(Escape(family.RfaPath)).Append('"');
                sb.Append(",\"fileHash\":");
                if (string.IsNullOrWhiteSpace(family.FileHash))
                {
                    sb.Append("null");
                }
                else
                {
                    sb.Append('"').Append(Escape(family.FileHash)).Append('"');
                }

                sb.Append('}');
            }

            sb.Append("]}");
            return sb.ToString();
        }

        private static void ParseFilterResponse(string body, UnchangedFilterResult result)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return;
            }

            ExtractPathArray(body, "unchangedRfaPaths", result.UnchangedRfaPaths);
            ExtractPathArray(body, "needsPreviewOnlyRfaPaths", result.NeedsPreviewOnlyRfaPaths);
        }

        private static void ExtractPathArray(string json, string key, HashSet<string> target)
        {
            var marker = "\"" + key + "\":[";
            var start = json.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                return;
            }

            start += marker.Length;
            var end = json.IndexOf(']', start);
            if (end <= start)
            {
                return;
            }

            var segment = json.Substring(start, end - start);
            var parts = segment.Split(new[] { '"' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (part == "," || part == ":")
                {
                    continue;
                }

                var path = part.Trim();
                if (path.Length > 0)
                {
                    target.Add(path);
                }
            }
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }
    }
}
