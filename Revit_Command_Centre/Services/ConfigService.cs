using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Revit_Command_Centre.Models;

namespace Revit_Command_Centre.Services
{
    /// <summary>
    /// Handles serialisation and deserialisation of ProjectConfig to/from a JSON sidecar
    /// file stored alongside the .rvt file.  Also provides the canonical parameter list
    /// for each compliance tier.
    /// </summary>
    public static class ConfigService
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        /// <summary>
        /// Returns the path to the JSON sidecar for a given .rvt path.
        /// e.g. C:\Projects\MyProject.rvt → C:\Projects\MyProject.bimconfig.json
        /// </summary>
        private static string GetConfigPath(string rvtFilePath) =>
            Path.ChangeExtension(rvtFilePath, ".bimconfig.json");

        /// <summary>
        /// Serialises <paramref name="config"/> to JSON alongside the .rvt file.
        /// </summary>
        public static void SaveConfig(ProjectConfig config, string rvtFilePath)
        {
            if (string.IsNullOrWhiteSpace(rvtFilePath))
                throw new ArgumentException("Revit file path must not be empty.", nameof(rvtFilePath));

            config.LastModified = DateTime.UtcNow;
            string json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(GetConfigPath(rvtFilePath), json);
        }

        /// <summary>
        /// Deserialises the JSON sidecar alongside the .rvt file.
        /// Returns null if the file does not exist.
        /// </summary>
        public static ProjectConfig? LoadConfig(string rvtFilePath)
        {
            string configPath = GetConfigPath(rvtFilePath);
            if (!File.Exists(configPath))
                return null;

            string json = File.ReadAllText(configPath);
            return JsonSerializer.Deserialize<ProjectConfig>(json, JsonOptions);
        }

        /// <summary>
        /// Returns the canonical list of shared parameters for the given compliance tier.
        /// Tier 1 = minimum, Tier 2 = BIM compliant (IFC4 + Uniclass), Tier 3 = ISO 19650 full.
        /// </summary>
        public static List<FamilyParameter> GetDefaultParameters(int tier)
        {
            var parameters = new List<FamilyParameter>
            {
                // Tier 1 — Standard (all tiers inherit these)
                new FamilyParameter { Name = "BIM_Description",    Group = "Identity Data",  ParameterType = "Text",   Tier = 1, IsRequired = true,  Description = "Element description" },
                new FamilyParameter { Name = "BIM_Material",       Group = "Materials",      ParameterType = "Text",   Tier = 1, IsRequired = true,  Description = "Primary material" },
                new FamilyParameter { Name = "BIM_Phase",          Group = "Phasing",        ParameterType = "Text",   Tier = 1, IsRequired = true,  Description = "Construction phase" },
                new FamilyParameter { Name = "BIM_Width",          Group = "Dimensions",     ParameterType = "Length", Tier = 1, IsRequired = true,  Description = "Overall width" },
                new FamilyParameter { Name = "BIM_Height",         Group = "Dimensions",     ParameterType = "Length", Tier = 1, IsRequired = true,  Description = "Overall height" },
                new FamilyParameter { Name = "BIM_Depth",          Group = "Dimensions",     ParameterType = "Length", Tier = 1, IsRequired = false, Description = "Overall depth" },

                // Tier 2 — BIM Compliant (IFC4, Uniclass, fire rating)
                new FamilyParameter { Name = "IFC_GUID",           Group = "IFC Parameters", ParameterType = "Text",   Tier = 2, IsRequired = true,  IfcMapping = "GlobalId",          Description = "IFC global unique identifier" },
                new FamilyParameter { Name = "IFC_Type",           Group = "IFC Parameters", ParameterType = "Text",   Tier = 2, IsRequired = true,  IfcMapping = "ObjectType",        Description = "IFC object type" },
                new FamilyParameter { Name = "Uniclass_Code",      Group = "Classification", ParameterType = "Text",   Tier = 2, IsRequired = true,  Description = "Uniclass 2015 code" },
                new FamilyParameter { Name = "Uniclass_Title",     Group = "Classification", ParameterType = "Text",   Tier = 2, IsRequired = true,  Description = "Uniclass 2015 title" },
                new FamilyParameter { Name = "BIM_FireRating",     Group = "Fire Safety",    ParameterType = "Text",   Tier = 2, IsRequired = false, Description = "Fire resistance rating (e.g. REI 60)" },
                new FamilyParameter { Name = "BIM_Manufacturer",   Group = "Identity Data",  ParameterType = "Text",   Tier = 2, IsRequired = false, Description = "Product manufacturer" },
                new FamilyParameter { Name = "BIM_Model",          Group = "Identity Data",  ParameterType = "Text",   Tier = 2, IsRequired = false, Description = "Product model number" },

                // Tier 3 — ISO 19650 Full (COBie, asset data, FM handover)
                new FamilyParameter { Name = "COBie_Space",        Group = "COBie",          ParameterType = "Text",   Tier = 3, IsRequired = true,  Description = "COBie space reference" },
                new FamilyParameter { Name = "COBie_Zone",         Group = "COBie",          ParameterType = "Text",   Tier = 3, IsRequired = true,  Description = "COBie zone reference" },
                new FamilyParameter { Name = "COBie_SystemName",   Group = "COBie",          ParameterType = "Text",   Tier = 3, IsRequired = false, Description = "COBie system name" },
                new FamilyParameter { Name = "Asset_SerialNumber", Group = "Asset Data",     ParameterType = "Text",   Tier = 3, IsRequired = false, Description = "Asset serial number for FM" },
                new FamilyParameter { Name = "Asset_InstallDate",  Group = "Asset Data",     ParameterType = "Text",   Tier = 3, IsRequired = false, Description = "Installation date (ISO 8601)" },
                new FamilyParameter { Name = "Asset_Warranty",     Group = "Asset Data",     ParameterType = "Text",   Tier = 3, IsRequired = false, Description = "Warranty period (years)" },
                new FamilyParameter { Name = "FM_MaintenanceCycle",Group = "FM Handover",    ParameterType = "Text",   Tier = 3, IsRequired = false, Description = "Planned maintenance interval" },
            };

            return parameters.FindAll(p => p.Tier <= tier);
        }
    }
}
