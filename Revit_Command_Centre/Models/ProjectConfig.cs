using System;
using System.Collections.Generic;

namespace Revit_Command_Centre.Models
{
    /// <summary>
    /// Stores all project-level BIM configuration data.
    /// Serialised to JSON alongside the .rvt file and also written to Revit extensible storage.
    /// </summary>
    public class ProjectConfig
    {
        public string ClientName { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string ProjectNumber { get; set; } = string.Empty;
        public string Language { get; set; } = "English";
        public string TitleBlock { get; set; } = "Standard A1";

        /// <summary>1 = Standard, 2 = BIM Compliant, 3 = ISO 19650 Full</summary>
        public int ComplianceTier { get; set; } = 2;

        public string ClassificationSystem { get; set; } = "Uniclass";
        public string IfcSchema { get; set; } = "IFC4";
        public bool CobieEnabled { get; set; } = false;
        public List<string> ActiveDisciplines { get; set; } = new List<string>();
        public string SheetNamingFormat { get; set; } = "[Disc]-[Floor]-[Number]";
        public string SheetNumberPadding { get; set; } = "3 digits";
        public string SheetSeparator { get; set; } = "Dash";
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
    }
}
