using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitFamilyDb.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace RevitFamilyDb.Addin2025;

public class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        const string tabName = "Family DB";
        try { application.CreateRibbonTab(tabName); } catch { }
        var panel = application.CreateRibbonPanel(tabName, "Database");
        var tools = panel.AddItem(new PulldownButtonData("FamilyDbTools2025", "DB")) as PulldownButton;
        if (tools != null)
        {
            var iconPath = Path.Combine(Path.GetDirectoryName(typeof(App).Assembly.Location) ?? "", "dbcloud.png");
            var icon = LoadRibbonIconFromFile(iconPath);
            if (icon != null)
            {
                tools.Image = icon;
                tools.LargeImage = icon;
            }
            tools.AddPushButton(new PushButtonData("TestDb2025", "Test DB", typeof(App).Assembly.Location, typeof(TestDbConnectionCommand).FullName));
            tools.AddPushButton(new PushButtonData("SyncArc2025", "Sync ARC", typeof(App).Assembly.Location, typeof(SyncArcCommand).FullName));
            tools.AddPushButton(new PushButtonData("SyncFur2025", "Sync FUR", typeof(App).Assembly.Location, typeof(SyncFurCommand).FullName));
            tools.AddPushButton(new PushButtonData("SyncAll2025", "Sync ALL", typeof(App).Assembly.Location, typeof(SyncAllCommand).FullName));
            tools.AddPushButton(new PushButtonData("PushLib2025", "Push libreria → DB", typeof(App).Assembly.Location, typeof(PushLibraryToDbCommand).FullName));
            tools.AddPushButton(new PushButtonData("ApplyWebQ2025", "Applica coda Web → progetto", typeof(App).Assembly.Location, typeof(ApplyWebQueueCommand).FullName));
            tools.AddPushButton(new PushButtonData("ListDb2025", "Elenco + Carica", typeof(App).Assembly.Location, typeof(ListFamiliesVisualCommand).FullName));
        }
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;

    private static System.Windows.Media.ImageSource LoadRibbonIconFromFile(string absolutePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
            {
                return null;
            }

            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(absolutePath, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}

[Transaction(TransactionMode.Manual)]
public class TestDbConnectionCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var repo = DbContextFactory.CreateRepository();
            repo.EnsureExtendedSchema();
            var dbName = new DbHealthService(new SqlConnectionFactory(new DbSettings(DbContextFactory.ConnectionString))).Ping();
            TaskDialog.Show("Family DB", "Connessione OK. Database: " + dbName);
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("Family DB", "Connessione fallita:\n" + ex.Message);
            return Result.Failed;
        }
    }
}

[Transaction(TransactionMode.Manual)]
public class SyncArcCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => LibrarySync.SyncDiscipline(c, "ARC", ref m);
}

[Transaction(TransactionMode.Manual)]
public class SyncFurCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => LibrarySync.SyncDiscipline(c, "FUR", ref m);
}

[Transaction(TransactionMode.Manual)]
public class SyncAllCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => LibrarySync.SyncAll(c, ref m);
}

/// <summary>
/// Stesso flusso di Sync ALL (modello libreria ARC/FUR → SQL + anteprime + export .rfa + parametri). Nome esplicito per il team.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class PushLibraryToDbCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => LibrarySync.SyncAll(c, ref m);
}

/// <summary>
/// Elabora la coda creata dalla web app: carica nel progetto attivo le famiglie richieste.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class ApplyWebQueueCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var repo = DbContextFactory.CreateRepository();
            repo.EnsureExtendedSchema();
            var pending = repo.GetPendingWebQueueWithFamilies(50);
            if (pending.Count == 0)
            {
                TaskDialog.Show("Family DB", "Nessuna richiesta in coda dalla web app.");
                return Result.Succeeded;
            }

            var ok = 0;
            var failed = 0;
            foreach (var (queueId, rec) in pending)
            {
                var localMsg = "";
                var list = new List<FamilyRecord> { rec };
                var r = ListFamiliesVisualCommand.LoadFromRecords(commandData, list, ref localMsg);
                if (r == Result.Succeeded)
                {
                    repo.MarkWebQueueItem(queueId, true, null);
                    ok++;
                }
                else
                {
                    repo.MarkWebQueueItem(queueId, false, string.IsNullOrWhiteSpace(localMsg) ? "Operazione non riuscita" : localMsg);
                    failed++;
                }
            }

            RevitFamilyDbLog.Info($"ApplyWebQueue: completate={ok}, fallite={failed}");
            TaskDialog.Show(
                "Family DB",
                "Coda Web elaborata.\nCompletate: " + ok + "\nFallite: " + failed);
            return failed == 0 ? Result.Succeeded : Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("Family DB", "Applica coda fallita:\n" + ex.Message);
            return Result.Failed;
        }
    }
}

