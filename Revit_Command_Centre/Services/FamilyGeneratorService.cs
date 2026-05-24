using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BimParameter = Revit_Command_Centre.Models.FamilyParameter;

namespace Revit_Command_Centre.Services
{
    /// <summary>
    /// Creates new Revit family documents (.rfa) from built-in templates,
    /// injects shared parameters, and saves them to a specified folder.
    /// </summary>
    public static class FamilyGeneratorService
    {
        private static readonly Dictionary<string, string> TemplateMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Door",        "Door.rft" },
            { "Window",      "Window.rft" },
            { "Column",      "Structural Column.rft" },
            { "Beam",        "Structural Framing - Beams and Braces.rft" },
            { "Furniture",   "Furniture.rft" },
            { "MEP fixture", "Plumbing Fixture.rft" },
            { "Electrical",  "Electrical Fixture.rft" },
            { "Generic",     "Generic Model.rft" },
        };

        /// <summary>
        /// Creates a new Revit family from the specified template, sets basic type
        /// parameters for width and height, injects the provided shared parameters,
        /// and saves the family to <paramref name="savePath"/>.
        /// </summary>
        public static string GenerateFamily(UIApplication app, string templateType, double widthMm,
            double heightMm, string name, string savePath, List<BimParameter> parameters)
        {
            string templateFile = ResolveTemplatePath(app, templateType);

            Document familyDoc = app.Application.NewFamilyDocument(templateFile)
                ?? throw new InvalidOperationException(
                    $"Revit returned null for template '{Path.GetFileName(templateFile)}'. " +
                    "Ensure a project is open and Revit is in a ready state.");

            if (!familyDoc.IsFamilyDocument)
                throw new InvalidOperationException("The document created from the template is not a family document.");

            using (Transaction t = new Transaction(familyDoc, "Set Default Parameters"))
            {
                t.Start();

                FamilyManager fm = familyDoc.FamilyManager;

                double widthFt  = UnitUtils.ConvertToInternalUnits(widthMm,  UnitTypeId.Millimeters);
                double heightFt = UnitUtils.ConvertToInternalUnits(heightMm, UnitTypeId.Millimeters);

                EnsureParameter(fm, "BIM_Width",  SpecTypeId.Length, GroupTypeId.Geometry, widthFt);
                EnsureParameter(fm, "BIM_Height", SpecTypeId.Length, GroupTypeId.Geometry, heightFt);

                var existingNames = GetExistingParameterNames(fm);
                foreach (BimParameter param in parameters)
                {
                    if (existingNames.Contains(param.Name))
                        continue;

                    ForgeTypeId specType = MapParameterType(param.ParameterType);
                    fm.AddParameter(param.Name, GroupTypeId.IdentityData, specType, true);
                    existingNames.Add(param.Name);
                }

                t.Commit();
            }

            if (!Directory.Exists(savePath))
                Directory.CreateDirectory(savePath);

            string outputPath = Path.Combine(savePath, $"{SanitiseFileName(name)}.rfa");
            familyDoc.SaveAs(outputPath);
            familyDoc.Close(false);

            return outputPath;
        }

        // ──────────────────────────────────────────── helpers ────

        private static string ResolveTemplatePath(UIApplication app, string templateType)
        {
            if (!TemplateMap.TryGetValue(templateType, out string? templateFileName))
                templateFileName = "Generic Model.rft";

            string templatesRoot = app.Application.FamilyTemplatePath;
            if (string.IsNullOrEmpty(templatesRoot))
                throw new InvalidOperationException(
                    "Revit FamilyTemplatePath is not set. Check Options → File Locations in Revit.");

            string templatePath  = Path.Combine(templatesRoot, "English", templateFileName);

            if (!File.Exists(templatePath))
                templatePath = Path.Combine(templatesRoot, templateFileName);

            if (!File.Exists(templatePath))
                throw new FileNotFoundException($"Template '{templateFileName}' not found under {templatesRoot}.", templatePath);

            return templatePath;
        }

        /// <summary>Adds a parameter and sets its current type value, skips if already present.</summary>
        private static void EnsureParameter(FamilyManager fm, string name, ForgeTypeId specType,
            ForgeTypeId groupType, double value)
        {
            Autodesk.Revit.DB.FamilyParameter? existing = null;
            foreach (Autodesk.Revit.DB.FamilyParameter p in fm.Parameters)
            {
                if (p.Definition.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    existing = p;
                    break;
                }
            }

            Autodesk.Revit.DB.FamilyParameter target = existing ?? fm.AddParameter(name, groupType, specType, false);

            if (fm.CurrentType != null)
                fm.Set(target, value);
        }

        private static HashSet<string> GetExistingParameterNames(FamilyManager fm)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Autodesk.Revit.DB.FamilyParameter p in fm.Parameters)
                names.Add(p.Definition.Name);
            return names;
        }

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

        private static string SanitiseFileName(string name) =>
            string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
    }
}
