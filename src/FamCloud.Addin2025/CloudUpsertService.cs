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
    }

    public sealed class CloudUploadResult
    {
        public int Total { get; set; }
        public int Uploaded { get; set; }
        public int Failed { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    internal static class CloudUpsertService
    {
        public const int InitialBatchSize = 5;
        public const int MaxParametersPerFamily = 40;
        public const int MaxParameterValueChars = 400;

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

        public static CloudUploadResult Upload(IReadOnlyList<FamilyUploadItem> uploadItems)
        {
            var result = new CloudUploadResult { Total = uploadItems?.Count ?? 0 };
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

                        result.Failed += take;
                        result.Errors.Add($"Item {index + 1}: HTTP 413 (payload troppo grande)");
                        sent = true;
                        continue;
                    }

                    if (response.StatusCode < 200 || response.StatusCode >= 300)
                    {
                        result.Failed += take;
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
