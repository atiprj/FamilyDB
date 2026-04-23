using System.Reflection;
using Microsoft.AspNetCore.StaticFiles;
using Npgsql;
using RevitFamilyDb.Core;
using System.Data;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

var app = builder.Build();

const string fallbackConn =
    "Host=127.0.0.1;Port=5432;Database=revit_family_library;Username=postgres;Password=postgres;SSL Mode=Prefer;Trust Server Certificate=true";
var fromConfig = builder.Configuration.GetConnectionString("RevitFamilyDb");
var connectionString = ConnectionStringResolver.Resolve(string.IsNullOrWhiteSpace(fromConfig) ? fallbackConn : fromConfig);

var apiKey = builder.Configuration["Inspector:ApiKey"];

app.Use(async (context, next) =>
{
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        await next();
        return;
    }

    var path = context.Request.Path.Value ?? "";
    if (!path.StartsWith("/api/queue", StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }

    if (IsAuthorized(context, apiKey))
    {
        await next();
        return;
    }

    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    context.Response.ContentType = "text/plain; charset=utf-8";
    await context.Response.WriteAsync("Richiesta API key: header Authorization: Bearer <chiave> oppure X-Api-Key.");
});

static bool IsAuthorized(HttpContext ctx, string expected)
{
    var auth = ctx.Request.Headers.Authorization.ToString();
    if (!string.IsNullOrEmpty(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        var token = auth["Bearer ".Length..].Trim();
        if (string.Equals(token, expected, StringComparison.Ordinal))
        {
            return true;
        }
    }

    if (ctx.Request.Headers.TryGetValue("X-Api-Key", out var x) &&
        string.Equals(x.ToString(), expected, StringComparison.Ordinal))
    {
        return true;
    }

    return false;
}

var staticTypes = new FileExtensionContentTypeProvider();
staticTypes.Mappings[".html"] = "text/html; charset=utf-8";
staticTypes.Mappings[".js"] = "application/javascript; charset=utf-8";
staticTypes.Mappings[".css"] = "text/css; charset=utf-8";

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = staticTypes });

var asm = Assembly.GetExecutingAssembly();
var informational = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? "?";

app.MapGet("/api/version", () => Results.Ok(new
{
    product = "RevitFamilyDb.Inspector",
    version = informational,
    framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
}));

app.MapGet("/api/config", () => Results.Ok(new
{
    queueApiKeyRequired = !string.IsNullOrWhiteSpace(apiKey)
}));

app.MapGet("/api/health", async () =>
{
    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();
    await using var cmd = new NpgsqlCommand("SELECT current_database();", conn);
    var dbName = Convert.ToString(await cmd.ExecuteScalarAsync()) ?? "N/A";
    return Results.Ok(new { ok = true, dbName });
});

app.MapGet("/api/tables", async () =>
{
    const string sql = @"
SELECT table_schema || '.' || table_name AS table_name
FROM INFORMATION_SCHEMA.TABLES
WHERE table_type='BASE TABLE'
  AND table_schema = 'app'
ORDER BY table_schema, table_name;";

    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();
    await using var cmd = new NpgsqlCommand(sql, conn);
    await using var reader = await cmd.ExecuteReaderAsync();

    var rows = new List<string>();
    while (await reader.ReadAsync())
    {
        rows.Add(reader.GetString(0));
    }

    return Results.Ok(rows);
});

app.MapGet("/api/disciplines", async () =>
{
    const string sql = @"
SELECT
  CASE
    WHEN source_discipline IS NULL OR btrim(source_discipline) = '' THEN
      CASE
        WHEN source_model_path LIKE '%\ARC\%' THEN 'ARC'
        WHEN source_model_path LIKE '%\FUR\%' THEN 'FUR'
        ELSE '(Senza disciplina)'
      END
    ELSE UPPER(btrim(source_discipline))
  END AS discipline,
  COUNT(*) AS total_rows
FROM app.families
GROUP BY
  CASE
    WHEN source_discipline IS NULL OR btrim(source_discipline) = '' THEN
      CASE
        WHEN source_model_path LIKE '%\ARC\%' THEN 'ARC'
        WHEN source_model_path LIKE '%\FUR\%' THEN 'FUR'
        ELSE '(Senza disciplina)'
      END
    ELSE UPPER(btrim(source_discipline))
  END
ORDER BY discipline;";

    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();
    await using var cmd = new NpgsqlCommand(sql, conn);
    await using var reader = await cmd.ExecuteReaderAsync();

    var rows = new List<object>();
    while (await reader.ReadAsync())
    {
        rows.Add(new
        {
            discipline = reader["discipline"] as string ?? "(Senza disciplina)",
            rowCount = Convert.ToInt32(reader["total_rows"])
        });
    }

    return Results.Ok(rows);
});

