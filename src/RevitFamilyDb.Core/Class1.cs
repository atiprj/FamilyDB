using System.Data.SqlClient;
using System.Collections.Generic;
using System.Linq;

namespace RevitFamilyDb.Core;

public sealed class FamilyParameterRecord
{
    public string ParameterName { get; set; }
    public string ParameterGroupName { get; set; }
    public string StorageType { get; set; }
    public string StringValue { get; set; }
}

public sealed class WebQueueItem
{
    public long QueueId { get; set; }
    public int FamilyId { get; set; }
    public string Status { get; set; }
}

public sealed class DbSettings
{
    public string ConnectionString { get; }

    public DbSettings(string connectionString)
    {
        ConnectionString = connectionString;
    }
}

public sealed class SqlConnectionFactory
{
    private readonly DbSettings _settings;

    public SqlConnectionFactory(DbSettings settings)
    {
        _settings = settings;
    }

    public SqlConnection CreateOpenConnection()
    {
        var connection = new SqlConnection(_settings.ConnectionString);
        connection.Open();
        return connection;
    }
}

public sealed class DbHealthService
{
    private readonly SqlConnectionFactory _connectionFactory;

    public DbHealthService(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public string Ping()
    {
        using (var connection = _connectionFactory.CreateOpenConnection())
        using (var command = new SqlCommand("SELECT DB_NAME();", connection))
        {
            var dbName = command.ExecuteScalar();
            return dbName == null ? "N/A" : dbName.ToString();
        }
    }
}

public sealed class FamilyRecord
{
    public int? FamilyId { get; set; }
    public string FamilyName { get; set; }
    public string CategoryName { get; set; }
    public string RfaPath { get; set; }
    public string FamilyKind { get; set; }
    public string SourceModelPath { get; set; }
    public int? SourceElementTypeId { get; set; }
    public string SourceDiscipline { get; set; }
    public string PreviewPath { get; set; }
    public int? RevitVersion { get; set; }
    public string FileHash { get; set; }
    public string ApprovalStatus { get; set; }
}

public sealed class FamilyRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public FamilyRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public void EnsureExtendedSchema()
    {
        const string sql = @"
IF COL_LENGTH('dbo.Families','FamilyKind') IS NULL
    ALTER TABLE dbo.Families ADD FamilyKind NVARCHAR(16) NULL;
IF COL_LENGTH('dbo.Families','SourceModelPath') IS NULL
    ALTER TABLE dbo.Families ADD SourceModelPath NVARCHAR(1024) NULL;
IF COL_LENGTH('dbo.Families','SourceElementTypeId') IS NULL
    ALTER TABLE dbo.Families ADD SourceElementTypeId INT NULL;
IF COL_LENGTH('dbo.Families','SourceDiscipline') IS NULL
    ALTER TABLE dbo.Families ADD SourceDiscipline NVARCHAR(16) NULL;

IF OBJECT_ID(N'dbo.WebToRevitQueue', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WebToRevitQueue (
        QueueId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        FamilyId INT NOT NULL,
        Status NVARCHAR(32) NOT NULL CONSTRAINT DF_WebToRevitQueue_Status DEFAULT (N'Pending'),
        RequestedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_WebToRevitQueue_RequestedAtUtc DEFAULT (SYSUTCDATETIME()),
        ProcessedAtUtc DATETIME2 NULL,
        ErrorMessage NVARCHAR(MAX) NULL,
        CONSTRAINT FK_WebToRevitQueue_Families FOREIGN KEY (FamilyId) REFERENCES dbo.Families(FamilyId) ON DELETE CASCADE
    );
    CREATE INDEX IX_WebToRevitQueue_Status ON dbo.WebToRevitQueue(Status);
END";

        using (var connection = _connectionFactory.CreateOpenConnection())
        using (var command = new SqlCommand(sql, connection))
        {
            command.ExecuteNonQuery();
        }
    }