[Transaction(TransactionMode.Manual)]
public class ListFamiliesVisualCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var repo = DbContextFactory.CreateRepository();
            repo.EnsureExtendedSchema();
            var rows = repo.GetFamiliesBalancedForBrowse(500, LibrarySync.CatalogArcModelPath, LibrarySync.CatalogFurModelPath);
            var targetDoc = commandData.Application.ActiveUIDocument.Document;
            var displayRows = rows;
            if (!LibrarySync.IsLibraryModelDocument(targetDoc))
            {
                displayRows = rows.Where(r => !IsAlreadyInActiveDocument(targetDoc, r)).ToList();
            }

            if (displayRows.Count == 0)
            {
                TaskDialog.Show(
                    "Family DB",
                    "Nessun elemento da mostrare: il catalogo e' vuoto oppure tutte le voci risultano gia' presenti nel progetto attivo.");
                return Result.Cancelled;
            }

            var selected = ShowBrowser(displayRows, commandData.Application);
            if (selected == null || selected.Count == 0)
            {
                return Result.Cancelled;
            }

            return LoadFromRecords(commandData, selected, ref message);
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("Family DB", "Elenco fallito:\n" + ex.Message);
            return Result.Failed;
        }
    }

    private static List<FamilyRecord> ShowBrowser(List<FamilyRecord> rows, UIApplication uiapp)
    {
        var app = uiapp.Application;
        List<FamilyRecord> picked = null;
        var docCache = OpenSourceDocuments(rows, app);
        try
        {
            using (var form = new System.Windows.Forms.Form())
            {
                form.Text = "Family DB - Elenco e Caricamento (doppio click = Proprieta tipo; checkbox = elementi da caricare)";
                form.Width = 1300;
                form.Height = 760;
                form.BackColor = System.Drawing.Color.FromArgb(26, 26, 26);
                form.ForeColor = System.Drawing.Color.White;
                form.Font = new Font("Calibri", 10f, FontStyle.Regular);
                var grid = new DataGridView
                {
                    Dock = DockStyle.Fill,
                    ReadOnly = false,
                    AutoGenerateColumns = false,
                    AllowUserToAddRows = false,
                    SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                    MultiSelect = false,
                    RowTemplate = { Height = 56 }
                };
                grid.RowHeadersVisible = false;
                grid.EditMode = DataGridViewEditMode.EditOnEnter;
                grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
                grid.BackgroundColor = System.Drawing.Color.FromArgb(26, 26, 26);
                grid.BorderStyle = BorderStyle.None;
                grid.EnableHeadersVisualStyles = false;
                grid.ColumnHeadersDefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(47, 47, 47);
                grid.ColumnHeadersDefaultCellStyle.ForeColor = System.Drawing.Color.White;
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = System.Drawing.Color.FromArgb(47, 47, 47);
                grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = System.Drawing.Color.White;
                grid.DefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(34, 34, 34);
                grid.DefaultCellStyle.ForeColor = System.Drawing.Color.White;
                grid.DefaultCellStyle.SelectionBackColor = System.Drawing.Color.FromArgb(31, 83, 141);
                grid.DefaultCellStyle.SelectionForeColor = System.Drawing.Color.White;
                grid.DefaultCellStyle.Padding = new Padding(2);
                grid.GridColor = System.Drawing.Color.FromArgb(64, 64, 64);
                var selectCol = new DataGridViewCheckBoxColumn
                {
                    HeaderText = "Sel",
                    Width = 52,
                    DataPropertyName = "IsSelected",
                    ReadOnly = false
                };
                selectCol.Frozen = true;
                grid.Columns.Add(selectCol);
                var previewCol = new DataGridViewImageColumn
                {
                    HeaderText = "Preview",
                    Width = 64,
                    DataPropertyName = "Preview",
                    // Normal = immagine a dimensione nativa → ritaglio in cella piccola; Zoom = scala tutta l'anteprima nella cella
                    ImageLayout = DataGridViewImageCellLayout.Zoom
                };
                previewCol.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                previewCol.DefaultCellStyle.Padding = new Padding(2);
                grid.Columns.Add(previewCol);
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tipo", Width = 50, DataPropertyName = "KindShort", ReadOnly = true });
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Famiglia", Width = 340, DataPropertyName = "FamilyName", ReadOnly = true });
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Categoria", Width = 220, DataPropertyName = "CategoryName", ReadOnly = true });
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Disciplina", Width = 80, DataPropertyName = "SourceDiscipline", ReadOnly = true });
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Sorgente", Width = 340, DataPropertyName = "SourceModelPath", ReadOnly = true });

                var allRows = rows.Select(r => new BrowserRow
                {
                    Record = r,
                    Preview = BuildPreview(r, docCache),
                    KindShort = string.Equals(r.FamilyKind, "System", StringComparison.OrdinalIgnoreCase) ? "S" : "L",
                    FamilyName = r.FamilyName,
                    CategoryName = r.CategoryName,
                    SourceDiscipline = r.SourceDiscipline,
                    SourceModelPath = r.SourceModelPath
                }).ToList();
                Func<string, string> norm = s => (s ?? "").Trim().ToLowerInvariant();
                Action<string> applyFilter = search =>
                {
                    var q = norm(search);
                    var filtered = string.IsNullOrWhiteSpace(q)
                        ? allRows
                        : allRows.Where(x =>
                            norm(x.FamilyName).Contains(q)
                            || norm(x.CategoryName).Contains(q)
                            || norm(x.SourceDiscipline).Contains(q)
                            || norm(x.SourceModelPath).Contains(q)
                        ).ToList();
                    grid.DataSource = filtered;
                };

                applyFilter(null);

                grid.CellDoubleClick += (_, e) =>
                {
                    if (e.RowIndex < 0)
                    {
                        return;
                    }

                    if (grid.Rows[e.RowIndex].DataBoundItem is not BrowserRow br)
                    {
                        return;
                    }

                    form.Hide();
                    try
                    {
                        ShowFamilyDetailDialog(br.Record);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            "Dettaglio famiglia: " + ex.Message,
                            "Family DB",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                    finally
                    {
                        form.Show();
                        form.Activate();
                    }
                };
                grid.CurrentCellDirtyStateChanged += (_, __) =>
                {
                    if (grid.IsCurrentCellDirty)
                    {
                        grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                    }
                };

                var top = new FlowLayoutPanel
                {
                    Dock = DockStyle.Top,
                    Height = 40,
                    FlowDirection = FlowDirection.LeftToRight,
                    WrapContents = false,
                    BackColor = System.Drawing.Color.FromArgb(26, 26, 26)
                };
                top.Controls.Add(new Label
                {
                    Text = "Cerca:",
                    AutoSize = true,
                    Padding = new Padding(0, 10, 4, 0),
                    ForeColor = System.Drawing.Color.White
                });
                var searchBox = new System.Windows.Forms.TextBox { Width = 340 };
                searchBox.BackColor = System.Drawing.Color.FromArgb(47, 47, 47);
                searchBox.ForeColor = System.Drawing.Color.White;
                searchBox.BorderStyle = BorderStyle.FixedSingle;
                searchBox.TextChanged += (_, __) => applyFilter(searchBox.Text);
                top.Controls.Add(new Label
                {
                    Text = "O",
                    AutoSize = true,
                    ForeColor = System.Drawing.Color.White,
                    Font = new Font("Calibri", 10f, FontStyle.Bold),
                    Padding = new Padding(2, 9, 0, 0)
                });
                top.Controls.Add(new Label
                {
                    Text = "/",
                    AutoSize = true,
                    ForeColor = System.Drawing.Color.White,
                    Font = new Font("Calibri", 10f, FontStyle.Bold),
                    Padding = new Padding(0, 11, 6, 0)
                });
                top.Controls.Add(searchBox);
                var selectFilteredBtn = new Button { Text = "Seleziona tutti (filtrati)", Width = 170, Height = 28 };
                selectFilteredBtn.Click += (_, __) =>
                {
                    if (grid.DataSource is List<BrowserRow> visibleRows)
                    {
                        foreach (var r in visibleRows)
                        {
                            r.IsSelected = true;
                        }
                        grid.Refresh();
                    }
                };
                top.Controls.Add(selectFilteredBtn);
                var deselectFilteredBtn = new Button { Text = "Deseleziona tutti", Width = 140, Height = 28 };
                deselectFilteredBtn.Click += (_, __) =>
                {
                    if (grid.DataSource is List<BrowserRow> visibleRows)
                    {
                        foreach (var r in visibleRows)
                        {
                            r.IsSelected = false;
                        }
                        grid.Refresh();
                    }
                };
                top.Controls.Add(deselectFilteredBtn);
                var closeTopBtn = new Button { Text = "×", Width = 28, Height = 28 };
                closeTopBtn.Click += (_, __) =>
                {
                    form.DialogResult = DialogResult.Cancel;
                    form.Close();
                };
                top.Controls.Add(closeTopBtn);

                Action<Button, System.Drawing.Color> styleBtn = (b, bg) =>
                {
                    b.FlatStyle = FlatStyle.Flat;
                    b.FlatAppearance.BorderSize = 0;
                    b.BackColor = bg;
                    b.ForeColor = System.Drawing.Color.White;
                    b.Font = new Font("Calibri", 10f, FontStyle.Bold);
                    b.Cursor = Cursors.Hand;
                };
                styleBtn(selectFilteredBtn, System.Drawing.Color.FromArgb(31, 83, 141));
                styleBtn(deselectFilteredBtn, System.Drawing.Color.FromArgb(68, 68, 68));
                styleBtn(closeTopBtn, System.Drawing.Color.FromArgb(31, 83, 141));
                closeTopBtn.Font = new Font("Calibri", 12f, FontStyle.Bold);
                closeTopBtn.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(168, 203, 226);
                closeTopBtn.ForeColor = System.Drawing.Color.Black;

                var bottom = new FlowLayoutPanel
                {
                    Dock = DockStyle.Bottom,
                    Height = 46,
                    FlowDirection = FlowDirection.RightToLeft,
                    BackColor = System.Drawing.Color.FromArgb(26, 26, 26)
                };
                var loadBtn = new Button { Text = "Carica selezionate", Width = 170, Height = 30 };
                var closeBtn = new Button { Text = "Chiudi", Width = 90, Height = 30 };
                styleBtn(loadBtn, System.Drawing.Color.FromArgb(31, 83, 141));
                styleBtn(closeBtn, System.Drawing.Color.FromArgb(68, 68, 68));
                loadBtn.Click += (_, __) =>
                {
                    var list = new List<FamilyRecord>();
                    foreach (var br in allRows.Where(x => x.IsSelected))
                    {
                        list.Add(br.Record);
                    }

                    if (list.Count == 0)
                    {
                        MessageBox.Show(
                            "Seleziona almeno una famiglia con la checkbox 'Sel'.",
                            "Family DB",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        return;
                    }

                    picked = list;
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                };
                closeBtn.Click += (_, __) => { form.DialogResult = DialogResult.Cancel; form.Close(); };
                bottom.Controls.Add(loadBtn);
                bottom.Controls.Add(closeBtn);

                form.Controls.Add(grid);
                form.Controls.Add(top);
                form.Controls.Add(bottom);
                form.StartPosition = FormStartPosition.CenterScreen;
                form.ShowDialog();
            }
        }
        finally
        {
            foreach (var kv in docCache)
            {
                kv.Value.Close(false);
            }
        }

        return picked;
    }

    /// <summary>
    /// Apre un dettaglio famiglia con metadati e parametri (stile web app), senza aprire documenti libreria.
    /// </summary>
    private static void ShowFamilyDetailDialog(FamilyRecord rec)
    {
        var repo = DbContextFactory.CreateRepository();
        repo.EnsureExtendedSchema();
        var familyId = rec.FamilyId ?? repo.GetFamilyIdByRfaPath(rec.RfaPath);
        var parameters = familyId.HasValue ? repo.GetParametersForFamily(familyId.Value) : new List<FamilyParameterRecord>();

        using (var dlg = new System.Windows.Forms.Form())
        {
            dlg.Text = "Dettaglio famiglia";
            dlg.Width = 980;
            dlg.Height = 680;
            dlg.StartPosition = FormStartPosition.CenterScreen;
            dlg.BackColor = System.Drawing.Color.FromArgb(26, 26, 26);
            dlg.ForeColor = System.Drawing.Color.White;
            dlg.Font = new Font("Calibri", 10f);

            var top = new System.Windows.Forms.Panel { Dock = DockStyle.Top, Height = 170, Padding = new Padding(12), BackColor = System.Drawing.Color.FromArgb(34, 34, 34) };
            var info = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                ForeColor = System.Drawing.Color.White,
                Text = "FamilyId: " + (familyId.HasValue ? familyId.Value.ToString() : "N/A")
                       + "\nFamiglia: " + (rec.FamilyName ?? "")
                       + "\nCategoria: " + (rec.CategoryName ?? "")
                       + "\nDisciplina: " + (rec.SourceDiscipline ?? "")
                       + "\nTipo: " + (rec.FamilyKind ?? "")
                       + "\nRfaPath: " + (rec.RfaPath ?? "")
                       + "\nSourceModelPath: " + (rec.SourceModelPath ?? "")
                       + "\nTypeId: " + (rec.SourceElementTypeId.HasValue ? rec.SourceElementTypeId.Value.ToString() : "")
            };
            top.Controls.Add(info);

            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                BackgroundColor = System.Drawing.Color.FromArgb(26, 26, 26),
                BorderStyle = BorderStyle.None,
                EnableHeadersVisualStyles = false
            };
            grid.ColumnHeadersDefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(47, 47, 47);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = System.Drawing.Color.White;
            grid.DefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(34, 34, 34);
            grid.DefaultCellStyle.ForeColor = System.Drawing.Color.White;
            grid.GridColor = System.Drawing.Color.FromArgb(64, 64, 64);
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Parametro", Width = 260, DataPropertyName = "ParameterName" });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Gruppo", Width = 220, DataPropertyName = "ParameterGroupName" });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tipo", Width = 140, DataPropertyName = "StorageType" });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Valore", Width = 320, DataPropertyName = "StringValue", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            grid.DataSource = parameters;

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, BackColor = System.Drawing.Color.FromArgb(26, 26, 26) };
            var close = new Button { Text = "Chiudi", Width = 100, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = System.Drawing.Color.FromArgb(68, 68, 68), ForeColor = System.Drawing.Color.White };
            close.FlatAppearance.BorderSize = 0;
            close.Click += (_, __) => dlg.Close();
            bottom.Controls.Add(close);

            dlg.Controls.Add(grid);
            dlg.Controls.Add(top);
            dlg.Controls.Add(bottom);
            dlg.ShowDialog();
        }
    }

    /// <summary>
    /// True se nel documento attivo esiste gia' una famiglia loadable o un tipo sistema corrispondente al record.
    /// </summary>
    private static bool IsAlreadyInActiveDocument(Document doc, FamilyRecord r)
    {
        if (string.Equals(r.FamilyKind, "System", StringComparison.OrdinalIgnoreCase))
        {
            var targetName = (r.FamilyName ?? "").Trim();
            if (string.IsNullOrEmpty(targetName))
            {
                return false;
            }

            foreach (var et in new FilteredElementCollector(doc).WhereElementIsElementType().Cast<ElementType>())
            {
                if (et.Category == null)
                {
                    continue;
                }

                if (!string.Equals(et.Category.Name, r.CategoryName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var display = string.IsNullOrWhiteSpace(et.FamilyName) ? et.Name : et.FamilyName + " : " + et.Name;
                if (string.Equals(display, targetName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        var baseName = (r.FamilyName ?? "").Split(':')[0].Trim();
        if (string.IsNullOrEmpty(baseName))
        {
            return false;
        }

        return new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>()
            .Any(f => string.Equals(f.Name, baseName, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, Document> OpenSourceDocuments(List<FamilyRecord> rows, Autodesk.Revit.ApplicationServices.Application app)
    {
        var docs = new Dictionary<string, Document>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in rows.Select(x => x.SourceModelPath).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
        {
            if (File.Exists(path))
            {
                docs[path] = app.OpenDocumentFile(path);
            }
        }
        return docs;
    }

    private static readonly System.Drawing.Size[] PreviewSizes =
    {
        new System.Drawing.Size(128, 128),
        new System.Drawing.Size(96, 96),
        new System.Drawing.Size(64, 64),
        new System.Drawing.Size(48, 48),
        new System.Drawing.Size(32, 32)
    };

    /// <summary>Dimensione massima lato dell'anteprima ridimensionata (coerente con altezza riga griglia).</summary>
    private const int PreviewThumbPixelSize = 52;

    private static Bitmap CloneBitmap(Image source)
    {
        if (source == null)
        {
            return null;
        }

        var bmp = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        try
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.DrawImage(source, 0, 0);
            }
        }
        finally
        {
            source.Dispose();
        }

        return bmp;
    }

    /// <summary>Ridimensiona in modo uniforme (intera immagine visibile, senza ritagli) entro un quadrato di lato maxEdge.</summary>
    private static Bitmap ScaleBitmapUniformToMaxEdge(Bitmap source, int maxEdge)
    {
        if (source == null)
        {
            return null;
        }

        var w = source.Width;
        var h = source.Height;
        if (w <= 0 || h <= 0)
        {
            source.Dispose();
            return null;
        }

        var scale = Math.Min(maxEdge / (float)w, maxEdge / (float)h);
        var tw = Math.Max(1, (int)Math.Round(w * scale));
        var th = Math.Max(1, (int)Math.Round(h * scale));
        var bmp = new Bitmap(tw, th, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.DrawImage(source, 0, 0, tw, th);
        }

        source.Dispose();
        return bmp;
    }

    private static Bitmap FinalizePreviewBitmap(Bitmap revitClone)
    {
        return ScaleBitmapUniformToMaxEdge(revitClone, PreviewThumbPixelSize);
    }

    private static string GetPlaceholderLetter(FamilyRecord row)
    {
        var c = (row.CategoryName ?? "").Trim();
        if (c.Length > 0)
        {
            return c.Substring(0, 1).ToUpperInvariant();
        }

        var f = (row.FamilyName ?? "").Trim();
        if (f.Length > 0)
        {
            return char.ToUpperInvariant(f[0]).ToString();
        }

        return "?";
    }

    private static Bitmap CreatePlaceholderBitmap(FamilyRecord row)
    {
        var n = PreviewThumbPixelSize;
        var bmp = new Bitmap(n, n, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            var isSys = string.Equals(row.FamilyKind, "System", StringComparison.OrdinalIgnoreCase);
            g.Clear(isSys ? System.Drawing.Color.LightSteelBlue : System.Drawing.Color.Honeydew);
            var letter = GetPlaceholderLetter(row);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (var font = new Font("Segoe UI", 12f, FontStyle.Bold))
            using (var brush = new SolidBrush(System.Drawing.Color.FromArgb(100, 40, 40, 40)))
            {
                var sz = g.MeasureString(letter, font);
                g.DrawString(letter, font, brush, (n - sz.Width) / 2f, (n - sz.Height) / 2f);
            }
        }

        return bmp;
    }

    private static Image BuildPreview(FamilyRecord row, Dictionary<string, Document> docCache)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(row.SourceModelPath) && docCache.TryGetValue(row.SourceModelPath, out var src))
            {
                if (string.Equals(row.FamilyKind, "System", StringComparison.OrdinalIgnoreCase) && row.SourceElementTypeId.HasValue)
                {
                    var type = src.GetElement(new ElementId(row.SourceElementTypeId.Value)) as ElementType;
                    foreach (var s in PreviewSizes)
                    {
                        var img = type?.GetPreviewImage(s);
                        if (img != null)
                        {
                            return FinalizePreviewBitmap(CloneBitmap(img));
                        }
                    }
                }
                else if (!string.IsNullOrWhiteSpace(row.FamilyName))
                {
                    var famName = row.FamilyName.Split(':')[0].Trim();
                    var fam = new FilteredElementCollector(src).OfClass(typeof(Family)).Cast<Family>()
                        .FirstOrDefault(f => string.Equals(f.Name, famName, StringComparison.OrdinalIgnoreCase));
                    if (fam != null)
                    {
                        foreach (var symId in fam.GetFamilySymbolIds())
                        {
                            var sym = src.GetElement(symId) as ElementType;
                            if (sym == null)
                            {
                                continue;
                            }

                            foreach (var s in PreviewSizes)
                            {
                                var img = sym.GetPreviewImage(s);
                                if (img != null)
                                {
                                    return FinalizePreviewBitmap(CloneBitmap(img));
                                }
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // fallback sotto
        }

        return CreatePlaceholderBitmap(row);
    }

    internal static Result LoadFromRecords(ExternalCommandData commandData, IReadOnlyList<FamilyRecord> items, ref string message)
    {
        try
        {
            var targetDoc = commandData.Application.ActiveUIDocument.Document;
            var app = commandData.Application.Application;
            var ok = 0;
            var skipped = 0;
            var errors = new List<string>();
            var syncStamp = BuildDbSynchStamp(commandData);

            var itemsToLoad = LibrarySync.IsLibraryModelDocument(targetDoc)
                ? items.ToList()
                : items.Where(i => !IsAlreadyInActiveDocument(targetDoc, i)).ToList();
            var alreadyPresent = items.Count - itemsToLoad.Count;
            if (itemsToLoad.Count == 0)
            {
                TaskDialog.Show(
                    "Family DB",
                    alreadyPresent > 0
                        ? "Nessuna operazione: le voci selezionate risultano gia' presenti nel progetto."
                        : "Nessuna voce da elaborare.");
                return Result.Cancelled;
            }

            var systemRows = itemsToLoad
                .Where(x => string.Equals(x.FamilyKind, "System", StringComparison.OrdinalIgnoreCase)
                            && x.SourceElementTypeId.HasValue
                            && !string.IsNullOrWhiteSpace(x.SourceModelPath)
                            && File.Exists(x.SourceModelPath))
                .GroupBy(x => x.SourceModelPath + "\0" + x.SourceElementTypeId.Value)
                .Select(g => g.First())
                .ToList();
            var rfaRows = itemsToLoad
                .Where(x => !string.Equals(x.FamilyKind, "System", StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(x.RfaPath)
                            && File.Exists(x.RfaPath))
                .GroupBy(x => x.RfaPath)
                .Select(g => g.First())
                .ToList();

            var totalOps = systemRows.Count + rfaRows.Count;
            var doneOps = 0;
            var progress = CreateProgressForm(totalOps);
            try
            {
                progress.Show();
                progress.BringToFront();

            foreach (var group in systemRows.GroupBy(x => x.SourceModelPath))
            {
                Document srcDoc = null;
                try
                {
                    srcDoc = app.OpenDocumentFile(group.Key);
                    var ids = group.Select(x => new ElementId(x.SourceElementTypeId.Value)).ToList();
                    using (var tx = new Transaction(targetDoc, "Importa tipi sistema (selezione multipla)"))
                    {
                        tx.Start();
                        var opt = new CopyPasteOptions();
                        opt.SetDuplicateTypeNamesHandler(new UseDestinationTypesHandler());
                        var copiedIds = ElementTransformUtils.CopyElements(srcDoc, ids, targetDoc, Transform.Identity, opt);
                        foreach (var cid in copiedIds)
                        {
                            if (targetDoc.GetElement(cid) is ElementType copiedType)
                            {
                                TrySetDbSynchStampOnType(targetDoc, copiedType, syncStamp);
                            }
                        }
                        tx.Commit();
                    }

                    ok += group.Count();
                }
                catch (Exception ex)
                {
                    skipped += group.Count();
                    errors.Add("Sistema " + Path.GetFileName(group.Key) + ": " + ex.Message);
                }
                finally
                {
                    srcDoc?.Close(false);
                }
                doneOps += group.Count();
                UpdateProgress(progress, doneOps, totalOps, "Import tipi sistema...");
            }

            foreach (var r in rfaRows)
            {
                try
                {
                    using (var tx = new Transaction(targetDoc, "Carica famiglia .rfa"))
                    {
                        tx.Start();
                        targetDoc.LoadFamily(r.RfaPath, out var loadedFamily);
                        if (loadedFamily != null)
                        {
                            foreach (var sid in loadedFamily.GetFamilySymbolIds())
                            {
                                if (targetDoc.GetElement(sid) is ElementType typ)
                                {
                                    TrySetDbSynchStampOnType(targetDoc, typ, syncStamp);
                                }
                            }
                        }
                        tx.Commit();
                    }

                    ok++;
                }
                catch (Exception ex)
                {
                    skipped++;
                    errors.Add(r.FamilyName + ": " + ex.Message);
                }
                doneOps++;
                UpdateProgress(progress, doneOps, totalOps, "Carico famiglie .rfa...");
            }
            }
            finally
            {
                progress.Close();
                progress.Dispose();
            }

            var notApplicable = itemsToLoad.Count(x =>
            {
                if (string.Equals(x.FamilyKind, "System", StringComparison.OrdinalIgnoreCase))
                {
                    return !x.SourceElementTypeId.HasValue
                           || string.IsNullOrWhiteSpace(x.SourceModelPath)
                           || !File.Exists(x.SourceModelPath);
                }

                return string.IsNullOrWhiteSpace(x.RfaPath) || !File.Exists(x.RfaPath);
            });

            var summary = "Operazioni completate: " + ok + " riuscite.";
            if (alreadyPresent > 0)
            {
                summary += "\nGia' presenti nel progetto (saltate): " + alreadyPresent;
            }

            if (notApplicable > 0)
            {
                summary += "\nNon applicabili (record sistema incompleto o nessun file .rfa reale): " + notApplicable;
            }

            if (skipped > 0)
            {
                summary += "\nFallite durante l'operazione: " + skipped;
            }

            if (errors.Count > 0)
            {
                summary += "\n\nErrori (max 8):\n" + string.Join("\n", errors.Take(8));
            }

            TaskDialog.Show("Family DB", summary);
            return ok > 0 ? Result.Succeeded : Result.Failed;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("Family DB", "Caricamento fallito:\n" + ex.Message);
            return Result.Failed;
        }
    }

    private static string BuildDbSynchStamp(ExternalCommandData commandData)
    {
        var user = Environment.UserName;
        try
        {
            var u = commandData?.Application?.Application?.Username;
            if (!string.IsNullOrWhiteSpace(u)) user = u;
        }
        catch { }
        return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " | " + user;
    }

    private static System.Windows.Forms.Form CreateProgressForm(int total)
    {
        var form = new System.Windows.Forms.Form
        {
            Text = "DB Family - Caricamento in corso",
            Width = 520,
            Height = 120,
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            TopMost = true
        };
        var label = new Label
        {
            Name = "lbl",
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 34,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 8, 10, 0),
            Text = "Preparazione..."
        };
        var bar = new ProgressBar
        {
            Name = "bar",
            Dock = DockStyle.Top,
            Height = 24,
            Minimum = 0,
            Maximum = Math.Max(1, total),
            Value = 0
        };
        form.Controls.Add(bar);
        form.Controls.Add(label);
        return form;
    }

    private static void UpdateProgress(System.Windows.Forms.Form form, int done, int total, string prefix)
    {
        if (form == null || form.IsDisposed) return;
        var max = Math.Max(1, total);
        var safeDone = Math.Max(0, Math.Min(done, max));
        var bar = form.Controls.Find("bar", true).FirstOrDefault() as ProgressBar;
        var lbl = form.Controls.Find("lbl", true).FirstOrDefault() as Label;
        if (bar != null)
        {
            bar.Maximum = max;
            bar.Value = safeDone;
        }
        if (lbl != null)
        {
            lbl.Text = prefix + " " + safeDone + "/" + max;
        }
        System.Windows.Forms.Application.DoEvents();
    }

    private static void TrySetDbSynchStampOnType(Document doc, ElementType type, string stamp)
    {
        if (doc == null || type == null || string.IsNullOrWhiteSpace(stamp))
        {
            return;
        }

        var p = type.LookupParameter("DB_Synch");
        if ((p == null || p.IsReadOnly) && type.Category != null)
        {
            EnsureDbSynchParameter(doc, type.Category);
            p = type.LookupParameter("DB_Synch");
        }

        if (p != null && !p.IsReadOnly)
        {
            p.Set(stamp);
        }
    }

    private static void EnsureDbSynchParameter(Document doc, Category category)
    {
        if (doc == null || category == null || !category.AllowsBoundParameters)
        {
            return;
        }

        var app = doc.Application;
        Definition existingDef = null;
        var it = doc.ParameterBindings.ForwardIterator();
        it.Reset();
        while (it.MoveNext())
        {
            var def = it.Key;
            if (def != null && string.Equals(def.Name, "DB_Synch", StringComparison.OrdinalIgnoreCase))
            {
                existingDef = def;
                break;
            }
        }

        if (existingDef != null)
        {
            return;
        }

        var oldShared = app.SharedParametersFilename;
        try
        {
            var spDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RevitFamilyDb");
            Directory.CreateDirectory(spDir);
            var spFile = Path.Combine(spDir, "shared-parameters.txt");
            if (!File.Exists(spFile))
            {
                File.WriteAllText(spFile, "# Shared Parameters");
            }

            app.SharedParametersFilename = spFile;
            var defFile = app.OpenSharedParameterFile();
            if (defFile == null) return;
            var group = defFile.Groups.get_Item("RevitFamilyDb") ?? defFile.Groups.Create("RevitFamilyDb");
            var opt = new ExternalDefinitionCreationOptions("DB_Synch", SpecTypeId.String.Text);
            var defCreated = group.Definitions.get_Item("DB_Synch") as Definition ?? group.Definitions.Create(opt);
            if (defCreated == null) return;

            var cats = app.Create.NewCategorySet();
            cats.Insert(category);
            var binding = app.Create.NewTypeBinding(cats);
            doc.ParameterBindings.Insert(defCreated, binding, GroupTypeId.IdentityData);
        }
        catch
        {
            // ignore: parametro opzionale
        }
        finally
        {
            try { app.SharedParametersFilename = oldShared; } catch { }
        }
    }

    private sealed class BrowserRow
    {
        public bool IsSelected { get; set; }
        public FamilyRecord Record { get; set; }
        public Image Preview { get; set; }
        public string KindShort { get; set; }
        public string FamilyName { get; set; }
        public string CategoryName { get; set; }
        public string SourceDiscipline { get; set; }
        public string SourceModelPath { get; set; }
    }
}

internal class UseDestinationTypesHandler : IDuplicateTypeNamesHandler
{
    public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
    {
        return DuplicateTypeAction.UseDestinationTypes;
    }
}

internal static class LibrarySync
{
    private const string ArcModel = @"F:\Documenti Utili\BANCA DATI REVIT PROGETTI\Famiglie\Database Famiglie\ARC\00_Library\2025_ATI_ARC_rfa.rvt";
    private const string FurModel = @"F:\Documenti Utili\BANCA DATI REVIT PROGETTI\Famiglie\Database Famiglie\FUR\00_Library\2025_ATI_FUR_rfa.rvt";

    private static readonly string ProgramDataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "RevitFamilyDb",
        "2025");

    /// <summary>File .rvt ARC usato da Sync e come unica sorgente catalogo per quella disciplina.</summary>
    internal static string CatalogArcModelPath => ArcModel;

    /// <summary>File .rvt FUR usato da Sync e come unica sorgente catalogo per quella disciplina.</summary>
    internal static string CatalogFurModelPath => FurModel;

    /// <summary>
    /// Se il documento attivo e' uno dei file libreria ARC/FUR, non si applica il filtro "gia' nel progetto"
    /// (altrimenti l'elenco risulterebbe quasi vuoto).
    /// </summary>
    internal static bool IsLibraryModelDocument(Document doc)
    {
        try
        {
            var p = doc?.PathName;
            if (string.IsNullOrWhiteSpace(p))
            {
                return false;
            }

            var normalized = Path.GetFullPath(p);
            return string.Equals(normalized, Path.GetFullPath(ArcModel), StringComparison.OrdinalIgnoreCase)
                   || string.Equals(normalized, Path.GetFullPath(FurModel), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static Result SyncDiscipline(ExternalCommandData commandData, string discipline, ref string message)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "ARC", ArcModel },
            { "FUR", FurModel }
        };
        if (!map.ContainsKey(discipline))
        {
            message = "Disciplina non gestita: " + discipline;
            return Result.Failed;
        }
        return SyncModelPath(commandData, map[discipline], discipline, ref message, out _, showCompletionDialog: true);
    }

    public static Result SyncAll(ExternalCommandData commandData, ref string message)
    {
        var okArc = SyncModelPath(commandData, ArcModel, "ARC", ref message, out var countArc, showCompletionDialog: false);
        if (okArc != Result.Succeeded)
        {
            return okArc;
        }

        var okFur = SyncModelPath(commandData, FurModel, "FUR", ref message, out var countFur, showCompletionDialog: false);
        if (okFur != Result.Succeeded)
        {
            return okFur;
        }

        RevitFamilyDbLog.Info($"SyncAll: ARC record={countArc}, FUR record={countFur}");
        TaskDialog.Show(
            "Family DB",
            "Sync ALL completata.\n\nARC: " + countArc + " record aggiornati\nFUR: " + countFur + " record aggiornati");
        return Result.Succeeded;
    }

    private static Result SyncModelPath(ExternalCommandData commandData, string modelPath, string discipline, ref string message, out int recordCount, bool showCompletionDialog = true)
    {
        recordCount = 0;
        try
        {
            RevitFamilyDbLog.Info($"SyncModelPath start: discipline={discipline}, model={modelPath}");
            if (!File.Exists(modelPath))
            {
                message = "Modello non trovato: " + modelPath;
                TaskDialog.Show("Family DB", message);
                return Result.Failed;
            }

            var app = commandData.Application.Application;
            var repo = DbContextFactory.CreateRepository();
            repo.EnsureExtendedSchema();
            var count = 0;
            var skippedUnchanged = 0;
            var deletedOrphans = 0;
            var revitYear = int.TryParse(app.VersionNumber, out var yr) ? (int?)yr : null;

            var famDoc = app.OpenDocumentFile(modelPath);
            try
            {
                var modelBase = SanitizeFileName(Path.GetFileNameWithoutExtension(modelPath) ?? "model");
                var exportDir = Path.Combine(ProgramDataRoot, "ExportedRfa", discipline, modelBase);
                var previewDir = Path.Combine(ProgramDataRoot, "Previews", discipline + "_" + modelBase);
                Directory.CreateDirectory(exportDir);
                Directory.CreateDirectory(previewDir);
                var sourceKeysSeen = new HashSet<string>(StringComparer.Ordinal);

                var loadables = new FilteredElementCollector(famDoc).OfClass(typeof(Family)).Cast<Family>();
                foreach (var fam in loadables)
                {
                    if (LibrarySync.IsAnnotationCategory(fam.FamilyCategory))
                    {
                        continue;
                    }

                    var familyName = string.IsNullOrWhiteSpace(fam.Name) ? "UnnamedFamily" : fam.Name;
                    sourceKeysSeen.Add(BuildSourceKey("Loadable", familyName, null));
                    var pseudo = "loadable://" + modelPath + "#" + familyName;
                    var rfaPathFinal = pseudo;
                    string previewPathFinal = null;
                    ElementType symEt = null;
                    var paramRows = new List<FamilyParameterRecord>();
                    try
                    {
                        var symIds = fam.GetFamilySymbolIds();
                        if (symIds != null && symIds.Count > 0)
                        {
                            symEt = famDoc.GetElement(symIds.First()) as ElementType;
                            if (symEt != null) paramRows = CollectParameters(symEt);
                        }
                    }
                    catch
                    {
                        // ignore
                    }

                    var signature = ComputeRowSignature("Loadable", modelPath, familyName, null, paramRows);
                    var existingLoadable = repo.GetFamilyBySourceKey("Loadable", modelPath, familyName, null);
                    if (existingLoadable != null && string.Equals(existingLoadable.FileHash, signature, StringComparison.Ordinal))
                    {
                        skippedUnchanged++;
                        continue;
                    }

                    if (symEt != null)
                    {
                        previewPathFinal = TrySaveTypePreview(symEt, previewDir, pseudo);
                        if (string.IsNullOrWhiteSpace(previewPathFinal) && existingLoadable != null && !string.IsNullOrWhiteSpace(existingLoadable.PreviewPath))
                        {
                            previewPathFinal = existingLoadable.PreviewPath;
                        }
                    }

                    try
                    {
                        var exported = TryExportFamilyToRfa(famDoc, fam, exportDir);
                        if (!string.IsNullOrWhiteSpace(exported) && File.Exists(exported))
                        {
                            rfaPathFinal = exported;
                        }
                    }
                    catch
                    {
                        // ignore
                    }

                    repo.UpsertFamily(new FamilyRecord
                    {
                        FamilyName = familyName,
                        CategoryName = fam.FamilyCategory?.Name ?? "N/A",
                        FamilyKind = "Loadable",
                        SourceModelPath = modelPath,
                        SourceDiscipline = discipline,
                        RfaPath = rfaPathFinal,
                        PreviewPath = previewPathFinal,
                        RevitVersion = revitYear,
                        FileHash = signature,
                        ApprovalStatus = "Draft"
                    });
                    count++;

                    try
                    {
                        var fid = repo.GetFamilyIdByRfaPath(rfaPathFinal);
                        if (fid.HasValue && symEt != null)
                        {
                            repo.ReplaceParametersForFamily(fid.Value, paramRows);
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }

                var systemTypes = new FilteredElementCollector(famDoc).WhereElementIsElementType().Cast<ElementType>()
                    .Where(t => t.Category != null && !(t is FamilySymbol));
                foreach (var typ in systemTypes)
                {
                    if (LibrarySync.IsAnnotationCategory(typ.Category))
                    {
                        continue;
                    }

                    var name = string.IsNullOrWhiteSpace(typ.FamilyName) ? typ.Name : typ.FamilyName + " : " + typ.Name;
                    sourceKeysSeen.Add(BuildSourceKey("System", name, typ.Id.IntegerValue));
                    var paramRows = CollectParameters(typ);
                    var signature = ComputeRowSignature("System", modelPath, name, typ.Id.IntegerValue, paramRows);
                    var existingSystem = repo.GetFamilyBySourceKey("System", modelPath, name, typ.Id.IntegerValue);
                    if (existingSystem != null && string.Equals(existingSystem.FileHash, signature, StringComparison.Ordinal))
                    {
                        skippedUnchanged++;
                        continue;
                    }

                    var rfaKey = "system://" + modelPath + "#type:" + typ.Id.IntegerValue;
                    var previewPath = TrySaveTypePreview(typ, previewDir, rfaKey);
                    if (string.IsNullOrWhiteSpace(previewPath) && existingSystem != null && !string.IsNullOrWhiteSpace(existingSystem.PreviewPath))
                    {
                        previewPath = existingSystem.PreviewPath;
                    }
                    repo.UpsertFamily(new FamilyRecord
                    {
                        FamilyName = name,
                        CategoryName = typ.Category?.Name ?? "System",
                        FamilyKind = "System",
                        SourceModelPath = modelPath,
                        SourceDiscipline = discipline,
                        SourceElementTypeId = typ.Id.IntegerValue,
                        RfaPath = rfaKey,
                        PreviewPath = previewPath,
                        RevitVersion = revitYear,
                        FileHash = signature,
                        ApprovalStatus = "Draft"
                    });
                    count++;

                    try
                    {
                        var fid = repo.GetFamilyIdByRfaPath(rfaKey);
                        if (fid.HasValue)
                        {
                            repo.ReplaceParametersForFamily(fid.Value, paramRows);
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }

                deletedOrphans = repo.DeleteFamiliesNotInSourceKeys(modelPath, sourceKeysSeen);
            }
            finally
            {
                // Non si puo' chiudere da API il documento attualmente attivo in Revit.
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

            recordCount = count;
            RevitFamilyDbLog.Info($"SyncModelPath ok: discipline={discipline}, records={count}, unchangedSkipped={skippedUnchanged}, deletedOrphans={deletedOrphans}, model={modelPath}");
            if (showCompletionDialog)
            {
                TaskDialog.Show("Family DB", "Sync " + discipline + " completata.\nRecord aggiornati: " + count + "\nFamiglie non cambiate (saltate): " + skippedUnchanged + "\nRecord orfani rimossi: " + deletedOrphans);
            }

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            RevitFamilyDbLog.Info($"SyncModelPath error: discipline={discipline}, {ex.Message}");
            message = ex.Message;
            TaskDialog.Show("Family DB", "Sync " + discipline + " fallita:\n" + ex.Message);
            return Result.Failed;
        }
    }

    /// <summary>
    /// True se <paramref name="doc"/> e' il documento attivo (stesso riferimento o stesso file su disco).
    /// </summary>
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

    private static void DeleteLibraryArtifactFolders(string modelPath, string discipline)
    {
        try
        {
            var modelBase = SanitizeFileName(Path.GetFileNameWithoutExtension(modelPath) ?? "model");
            var exportDir = Path.Combine(ProgramDataRoot, "ExportedRfa", discipline, modelBase);
            if (Directory.Exists(exportDir))
            {
                Directory.Delete(exportDir, true);
            }

            var previewDir = Path.Combine(ProgramDataRoot, "Previews", discipline + "_" + modelBase);
            if (Directory.Exists(previewDir))
            {
                Directory.Delete(previewDir, true);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "unknown";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        return sb.ToString();
    }

    private static string ShortHash(string s)
    {
        using (var sha = SHA256.Create())
        {
            var b = sha.ComputeHash(Encoding.UTF8.GetBytes(s ?? ""));
            var hex = BitConverter.ToString(b).Replace("-", "");
            return hex.Length >= 12 ? hex.Substring(0, 12) : hex;
        }
    }

    private static string BuildSourceKey(string familyKind, string familyName, int? sourceElementTypeId)
    {
        return (familyKind ?? "") + "|" + (familyName ?? "") + "|" + (sourceElementTypeId.HasValue ? sourceElementTypeId.Value.ToString() : "");
    }

    private static string ComputeRowSignature(string kind, string modelPath, string familyName, int? sourceTypeId, IReadOnlyList<FamilyParameterRecord> parameters)
    {
        var sb = new StringBuilder();
        sb.Append(kind ?? "").Append("|")
          .Append(modelPath ?? "").Append("|")
          .Append(familyName ?? "").Append("|")
          .Append(sourceTypeId.HasValue ? sourceTypeId.Value.ToString() : "");
        if (parameters != null)
        {
            foreach (var p in parameters
                .OrderBy(x => x.ParameterName ?? "")
                .ThenBy(x => x.ParameterGroupName ?? "")
                .ThenBy(x => x.StorageType ?? ""))
            {
                sb.Append("\n")
                  .Append(p.ParameterName ?? "").Append("|")
                  .Append(p.ParameterGroupName ?? "").Append("|")
                  .Append(p.StorageType ?? "").Append("|")
                  .Append(p.StringValue ?? "");
            }
        }

        using (var sha = SHA256.Create())
        {
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            return BitConverter.ToString(bytes).Replace("-", "");
        }
    }

    private static string TrySaveTypePreview(ElementType typ, string previewDir, string hashKey)
    {
        try
        {
            if (typ == null)
            {
                return null;
            }

            Directory.CreateDirectory(previewDir);
            foreach (var sz in new[] { 128, 96, 64 })
            {
                var img = typ.GetPreviewImage(new System.Drawing.Size(sz, sz));
                if (img == null)
                {
                    continue;
                }

                var path = Path.Combine(previewDir, ShortHash(hashKey) + ".png");
                using (var bmp = new Bitmap(img))
                {
                    bmp.Save(path, ImageFormat.Png);
                }

                img.Dispose();
                return path;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    /// <summary>
    /// Percorso .rfa univoco: nome famiglia (sanificato), senza hash. In caso di file gia' presente: Nome_2.rfa, Nome_3.rfa, ...
    /// </summary>
    private static string GetUniqueRfaExportPath(string exportDir, string familyNameSanitized)
    {
        var safe = string.IsNullOrWhiteSpace(familyNameSanitized) ? "Family" : familyNameSanitized;
        if (safe.Length > 120)
        {
            safe = safe.Substring(0, 120);
        }

        const string ext = ".rfa";
        var candidate = Path.Combine(exportDir, safe + ext);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        for (var n = 2; n < 5000; n++)
        {
            var p = Path.Combine(exportDir, safe + "_" + n + ext);
            if (!File.Exists(p))
            {
                return p;
            }
        }

        return Path.Combine(exportDir, safe + "_" + ShortHash(safe + Guid.NewGuid().ToString("N")) + ext);
    }

    private static string TryExportFamilyToRfa(Document doc, Family fam, string exportDir)
    {
        try
        {
            Directory.CreateDirectory(exportDir);
            var familyDoc = doc.EditFamily(fam);
            try
            {
                var baseName = SanitizeFileName(fam.Name);
                var path = GetUniqueRfaExportPath(exportDir, baseName);
                var opts = new SaveAsOptions { OverwriteExistingFile = true };
                familyDoc.SaveAs(path, opts);
                return path;
            }
            finally
            {
                familyDoc.Close(false);
            }
        }
        catch
        {
            return null;
        }
    }

    internal static bool IsAnnotationCategory(Category category)
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
            if (p == null) continue;
            if (!p.HasValue) continue;
            var def = p.Definition;
            if (def == null) continue;
            var name = def.Name;
            if (string.IsNullOrWhiteSpace(name)) continue;
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
}

internal static class DbContextFactory
{
    private const string DefaultConnectionString = "Server=DESKTOP-A6NC714\\REVITLIB;Database=RevitFamilyLibrary;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;";
    public static string ConnectionString => ConnectionStringResolver.Resolve(DefaultConnectionString);
    public static FamilyRepository CreateRepository() => new FamilyRepository(new SqlConnectionFactory(new DbSettings(ConnectionString)));
}