app.MapGet("/api/families", async (string? discipline, string? kind, string? category, string? q, int? take) =>
{
    var max = take ?? 300;
    if (max < 1) max = 1;
    if (max > 2000) max = 2000;

    const string sql = @"
SELECT
  family_id, family_name, category_name, family_kind, source_discipline, source_model_path, rfa_path, preview_path, source_element_type_id, updated_at_utc
FROM app.families
WHERE (@Discipline IS NULL OR source_discipline = @Discipline)
  AND (@Kind IS NULL OR family_kind = @Kind)
  AND (@Category IS NULL OR category_name = @Category)
  AND (
       @Q IS NULL
       OR family_name ILIKE '%' || @Q || '%'
       OR category_name ILIKE '%' || @Q || '%'
       OR source_model_path ILIKE '%' || @Q || '%'
      )
ORDER BY source_discipline, category_name, family_name
LIMIT @Take;";

    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();
    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("@Take", max);
    cmd.Parameters.AddWithValue("@Discipline", string.IsNullOrWhiteSpace(discipline) ? DBNull.Value : discipline);
    cmd.Parameters.AddWithValue("@Kind", string.IsNullOrWhiteSpace(kind) ? DBNull.Value : kind);
    cmd.Parameters.AddWithValue("@Category", string.IsNullOrWhiteSpace(category) ? DBNull.Value : category);
    cmd.Parameters.AddWithValue("@Q", string.IsNullOrWhiteSpace(q) ? DBNull.Value : q);

    await using var reader = await cmd.ExecuteReaderAsync();
    var rows = new List<object>();
    while (await reader.ReadAsync())
    {
        rows.Add(new
        {
            familyId = reader["family_id"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["family_id"]),
            familyName = reader["family_name"] as string,
            categoryName = reader["category_name"] as string,
            familyKind = reader["family_kind"] as string,
            sourceDiscipline = reader["source_discipline"] as string,
            sourceModelPath = reader["source_model_path"] as string,
            rfaPath = reader["rfa_path"] as string,
            previewPath = reader["preview_path"] as string,
            sourceElementTypeId = reader["source_element_type_id"] == DBNull.Value ? null : (int?)Convert.ToInt32(reader["source_element_type_id"]),
            updatedAtUtc = reader["updated_at_utc"] == DBNull.Value ? null : (DateTime?)Convert.ToDateTime(reader["updated_at_utc"])
        });
    }

    return Results.Ok(rows);
});

app.MapGet("/api/family/{familyId:int}", async (int familyId) =>
{
    const string sql = @"
SELECT family_id, family_name, category_name, family_kind, source_discipline, source_model_path, rfa_path, preview_path, source_element_type_id, revit_version, approval_status, updated_at_utc
FROM app.families WHERE family_id = @Id;";
    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();
    object? fam;
    await using (var cmd = new NpgsqlCommand(sql, conn))
    {
        cmd.Parameters.AddWithValue("@Id", familyId);
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow);
        if (!await reader.ReadAsync())
        {
            return Results.NotFound();
        }

        fam = new
        {
            familyId = Convert.ToInt32(reader["family_id"]),
            familyName = reader["family_name"] as string,
            categoryName = reader["category_name"] as string,
            familyKind = reader["family_kind"] as string,
            sourceDiscipline = reader["source_discipline"] as string,
            sourceModelPath = reader["source_model_path"] as string,
            rfaPath = reader["rfa_path"] as string,
            previewPath = reader["preview_path"] as string,
            sourceElementTypeId = reader["source_element_type_id"] == DBNull.Value ? null : (int?)Convert.ToInt32(reader["source_element_type_id"]),
            revitVersion = reader["revit_version"] == DBNull.Value ? null : (int?)Convert.ToInt32(reader["revit_version"]),
            approvalStatus = reader["approval_status"] as string,
            updatedAtUtc = reader["updated_at_utc"] == DBNull.Value ? null : (DateTime?)Convert.ToDateTime(reader["updated_at_utc"])
        };
    }

    var parameters = new List<object>();
    const string psql = @"
SELECT parameter_name, parameter_group_name, storage_type, string_value
FROM app.parameters WHERE family_id = @Fid ORDER BY parameter_name;";
    await using (var pcmd = new NpgsqlCommand(psql, conn))
    {
        pcmd.Parameters.AddWithValue("@Fid", familyId);
        await using var pr = await pcmd.ExecuteReaderAsync();
        while (await pr.ReadAsync())
        {
            parameters.Add(new
            {
                parameterName = pr["parameter_name"] as string,
                parameterGroupName = pr["parameter_group_name"] as string,
                storageType = pr["storage_type"] as string,
                stringValue = pr["string_value"] as string
            });
        }
    }

    return Results.Ok(new { family = fam, parameters });
});

