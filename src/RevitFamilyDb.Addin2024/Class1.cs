using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitFamilyDb.Core;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace RevitFamilyDb.Addin2024;

public class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        const string tabName = "Family DB";
        try
        {
            application.CreateRibbonTab(tabName);
        }
        catch
        {
        }

        var panel = application.CreateRibbonPanel(tabName, "Database");
        var tools = panel.AddItem(new PulldownButtonData("FamilyDbTools2024", "DB")) as PulldownButton;
        if (tools != null)
        {
            tools.AddPushButton(new PushButtonData(
                "TestDb2024",
                "Test DB",
                typeof(App).Assembly.Location,
                typeof(TestDbConnectionCommand).FullName));
            tools.AddPushButton(new PushButtonData(
                "SyncDb2024",
                "Sync Model->DB",
                typeof(App).Assembly.Location,
                typeof(SyncModelToDbCommand).FullName));
            tools.AddPushButton(new PushButtonData(
                "ListDb2024",
                "Elenco Famiglie",
                typeof(App).Assembly.Location,
                typeof(ListFamiliesFromDbCommand).FullName));
            tools.AddPushButton(new PushButtonData(
                "LoadDb2024",
                "Carica da DB",
                typeof(App).Assembly.Location,
                typeof(LoadFamilyFromDbCommand).FullName));
            tools.AddPushButton(new PushButtonData(
                "ScanRfa2024",
                "Scan Folder RFA->DB",
                typeof(App).Assembly.Location,
                typeof(ScanRfaFolderToDbCommand).FullName));
        }
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public class TestDbConnectionCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var dbName = new DbHealthService(new SqlConnectionFactory(new DbSettings(DbContextFactory.ConnectionString))).Ping();
            TaskDialog.Show("Family DB", "Connessione OK. Database: " + dbName);
            return Result.Succeeded;
        }
        catch (System.Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("Family DB", "Connessione fallita:\n" + ex.Message);
            return Result.Failed;
        }
    }
}

[Transaction(TransactionMode.Manual)]
public class SyncModelToDbCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var repo = DbContextFactory.CreateRepository();
            var doc = commandData.Application.ActiveUIDocument.Document;
            var revitYear = ParseRevitYear(commandData.Application.Application.VersionNumber);
            var added = 0;

            var loadableFamilies = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>();
            foreach (var fam in loadableFamilies)
            {
                var category = fam.FamilyCategory == null ? "N/A" : fam.FamilyCategory.Name;
                var familyName = string.IsNullOrWhiteSpace(fam.Name) ? "UnnamedFamily" : fam.Name;
                repo.UpsertFamily(new FamilyRecord { FamilyName = familyName, CategoryName = category, RfaPath = BuildPseudoLoadablePath(doc.PathName, familyName), RevitVersion = revitYear, ApprovalStatus = "Draft" });
                added++;
            }

            var systemTypes = new FilteredElementCollector(doc).WhereElementIsElementType().Cast<ElementType>().Where(x => x.Category != null);
            foreach (var typ in systemTypes)
            {
                var category = typ.Category?.Name ?? "System";
                var familyName = string.IsNullOrWhiteSpace(typ.FamilyName) ? typ.Name : typ.FamilyName + " : " + typ.Name;
                repo.UpsertFamily(new FamilyRecord { FamilyName = familyName, CategoryName = category, RfaPath = BuildPseudoSystemPath(doc.PathName, typ.Id.IntegerValue), RevitVersion = revitYear, ApprovalStatus = "Draft" });
                added++;
            }

            TaskDialog.Show("Family DB", "Sync completata. Record aggiornati: " + added);
            return Result.Succeeded;
        }
        catch (System.Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("Family DB", "Sync fallita:\n" + ex.Message);
            return Result.Failed;
        }
    }

    private static int? ParseRevitYear(string versionNumber) => int.TryParse(versionNumber, out var year) ? year : null;
    private static string BuildPseudoLoadablePath(string modelPath, string familyName) => "loadable://" + (string.IsNullOrWhiteSpace(modelPath) ? "UNSAVED_MODEL" : modelPath) + "#" + familyName;
    private static string BuildPseudoSystemPath(string modelPath, int typeId) => "system://" + (string.IsNullOrWhiteSpace(modelPath) ? "UNSAVED_MODEL" : modelPath) + "#type:" + typeId;
}

