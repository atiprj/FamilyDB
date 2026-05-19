using System;
using System.Net.Http;
using System.Net.Http.Headers;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

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

        private static string BuildRequestUri(string baseUrl, string relativePath)
        {
            var normalizedBase = baseUrl.TrimEnd('/');
            var normalizedPath = relativePath.StartsWith("/") ? relativePath : "/" + relativePath;
            return normalizedBase + normalizedPath;
        }
    }
}
