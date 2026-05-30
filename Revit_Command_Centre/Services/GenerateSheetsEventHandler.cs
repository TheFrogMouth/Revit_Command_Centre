using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Revit_Command_Centre.Services
{
    public class GenerateSheetsEventHandler : IExternalEventHandler
    {
        public enum OperationMode { Generate, FetchSheets, DeleteSheets }

        public OperationMode Mode { get; set; } = OperationMode.Generate;

        // ── Generate mode ──────────────────────────────────────────────────────────
        public List<(string Code, string Name)> Disciplines { get; set; } = new();
        public string   Format    { get; set; } = "[Disc]-[Floor]-[Number]";
        public string   Padding   { get; set; } = "3 digits";
        public string   Separator { get; set; } = "Dash";
        public string[] Floors    { get; set; } = Array.Empty<string>();
        public Dictionary<string, (int Start, int? End)> NumberRanges { get; set; } = new();

        // ── FetchSheets mode ───────────────────────────────────────────────────────
        public Action<List<(string Number, string Name)>>? OnSheetsLoaded { get; set; }

        // ── DeleteSheets mode ──────────────────────────────────────────────────────
        public List<string> SheetsToDelete   { get; set; } = new();
        public Action<int>? OnDeleteComplete { get; set; }

        public void Execute(UIApplication app)
        {
            try
            {
                switch (Mode)
                {
                    case OperationMode.Generate:     ExecuteGenerate(app);     break;
                    case OperationMode.FetchSheets:  ExecuteFetch(app);        break;
                    case OperationMode.DeleteSheets: ExecuteDelete(app);       break;
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("BIM Command Centre", $"Sheet operation failed:\n\n{ex.Message}");
            }
        }

        public string GetName() => "BIM Command Centre — Generate Sheets";

        // ── fetch ──────────────────────────────────────────────────────────────────

        private void ExecuteFetch(UIApplication app)
        {
            Document? doc = app.ActiveUIDocument?.Document;
            if (doc == null)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(
                    () => OnSheetsLoaded?.Invoke(new()));
                return;
            }

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber)
                .Select(s => (s.SheetNumber, s.Name))
                .ToList();

            System.Windows.Application.Current.Dispatcher.BeginInvoke(
                () => OnSheetsLoaded?.Invoke(sheets));
        }

        // ── delete ─────────────────────────────────────────────────────────────────

        private void ExecuteDelete(UIApplication app)
        {
            Document? doc = app.ActiveUIDocument?.Document;
            if (doc == null || SheetsToDelete.Count == 0)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(
                    () => OnDeleteComplete?.Invoke(0));
                return;
            }

            var toDelete = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => SheetsToDelete.Contains(s.SheetNumber, StringComparer.OrdinalIgnoreCase))
                .Select(s => s.Id)
                .ToList();

            int deleted = 0;
            var errors  = new List<string>();

            using var tx = new Transaction(doc, "Delete sheets");
            tx.Start();
            foreach (var id in toDelete)
            {
                try   { doc.Delete(id); deleted++; }
                catch (Exception ex) { errors.Add(ex.Message); }
            }
            tx.Commit();

            if (errors.Count > 0)
                TaskDialog.Show("BIM Command Centre",
                    $"Deleted {deleted} sheet(s).\n{errors.Count} failed:\n{string.Join("\n", errors.Take(5))}");

            System.Windows.Application.Current.Dispatcher.BeginInvoke(
                () => OnDeleteComplete?.Invoke(deleted));
        }

        // ── generate ───────────────────────────────────────────────────────────────

        private void ExecuteGenerate(UIApplication app)
        {
            Document? doc = app.ActiveUIDocument?.Document;
            if (doc == null)
            {
                TaskDialog.Show("BIM Command Centre", "No project is open. Open a Revit project and try again.");
                return;
            }

            FamilySymbol? tbSymbol = GetFirstTitleBlock(doc);

            if (tbSymbol != null && !tbSymbol.IsActive)
            {
                using var txAct = new Transaction(doc, "Activate title block");
                txAct.Start();
                tbSymbol.Activate();
                txAct.Commit();
            }

            ElementId tbId  = tbSymbol?.Id ?? ElementId.InvalidElementId;
            string[]  floors = Floors.Length > 0 ? Floors : new[] { string.Empty };
            char      sep    = Separator switch { "Dot" => '.', "None" => '\0', _ => '-' };
            int       padLen = Padding.StartsWith("2") ? 2 : 3;

            var existing = GetExistingSheetNumbers(doc);
            int created = 0, skipped = 0;

            using var tx = new Transaction(doc, "Generate BIM sheets");
            tx.Start();

            foreach (var (code, name) in Disciplines)
            {
                var (startNum, endNum) = NumberRanges.GetValueOrDefault(code, (1, null));
                int num = startNum;

                foreach (string floor in floors)
                {
                    if (endNum.HasValue && num > endNum.Value) break;

                    string numStr   = num.ToString().PadLeft(padLen, '0');
                    string sheetNum = BuildCode(Format, code, floor, numStr, sep);
                    string sheetName = string.IsNullOrEmpty(floor)
                        ? $"{name} — General Arrangement"
                        : $"{name} — {floor} General Arrangement";

                    if (existing.Contains(sheetNum))
                    {
                        skipped++;
                    }
                    else
                    {
                        ViewSheet sheet = ViewSheet.Create(doc, tbId);
                        sheet.SheetNumber = sheetNum;
                        sheet.Name        = sheetName;
                        TrySetSheetCollection(sheet, name);
                        created++;
                    }

                    num++;
                }
            }

            tx.Commit();

            string msg = $"Generated {created} sheet{(created == 1 ? "" : "s")}.";
            if (skipped > 0) msg += $"\n{skipped} skipped (sheet numbers already exist).";
            if (tbSymbol == null) msg += "\nNo title block found — sheets created without one.";

            TaskDialog.Show("BIM Command Centre", msg);
        }

        // ── helpers ────────────────────────────────────────────────────────────────

        private static void TrySetSheetCollection(ViewSheet sheet, string disciplineName)
        {
            Parameter? p = sheet.LookupParameter("Sheet Collection");
            p ??= sheet.LookupParameter("Sheet Group");
            p ??= sheet.LookupParameter("Folder");
            if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                p.Set(disciplineName);
        }

        private static FamilySymbol? GetFirstTitleBlock(Document doc) =>
            new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .Cast<FamilySymbol>()
                .FirstOrDefault();

        private static HashSet<string> GetExistingSheetNumbers(Document doc) =>
            new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Select(s => s.SheetNumber)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        private static string BuildCode(string format, string disc, string floor, string num, char sep)
        {
            string s = sep == '\0' ? string.Empty : sep.ToString();
            return format switch
            {
                "[Disc][Number]" => $"{disc}{s}{num}",
                _                => string.IsNullOrEmpty(floor)
                                     ? $"{disc}{s}{num}"
                                     : $"{disc}{s}{floor}{s}{num}"
            };
        }
    }
}