    public void UpsertFamily(FamilyRecord item)
    {
        const string sql = @"
IF EXISTS (SELECT 1 FROM dbo.Families WHERE RfaPath = @RfaPath)
BEGIN
    UPDATE dbo.Families
    SET FamilyName = @FamilyName,
        CategoryName = @CategoryName,
        PreviewPath = @PreviewPath,
        RevitVersion = @RevitVersion,
        FileHash = @FileHash,
        FamilyKind = @FamilyKind,
        SourceModelPath = @SourceModelPath,
        SourceElementTypeId = @SourceElementTypeId,
        SourceDiscipline = @SourceDiscipline,
        ApprovalStatus = @ApprovalStatus,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE RfaPath = @RfaPath;
END
ELSE
BEGIN
    INSERT INTO dbo.Families (FamilyName, CategoryName, RfaPath, PreviewPath, RevitVersion, FileHash, FamilyKind, SourceModelPath, SourceElementTypeId, SourceDiscipline, ApprovalStatus)
    VALUES (@FamilyName, @CategoryName, @RfaPath, @PreviewPath, @RevitVersion, @FileHash, @FamilyKind, @SourceModelPath, @SourceElementTypeId, @SourceDiscipline, @ApprovalStatus);
END";

        using (var connection = _connectionFactory.CreateOpenConnection())
        using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("@FamilyName", (object)item.FamilyName ?? "");
            command.Parameters.AddWithValue("@CategoryName", (object)item.CategoryName ?? (object)System.DBNull.Value);
            command.Parameters.AddWithValue("@RfaPath", (object)item.RfaPath ?? "");
            command.Parameters.AddWithValue("@PreviewPath", (object)item.PreviewPath ?? (object)System.DBNull.Value);
            command.Parameters.AddWithValue("@RevitVersion", (object)item.RevitVersion ?? (object)System.DBNull.Value);
            command.Parameters.AddWithValue("@FileHash", (object)item.FileHash ?? (object)System.DBNull.Value);
            command.Parameters.AddWithValue("@FamilyKind", (object)item.FamilyKind ?? (object)System.DBNull.Value);
            command.Parameters.AddWithValue("@SourceModelPath", (object)item.SourceModelPath ?? (object)System.DBNull.Value);
            command.Parameters.AddWithValue("@SourceElementTypeId", (object)item.SourceElementTypeId ?? (object)System.DBNull.Value);
            command.Parameters.AddWithValue("@SourceDiscipline", (object)item.SourceDiscipline ?? (object)System.DBNull.Value);
            command.Parameters.AddWithValue("@ApprovalStatus", (object)item.ApprovalStatus ?? "Draft");
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Rimuove tutte le righe catalogate per un file libreria, prima di un sync completo (evita record obsoleti).
    /// </summary>
    public void DeleteFamiliesForSourceModelPath(string sourceModelPath)
    {
        if (string.IsNullOrWhiteSpace(sourceModelPath))
        {
            return;
        }

        const string sql = @"DELETE FROM dbo.Families WHERE SourceModelPath = @SourceModelPath;";
        using (var connection = _connectionFactory.CreateOpenConnection())
        using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("@SourceModelPath", sourceModelPath);
            command.ExecuteNonQuery();
        }
    }

    public int? GetFamilyIdByRfaPath(string rfaPath)
    {
        if (string.IsNullOrWhiteSpace(rfaPath))
        {
            return null;
        }

        const string sql = "SELECT FamilyId FROM dbo.Families WHERE RfaPath = @RfaPath;";
        using (var connection = _connectionFactory.CreateOpenConnection())
        using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("@RfaPath", rfaPath);
            var o = command.ExecuteScalar();
            if (o == null || o == System.DBNull.Value)
            {
                return null;
            }

            return System.Convert.ToInt32(o);
        }
    }

