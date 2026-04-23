using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitFamilyDb.Core;

namespace RevitFamilyDb.Addin2026;

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

        var panel = application.CreateRibbonPanel(tabName, "Connection");
        var button = new PushButtonData(
            "TestDb2026",
            "Test DB",
            typeof(App).Assembly.Location,
            typeof(TestDbConnectionCommand).FullName);
        panel.AddItem(button);
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
    private const string DefaultConnectionString =
        "Server=DESKTOP-A6NC714\\REVITLIB;Database=RevitFamilyLibrary;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var connString = ConnectionStringResolver.Resolve(DefaultConnectionString);
            var settings = new DbSettings(connString);
            var factory = new SqlConnectionFactory(settings);
            var health = new DbHealthService(factory);
            var dbName = health.Ping();

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