[Transaction(TransactionMode.Manual)]
public class ListFamiliesFromDbCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var repo = DbContextFactory.CreateRepository();
            var rows = repo.GetFamilies(80);
            if (rows.Count == 0) { TaskDialog.Show("Family DB", "Nessuna famiglia presente nel DB."); return Result.Succeeded; }
            var preview = new StringBuilder();
            foreach (var row in rows.Take(25)) { preview.AppendLine("- " + row.FamilyName + " [" + row.CategoryName + "]"); }
            TaskDialog.Show("Family DB", "Totale record: " + rows.Count + "\n\nPrime famiglie:\n" + preview);
            return Result.Succeeded;
        }
        catch (System.Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("Family DB", "Lettura DB fallita:\n" + ex.Message);
            return Result.Failed;
        }
    }
}

[Transaction(TransactionMode.Manual)]
public class LoadFamilyFromDbCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var doc = commandData.Application.ActiveUIDocument.Document;
            var repo = DbContextFactory.CreateRepository();
            var families = repo.GetLoadableFamilies(4);
            if (families.Count == 0) { TaskDialog.Show("Family DB", "Nessuna famiglia caricabile trovata nel DB."); return Result.Succeeded; }
            var chooser = new TaskDialog("Carica da DB") { MainInstruction = "Seleziona una famiglia da caricare", MainContent = "Il comando carica solo record con percorso .rfa reale." };
            for (var i = 0; i < families.Count; i++) { chooser.AddCommandLink((TaskDialogCommandLinkId)(1001 + i), families[i].FamilyName); }
            var selected = chooser.Show();
            var index = ((int)selected) - 1001;
            if (index < 0 || index >= families.Count) return Result.Cancelled;
            var path = families[index].RfaPath;
            if (!File.Exists(path)) { TaskDialog.Show("Family DB", "File non trovato:\n" + path + "\n\nAggiorna il DB con percorsi .rfa validi."); return Result.Failed; }
            using (var tx = new Transaction(doc, "Load Family From DB")) { tx.Start(); doc.LoadFamily(path, out var _); tx.Commit(); }
            TaskDialog.Show("Family DB", "Famiglia caricata con successo:\n" + families[index].FamilyName);
            return Result.Succeeded;
        }
        catch (System.Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("Family DB", "Caricamento fallito:\n" + ex.Message);
            return Result.Failed;
        }
    }
}

[Transaction(TransactionMode.Manual)]
public class ScanRfaFolderToDbCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Seleziona la cartella libreria contenente file .rfa";
                dialog.ShowNewFolderButton = false;
                var pick = dialog.ShowDialog();
                if (pick != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
                {
                    return Result.Cancelled;
                }

                var root = dialog.SelectedPath;
                var files = Directory.GetFiles(root, "*.rfa", SearchOption.AllDirectories);
                if (files.Length == 0)
                {
                    TaskDialog.Show("Family DB", "Nessun file .rfa trovato nella cartella selezionata.");
                    return Result.Succeeded;
                }

                var repo = DbContextFactory.CreateRepository();
                var revitYear = int.TryParse(commandData.Application.Application.VersionNumber, out var year) ? (int?)year : null;
                var count = 0;
                foreach (var file in files)
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    var category = GuessCategoryFromPath(file);
                    repo.UpsertFamily(new FamilyRecord
                    {
                        FamilyName = name,
                        CategoryName = category,
                        RfaPath = file,
                        RevitVersion = revitYear,
                        ApprovalStatus = "Draft"
                    });
                    count++;
                }

                TaskDialog.Show("Family DB", "Scan completata.\nFile .rfa indicizzati: " + count);
                return Result.Succeeded;
            }
        }
        catch (System.Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("Family DB", "Scan cartella fallita:\n" + ex.Message);
            return Result.Failed;
        }
    }

    private static string GuessCategoryFromPath(string fullPath)
    {
        var parts = fullPath.Split('\\');
        if (parts.Length >= 2)
        {
            return parts[parts.Length - 2];
        }
        return "Unknown";
    }
}

internal static class DbContextFactory
{
    private const string DefaultConnectionString = "Server=DESKTOP-A6NC714\\REVITLIB;Database=RevitFamilyLibrary;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;";
    public static string ConnectionString => ConnectionStringResolver.Resolve(DefaultConnectionString);
    public static FamilyRepository CreateRepository() => new FamilyRepository(new SqlConnectionFactory(new DbSettings(ConnectionString)));
}
