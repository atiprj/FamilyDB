using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;

namespace FamCloud.Addin2025
{
    internal static class PreviewThumbnailHelper
    {
        private static readonly string PreviewRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "FamCloud",
            "2025",
            "Previews");

        public static string TrySaveTypePreview(ElementType elementType, string hashKey)
        {
            try
            {
                if (elementType == null)
                {
                    return null;
                }

                Directory.CreateDirectory(PreviewRoot);
                foreach (var size in new[] { 128, 96, 64 })
                {
                    using (var img = elementType.GetPreviewImage(new Size(size, size)))
                    {
                        if (img == null)
                        {
                            continue;
                        }

                        var path = Path.Combine(PreviewRoot, ShortHash(hashKey) + ".png");
                        using (var bmp = new Bitmap(img))
                        {
                            bmp.Save(path, ImageFormat.Png);
                        }

                        return path;
                    }
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        public static void AttachCloudPreviews(System.Collections.Generic.IList<FamilyUploadItem> items)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            foreach (var item in items)
            {
                if (item == null || item.Family == null)
                {
                    continue;
                }

                if (IsCloudUrl(item.Family.PreviewPath))
                {
                    continue;
                }

                var localPath = item.LocalPreviewPath;
                if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
                {
                    continue;
                }

                var url = PreviewCloudClient.UploadLocalPng(item.Family.RfaPath, localPath);
                if (!string.IsNullOrWhiteSpace(url))
                {
                    item.Family.PreviewPath = url;
                }
            }
        }

        public static bool IsCloudUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        }

        private static string ShortHash(string input)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input ?? ""));
                return BitConverter.ToString(bytes).Replace("-", "").Substring(0, 16).ToLowerInvariant();
            }
        }
    }
}
