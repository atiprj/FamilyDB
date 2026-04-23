using System;
using System.IO;

namespace RevitFamilyDb.Core;

/// <summary>
/// Log append-only in CommonApplicationData (simile a telemetria/file log lato add-in).
/// </summary>
public static class RevitFamilyDbLog
{
    private static readonly object SyncRoot = new object();

    private static string LogDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "RevitFamilyDb",
            "logs");

    public static void Info(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(LogDirectory);
            var line = $"{DateTime.UtcNow:O}\t{message}{Environment.NewLine}";
            var file = Path.Combine(LogDirectory, $"revit-family-db-{DateTime.UtcNow:yyyyMMdd}.log");
            lock (SyncRoot)
            {
                File.AppendAllText(file, line);
            }
        }
        catch
        {
            // non interrompere Revit per un log
        }
    }
}
