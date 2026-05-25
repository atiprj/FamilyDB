using System;
using System.Collections.Generic;
using System.IO;
using RevitFamilyDb.Core;

namespace FamCloud.Addin2025
{
    internal static class CloudCatalogClient
    {
        public static List<FamilyRecord> FetchCatalogForBrowse(int maxRows = 5000)
        {
            var records = new List<FamilyRecord>();
            const int pageSize = 500;
            var offset = 0;

            while (records.Count < maxRows)
            {
                var take = Math.Min(pageSize, maxRows - records.Count);
                var response = CloudApiClient.Get(
                    "/api/families?take=" + take + "&offset=" + offset,
                    includeApiKey: false);

                if (string.IsNullOrWhiteSpace(response) || response.IndexOf("\"items\"", StringComparison.Ordinal) < 0)
                {
                    break;
                }

                var page = ParseFamiliesPage(response);
                if (page.Count == 0)
                {
                    break;
                }

                records.AddRange(page);
                if (page.Count < take)
                {
                    break;
                }

                offset += take;
            }

            EnrichFromLocalDatabase(records);
            return records;
        }

        private static void EnrichFromLocalDatabase(List<FamilyRecord> records)
        {
            try
            {
                var repo = LocalDbFactory.CreateRepository();
                repo.EnsureExtendedSchema();
                var localRows = repo.GetFamilies(20000);
                var byPath = new Dictionary<string, FamilyRecord>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in localRows)
                {
                    if (!string.IsNullOrWhiteSpace(row.RfaPath))
                    {
                        byPath[row.RfaPath] = row;
                    }
                }

                foreach (var cloud in records)
                {
                    if (cloud == null || string.IsNullOrWhiteSpace(cloud.RfaPath))
                    {
                        continue;
                    }

                    if (!byPath.TryGetValue(cloud.RfaPath, out var local))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(local.RfaPath) && File.Exists(local.RfaPath))
                    {
                        cloud.RfaPath = local.RfaPath;
                    }

                    if (string.IsNullOrWhiteSpace(cloud.PreviewPath) && !string.IsNullOrWhiteSpace(local.PreviewPath))
                    {
                        cloud.PreviewPath = local.PreviewPath;
                    }

                    if (!cloud.SourceElementTypeId.HasValue && local.SourceElementTypeId.HasValue)
                    {
                        cloud.SourceElementTypeId = local.SourceElementTypeId;
                    }

                    if (string.IsNullOrWhiteSpace(cloud.SourceModelPath) && !string.IsNullOrWhiteSpace(local.SourceModelPath))
                    {
                        cloud.SourceModelPath = local.SourceModelPath;
                    }
                }
            }
            catch
            {
                // OK senza SQL locale.
            }
        }

        private static List<FamilyRecord> ParseFamiliesPage(string json)
        {
            var list = new List<FamilyRecord>();
            const string marker = "\"familyId\":";
            var idx = 0;

            while (idx < json.Length)
            {
                var pos = json.IndexOf(marker, idx, StringComparison.Ordinal);
                if (pos < 0)
                {
                    break;
                }

                var record = ParseOneItem(json, pos);
                if (record != null)
                {
                    list.Add(record);
                }

                idx = pos + marker.Length;
            }

            return list;
        }

        private static FamilyRecord ParseOneItem(string json, int familyIdPos)
        {
            var windowStart = Math.Max(0, familyIdPos - 40);
            var windowEnd = Math.Min(json.Length, familyIdPos + 1200);
            var chunk = json.Substring(windowStart, windowEnd - windowStart);

            var familyId = ReadIntValue(chunk, "familyId");
            var familyName = ReadStringValue(chunk, "familyName");
            var rfaPath = ReadStringValue(chunk, "rfaPath");
            if (!familyId.HasValue || string.IsNullOrWhiteSpace(familyName) || string.IsNullOrWhiteSpace(rfaPath))
            {
                return null;
            }

            var rfa = rfaPath;
            var sourceModel = ReadStringValue(chunk, "sourceModelPath");
            if (string.IsNullOrWhiteSpace(sourceModel) && rfa.StartsWith("loadable://", StringComparison.OrdinalIgnoreCase))
            {
                var hash = rfa.IndexOf('#');
                if (hash > 9)
                {
                    sourceModel = rfa.Substring(9, hash - 9);
                }
            }
            else if (string.IsNullOrWhiteSpace(sourceModel) && rfa.StartsWith("system://", StringComparison.OrdinalIgnoreCase))
            {
                var hash = rfa.IndexOf('#');
                if (hash > 8)
                {
                    sourceModel = rfa.Substring(8, hash - 8);
                }
            }

            return new FamilyRecord
            {
                FamilyId = familyId,
                FamilyName = familyName,
                CategoryName = ReadStringValue(chunk, "categoryName"),
                RfaPath = rfaPath,
                PreviewPath = ReadStringValue(chunk, "previewPath"),
                FamilyKind = ReadStringValue(chunk, "familyKind"),
                ApprovalStatus = ReadStringValue(chunk, "approvalStatus") ?? "Draft",
                SourceDiscipline = ReadStringValue(chunk, "sourceDiscipline"),
                SourceModelPath = sourceModel,
                SourceElementTypeId = ReadIntValue(chunk, "sourceElementTypeId")
            };
        }

        private static int? ReadIntValue(string chunk, string key)
        {
            var marker = "\"" + key + "\":";
            var pos = chunk.IndexOf(marker, StringComparison.Ordinal);
            if (pos < 0)
            {
                return null;
            }

            pos += marker.Length;
            while (pos < chunk.Length && char.IsWhiteSpace(chunk[pos]))
            {
                pos++;
            }

            if (pos >= chunk.Length || chunk[pos] == 'n')
            {
                return null;
            }

            var end = pos;
            while (end < chunk.Length && (char.IsDigit(chunk[end]) || chunk[end] == '-'))
            {
                end++;
            }

            if (int.TryParse(chunk.Substring(pos, end - pos), out var value))
            {
                return value;
            }

            return null;
        }

        private static string ReadStringValue(string chunk, string key)
        {
            var marker = "\"" + key + "\":";
            var pos = chunk.IndexOf(marker, StringComparison.Ordinal);
            if (pos < 0)
            {
                return null;
            }

            pos += marker.Length;
            while (pos < chunk.Length && char.IsWhiteSpace(chunk[pos]))
            {
                pos++;
            }

            if (pos >= chunk.Length || chunk[pos] == 'n')
            {
                return null;
            }

            if (chunk[pos] != '"')
            {
                return null;
            }

            pos++;
            var end = chunk.IndexOf('"', pos);
            if (end < 0)
            {
                return null;
            }

            return chunk
                .Substring(pos, end - pos)
                .Replace("\\/", "/")
                .Replace("\\\"", "\"");
        }
    }
}
