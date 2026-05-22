namespace Revit_Command_Centre.Models
{
    /// <summary>
    /// Represents a shared parameter definition to be injected into Revit families.
    /// </summary>
    public class FamilyParameter
    {
        public string Name { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty;
        public string ParameterType { get; set; } = "Text";

        /// <summary>Minimum compliance tier that requires this parameter (1, 2, or 3).</summary>
        public int Tier { get; set; } = 1;

        public bool IsRequired { get; set; } = true;
        public string IfcMapping { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
