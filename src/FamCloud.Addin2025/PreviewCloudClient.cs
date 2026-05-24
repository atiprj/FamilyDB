using System;
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
                var json = BuildUploadJson(rfaPath, base64);
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

        private static string BuildUploadJson(string rfaPath, string imageBase64)
        {
            var sb = new StringBuilder(imageBase64.Length + 256);
            sb.Append("{\"rfaPath\":\"");
            sb.Append(EscapeJson(rfaPath));
            sb.Append("\",\"imageBase64\":\"");
            sb.Append(imageBase64);
            sb.Append("\",\"contentType\":\"image/png\"}");
            return sb.ToString();
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
