using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitFamilyDb.Core;

namespace FamCloud.Addin2025
{
    public sealed class App : IExternalApplication
    {
        private const string RibbonTab = "FamCloud";
        private const string RibbonPanel = "Cloud";

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                application.CreateRibbonTab(RibbonTab);
            }
            catch
            {
                // Tab already exists for this session.
            }

            var panel = application.CreateRibbonPanel(RibbonTab, RibbonPanel);
            var assemblyPath = typeof(App).Assembly.Location;

            panel.AddItem(new PushButtonData(
                "FamCloudHealthCheck",
                "Health API",
                assemblyPath,
                typeof(HealthCheckCommand).FullName));

            panel.AddItem(new PushButtonData(
                "FamCloudPendingQueue",
                "Pending Queue",
                assemblyPath,
                typeof(PendingQueueCommand).FullName));

            panel.AddItem(new PushButtonData(
                "FamCloudPublishCatalog",
                "Publish to Cloud",
                assemblyPath,
                typeof(PublishCatalogCommand).FullName));

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class HealthCheckCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var response = CloudApiClient.Get("/api/health", includeApiKey: false);
                TaskDialog.Show("FamCloud", "Health check:\n\n" + response);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("FamCloud", "Health check fallito:\n\n" + ex.Message);
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class PendingQueueCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var response = CloudApiClient.Get("/api/queue/pending-count", includeApiKey: true);
                TaskDialog.Show("FamCloud", "Queue pending:\n\n" + response);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("FamCloud", "Lettura queue fallita:\n\n" + ex.Message);
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class PublishCatalogCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var repo = LocalDbFactory.CreateRepository();
                repo.EnsureExtendedSchema();
                var families = repo.GetFamilies(1000);
                if (families.Count == 0)
                {
                    TaskDialog.Show("FamCloud", "Nessuna famiglia nel DB locale da pubblicare.");
                    return Result.Succeeded;
                }

                var jsonPayload = BuildUpsertPayloadJson(families, repo, out var validCount);
                if (validCount == 0)
                {
                    TaskDialog.Show("FamCloud", "Nessuna famiglia valida da inviare (familyName/rfaPath mancanti).");
                    return Result.Succeeded;
                }