app.MapPost("/api/queue/enqueue", async (HttpRequest req) =>
{
    EnqueueDto? body;
    try
    {
        body = await JsonSerializer.DeserializeAsync<EnqueueDto>(req.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
    catch
    {
        return Results.BadRequest("JSON non valido");
    }

    if (body == null || body.FamilyId <= 0)
    {
        return Results.BadRequest("familyId richiesto");
    }

    const string ins = @"
INSERT INTO app.web_to_revit_queue (family_id, status) VALUES (@FamilyId, 'Pending')
RETURNING queue_id;";
    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();
    await using var cmd = new NpgsqlCommand(ins, conn);
    cmd.Parameters.AddWithValue("@FamilyId", body.FamilyId);
    object? o;
    try
    {
        o = await cmd.ExecuteScalarAsync();
    }
    catch (NpgsqlException ex)
    {
        return Results.BadRequest("Impossibile accodare (tabella WebToRevitQueue o FK mancante). Esegui Sync da Revit dopo aggiornamento add-in. Dettaglio: " + ex.Message);
    }

    var qid = Convert.ToInt64(o ?? 0);
    return Results.Ok(new { queueId = qid, message = "Richiesta accodata. In Revit usa il comando 'Applica coda Web → progetto'." });
});

app.MapGet("/api/queue/pending-count", async () =>
{
    const string sql = "SELECT COUNT(*) FROM app.web_to_revit_queue WHERE status = 'Pending';";
    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();
    await using var cmd = new NpgsqlCommand(sql, conn);
    object? c;
    try
    {
        c = await cmd.ExecuteScalarAsync();
    }
    catch
    {
        return Results.Ok(new { pending = 0, note = "Tabella coda non presente" });
    }

    return Results.Ok(new { pending = Convert.ToInt32(c ?? 0) });
});

app.MapGet("/api/preview", (string? path) =>
{
    if (string.IsNullOrWhiteSpace(path))
    {
        return Results.NotFound();
    }

    try
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
        {
            return Results.NotFound();
        }

        var ext = Path.GetExtension(full).ToLowerInvariant();
        var mime = ext switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };

        return Results.File(full, mime);
    }
    catch
    {
        return Results.NotFound();
    }
});

app.MapGet("/api/quality", async () =>
{
    const string sql = @"
SELECT
  SUM(CASE WHEN family_kind = 'Loadable' AND (rfa_path NOT LIKE '%.rfa' OR rfa_path IS NULL) THEN 1 ELSE 0 END) AS loadable_without_real_rfa,
  SUM(CASE WHEN family_kind = 'System' AND source_element_type_id IS NULL THEN 1 ELSE 0 END) AS system_without_type_id,
  SUM(CASE WHEN source_model_path IS NULL OR btrim(source_model_path) = '' THEN 1 ELSE 0 END) AS missing_source_model_path,
  SUM(CASE WHEN preview_path IS NULL OR btrim(preview_path) = '' THEN 1 ELSE 0 END) AS missing_preview_path
FROM app.families;";

    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();
    await using var cmd = new NpgsqlCommand(sql, conn);
    await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow);
    await reader.ReadAsync();
    return Results.Ok(new
    {
        loadableWithoutRealRfa = Convert.ToInt32(reader["loadable_without_real_rfa"]),
        systemWithoutTypeId = Convert.ToInt32(reader["system_without_type_id"]),
        missingSourceModelPath = Convert.ToInt32(reader["missing_source_model_path"]),
        missingPreviewPath = Convert.ToInt32(reader["missing_preview_path"])
    });
});

