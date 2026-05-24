using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitFamilyDb.Core;

namespace FamCloud.Addin2025
{
    internal static class LibraryCloudSync
    {
        private const string ArcModel =
            @"F:\Documenti Utili\BANCA DATI REVIT PROGETTI\Famiglie\Database Famiglie\ARC\00_Library\2025_ATI_ARC_rfa.rvt";

        private const string FurModel =
            @"F:\Documenti Utili\BANCA DATI REVIT PROGETTI\Famiglie\Database Famiglie\FUR\00_Library\2025_ATI_FUR_rfa.rvt";

        public static Result SyncDiscipline(ExternalCommandData commandData, string discipline, ref string message)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "ARC", ArcModel },
                { "FUR", FurModel }
            };

            if (!map.TryGetValue(discipline, out var modelPath))
            {
                message = "Disciplina non gestita: " + discipline;
                return Result.Failed;
            }

            return SyncModelToCloud(commandData, modelPath, discipline, ref message, showDialog: true);
        }

        public static Result SyncAll(ExternalCommandData commandData, ref string message)
        {
            var countArc = 0;
            var okArc = SyncModelToCloud(commandData, ArcModel, "ARC", ref message, showDialog: false, out countArc);
            if (okArc != Result.Succeeded)
            {
                return okArc;
            }

            var countFur = 0;
            var okFur = SyncModelToCloud(commandData, FurModel, "FUR", ref message, showDialog: false, out countFur);
            if (okFur != Result.Succeeded)
            {
                return okFur;
            }

            TaskDialog.Show(
                "FamCloud",
                "Sync ALL → Cloud completata.\n\nARC: " + countArc + " famiglie\nFUR: " + countFur + " famiglie");
            return Result.Succeeded;
        }

        private static Result SyncModelToCloud(
            ExternalCommandData commandData,
            string modelPath,
            string discipline,
            ref string message,
            bool showDialog,
            out int collectedCount)
        {
            collectedCount = 0;
            try
            {
                if (!File.Exists(modelPath))
                {
                    message = "Modello libreria non trovato:\n" + modelPath;
                    TaskDialog.Show("FamCloud", message);
                    return Result.Failed;
                }

                var app = commandData.Application.Application;
                var revitYear = int.TryParse(app.VersionNumber, out var yr) ? (int?)yr : null;
                var famDoc = app.OpenDocumentFile(modelPath);
                List<FamilyUploadItem> items;

                try
                {
                    items = CollectFromLibraryDocument(famDoc, modelPath, discipline, revitYear);
                }
                finally
                {
                    try
                    {
                        if (famDoc != null && !IsSameDocumentAsActive(commandData, famDoc))
                        {
                            famDoc.Close(false);
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }

                collectedCount = items.Count;
                if (items.Count == 0)
                {
                    message = "Nessuna famiglia da sincronizzare in " + discipline + ".";
                    TaskDialog.Show("FamCloud", message);
                    return Result.Succeeded;
                }

                var upload = CloudUpsertService.UploadInChunks(items);
                var summary =
                    "Sync " + discipline + " → Cloud\n\n" +
                    "Raccolte: " + upload.Total + "\n" +
                    "Upload OK: " + upload.Uploaded + "\n" +
                    "Fallite: " + upload.Failed + "\n" +
                    "Cicli push: " + upload.Passes;

                if (upload.Errors.Count > 0)
                {
                    summary += "\n\nErrori (max 5):\n" +
                        string.Join("\n", upload.Errors.GetRange(0, Math.Min(5, upload.Errors.Count)));
                }

                if (showDialog)
                {
                    TaskDialog.Show("FamCloud", summary);
                }

                message = summary;
                return upload.Failed == 0 ? Result.Succeeded : Result.Failed;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("FamCloud", "Sync " + discipline + " → Cloud fallita:\n\n" + ex.Message);
                return Result.Failed;
            }
        }

        private static Result SyncModelToCloud(
            ExternalCommandData commandData,
            string modelPath,
            string discipline,
            ref string message,
            bool showDialog)
        {
            return SyncModelToCloud(commandData, modelPath, discipline, ref message, showDialog, out _);
        }

        private static List<FamilyUploadItem> CollectFromLibraryDocument(
            Document famDoc,
            string modelPath,
            string discipline,
            int? revitYear)
        {
            var items = new List<FamilyUploadItem>();

            var loadables = new FilteredElementCollector(famDoc).OfClass(typeof(Family)).Cast<Family>();
            foreach (var fam in loadables)
            {
                if (IsAnnotationCategory(fam.FamilyCategory))
                {
                    continue;
                }

                var familyName = string.IsNullOrWhiteSpace(fam.Name) ? "UnnamedFamily" : fam.Name;
                var rfaPath = "loadable://" + modelPath + "#" + familyName;
                var paramRows = new List<FamilyParameterRecord>();
                ElementType symEt = null;
                try
                {
                    var symIds = fam.GetFamilySymbolIds();
                    if (symIds != null && symIds.Count > 0)
                    {
                        symEt = famDoc.GetElement(symIds.First()) as ElementType;
                        if (symEt != null)
                        {
                            paramRows = CollectParameters(symEt);
                        }
                    }
                }
                catch
                {
                    // ignore
                }

                var localPreview = symEt != null
                    ? PreviewThumbnailHelper.TrySaveTypePreview(symEt, rfaPath)
                    : null;

                items.Add(new FamilyUploadItem
                {
                    Family = new FamilyRecord
                    {
                        FamilyName = familyName,
                        CategoryName = fam.FamilyCategory?.Name ?? "N/A",
                        FamilyKind = "Loadable",
                        SourceModelPath = modelPath,
                        SourceDiscipline = discipline,
                        RfaPath = rfaPath,
                        RevitVersion = revitYear,
                        FileHash = ComputeRowSignature("Loadable", modelPath, familyName, null, paramRows),
                        ApprovalStatus = "Draft"
                    },
                    Parameters = CloudUpsertService.TrimParametersForCloud(paramRows),
                    LocalPreviewPath = localPreview
                });
            }

            var systemTypes = new FilteredElementCollector(famDoc)
                .WhereElementIsElementType()
                .Cast<ElementType>()
                .Where(t => t.Category != null && !(t is FamilySymbol));

            foreach (var typ in systemTypes)
            {
                if (IsAnnotationCategory(typ.Category))
                {
                    continue;
                }

                var name = string.IsNullOrWhiteSpace(typ.FamilyName)
                    ? typ.Name
                    : typ.FamilyName + " : " + typ.Name;
                var paramRows = CollectParameters(typ);
                var rfaKey = "system://" + modelPath + "#type:" + typ.Id.IntegerValue;
                var localPreview = PreviewThumbnailHelper.TrySaveTypePreview(typ, rfaKey);

                items.Add(new FamilyUploadItem
                {
                    Family = new FamilyRecord
                    {
                        FamilyName = name,
                        CategoryName = typ.Category?.Name ?? "System",
                        FamilyKind = "System",
                        SourceModelPath = modelPath,
                        SourceDiscipline = discipline,
                        SourceElementTypeId = typ.Id.IntegerValue,
                        RfaPath = rfaKey,
                        RevitVersion = revitYear,
                        FileHash = ComputeRowSignature("System", modelPath, name, typ.Id.IntegerValue, paramRows),
                        ApprovalStatus = "Draft"
                    },
                    Parameters = CloudUpsertService.TrimParametersForCloud(paramRows),
                    LocalPreviewPath = localPreview
                });
            }

            return items;
        }

        private static bool IsSameDocumentAsActive(ExternalCommandData commandData, Document doc)
        {
            if (doc == null)
            {
                return false;
            }

            var active = commandData.Application.ActiveUIDocument?.Document;
            if (active == null)
            {
                return false;
            }

            if (ReferenceEquals(active, doc))
            {
                return true;
            }

            try
            {
                var p1 = doc.PathName;
                var p2 = active.PathName;
                if (string.IsNullOrWhiteSpace(p1) || string.IsNullOrWhiteSpace(p2))
                {
                    return false;
                }

                return string.Equals(Path.GetFullPath(p1), Path.GetFullPath(p2), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsAnnotationCategory(Category category)
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
                // ignore
            }

            var name = category.Name ?? "";
            return name.IndexOf("annotation", StringComparison.OrdinalIgnoreCase) >= 0
                || name.EndsWith(" Tags", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Tags", StringComparison.OrdinalIgnoreCase);
        }

        private static List<FamilyParameterRecord> CollectParameters(Element elem)
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

        private static string ComputeRowSignature(
            string kind,
            string modelPath,
            string familyName,
            int? elementTypeId,
            List<FamilyParameterRecord> parameters)
        {
            var sb = new StringBuilder(4096);
            sb.Append(kind).Append('|').Append(modelPath).Append('|').Append(familyName).Append('|')
                .Append(elementTypeId?.ToString() ?? "");
            foreach (var p in parameters)
            {
                sb.Append('|').Append(p.ParameterName).Append('=').Append(p.StringValue);
            }

            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                var hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "");
            }
        }
    }
}