                var response = CloudApiClient.PostJson("/api/families/upsert", jsonPayload, includeApiKey: true);
                TaskDialog.Show("FamCloud", "Publish to Cloud:\n\n" + response);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("FamCloud", "Publish to Cloud fallito:\n\n" + ex.Message);
                return Result.Failed;
            }
        }

        private static string BuildUpsertPayloadJson(
            System.Collections.Generic.IReadOnlyList<FamilyRecord> families,
            FamilyRepository repo,
            out int validCount)
        {
            var sb = new StringBuilder(16384);
            validCount = 0;
            sb.Append("{\"items\":[");
            var firstItem = true;

            foreach (var family in families)
            {
                if (string.IsNullOrWhiteSpace(family.RfaPath) || string.IsNullOrWhiteSpace(family.FamilyName))
                {
                    continue;
                }

                var parameters = family.FamilyId.HasValue
                    ? repo.GetParametersForFamily(family.FamilyId.Value)
                    : new System.Collections.Generic.List<FamilyParameterRecord>();

                if (!firstItem)
                {
                    sb.Append(',');
                }

                firstItem = false;
                validCount++;
                sb.Append("{\"family\":{");
                var firstFamilyProp = true;
                AppendJsonStringProp(sb, "familyName", family.FamilyName, ref firstFamilyProp);
                AppendJsonStringProp(sb, "categoryName", family.CategoryName, ref firstFamilyProp);
                AppendJsonStringProp(sb, "rfaPath", family.RfaPath, ref firstFamilyProp);
                AppendJsonStringProp(sb, "previewPath", family.PreviewPath, ref firstFamilyProp);
                AppendJsonIntProp(sb, "revitVersion", family.RevitVersion, ref firstFamilyProp);
                AppendJsonStringProp(sb, "fileHash", family.FileHash, ref firstFamilyProp);
                AppendJsonStringProp(sb, "approvalStatus", family.ApprovalStatus, ref firstFamilyProp);
                AppendJsonStringProp(sb, "familyKind", family.FamilyKind, ref firstFamilyProp);
                AppendJsonStringProp(sb, "sourceModelPath", family.SourceModelPath, ref firstFamilyProp);
                AppendJsonIntProp(sb, "sourceElementTypeId", family.SourceElementTypeId, ref firstFamilyProp);
                AppendJsonStringProp(sb, "sourceDiscipline", family.SourceDiscipline, ref firstFamilyProp);
                sb.Append("},\"parameters\":[");

                for (var i = 0; i < parameters.Count; i++)
                {
                    var p = parameters[i];
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    sb.Append('{');
                    var firstParamProp = true;
                    AppendJsonStringProp(sb, "parameterName", p.ParameterName, ref firstParamProp);
                    AppendJsonStringProp(sb, "parameterGroupName", p.ParameterGroupName, ref firstParamProp);
                    AppendJsonStringProp(sb, "storageType", p.StorageType, ref firstParamProp);
                    AppendJsonStringProp(sb, "stringValue", p.StringValue, ref firstParamProp);
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

    internal static class LocalDbFactory
    {
        private const string DefaultConnectionString =
            "Server=.\\REVITLIB;Database=RevitFamilyLibrary;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;";

        public static FamilyRepository CreateRepository()
        {
            var conn = ConnectionStringResolver.Resolve(DefaultConnectionString);
            return new FamilyRepository(new SqlConnectionFactory(new DbSettings(conn)));
        }
    }

    internal static class CloudApiClient
    {
        private const string DefaultBaseUrl = "https://your-vercel-project.vercel.app";

        public static string Get(string relativePath, bool includeApiKey)
        {
            var baseUrl = Environment.GetEnvironmentVariable("FAMCLOUD_API_BASE_URL");
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                baseUrl = DefaultBaseUrl;
            }

            if (baseUrl.IndexOf("your-vercel-project", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new InvalidOperationException(
                    "Imposta FAMCLOUD_API_BASE_URL con l'URL reale Vercel prima di usare l'addin."
                );
            }

            var apiKey = Environment.GetEnvironmentVariable("FAMCLOUD_ADDIN_API_KEY");
            if (includeApiKey && string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException(
                    "Endpoint protetto: imposta FAMCLOUD_ADDIN_API_KEY sul PC dell'utente."
                );
            }

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(20);
                if (includeApiKey && !string.IsNullOrWhiteSpace(apiKey))
                {
                    client.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", apiKey);
                }

                var requestUri = BuildRequestUri(baseUrl, relativePath);
                var response = client.GetAsync(requestUri).GetAwaiter().GetResult();
                var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return $"HTTP {(int)response.StatusCode}\n{body}";
            }
        }

        public static string PostJson(string relativePath, string jsonBody, bool includeApiKey)
        {
            var baseUrl = Environment.GetEnvironmentVariable("FAMCLOUD_API_BASE_URL");
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                baseUrl = DefaultBaseUrl;
            }

            if (baseUrl.IndexOf("your-vercel-project", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new InvalidOperationException(
                    "Imposta FAMCLOUD_API_BASE_URL con l'URL reale Vercel prima di usare l'addin."
                );
            }

            var apiKey = Environment.GetEnvironmentVariable("FAMCLOUD_ADDIN_API_KEY");
            if (includeApiKey && string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException(
                    "Endpoint protetto: imposta FAMCLOUD_ADDIN_API_KEY sul PC dell'utente."
                );
            }

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(60);
                if (includeApiKey && !string.IsNullOrWhiteSpace(apiKey))
                {
                    client.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", apiKey);
                }

                var requestUri = BuildRequestUri(baseUrl, relativePath);
                using (var content = new StringContent(jsonBody, Encoding.UTF8, "application/json"))
                {
                    var response = client.PostAsync(requestUri, content).GetAwaiter().GetResult();
                    var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    return $"HTTP {(int)response.StatusCode}\n{body}";
                }
            }
        }

        private static string BuildRequestUri(string baseUrl, string relativePath)
        {
            var normalizedBase = baseUrl.TrimEnd('/');
            var normalizedPath = relativePath.StartsWith("/") ? relativePath : "/" + relativePath;
            return normalizedBase + normalizedPath;
        }
    }
}