// Espressione disciplina effettiva (allineata a GET /api/disciplines)
const string SqlEffectiveDiscipline = """
CASE
  WHEN source_discipline IS NULL OR btrim(source_discipline) = '' THEN
    CASE
      WHEN source_model_path LIKE '%\ARC\%' THEN 'ARC'
      WHEN source_model_path LIKE '%\FUR\%' THEN 'FUR'
      ELSE '(Senza disciplina)'
    END
  ELSE UPPER(btrim(source_discipline))
END
""";

const string SqlNormCategory = @"coalesce(nullif(btrim(category_name), ''), '(Senza categoria)')";

/// <summary>Conteggi per grafico a torta (categorie famiglia). Opzionale filtro disciplina.</summary>
app.MapGet("/api/report/category-counts", async (string? discipline) =>
{
    var sql = $"""
SELECT
  {SqlNormCategory} AS CategoryName,
  COUNT(*) AS Cnt
FROM app.families
WHERE (@Discipline IS NULL OR ({SqlEffectiveDiscipline}) = @Discipline)
GROUP BY {SqlNormCategory}
ORDER BY Cnt DESC, CategoryName;
""";

    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();
    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("@Discipline", string.IsNullOrWhiteSpace(discipline) ? DBNull.Value : discipline);
    await using var reader = await cmd.ExecuteReaderAsync();
    var rows = new List<object>();
    while (await reader.ReadAsync())
    {
        rows.Add(new
        {
            categoryName = reader["categoryname"] as string ?? "(Senza categoria)",
            count = Convert.ToInt32(reader["cnt"])
        });
    }

    return Results.Ok(rows);
});

/// <summary>
/// Elenco compatto per Report Status. Filtri: disciplina effettiva, categoria esatta o __ALTRO__ (categorie oltre top N).
/// </summary>
app.MapGet("/api/report/families-slim", async (int? take, string? discipline, string? category, int? topN) =>
{
    var max = take ?? 800;
    if (max < 1) max = 1;
    if (max > 5000) max = 5000;

    var tn = topN ?? 10;
    if (tn < 1) tn = 1;
    if (tn > 100) tn = 100;

    var hasCat = !string.IsNullOrWhiteSpace(category);
    var isAltro = hasCat && string.Equals(category, "__ALTRO__", StringComparison.Ordinal);

    // CTE: stesse regole di /api/disciplines per EffDisc e NormCat
    var sql = isAltro
        ? $"""
;WITH base AS (
  SELECT
    family_id,
    family_name,
    category_name,
    family_kind,
    preview_path,
    {SqlNormCategory} AS NormCat,
    ({SqlEffectiveDiscipline}) AS EffDisc
  FROM app.families
),
fil AS (
  SELECT * FROM base
  WHERE (@Discipline IS NULL OR EffDisc = @Discipline)
),
rnk AS (
  SELECT
    NormCat,
    ROW_NUMBER() OVER (ORDER BY cnt DESC, NormCat) AS rk
  FROM (
    SELECT NormCat, COUNT(*) AS cnt
    FROM fil
    GROUP BY NormCat
  ) c
)
SELECT
  f.family_id,
  f.family_name,
  f.category_name,
  f.family_kind,
  f.preview_path
FROM fil f
INNER JOIN rnk ON rnk.NormCat = f.NormCat AND rnk.rk > @TopN
ORDER BY f.NormCat, f.family_name
LIMIT @Take;
"""
        : $"""
SELECT
  family_id,
  family_name,
  category_name,
  family_kind,
  preview_path
FROM app.families
WHERE (@Discipline IS NULL OR ({SqlEffectiveDiscipline}) = @Discipline)
  AND (@Category IS NULL OR {SqlNormCategory} = @Category)
ORDER BY category_name, family_name
LIMIT @Take;
""";

    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();
    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("@Take", max);
    cmd.Parameters.AddWithValue("@Discipline", string.IsNullOrWhiteSpace(discipline) ? DBNull.Value : discipline);
    cmd.Parameters.AddWithValue("@Category", !hasCat || isAltro ? DBNull.Value : (object)category!);
    cmd.Parameters.AddWithValue("@TopN", tn);
    await using var reader = await cmd.ExecuteReaderAsync();
    var rows = new List<object>();
    while (await reader.ReadAsync())
    {
        rows.Add(new
        {
            familyId = reader["family_id"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["family_id"]),
            familyName = reader["family_name"] as string,
            categoryName = reader["category_name"] as string,
            familyKind = reader["family_kind"] as string,
            previewPath = reader["preview_path"] as string
        });
    }

    return Results.Ok(rows);
});

app.Run();

internal sealed class EnqueueDto
{
    public int FamilyId { get; set; }
}
