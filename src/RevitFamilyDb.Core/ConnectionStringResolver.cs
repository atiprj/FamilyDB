using System;
using Microsoft.Win32;

namespace RevitFamilyDb.Core;

/// <summary>
/// Risolve la connection string SQL come Anker risolve <c>API_ENDPOINT</c>:
/// variabile d'ambiente <c>REVIT_FAMILY_DB_CONN</c>, poi registry, poi fallback.
/// </summary>
public static class ConnectionStringResolver
{
    public const string RegistrySubKey = @"Software\RevitFamilyDb";
    public const string RegistryValueName = "ConnectionString";

    /// <param name="fallbackConnectionString">Es. da appsettings o costante di progetto.</param>
#pragma warning disable CA1416 // Registry: Revit/Inspector sono solo Windows
    public static string Resolve(string fallbackConnectionString)
    {
        var env = Environment.GetEnvironmentVariable("REVIT_FAMILY_DB_CONN");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env.Trim();
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistrySubKey, false);
            var reg = key?.GetValue(RegistryValueName) as string;
            if (!string.IsNullOrWhiteSpace(reg))
            {
                return reg.Trim();
            }
        }
        catch
        {
            // registry non disponibile o negato
        }

        return fallbackConnectionString?.Trim() ?? "";
    }
#pragma warning restore CA1416
}
