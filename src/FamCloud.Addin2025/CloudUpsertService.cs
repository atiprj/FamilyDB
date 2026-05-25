using System;
using System.Collections.Generic;
using System.Text;
using RevitFamilyDb.Core;

namespace FamCloud.Addin2025
{
    public sealed class FamilyUploadItem
    {
        public FamilyRecord Family { get; set; }
        public List<FamilyParameterRecord> Parameters { get; set; }
        public string LocalPreviewPath { get; set; }
    }

    public sealed class CloudUploadResult
    {
        public int Total { get; set; }
        public int Uploaded { get; set; }
        public int Failed { get; set; }
        public int SkippedUnchanged { get; set; }
        public int PreviewOnlyUpdated { get; set; }
        public int Passes { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    internal static class CloudUpsertService
    {
        public const int InitialBatchSize = 5;
        public const int MaxParametersPerFamily = 40;
        public const int MaxParameterValueChars = 400;
        public const int DefaultUploadChunkSize = 200;
        public const int DefaultMaxPasses = 8;

        public static List<FamilyParameterRecord> TrimParametersForCloud(List<FamilyParameterRecord> parameters)
        {
            if (parameters == null || parameters.Count == 0)
            {
                return new List<FamilyParameterRecord>();
            }

            if (parameters.Count <= MaxParametersPerFamily)
            {
                return parameters;
            }

            return parameters.GetRange(0, MaxParametersPerFamily);
        }

        /// <summary>
        /// Upload in chunk cycles (library sync) with retry until done or max passes.
        /// </summary>
        public static CloudUploadResult UploadInChunks(
            IReadOnlyList<FamilyUploadItem> allItems,
            int chunkSize = DefaultUploadChunkSize,
            int maxPassesPerChunk = DefaultMaxPasses,
            bool uploadPreviews = true)
        {
            var aggregate = new CloudUploadResult();
            if (allItems == null || allItems.Count == 0)
            {
                return aggregate;
            }

            aggregate.Total = allItems.Count;
            var prepared = PrepareUploadList(allItems, uploadPreviews, aggregate);
            if (prepared.Count == 0)
            {
                aggregate.Failed = 0;
                return aggregate;
            }

            if (chunkSize <= 0)
            {
                chunkSize = DefaultUploadChunkSize;
            }

            for (var offset = 0; offset < prepared.Count; offset += chunkSize)
            {
                var take = Math.Min(chunkSize, prepared.Count - offset);
                var chunk = new List<FamilyUploadItem>(take);
                for (var i = 0; i < take; i++)
                {
                    chunk.Add(prepared[offset + i]);
                }

                var chunkResult = UploadUntilComplete(chunk, maxPassesPerChunk, uploadPreviews);
                aggregate.Uploaded += chunkResult.Uploaded;
                aggregate.Failed = aggregate.Total - aggregate.Uploaded;
                aggregate.Passes = Math.Max(aggregate.Passes, chunkResult.Passes);
                if (chunkResult.Errors.Count > 0)
                {
                    aggregate.Errors.AddRange(chunkResult.Errors);
                }
            }

            aggregate.Failed = aggregate.Total - aggregate.SkippedUnchanged - aggregate.Uploaded;
            if (aggregate.Failed < 0)
            {
                aggregate.Failed = 0;
            }

            return aggregate;
        }

        private static List<FamilyUploadItem> PrepareUploadList(
            IReadOnlyList<FamilyUploadItem> allItems,
            bool uploadPreviews,
            CloudUploadResult aggregate)
        {
            var filter = CloudSyncFilterClient.FilterUnchanged(allItems);
            var toUpload = new List<FamilyUploadItem>();
            var previewOnly = new List<FamilyUploadItem>();

            foreach (var item in allItems)
            {
                if (item?.Family == null || string.IsNullOrWhiteSpace(item.Family.RfaPath))
                {
                    continue;
                }

                var path = item.Family.RfaPath;
                if (filter.UnchangedRfaPaths.Contains(path))
                {
                    if (filter.NeedsPreviewOnlyRfaPaths.Contains(path))
                    {
                        previewOnly.Add(item);
                    }
                    else
                    {
                        aggregate.SkippedUnchanged++;
                    }

                    continue;
                }

                toUpload.Add(item);
            }

            if (uploadPreviews && previewOnly.Count > 0)
            {
                aggregate.PreviewOnlyUpdated = PreviewThumbnailHelper.UploadPreviewOnlyBatch(previewOnly);
            }

            return toUpload;
        }

        public static CloudUploadResult Upload(IReadOnlyList<FamilyUploadItem> uploadItems)
        {
            return UploadInChunks(uploadItems, uploadItems?.Count ?? DefaultUploadChunkSize, DefaultMaxPasses, true);
        }

        public static CloudUploadResult UploadUntilComplete(
            IReadOnlyList<FamilyUploadItem> uploadItems,
            int maxPasses = DefaultMaxPasses,
            bool uploadPreviews = true)
        {
            var aggregate = new CloudUploadResult { Total = uploadItems?.Count ?? 0 };
            if (uploadItems == null || uploadItems.Count == 0)
            {
                return aggregate;
            }

            if (maxPasses < 1)
            {
                maxPasses = 1;
            }

            var pending = PrepareUploadList(uploadItems, uploadPreviews, aggregate);
            if (pending.Count == 0)
            {
                aggregate.Failed = 0;
                return aggregate;
            }
            for (var pass = 1; pass <= maxPasses && pending.Count > 0; pass++)
            {
                aggregate.Passes = pass;
                if (uploadPreviews)
                {
                    PreviewThumbnailHelper.AttachCloudPreviews(pending);
                }

                var failed = new List<FamilyUploadItem>();
                var passResult = UploadPass(pending, failed);
                aggregate.Uploaded += passResult.Uploaded;
                aggregate.Errors.AddRange(passResult.Errors);
                pending = failed;
            }

            aggregate.Failed = aggregate.Total - aggregate.SkippedUnchanged - aggregate.Uploaded;
            if (aggregate.Failed < 0)
            {
                aggregate.Failed = 0;
            }

            if (aggregate.Errors.Count > 10)
            {
                aggregate.Errors = aggregate.Errors.GetRange(0, 10);
            }

            return aggregate;
        }

        private static CloudUploadResult UploadPass(
            IReadOnlyList<FamilyUploadItem> uploadItems,
            List<FamilyUploadItem> failedItems)
        {
            var result = new CloudUploadResult { Total = uploadItems.Count };
            if (uploadItems == null || uploadItems.Count == 0)
            {
                return result;
            }

            var batchSize = InitialBatchSize;
            var index = 0;

            while (index < uploadItems.Count)
            {
                var take = Math.Min(batchSize, uploadItems.Count - index);
                var omitParameters = false;
                var sent = false;
                var batchFailed = false;

                for (var attempt = 0; attempt < 8 && !sent; attempt++)
                {
                    var jsonPayload = BuildUpsertPayloadJson(uploadItems, index, take, omitParameters);
                    var response = CloudApiClient.PostJsonDetailed(
                        "/api/families/upsert",
                        jsonPayload,
                        includeApiKey: true);

                    if (response.StatusCode == 413)
                    {
                        if (take > 1)
                        {
                            batchSize = Math.Max(1, take / 2);
                            take = Math.Min(batchSize, uploadItems.Count - index);
                            continue;
                        }

                        if (!omitParameters)
                        {
                            omitParameters = true;
                            continue;
                        }

                        batchFailed = true;
                        sent = true;
                        result.Errors.Add($"Item {index + 1}: HTTP 413 (payload troppo grande)");
                        continue;
                    }

                    if (response.StatusCode < 200 || response.StatusCode >= 300)
                    {
                        batchFailed = true;
                        sent = true;
                        var detail = response.Body;
                        if (!string.IsNullOrWhiteSpace(detail) && detail.Length > 120)
                        {
                            detail = detail.Substring(0, 120) + "...";
                        }

                        result.Errors.Add($"Batch {index + 1}-{index + take}: HTTP {response.StatusCode} {detail}");
                    }
                    else
                    {
                        result.Uploaded += take;
                    }

                    sent = true;
                }

                if (batchFailed)
                {
                    for (var i = 0; i < take; i++)
                    {
                        failedItems.Add(uploadItems[index + i]);
                    }
                }

                index += take;
            }

            return result;
        }

        private static string BuildUpsertPayloadJson(
            IReadOnlyList<FamilyUploadItem> items,
            int start,
            int take,
            bool omitParameters)
        {
            var sb = new StringBuilder(16384);
            sb.Append("{\"items\":[");
            var firstItem = true;

            for (var i = 0; i < take; i++)
            {
                var item = items[start + i];
                var family = item.Family;
                var parameters = omitParameters
                    ? new List<FamilyParameterRecord>()
                    : item.Parameters ?? new List<FamilyParameterRecord>();

                if (!firstItem)
                {
                    sb.Append(',');
                }

                firstItem = false;
                sb.Append("{\"family\":{");
                var firstFamilyProp = true;
                AppendJsonStringProp(sb, "familyName", family.FamilyName, ref firstFamilyProp);
                AppendJsonStringProp(sb, "categoryName", family.CategoryName, ref firstFamilyProp);
                AppendJsonStringProp(sb, "rfaPath", family.RfaPath, ref firstFamilyProp);
                if (PreviewThumbnailHelper.IsCloudUrl(family.PreviewPath))
                {
                    AppendJsonStringProp(sb, "previewPath", family.PreviewPath, ref firstFamilyProp);
                }

                AppendJsonIntProp(sb, "revitVersion", family.RevitVersion, ref firstFamilyProp);
                AppendJsonStringProp(sb, "fileHash", family.FileHash, ref firstFamilyProp);
                AppendJsonStringProp(sb, "approvalStatus", family.ApprovalStatus, ref firstFamilyProp);
                AppendJsonStringProp(sb, "familyKind", family.FamilyKind, ref firstFamilyProp);
                AppendJsonStringProp(sb, "sourceModelPath", family.SourceModelPath, ref firstFamilyProp);
                AppendJsonIntProp(sb, "sourceElementTypeId", family.SourceElementTypeId, ref firstFamilyProp);
                AppendJsonStringProp(sb, "sourceDiscipline", family.SourceDiscipline, ref firstFamilyProp);
                sb.Append("},\"parameters\":[");

                for (var j = 0; j < parameters.Count; j++)
                {
                    var p = parameters[j];
                    if (j > 0)
                    {
                        sb.Append(',');
                    }

                    sb.Append('{');
                    var firstParamProp = true;
                    AppendJsonStringProp(sb, "parameterName", p.ParameterName, ref firstParamProp);
                    AppendJsonStringProp(sb, "parameterGroupName", p.ParameterGroupName, ref firstParamProp);
                    AppendJsonStringProp(sb, "storageType", p.StorageType, ref firstParamProp);
                    AppendJsonStringProp(
                        sb,
                        "stringValue",
                        TruncateForCloud(p.StringValue, MaxParameterValueChars),
                        ref firstParamProp);
                    AppendJsonBoolProp(sb, "isInstance", false, ref firstParamProp);
                    AppendJsonBoolProp(sb, "isShared", false, ref firstParamProp);
                    sb.Append('}');
                }

                sb.Append("]}");
            }

            sb.Append("]}");
            return sb.ToString();
        }

        private static void AppendJsonStringProp(StringBuilder sb, string name, string value, ref bool firstProp)
        {
            if (!firstProp)
            {
                sb.Append(',');
            }

            firstProp = false;
            sb.Append('"').Append(name).Append("\":");
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            sb.Append('"').Append(EscapeJson(value)).Append('"');
        }

        private static void AppendJsonIntProp(StringBuilder sb, string name, int? value, ref bool firstProp)
        {
            if (!firstProp)
            {
                sb.Append(',');
            }

            firstProp = false;
            sb.Append('"').Append(name).Append("\":");
            if (value.HasValue)
            {
                sb.Append(value.Value);
            }
            else
            {
                sb.Append("null");
            }
        }

        private static void AppendJsonBoolProp(StringBuilder sb, string name, bool value, ref bool firstProp)
        {
            if (!firstProp)
            {
                sb.Append(',');
            }

            firstProp = false;
            sb.Append('"').Append(name).Append("\":").Append(value ? "true" : "false");
        }

        private static string TruncateForCloud(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
            {
                return value;
            }

            return value.Substring(0, maxChars);
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value ?? "";
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }
    }
}