    public FamilyRecord GetFamilyBySourceKey(string familyKind, string sourceModelPath, string familyName, int? sourceElementTypeId)
    {
        const string sql = @"
SELECT TOP 1
    FamilyId, FamilyName, CategoryName, RfaPath, FamilyKind, SourceModelPath, SourceElementTypeId, SourceDiscipline, PreviewPath, RevitVersion, FileHash, ApprovalStatus
FROM dbo.Families
WHERE FamilyKind = @FamilyKind
  AND SourceModelPath = @SourceModelPath
  AND FamilyName = @FamilyName
  AND (
      (@SourceElementTypeId IS NULL AND SourceElementTypeId IS NULL)
      OR SourceElementTypeId = @SourceElementTypeId
  );";
        using (var connection = _connectionFactory.CreateOpenConnection())
        using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("@FamilyKind", (object)familyKind ?? "");
            command.Parameters.AddWithValue("@SourceModelPath", (object)sourceModelPath ?? "");
            command.Parameters.AddWithValue("@FamilyName", (object)familyName ?? "");
            command.Parameters.AddWithValue("@SourceElementTypeId", (object)sourceElementTypeId ?? (object)System.DBNull.Value);
            using (var reader = command.ExecuteReader())
            {
                if (!reader.Read())
                {
                    return null;
                }

                return ReadFamilyRecord(reader);
            }
        }
    }

    public int DeleteFamiliesNotInSourceKeys(string sourceModelPath, IReadOnlyCollection<string> keepSourceKeys)
    {
        if (string.IsNullOrWhiteSpace(sourceModelPath))
        {
            return 0;
        }

        var keep = keepSourceKeys == null
            ? new HashSet<string>(System.StringComparer.Ordinal)
            : new HashSet<string>(keepSourceKeys, System.StringComparer.Ordinal);

        var idsToDelete = new List<int>();
        const string scanSql = @"
SELECT FamilyId, FamilyKind, FamilyName, SourceElementTypeId
FROM dbo.Families
WHERE SourceModelPath = @SourceModelPath;";
        using (var connection = _connectionFactory.CreateOpenConnection())
        {
            using (var cmd = new SqlCommand(scanSql, connection))
            {
                cmd.Parameters.AddWithValue("@SourceModelPath", sourceModelPath);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var familyId = System.Convert.ToInt32(reader["FamilyId"]);
                        var kind = reader["FamilyKind"] == System.DBNull.Value ? "" : reader["FamilyKind"].ToString();
                        var name = reader["FamilyName"] == System.DBNull.Value ? "" : reader["FamilyName"].ToString();
                        int? typeId = reader["SourceElementTypeId"] == System.DBNull.Value
                            ? null
                            : (int?)System.Convert.ToInt32(reader["SourceElementTypeId"]);

                        var key = BuildSourceKey(kind, name, typeId);
                        if (!keep.Contains(key))
                        {
                            idsToDelete.Add(familyId);
                        }
                    }
                }
            }

            if (idsToDelete.Count == 0)
            {
                return 0;
            }

            var deleted = 0;
            foreach (var batch in Batch(idsToDelete, 200))
            {
                var pNames = batch.Select((_, i) => "@Id" + i).ToArray();
                var delParamsSql = "DELETE FROM dbo.Parameters WHERE FamilyId IN (" + string.Join(",", pNames) + ");";
                var delFamiliesSql = "DELETE FROM dbo.Families WHERE FamilyId IN (" + string.Join(",", pNames) + ");";
                using (var del = new SqlCommand(delParamsSql + delFamiliesSql, connection))
                {
                    for (var i = 0; i < batch.Count; i++)
                    {
                        del.Parameters.AddWithValue("@Id" + i, batch[i]);
                    }

                    del.ExecuteNonQuery();
                    deleted += batch.Count;
                }
            }

            return deleted;
        }
    }

    private static string BuildSourceKey(string familyKind, string familyName, int? sourceElementTypeId)
    {
        return (familyKind ?? "") + "|" + (familyName ?? "") + "|" + (sourceElementTypeId.HasValue ? sourceElementTypeId.Value.ToString() : "");
    }

    private static List<List<int>> Batch(List<int> source, int size)
    {
        var result = new List<List<int>>();
        if (source == null || source.Count == 0 || size <= 0)
        {
            return result;
        }

        for (var i = 0; i < source.Count; i += size)
        {
            result.Add(source.Skip(i).Take(size).ToList());
        }

        return result;
    }

    public FamilyRecord GetFamilyByFamilyId(int familyId)
    {
        const string sql = @"
SELECT FamilyId, FamilyName, CategoryName, RfaPath, FamilyKind, SourceModelPath, SourceElementTypeId, SourceDiscipline, PreviewPath, RevitVersion, FileHash, ApprovalStatus
FROM dbo.Families WHERE FamilyId = @Id;";
        using (var connection = _connectionFactory.CreateOpenConnection())
        using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("@Id", familyId);
            using (var reader = command.ExecuteReader())
            {
                if (!reader.Read())
                {
                    return null;
                }

                return ReadFamilyRecord(reader);
            }
        }
    }

    public void ReplaceParametersForFamily(int familyId, IReadOnlyList<FamilyParameterRecord> rows)
    {
        using (var connection = _connectionFactory.CreateOpenConnection())
        {
            using (var del = new SqlCommand("DELETE FROM dbo.Parameters WHERE FamilyId = @FamilyId;", connection))
            {
                del.Parameters.AddWithValue("@FamilyId", familyId);
                del.ExecuteNonQuery();
            }

            if (rows == null || rows.Count == 0)
            {
                return;
            }

            const string ins = @"
INSERT INTO dbo.Parameters (FamilyId, FamilyTypeId, ParameterName, ParameterGroupName, StorageType, IsInstance, IsShared, StringValue)
VALUES (@FamilyId, NULL, @ParameterName, @ParameterGroupName, @StorageType, 0, 0, @StringValue);";

            foreach (var r in rows)
            {
                using (var cmd = new SqlCommand(ins, connection))
                {
                    cmd.Parameters.AddWithValue("@FamilyId", familyId);
                    cmd.Parameters.AddWithValue("@ParameterName", (object)r.ParameterName ?? "");
                    cmd.Parameters.AddWithValue("@ParameterGroupName", (object)r.ParameterGroupName ?? (object)System.DBNull.Value);
                    cmd.Parameters.AddWithValue("@StorageType", (object)r.StorageType ?? (object)System.DBNull.Value);
                    cmd.Parameters.AddWithValue("@StringValue", (object)r.StringValue ?? (object)System.DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
            }
        }
    }

    public long EnqueueWebToRevit(int familyId)
    {
        const string sql = @"
INSERT INTO dbo.WebToRevitQueue (FamilyId, Status) VALUES (@FamilyId, N'Pending');
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";
        using (var connection = _connectionFactory.CreateOpenConnection())
        using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("@FamilyId", familyId);
            var o = command.ExecuteScalar();
            return System.Convert.ToInt64(o);
        }
    }

    public List<(long QueueId, FamilyRecord Record)> GetPendingWebQueueWithFamilies(int max = 50)
    {
        var maxRows = max;
        if (maxRows < 1) maxRows = 1;
        if (maxRows > 500) maxRows = 500;
        const string sql = @"
SELECT TOP (@Max)
  q.QueueId,
  f.FamilyId, f.FamilyName, f.CategoryName, f.RfaPath, f.FamilyKind, f.SourceModelPath, f.SourceElementTypeId, f.SourceDiscipline, f.PreviewPath, f.RevitVersion, f.FileHash, f.ApprovalStatus
FROM dbo.WebToRevitQueue q
INNER JOIN dbo.Families f ON f.FamilyId = q.FamilyId
WHERE q.Status = N'Pending'
ORDER BY q.RequestedAtUtc ASC;";

        var list = new List<(long, FamilyRecord)>();
        using (var connection = _connectionFactory.CreateOpenConnection())
        using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("@Max", maxRows);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var qid = System.Convert.ToInt64(reader["QueueId"]);
                    var rec = ReadFamilyRecord(reader);
                    list.Add((qid, rec));
                }
            }
        }

        return list;
    }

    public void MarkWebQueueItem(long queueId, bool success, string errorMessage = null)
    {
        const string sql = @"
UPDATE dbo.WebToRevitQueue
SET Status = @Status,
    ProcessedAtUtc = SYSUTCDATETIME(),
    ErrorMessage = @ErrorMessage
WHERE QueueId = @QueueId;";
        using (var connection = _connectionFactory.CreateOpenConnection())
        using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("@QueueId", queueId);
            command.Parameters.AddWithValue("@Status", success ? "Done" : "Failed");
            command.Parameters.AddWithValue("@ErrorMessage", (object)errorMessage ?? (object)System.DBNull.Value);
            command.ExecuteNonQuery();
        }
    }

    public List<FamilyParameterRecord> GetParametersForFamily(int familyId)
    {
        var list = new List<FamilyParameterRecord>();
        const string sql = @"
SELECT ParameterName, ParameterGroupName, StorageType, StringValue
FROM dbo.Parameters
WHERE FamilyId = @FamilyId
ORDER BY ParameterName;";
        using (var connection = _connectionFactory.CreateOpenConnection())
        using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("@FamilyId", familyId);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    list.Add(new FamilyParameterRecord
                    {
                        ParameterName = reader["ParameterName"]?.ToString(),
                        ParameterGroupName = reader["ParameterGroupName"] == System.DBNull.Value ? null : reader["ParameterGroupName"].ToString(),
                        StorageType = reader["StorageType"] == System.DBNull.Value ? null : reader["StorageType"].ToString(),
                        StringValue = reader["StringValue"] == System.DBNull.Value ? null : reader["StringValue"].ToString()
                    });
                }
            }
        }

        return list;
    }

    public List<FamilyRecord> GetFamilies(int take = 100)
    {
        var result = new List<FamilyRecord>();
        const string sql = @"
SELECT TOP (@Take)
    FamilyId, FamilyName, CategoryName, RfaPath, FamilyKind, SourceModelPath, SourceElementTypeId, SourceDiscipline, PreviewPath, RevitVersion, FileHash, ApprovalStatus
FROM dbo.Families
ORDER BY UpdatedAtUtc DESC;";

        using (var connection = _connectionFactory.CreateOpenConnection())
        using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("@Take", take);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    result.Add(ReadFamilyRecord(reader));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Elenco per una disciplina (ARC/FUR/...), ordinato in modo stabile — non per data ultimo sync.
    /// </summary>
    /// <param name="requiredSourceModelPath">Se valorizzato, solo righe il cui SourceModelPath coincide con il file .rvt di catalogo (sync da quel modello).</param>
    public List<FamilyRecord> GetFamiliesByDiscipline(string discipline, int take, string requiredSourceModelPath = null)
    {
        var result = new List<FamilyRecord>();
        const string sql = @"
SELECT TOP (@Take)
    FamilyId, FamilyName, CategoryName, RfaPath, FamilyKind, SourceModelPath, SourceElementTypeId, SourceDiscipline, PreviewPath, RevitVersion, FileHash, ApprovalStatus
FROM dbo.Families
WHERE SourceDiscipline = @Discipline
  AND (@RequiredPath IS NULL OR SourceModelPath = @RequiredPath)
ORDER BY CategoryName, FamilyName, RfaPath;";

        using (var connection = _connectionFactory.CreateOpenConnection())
        using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("@Take", take);
            command.Parameters.AddWithValue("@Discipline", discipline);
            command.Parameters.AddWithValue("@RequiredPath", (object)requiredSourceModelPath ?? System.DBNull.Value);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    result.Add(ReadFamilyRecord(reader));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Dopo Sync ALL l'ultima disciplina aggiorna UpdatedAtUtc per ultima: TOP N per UpdatedAtUtc mostrerebbe solo FUR.
    /// Qui si prendono fino a maxTotal/2 (o /N) righe per disciplina nota, così ARC e FUR compaiono insieme.
    /// </summary>
    /// <param name="arcCatalogSourcePath">Percorso del .rvt libreria ARC (come in sync); se null non si filtra per modello.</param>
    /// <param name="furCatalogSourcePath">Percorso del .rvt libreria FUR (come in sync); se null non si filtra per modello.</param>
    public List<FamilyRecord> GetFamiliesBalancedForBrowse(int maxTotal = 500, string arcCatalogSourcePath = null, string furCatalogSourcePath = null)
    {
        var disciplines = new[] { "ARC", "FUR" };
        var per = System.Math.Max(1, maxTotal / disciplines.Length);
        var merged = new List<FamilyRecord>();
        merged.AddRange(GetFamiliesByDiscipline("ARC", per, arcCatalogSourcePath));
        merged.AddRange(GetFamiliesByDiscipline("FUR", per, furCatalogSourcePath));

        return merged
            .OrderBy(x => x.SourceDiscipline)
            .ThenBy(x => x.CategoryName)
            .ThenBy(x => x.FamilyName)
            .Take(maxTotal)
            .ToList();
    }

    private static FamilyRecord ReadFamilyRecord(SqlDataReader reader)
    {
        int? familyId = null;
        try
        {
            var ord = reader.GetOrdinal("FamilyId");
            if (!reader.IsDBNull(ord))
            {
                familyId = reader.GetInt32(ord);
            }
        }
        catch
        {
            // colonna assente in query legacy
        }

        return new FamilyRecord
        {
            FamilyId = familyId,
            FamilyName = reader["FamilyName"].ToString(),
            CategoryName = reader["CategoryName"] == System.DBNull.Value ? null : reader["CategoryName"].ToString(),
            RfaPath = reader["RfaPath"].ToString(),
            FamilyKind = reader["FamilyKind"] == System.DBNull.Value ? null : reader["FamilyKind"].ToString(),
            SourceModelPath = reader["SourceModelPath"] == System.DBNull.Value ? null : reader["SourceModelPath"].ToString(),
            SourceElementTypeId = reader["SourceElementTypeId"] == System.DBNull.Value ? null : (int?)System.Convert.ToInt32(reader["SourceElementTypeId"]),
            SourceDiscipline = reader["SourceDiscipline"] == System.DBNull.Value ? null : reader["SourceDiscipline"].ToString(),
            PreviewPath = reader["PreviewPath"] == System.DBNull.Value ? null : reader["PreviewPath"].ToString(),
            RevitVersion = reader["RevitVersion"] == System.DBNull.Value ? null : (int?)System.Convert.ToInt32(reader["RevitVersion"]),
            FileHash = reader["FileHash"] == System.DBNull.Value ? null : reader["FileHash"].ToString(),
            ApprovalStatus = reader["ApprovalStatus"] == System.DBNull.Value ? null : reader["ApprovalStatus"].ToString()
        };
    }

    public List<FamilyRecord> GetLoadableFamilies(int take = 30)
    {
        var result = new List<FamilyRecord>();
        const string sql = @"
SELECT TOP (@Take)
    FamilyName, CategoryName, RfaPath, FamilyKind, SourceModelPath, SourceElementTypeId, SourceDiscipline, PreviewPath, RevitVersion, FileHash, ApprovalStatus
FROM dbo.Families
WHERE RfaPath LIKE '%.rfa' OR FamilyKind = 'Loadable'
ORDER BY FamilyName ASC;";

        using (var connection = _connectionFactory.CreateOpenConnection())
        using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("@Take", take);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    result.Add(ReadFamilyRecord(reader));
                }
            }
        }

        return result;
    }

    public List<FamilyRecord> GetSystemFamilies(int take = 200)
    {
        var result = new List<FamilyRecord>();
        const string sql = @"
SELECT TOP (@Take)
    FamilyName, CategoryName, RfaPath, FamilyKind, SourceModelPath, SourceElementTypeId, SourceDiscipline, PreviewPath, RevitVersion, FileHash, ApprovalStatus
FROM dbo.Families
WHERE FamilyKind = 'System'
ORDER BY SourceDiscipline ASC, CategoryName ASC, FamilyName ASC;";

        using (var connection = _connectionFactory.CreateOpenConnection())
        using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("@Take", take);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    result.Add(ReadFamilyRecord(reader));
                }
            }
        }

        return result;
    }
}
