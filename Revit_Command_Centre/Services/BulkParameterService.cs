using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Revit_Command_Centre.Services
{
    public record ParameterInfo(string Name, bool IsInstance, bool IsReadOnly, StorageType StorageType);

    public class ParameterRow
    {
        public ElementId ElementId   { get; set; } = ElementId.InvalidElementId;
        public string DisplayName    { get; set; } = "";
        public Dictionary<string, string> Values      { get; set; } = new();
        public Dictionary<string, bool>   IsEditable  { get; set; } = new();
        public Dictionary<string, StorageType> StorageTypes { get; set; } = new();
        public Dictionary<string, bool>   IsTypeParam { get; set; } = new();
    }

    public record CellEdit(ElementId ElementId, string ParameterName, string NewValue, bool IsTypeParameter);
    public record CommitResult(int Succeeded, int Failed, List<string> Errors);

    public static class BulkParameterService
    {
        public static List<(ElementId Id, string Name, int Count)> GetCategories(Document doc)
        {
            var counts = new Dictionary<int, (ElementId Id, string Name, int Count)>();

            foreach (var el in new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null &&
                            e.Category.CategoryType == CategoryType.Model &&
                            e.Category.Name != null))
            {
                int catInt = el.Category.Id.IntegerValue;
                if (counts.TryGetValue(catInt, out var existing))
                    counts[catInt] = (existing.Id, existing.Name, existing.Count + 1);
                else
                    counts[catInt] = (el.Category.Id, el.Category.Name, 1);
            }

            return counts.Values
                .OrderBy(c => c.Name)
                .Select(c => (c.Id, c.Name, c.Count))
                .ToList();
        }

        public static List<ParameterInfo> GetParameters(Document doc, ElementId categoryId)
        {
            var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<ParameterInfo>();

            var elements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category?.Id?.IntegerValue == categoryId.IntegerValue)
                .Take(20);

            foreach (var el in elements)
            {
                foreach (Parameter p in el.Parameters)
                    if (seen.Add(p.Definition.Name))
                        result.Add(new ParameterInfo(p.Definition.Name, true, p.IsReadOnly, p.StorageType));

                var typeId = el.GetTypeId();
                if (typeId != null && typeId != ElementId.InvalidElementId)
                {
                    var typeEl = doc.GetElement(typeId);
                    if (typeEl != null)
                        foreach (Parameter p in typeEl.Parameters)
                            if (seen.Add(p.Definition.Name))
                                result.Add(new ParameterInfo(p.Definition.Name, false, p.IsReadOnly, p.StorageType));
                }
            }

            return result.OrderBy(p => p.Name).ToList();
        }

        public static List<ParameterRow> BuildGrid(
            Document doc, ElementId categoryId, IEnumerable<string> parameterNames)
        {
            var paramList = parameterNames.ToList();
            var result    = new List<ParameterRow>();

            var elements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category?.Id?.IntegerValue == categoryId.IntegerValue)
                .OrderBy(e => e.Name)
                .ToList();

            foreach (var el in elements)
            {
                var typeId = el.GetTypeId();
                Element? typeEl = (typeId != null && typeId != ElementId.InvalidElementId)
                    ? doc.GetElement(typeId) : null;

                var row = new ParameterRow
                {
                    ElementId   = el.Id,
                    DisplayName = !string.IsNullOrEmpty(el.Name) ? el.Name
                                  : $"Id:{el.Id.IntegerValue}"
                };

                foreach (string paramName in paramList)
                {
                    Parameter? p      = el.LookupParameter(paramName);
                    bool       isType = false;

                    if (p == null && typeEl != null)
                    {
                        p = typeEl.LookupParameter(paramName);
                        isType = p != null;
                    }

                    if (p == null)
                    {
                        row.Values[paramName]       = "";
                        row.IsEditable[paramName]   = false;
                        row.StorageTypes[paramName] = StorageType.String;
                        row.IsTypeParam[paramName]  = false;
                    }
                    else
                    {
                        row.Values[paramName]       = ParameterToString(p);
                        row.IsEditable[paramName]   = !p.IsReadOnly;
                        row.StorageTypes[paramName] = p.StorageType;
                        row.IsTypeParam[paramName]  = isType;
                    }
                }

                result.Add(row);
            }

            return result;
        }

        public static CommitResult CommitChanges(Document doc, List<CellEdit> edits)
        {
            int succeeded = 0, failed = 0;
            var errors = new List<string>();

            using var tx = new Transaction(doc, "BIM CC — Bulk parameter edit");
            tx.Start();

            foreach (var edit in edits)
            {
                try
                {
                    var el = doc.GetElement(edit.ElementId);
                    if (el == null)
                    {
                        failed++;
                        errors.Add($"Element {edit.ElementId.IntegerValue} not found");
                        continue;
                    }

                    Parameter? p;
                    if (edit.IsTypeParameter)
                    {
                        var typeId = el.GetTypeId();
                        var typeEl = (typeId != null && typeId != ElementId.InvalidElementId)
                            ? doc.GetElement(typeId) : null;
                        p = typeEl?.LookupParameter(edit.ParameterName);
                    }
                    else
                    {
                        p = el.LookupParameter(edit.ParameterName);
                    }

                    if (p == null || p.IsReadOnly)
                    {
                        failed++;
                        errors.Add($"{edit.ParameterName} on {edit.ElementId.IntegerValue}: not writable");
                        continue;
                    }

                    if (SetParameter(p, edit.NewValue))
                        succeeded++;
                    else
                    {
                        failed++;
                        errors.Add($"{edit.ParameterName}: type mismatch (expected {p.StorageType})");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add($"{edit.ParameterName} on {edit.ElementId.IntegerValue}: {ex.Message}");
                }
            }

            tx.Commit();
            return new CommitResult(succeeded, failed, errors);
        }

        private static string ParameterToString(Parameter p) => p.StorageType switch
        {
            StorageType.String    => p.AsString()    ?? "",
            StorageType.Integer   => p.AsInteger().ToString(),
            StorageType.Double    => p.AsValueString() ?? p.AsDouble().ToString("G"),
            StorageType.ElementId => p.AsElementId()?.IntegerValue.ToString() ?? "",
            _                     => ""
        };

        private static bool SetParameter(Parameter p, string value)
        {
            try
            {
                switch (p.StorageType)
                {
                    case StorageType.String:
                        p.Set(value);
                        return true;
                    case StorageType.Integer:
                        if (!int.TryParse(value, out int iv)) return false;
                        p.Set(iv);
                        return true;
                    case StorageType.Double:
                        if (!double.TryParse(value,
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out double dv)) return false;
                        p.Set(dv);
                        return true;
                    default:
                        return false;
                }
            }
            catch { return false; }
        }
    }
}
