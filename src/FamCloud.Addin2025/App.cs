using System;
using System.Collections.Generic;
using System.Linq;
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
                "FamCloudSyncArc",
                "Sync ARC\n→ Cloud",
                assemblyPath,
                typeof(SyncArcCloudCommand).FullName));

            panel.AddItem(new PushButtonData(
                "FamCloudSyncFur",
                "Sync FUR\n→ Cloud",
                assemblyPath,
                typeof(SyncFurCloudCommand).FullName));

            panel.AddItem(new PushButtonData(
                "FamCloudSyncAll",
                "Sync ALL\n→ Cloud",
                assemblyPath,
                typeof(SyncAllCloudCommand).FullName));

            panel.AddItem(new PushButtonData(
                "FamCloudPublishCatalog",
                "Publish progetto\n→ Cloud",
                assemblyPath,
                typeof(PublishCatalogCommand).FullName));

            panel.AddItem(new PushButtonData(
                "FamCloudListLoad",
                "Elenco +\nCarica",
                assemblyPath,
                typeof(CloudListLoadCommand).FullName));

            panel.AddItem(new PushButtonData(
                "FamCloudPendingQueue",
                "Pending Queue",
                assemblyPath,
                typeof(PendingQueueCommand).FullName));

            panel.AddItem(new PushButtonData(
                "FamCloudHealthCheck",
                "Health API",
                assemblyPath,
                typeof(HealthCheckCommand).FullName));

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
    public sealed class CloudListLoadCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var rows = CloudCatalogClient.FetchCatalogForBrowse(5000);
                if (rows.Count == 0)
                {
                    TaskDialog.Show("FamCloud", "Catalogo cloud vuoto. Esegui prima Sync ALL → Cloud.");
                    return Result.Cancelled;
                }

                return RevitFamilyDb.Addin2025.ListFamiliesVisualCommand.BrowseAndLoadFromCatalog(
                    commandData,
                    rows,
                    ref message);
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("FamCloud", "Elenco + Carica fallito:\n\n" + ex.Message);
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class SyncArcCloudCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return LibraryCloudSync.SyncDiscipline(commandData, "ARC", ref message);
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class SyncFurCloudCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return LibraryCloudSync.SyncDiscipline(commandData, "FUR", ref message);
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class SyncAllCloudCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return LibraryCloudSync.SyncAll(commandData, ref message);
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class PublishCatalogCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    TaskDialog.Show("FamCloud", "Nessun documento attivo. Apri un modello di progetto.");
                    return Result.Failed;
                }

                var repo = LocalDbFactory.CreateRepository();
                repo.EnsureExtendedSchema();

                var uploadItems = BuildUploadItemsFromActiveDocument(doc, repo);
                if (uploadItems.Count == 0)
                {
                    TaskDialog.Show(
                        "FamCloud",
                        "Nessuna famiglia piazzata nel modello attivo da pubblicare.\n" +
                        "(Loadable + System; escluse annotazioni e tag.)");
                    return Result.Succeeded;
                }

                var upload = CloudUpsertService.UploadInChunks(uploadItems);
                var summary =
                    "Publish progetto → Cloud (Loadable + System)\n\n" +
                    "Totale: " + upload.Total + "\n" +
                    "Upload OK: " + upload.Uploaded + "\n" +
                    "Invariate (saltate): " + upload.SkippedUnchanged + "\n" +
                    "Solo anteprima: " + upload.PreviewOnlyUpdated + "\n" +
                    "Fallite: " + upload.Failed + "\n" +
                    "Cicli push: " + upload.Passes;
                if (upload.Errors.Count > 0)
                {
                    summary += "\n\nErrori (max 5):\n" +
                        string.Join("\n", upload.Errors.GetRange(0, Math.Min(5, upload.Errors.Count)));
                }

                TaskDialog.Show("FamCloud", summary);
                return upload.Failed == 0 ? Result.Succeeded : Result.Failed;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("FamCloud", "Publish to Cloud fallito:\n\n" + ex.Message);
                return Result.Failed;
            }
        }

        private static List<FamilyUploadItem> BuildUploadItemsFromActiveDocument(Document doc, FamilyRepository repo)
        {
            var result = new List<FamilyUploadItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dbCache = repo.GetFamilies(20000);

            var instances = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>();

            foreach (var instance in instances)
            {
                if (PlacedFamilyRules.IsAnnotation(instance))
                {
                    continue;
                }

                var symbol = instance.Symbol;
                var family = symbol?.Family;
                if (symbol == null || family == null)
                {
                    continue;
                }

                if (PlacedFamilyRules.IsAnnotation(symbol.Category) || PlacedFamilyRules.IsAnnotation(family.FamilyCategory))
                {
                    continue;
                }

                var placementKey = PlacedFamilyRules.BuildPlacementKey(family, symbol);
                if (!seen.Add(placementKey))
                {
                    continue;
                }

                var dbMatch = PlacedFamilyRules.TryFindDbMatch(dbCache, family.Name, symbol.Category?.Name);
                var record = dbMatch != null
                    ? CloneForPublish(dbMatch, doc)
                    : PlacedFamilyRules.CreateRecordFromPlacement(doc, family, symbol);

                if (string.IsNullOrWhiteSpace(record.RfaPath) || string.IsNullOrWhiteSpace(record.FamilyName))
                {
                    continue;
                }

                var parameters = dbMatch?.FamilyId != null
                    ? repo.GetParametersForFamily(dbMatch.FamilyId.Value)
                    : PlacedFamilyRules.CollectParameters(symbol);

                var localPreview = PreviewThumbnailHelper.TrySaveTypePreview(symbol, record.RfaPath);

                result.Add(new FamilyUploadItem
                {
                    Family = record,
                    Parameters = CloudUpsertService.TrimParametersForCloud(parameters),
                    LocalPreviewPath = localPreview
                });
            }

            AppendPlacedSystemTypes(doc, repo, dbCache, seen, result);

            return result;
        }

        private static void AppendPlacedSystemTypes(
            Document doc,
            FamilyRepository repo,
            IReadOnlyList<FamilyRecord> dbCache,
            HashSet<string> seen,
            List<FamilyUploadItem> result)
        {
            var typeIdsSeen = new HashSet<int>();

            foreach (var element in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                if (PlacedFamilyRules.IsAnnotation(element))
                {
                    continue;
                }

                var typeId = element.GetTypeId();
                if (typeId == null || typeId == ElementId.InvalidElementId)
                {
                    continue;
                }

                var elementType = doc.GetElement(typeId) as ElementType;
                if (elementType == null || elementType is FamilySymbol)
                {
                    continue;
                }

                if (PlacedFamilyRules.IsAnnotation(elementType.Category))
                {
                    continue;
                }

                var typeKey = elementType.Id.IntegerValue;
                if (!typeIdsSeen.Add(typeKey))
                {
                    continue;
                }

                var placementKey = PlacedFamilyRules.BuildSystemPlacementKey(elementType);
                if (!seen.Add(placementKey))
                {
                    continue;
                }

                var systemName = PlacedFamilyRules.BuildSystemFamilyName(elementType);
                var dbMatch = PlacedFamilyRules.TryFindDbMatch(
                    dbCache,
                    systemName,
                    elementType.Category?.Name,
                    "System");
                var record = dbMatch != null
                    ? CloneForPublish(dbMatch, doc)
                    : PlacedFamilyRules.CreateSystemRecordFromPlacement(doc, elementType);

                if (string.IsNullOrWhiteSpace(record.RfaPath) || string.IsNullOrWhiteSpace(record.FamilyName))
                {
                    continue;
                }

                var parameters = dbMatch?.FamilyId != null
                    ? repo.GetParametersForFamily(dbMatch.FamilyId.Value)
                    : PlacedFamilyRules.CollectParameters(elementType);

                var localPreview = PreviewThumbnailHelper.TrySaveTypePreview(elementType, record.RfaPath);

                result.Add(new FamilyUploadItem
                {
                    Family = record,
                    Parameters = CloudUpsertService.TrimParametersForCloud(parameters),
                    LocalPreviewPath = localPreview
                });
            }
        }

        private static FamilyRecord CloneForPublish(FamilyRecord source, Document doc)
        {
            return new FamilyRecord
            {
                FamilyId = source.FamilyId,
                FamilyName = source.FamilyName,
                CategoryName = source.CategoryName,
                RfaPath = source.RfaPath,
                PreviewPath = source.PreviewPath,
                FamilyKind = source.FamilyKind,
                SourceModelPath = doc.PathName,
                SourceElementTypeId = source.SourceElementTypeId,
                SourceDiscipline = source.SourceDiscipline,
                RevitVersion = source.RevitVersion,
                FileHash = source.FileHash,
                ApprovalStatus = source.ApprovalStatus
            };
        }
    }

    internal static class PlacedFamilyRules
    {
        public static bool IsAnnotation(Element element)
        {
            return element != null && IsAnnotation(element.Category);
        }

        public static bool IsAnnotation(Category category)
        {
            if (category == null)
            {
                return false;
            }

            try
            {
                if (category.CategoryType == CategoryType.Annotation)
                {
                    return true;
                }
            }
            catch
            {
                // Revit API edge cases on some categories.
            }

            var name = category.Name ?? "";
            if (name.IndexOf("annotation", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (name.EndsWith(" Tags", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Tags", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        public static bool IsAnnotationCategoryName(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
            {
                return false;
            }

            return categoryName.IndexOf("annotation", StringComparison.OrdinalIgnoreCase) >= 0
                || categoryName.EndsWith(" Tags", StringComparison.OrdinalIgnoreCase)
                || string.Equals(categoryName, "Tags", StringComparison.OrdinalIgnoreCase);
        }

        public static string BuildPlacementKey(Family family, FamilySymbol symbol)
        {
            return "L:" + family.Id.IntegerValue + ":" + symbol.Id.IntegerValue;
        }

        public static string BuildSystemPlacementKey(ElementType elementType)
        {
            return "S:" + elementType.Id.IntegerValue;
        }

        public static string BuildSystemFamilyName(ElementType elementType)
        {
            if (elementType == null)
            {
                return "UnnamedSystem";
            }

            return string.IsNullOrWhiteSpace(elementType.FamilyName)
                ? elementType.Name
                : elementType.FamilyName + " : " + elementType.Name;
        }

        public static FamilyRecord TryFindDbMatch(
            IReadOnlyList<FamilyRecord> dbCache,
            string familyName,
            string categoryName,
            string familyKind = null)
        {
            var baseName = NormalizeFamilyBaseName(familyName);
            foreach (var rec in dbCache)
            {
                if (IsAnnotationCategoryName(rec.CategoryName))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(familyKind)
                    && !string.Equals(rec.FamilyKind ?? "", familyKind, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var recBase = NormalizeFamilyBaseName(rec.FamilyName);
                if (string.Equals(recBase, baseName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(rec.CategoryName ?? "", categoryName ?? "", StringComparison.OrdinalIgnoreCase))
                {
                    return rec;
                }
            }

            return null;
        }

        public static FamilyRecord CreateSystemRecordFromPlacement(Document doc, ElementType elementType)
        {
            var docPath = string.IsNullOrWhiteSpace(doc.PathName) ? doc.Title : doc.PathName;
            var name = BuildSystemFamilyName(elementType);

            return new FamilyRecord
            {
                FamilyName = name,
                CategoryName = elementType.Category?.Name ?? "System",
                RfaPath = "placed://" + docPath + "#system:" + elementType.Id.IntegerValue,
                FamilyKind = "System",
                SourceModelPath = docPath,
                SourceElementTypeId = elementType.Id.IntegerValue,
                RevitVersion = int.TryParse(doc.Application.VersionNumber, out var year) ? year : (int?)null,
                ApprovalStatus = "Draft"
            };
        }

        public static FamilyRecord CreateRecordFromPlacement(Document doc, Family family, FamilySymbol symbol)
        {
            var docPath = string.IsNullOrWhiteSpace(doc.PathName) ? doc.Title : doc.PathName;
            var familyName = !string.IsNullOrWhiteSpace(symbol.Name) && symbol.Name != family.Name
                ? family.Name + " : " + symbol.Name
                : family.Name;

            return new FamilyRecord
            {
                FamilyName = familyName,
                CategoryName = symbol.Category?.Name ?? family.FamilyCategory?.Name ?? "N/A",
                RfaPath = "placed://" + docPath + "#family:" + family.Id.IntegerValue,
                FamilyKind = "Loadable",
                SourceModelPath = docPath,
                SourceElementTypeId = symbol.Id.IntegerValue,
                RevitVersion = int.TryParse(doc.Application.VersionNumber, out var year) ? year : (int?)null,
                ApprovalStatus = "Draft"
            };
        }

        public static List<FamilyParameterRecord> CollectParameters(Element elem)
        {
            var list = new List<FamilyParameterRecord>();
            if (elem == null)
            {
                return list;
            }

            foreach (Parameter p in elem.Parameters)
            {
                if (p == null || !p.HasValue)
                {
                    continue;
                }

                var def = p.Definition;
                if (def == null)
                {
                    continue;
                }

                var name = def.Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (name.Length > 200)
                {
                    name = name.Substring(0, 200);
                }

                var val = p.AsValueString();
                if (val != null && val.Length > 2000)
                {
                    val = val.Substring(0, 2000);
                }

                var groupName = "";
                try
                {
                    if (def is InternalDefinition idef)
                    {
                        groupName = idef.GetGroupTypeId().ToString();
                    }
                }
                catch
                {
                    groupName = "";
                }

                list.Add(new FamilyParameterRecord
                {
                    ParameterName = name,
                    ParameterGroupName = groupName,
                    StorageType = p.StorageType.ToString(),
                    StringValue = val
                });

                if (list.Count >= 500)
                {
                    break;
                }
            }

            return list;
        }

        private static string NormalizeFamilyBaseName(string familyName)
        {
            if (string.IsNullOrWhiteSpace(familyName))
            {
                return "";
            }

            var idx = familyName.IndexOf(':');
            return (idx >= 0 ? familyName.Substring(0, idx) : familyName).Trim();
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
            var result = PostJsonDetailed(relativePath, jsonBody, includeApiKey);
            return $"HTTP {result.StatusCode}\n{result.Body}";
        }

        public static CloudApiResponse PostJsonDetailed(string relativePath, string jsonBody, bool includeApiKey)
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
                client.Timeout = TimeSpan.FromSeconds(120);
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
                    return new CloudApiResponse
                    {
                        StatusCode = (int)response.StatusCode,
                        Body = body ?? ""
                    };
                }
            }
        }

        private static string BuildRequestUri(string baseUrl, string relativePath)
        {
            var normalizedBase = baseUrl.TrimEnd('/');
            var normalizedPath = relativePath.StartsWith("/") ? relativePath : "/" + relativePath;
            return normalizedBase + normalizedPath;
        }

        public sealed class CloudApiResponse
        {
            public int StatusCode { get; set; }
            public string Body { get; set; }
        }
    }
}
