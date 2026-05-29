using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit_Command_Centre.Models;

namespace Revit_Command_Centre.Services
{
    public record TitleBlockResult(int Updated, int Skipped, List<string> Errors);

    public static class TitleBlockService
    {
        private static readonly string[] ClientSynonyms      = { "Client", "Client Name", "Opdrachtgever", "Klant" };
        private static readonly string[] ProjectNameSynonyms = { "Project Name", "Projectnaam", "Project", "Name" };
        private static readonly string[] ProjectNumSynonyms  = { "Project Number", "Projectnummer", "Project Nr", "Job Number" };

        /// <summary>Returns distinct writable string-type parameter names found on title block instances.</summary>
        public static List<string> GetTitleBlockParameters(Document doc)
        {
            var paramNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var tbInstances = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>();

            foreach (var inst in tbInstances)
                foreach (Parameter p in inst.Parameters)
                    if (!p.IsReadOnly && p.StorageType == StorageType.String)
                        paramNames.Add(p.Definition.Name);

            return paramNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Suggests default field-to-parameter mappings based on common synonyms.</summary>
        public static Dictionary<string, string> SuggestDefaultMapping(List<string> availableParams)
        {
            static string? Find(List<string> available, string[] synonyms) =>
                synonyms.FirstOrDefault(s => available.Any(a => a.Equals(s, StringComparison.OrdinalIgnoreCase)));

            return new Dictionary<string, string>
            {
                ["ClientName"]    = Find(availableParams, ClientSynonyms)      ?? "",
                ["ProjectName"]   = Find(availableParams, ProjectNameSynonyms) ?? "",
                ["ProjectNumber"] = Find(availableParams, ProjectNumSynonyms)  ?? ""
            };
        }

        /// <summary>Returns what would be written without committing any changes.</summary>
        public static List<(string FieldName, string ConfigValue, string ParameterName)> PreviewPopulation(
            Document doc, ProjectConfig config, Dictionary<string, string> fieldMapping)
        {
            var configValues = GetConfigValues(config);
            return fieldMapping
                .Where(kvp => !string.IsNullOrEmpty(kvp.Value))
                .Select(kvp => (kvp.Key, configValues.GetValueOrDefault(kvp.Key, ""), kvp.Value))
                .ToList();
        }

        /// <summary>Writes config values to all title block instances across all sheets in a single transaction.</summary>
        public static TitleBlockResult PopulateTitleBlocks(
            Document doc, ProjectConfig config, Dictionary<string, string> fieldMapping)
        {
            var configValues = GetConfigValues(config);
            int updated = 0, skipped = 0;
            var errors = new List<string>();

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .ToList();

            using var tx = new Transaction(doc, "BIM CC — Populate title blocks");
            tx.Start();

            foreach (var sheet in sheets)
            {
                var tbInstances = new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsNotElementType()
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .ToList();

                if (tbInstances.Count == 0) { skipped++; continue; }

                foreach (var tb in tbInstances)
                    foreach (var kvp in fieldMapping)
                    {
                        if (string.IsNullOrEmpty(kvp.Value)) continue;
                        if (!configValues.TryGetValue(kvp.Key, out string value)) continue;
                        var p = tb.LookupParameter(kvp.Value);
                        if (p == null || p.IsReadOnly) continue;
                        p.Set(value);
                    }

                updated++;
            }

            tx.Commit();
            return new TitleBlockResult(updated, skipped, errors);
        }

        private static Dictionary<string, string> GetConfigValues(ProjectConfig config) => new()
        {
            ["ClientName"]    = config.ClientName,
            ["ProjectName"]   = config.ProjectName,
            ["ProjectNumber"] = config.ProjectNumber
        };
    }
}
