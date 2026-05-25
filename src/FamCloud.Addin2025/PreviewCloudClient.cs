using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FamCloud.Addin2025
{
    internal static class PreviewCloudClient
    {
        public static string UploadLocalPng(string rfaPath, string localPngPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rfaPath) || string.IsNullOrWhiteSpace(localPngPath))
                {
                    return null;
                }

                if (!File.Exists(localPngPath))
                {
                    return null;
                }

                var bytes = File.ReadAllBytes(localPngPath);
                if (bytes.Length == 0 || bytes.Length > 512 * 1024)
                {
                    return null;
                }

                var base64 = Convert.ToBase64String(bytes);
                var json = BuildSingleUploadJson(rfaPath, base64);
                var response = CloudApiClient.PostJsonDetailed(
                    "/api/previews/upload",
                    json,
                    includeApiKey: true);

                if (response.StatusCode < 200 || response.StatusCode >= 300)
                {
                    return null;
                }

                return ExtractPreviewUrl(response.Body);
            }
            catch
            {
                return null;
            }
        }

        public static int UploadBatchAndApply(IReadOnlyList<FamilyUploadItem> items)
        {
            if (items == null || items.Count == 0)
            {
                return 0;
            }

            var uploaded = 0;
            const int batchSize = 12;

            for (var offset = 0; offset < items.Count; offset += batchSize)
            {
                var take = Math.Min(batchSize, items.Count - offset);
                var json = BuildBatchJson(items, offset, take);
                if (json == null)
                {
                    continue;
                }

                var response = CloudApiClient.PostJsonDetailed(
                    "/api/previews/upload-batch",
                    json,
                    includeApiKey: true);

                if (response.StatusCode < 200 || response.StatusCode >= 300)
                {
                    continue;
                }

                uploaded += ApplyBatchResults(items, response.Body);
            }

            return uploaded;
        }

        private static string BuildBatchJson(IReadOnlyList<FamilyUploadItem> items, int start, int take)
        {
            var sb = new StringBuilder(65536);
            sb.Append("{\"items\":[");
            var first = true;
            var any = false;

            for (var i = 0; i < take; i++)
            {
                var item = items[start + i];
                if (item?.Family == null)
                {
                    continue;
                }

                var localPath = item.LocalPreviewPath;
                if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
                {
                    continue;
                }

                var bytes = File.ReadAllBytes(localPath);
                if (bytes.Length == 0 || bytes.Length > 512 * 1024)
                {
                    continue;
                }

                if (!first)
                {
                    sb.Append(',');
                }

                first = false;
                any = true;
                var base64 = Convert.ToBase64String(bytes);
                sb.Append("{\"rfaPath\":\"").Append(EscapeJson(item.Family.RfaPath)).Append('"');
                sb.Append(",\"imageBase64\":\"").Append(base64).Append('"');
                sb.Append(",\"contentType\":\"image/png\"}");
            }

            if (!any)
            {
                return null;
            }

            sb.Append("]}");
            return sb.ToString();
        }

        private static int ApplyBatchResults(IReadOnlyList<FamilyUploadItem> items, string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return 0;
            }

            var uploaded = 0;
            var searchFrom = 0;
            while (searchFrom < body.Length)
            {
                var okIdx = body.IndexOf("\"ok\":true", searchFrom, StringComparison.Ordinal);
                if (okIdx < 0)
                {
                    break;
                }

                var blockStart = body.LastIndexOf('{', okIdx);
                var blockEnd = body.IndexOf('}', okIdx);
                if (blockStart < 0 || blockEnd <= blockStart)
                {
                    searchFrom = okIdx + 8;
                    continue;
                }

                var block = body.Substring(blockStart, blockEnd - blockStart + 1);
                var rfaPath = ExtractJsonString(block, "rfaPath");
                var previewUrl = ExtractJsonString(block, "previewUrl");
                if (!string.IsNullOrWhiteSpace(rfaPath) && !string.IsNullOrWhiteSpace(previewUrl))
                {
                    foreach (var item in items)
                    {
                        if (item?.Family != null
                            && string.Equals(item.Family.RfaPath, rfaPath, StringComparison.OrdinalIgnoreCase))
                        {
                            item.Family.PreviewPath = previewUrl;
                            uploaded++;
                            break;
                        }
                    }
                }

                searchFrom = blockEnd + 1;
            }

            return uploaded;
        }

        private static string ExtractJsonString(string json, string key)
        {
            var marker = "\"" + key + "\":\"";
            var start = json.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                return null;
            }

            start += marker.Length;
            var end = json.IndexOf('"', start);
            if (end <= start)
            {
                return null;
            }

            return json
                .Substring(start, end - start)
                .Replace("\\/", "/")
                .Replace("\\\"", "\"");
        }

        private static string BuildSingleUploadJson(string rfaPath, string imageBase64)
        {
            var sb = new StringBuilder(imageBase64.Length + 256);
            sb.Append("{\"rfaPath\":\"");
            sb.Append(EscapeJson(rfaPath));
            sb.Append("\",\"imageBase64\":\"");
            sb.Append(imageBase64);
            sb.Append("\",\"contentType\":\"image/png\"}");
            return sb.ToString();
        }

        private static string ExtractPreviewUrl(string jsonBody)
        {
            if (string.IsNullOrWhiteSpace(jsonBody))
            {
                return null;
            }

            const string marker = "\"previewUrl\":\"";
            var start = jsonBody.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                return null;
            }

            start += marker.Length;
            var end = jsonBody.IndexOf('"', start);
            if (end <= start)
            {
                return null;
            }

            return jsonBody
                .Substring(start, end - start)
                .Replace("\\/", "/")
                .Replace("\\\"", "\"");
        }

        private static string EscapeJson(string value)
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
