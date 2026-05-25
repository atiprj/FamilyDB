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
            ResolveRecordsForLoad(records);
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
                    if (cloud == null)
                    {
                        continue;
                    }

                    FamilyRecord local = null;
                    if (!string.IsNullOrWhiteSpace(cloud.RfaPath))
                    {
                        byPath.TryGetValue(cloud.RfaPath, out local);
                    }

                    if (local == null)
                    {
                        local = TryGetLocalBySourceKey(repo, cloud);
                    }

                    if (local == null)
                    {
                        continue;
                    }

                    ApplyLocalRecord(cloud, local);
                }
            }
            catch
            {
                // OK senza SQL locale.
            }
        }

        private static void ResolveRecordsForLoad(List<FamilyRecord> records)
        {
            const string programDataRoot = @"C:\ProgramData\RevitFamilyDb\2025";
            foreach (var record in records)
            {
                if (record == null)
                {
                    continue;
                }

                NormalizePseudoPaths(record);
                record.SourceModelPath = ResolveLibraryModelPath(record.SourceModelPath, record.SourceDiscipline);

                if (HasRealRfaFile(record.RfaPath))
                {
                    continue;
                }

                if (string.Equals(record.FamilyKind, "System", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var exported = TryFindExportedRfa(programDataRoot, record);
                if (!string.IsNullOrWhiteSpace(exported))
                {
                    record.RfaPath = exported;
                }
            }
        }

        private static FamilyRecord TryGetLocalBySourceKey(FamilyRepository repo, FamilyRecord cloud)
        {
            if (repo == null || cloud == null)
            {
                return null;
            }

            if (TryParseSystemPseudo(cloud.RfaPath, out var systemModel, out var typeId))
            {
                return repo.GetFamilyBySourceKey("System", systemModel, cloud.FamilyName, typeId);
            }

            if (TryParseLoadablePseudo(cloud.RfaPath, out var loadableModel, out var loadableName))
            {
                return repo.GetFamilyBySourceKey("Loadable", loadableModel, loadableName, null);
            }

            if (!string.IsNullOrWhiteSpace(cloud.SourceModelPath) && !string.IsNullOrWhiteSpace(cloud.FamilyName))
            {
                var kind = string.Equals(cloud.FamilyKind, "System", StringComparison.OrdinalIgnoreCase)
                    ? "System"
                    : "Loadable";
                return repo.GetFamilyBySourceKey(
                    kind,
                    cloud.SourceModelPath,
                    cloud.FamilyName,
                    cloud.SourceElementTypeId);
            }

            return null;
        }

        private static void ApplyLocalRecord(FamilyRecord cloud, FamilyRecord local)
        {
            if (!string.IsNullOrWhiteSpace(local.RfaPath) && HasRealRfaFile(local.RfaPath))
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

            if (string.IsNullOrWhiteSpace(cloud.FamilyKind) && !string.IsNullOrWhiteSpace(local.FamilyKind))
            {
                cloud.FamilyKind = local.FamilyKind;
            }
        }

        private static void NormalizePseudoPaths(FamilyRecord record)
        {
            if (TryParseSystemPseudo(record.RfaPath, out var systemModel, out var typeId))
            {
                if (string.IsNullOrWhiteSpace(record.FamilyKind))
                {
                    record.FamilyKind = "System";
                }

                if (!record.SourceElementTypeId.HasValue)
                {
                    record.SourceElementTypeId = typeId;
                }

                if (string.IsNullOrWhiteSpace(record.SourceModelPath))
                {
                    record.SourceModelPath = systemModel;
                }
            }
            else if (TryParseLoadablePseudo(record.RfaPath, out var loadableModel, out _))
            {
                if (string.IsNullOrWhiteSpace(record.FamilyKind))
                {
                    record.FamilyKind = "Loadable";
                }

                if (string.IsNullOrWhiteSpace(record.SourceModelPath))
                {
                    record.SourceModelPath = loadableModel;
                }
            }
        }

        private static string ResolveLibraryModelPath(string modelPath, string discipline)
        {
            if (!string.IsNullOrWhiteSpace(modelPath) && File.Exists(modelPath))
            {
                return modelPath;
            }

            if (string.Equals(discipline, "ARC", StringComparison.OrdinalIgnoreCase)
                && File.Exists(LibraryArcModel))
            {
                return LibraryArcModel;
            }

            if (string.Equals(discipline, "FUR", StringComparison.OrdinalIgnoreCase)
                && File.Exists(LibraryFurModel))
            {
                return LibraryFurModel;
            }

            return modelPath;
        }

        private const string LibraryArcModel =
            @"F:\Documenti Utili\BANCA DATI REVIT PROGETTI\Famiglie\Database Famiglie\ARC\00_Library\2025_ATI_ARC_rfa.rvt";

        private const string LibraryFurModel =
            @"F:\Documenti Utili\BANCA DATI REVIT PROGETTI\Famiglie\Database Famiglie\FUR\00_Library\2025_ATI_FUR_rfa.rvt";

        private static bool HasRealRfaFile(string path)
        {
            return !string.IsNullOrWhiteSpace(path)
                   && !IsPseudoRfaPath(path)
                   && File.Exists(path);
        }

        private static bool IsPseudoRfaPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path)
                   && (path.StartsWith("loadable://", StringComparison.OrdinalIgnoreCase)
                       || path.StartsWith("system://", StringComparison.OrdinalIgnoreCase)
                       || path.StartsWith("placed://", StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryParseLoadablePseudo(string rfaPath, out string modelPath, out string familyName)
        {
            modelPath = null;
            familyName = null;
            if (string.IsNullOrWhiteSpace(rfaPath)
                || !rfaPath.StartsWith("loadable://", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var rest = rfaPath.Substring("loadable://".Length);
            var hash = rest.LastIndexOf('#');
            if (hash <= 0 || hash >= rest.Length - 1)
            {
                return false;
            }

            modelPath = rest.Substring(0, hash);
            familyName = rest.Substring(hash + 1);
            return !string.IsNullOrWhiteSpace(modelPath) && !string.IsNullOrWhiteSpace(familyName);
        }

        private static bool TryParseSystemPseudo(string rfaPath, out string modelPath, out int typeId)
        {
            modelPath = null;
            typeId = 0;
            if (string.IsNullOrWhiteSpace(rfaPath)
                || !rfaPath.StartsWith("system://", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var rest = rfaPath.Substring("system://".Length);
            var typeMarker = rest.LastIndexOf("#type:", StringComparison.OrdinalIgnoreCase);
            if (typeMarker <= 0 || typeMarker + 6 >= rest.Length)
            {
                return false;
            }

            modelPath = rest.Substring(0, typeMarker);
            var idPart = rest.Substring(typeMarker + 6);
            return !string.IsNullOrWhiteSpace(modelPath) && int.TryParse(idPart, out typeId);
        }

        private static string TryFindExportedRfa(string programDataRoot, FamilyRecord record)
        {
            var discipline = record.SourceDiscipline;
            var modelPath = record.SourceModelPath;
            if (string.IsNullOrWhiteSpace(discipline) || string.IsNullOrWhiteSpace(modelPath))
            {
                return null;
            }

            var familyName = record.FamilyName;
            if (TryParseLoadablePseudo(record.RfaPath, out _, out var parsedName)
                && !string.IsNullOrWhiteSpace(parsedName))
            {
                familyName = parsedName;
            }

            if (string.IsNullOrWhiteSpace(familyName))
            {
                return null;
            }

            var modelBase = SanitizeFileName(Path.GetFileNameWithoutExtension(modelPath) ?? "model");
            var dir = Path.Combine(programDataRoot, "ExportedRfa", discipline, modelBase);
            if (!Directory.Exists(dir))
            {
                return null;
            }

            var safe = SanitizeFileName(familyName.Split(':')[0].Trim());
            string best = null;
            foreach (var file in Directory.EnumerateFiles(dir, "*.rfa"))
            {
                var name = Path.GetFileNameWithoutExtension(file) ?? "";
                if (!name.StartsWith(safe, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (best == null || string.Compare(file, best, StringComparison.OrdinalIgnoreCase) > 0)
                {
                    best = file;
                }
            }

            return best;
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "unknown";
            }

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (var c in name)
            {
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }

            return sb.ToString();
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
