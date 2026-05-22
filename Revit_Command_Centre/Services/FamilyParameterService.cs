using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BimParameter = Revit_Command_Centre.Models.FamilyParameter;

namespace Revit_Command_Centre.Services
{
    /// <summary>
    /// Opens Revit family documents (.rfa), validates them against required shared
    /// parameters, and injects missing parameters.
    /// All Revit API calls that modify the family must run inside a transaction.
    /// </summary>
    public static class FamilyParameterService
    {
        /// <summary>
        /// Opens a single .rfa file, adds every parameter in <paramref name="parameters"/>
        /// that does not already exist, then saves and closes the family document.
        /// </summary>
        public static string AddParametersToFamily(UIApplication app, string rfaPath, List<BimParameter> parameters)
        {
            if (!File.Exists(rfaPath))
                throw new FileNotFoundException("Family file not found.", rfaPath);

            Document familyDoc = app.Application.OpenDocumentFile(rfaPath);
            if (!familyDoc.IsFamilyDocument)
            {
                familyDoc.Close(false);
                throw new InvalidOperationException($"{rfaPath} is not a family document.");
            }

            var log = new System.Text.StringBuilder();
            int added = 0;
            int skipped = 0;

            using (Transaction t = new Transaction(familyDoc, "Add BIM Parameters"))
            {
                t.Start();

                FamilyManager fm = familyDoc.FamilyManager;

                // Build a set of existing parameter names for fast lookup (uses Revit's FamilyParameter type)
                var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Autodesk.Revit.DB.FamilyParameter fp in fm.Parameters)
                    existingNames.Add(fp.Definition.Name);

                foreach (BimParameter param in parameters)
                {
                    if (existingNames.Contains(param.Name))
                    {
                        log.AppendLine($"  SKIP  {param.Name} (already exists)");
                        skipped++;
                        continue;
                    }

                    ForgeTypeId specType = MapParameterType(param.ParameterType);

                    // isInstance: true = instance parameter; type parameters are less common for BIM data
                    fm.AddParameter(param.Name, GroupTypeId.IdentityData, specType, true);
                    existingNames.Add(param.Name);
                    log.AppendLine($"  ADD   {param.Name}");
                    added++;
                }

                t.Commit();
            }

            familyDoc.Save();
            familyDoc.Close(false);

            log.Insert(0, $"[{Path.GetFileName(rfaPath)}] Added {added}, skipped {skipped}\n");
            return log.ToString();
        }

        /// <summary>
        /// Checks which required parameters are missing from a family without modifying it.
        /// </summary>
        public static List<string> ValidateFamily(UIApplication app, string rfaPath, List<BimParameter> required)
        {
            if (!File.Exists(rfaPath))
                throw new FileNotFoundException("Family file not found.", rfaPath);

            Document familyDoc = app.Application.OpenDocumentFile(rfaPath);
            var missing = new List<string>();

            try
            {
                if (!familyDoc.IsFamilyDocument)
                    return missing;

                var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Autodesk.Revit.DB.FamilyParameter fp in familyDoc.FamilyManager.Parameters)
                    existingNames.Add(fp.Definition.Name);

                foreach (BimParameter param in required.Where(p => p.IsRequired))
                {
                    if (!existingNames.Contains(param.Name))
                        missing.Add(param.Name);
                }
            }
            finally
            {
                familyDoc.Close(false);
            }

            return missing;
        }

        /// <summary>
        /// Processes every .rfa file in <paramref name="folderPath"/>, calling
        /// <see cref="AddParametersToFamily"/> on each one.
        /// Reports progress via the <paramref name="onProgress"/> callback.
        /// </summary>
        public static void BatchProcess(UIApplication app, string folderPath,
            List<BimParameter> parameters, Action<string, int, int> onProgress)
        {
            if (!Directory.Exists(folderPath))
                throw new DirectoryNotFoundException($"Folder not found: {folderPath}");

            string[] files = Directory.GetFiles(folderPath, "*.rfa", SearchOption.AllDirectories);
            int total = files.Length;

            for (int i = 0; i < total; i++)
            {
                onProgress(files[i], i + 1, total);
                try
                {
                    AddParametersToFamily(app, files[i], parameters);
                }
                catch (Exception ex)
                {
                    onProgress($"ERROR: {files[i]} — {ex.Message}", i + 1, total);
                }
            }
        }

        /// <summary>Maps a string type name to the Revit 2025 SpecTypeId.</summary>
        private static ForgeTypeId MapParameterType(string typeName) => typeName?.ToLowerInvariant() switch
        {
            "length"  => SpecTypeId.Length,
            "area"    => SpecTypeId.Area,
            "volume"  => SpecTypeId.Volume,
            "angle"   => SpecTypeId.Angle,
            "integer" => SpecTypeId.Int.Integer,
            "number"  => SpecTypeId.Number,
            "yesno"   => SpecTypeId.Boolean.YesNo,
            _         => SpecTypeId.String.Text
        };
    }
}
