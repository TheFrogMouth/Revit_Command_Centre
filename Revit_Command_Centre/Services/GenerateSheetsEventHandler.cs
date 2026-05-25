using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Revit_Command_Centre.Services
{
    /// <summary>
    /// Creates ViewSheets in Revit from the disciplines and floors configured in SheetsView.
    /// Must run inside Revit's API event loop — raised via ExternalEvent.
    /// </summary>
    public class GenerateSheetsEventHandler : IExternalEventHandler
    {
        public List<(string Code, string Name)> Disciplines { get; set; } = new();
        public string   Format    { get; set; } = "[Disc]-[Floor]-[Number]";
        public string   Padding   { get; set; } = "3 digits";
        public string   Separator { get; set; } = "Dash";
        public string[] Floors    { get; set; } = Array.Empty<string>();

        public void Execute(UIApplication app)
        {
            Document? doc = app.ActiveUIDocument?.Document;
            if (doc == null)
            {
                MessageBox.Show("No project is open. Open a Revit project and try again.",
                    "BIM Command Centre", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            FamilySymbol? tbSymbol = GetFirstTitleBlock(doc);

            // Activate the symbol if needed (requires its own transaction)
            if (tbSymbol != null && !tbSymbol.IsActive)
            {
                using var txAct = new Transaction(doc, "Activate title block");
                txAct.Start();
                tbSymbol.Activate();
                txAct.Commit();
            }

            ElementId tbId = tbSymbol?.Id ?? ElementId.InvalidElementId;

            string[] floors = Floors.Length > 0 ? Floors : new[] { string.Empty };
            char sep = Separator switch { "Dot" => '.', "None" => '\0', _ => '-' };
            int padLen = Padding.StartsWith("2") ? 2 : 3;

            var existing = GetExistingSheetNumbers(doc);
            int created = 0, skipped = 0;

            using var tx = new Transaction(doc, "Generate BIM sheets");
            tx.Start();

            foreach (var (code, name) in Disciplines)
            {
                int num = 1;
                foreach (string floor in floors)
                {
                    string numStr    = num.ToString().PadLeft(padLen, '0');
                    string sheetNum  = BuildCode(Format, code, floor, numStr, sep);
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
                        created++;
                    }

                    num++;
                }
            }

            tx.Commit();

            string msg = $"Generated {created} sheet{(created == 1 ? "" : "s")}.";
            if (skipped > 0) msg += $"\n{skipped} skipped (sheet numbers already exist).";
            if (tbSymbol == null) msg += "\nNo title block found in project — sheets created without one. Load a title block via Save & Apply first.";

            MessageBox.Show(msg, "BIM Command Centre", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public string GetName() => "BIM Command Centre — Generate Sheets";

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
                "[Disc][Number]"          => $"{disc}{s}{num}",
                "[Disc]-[Floor]-[Number]" => string.IsNullOrEmpty(floor)
                                              ? $"{disc}{s}{num}"
                                              : $"{disc}{s}{floor}{s}{num}",
                _                         => string.IsNullOrEmpty(floor)
                                              ? $"{disc}{s}{num}"
                                              : $"{disc}{s}{floor}{s}{num}"
            };
        }
    }
}
