using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Revit_Command_Centre.Services
{
    public record RenameCandidate(string CurrentPath, string ProposedName, bool IsCompliant, bool NeedsManualInput, string Reason);
    public record RenameOperation(string CurrentPath, string ApprovedName);
    public record RenameResult(int Renamed, int Skipped, List<string> Errors);

    /// <summary>
    /// Convention:  CATCODE-Type_Name_Size.rfa
    ///   CATCODE  = 2–6 uppercase letters identifying the Revit category
    ///              e.g. ELEC · LIGHT · MECH · PLUMB · ARCH · STRUCT · FIRE
    ///   Type     = PascalCase family type  (Transformer, Panel, Luminaire, AHU …)
    ///   Name     = underscore-joined brand / model tokens  (Trihal_Schneider_DryType)
    ///   Size     = electrical rating (100kVA, 400A, 11kV) OR physical (300x600mm, DN100)
    ///
    /// Examples:
    ///   ELEC-Transformer_Trihal_Schneider_100kVA.rfa
    ///   ELEC-Panel_MCC_ABB_400A.rfa
    ///   LIGHT-Downlight_Recessed_LED_150mm.rfa
    ///   MECH-AHU_Daikin_10000Lps.rfa
    ///   PLUMB-Valve_Gate_DN100.rfa
    /// </summary>
    public static class FamilyRenameService
    {
        private static readonly Regex CompliantPattern = new(
            @"^[A-Z]{2,6}-[A-Za-z][A-Za-z0-9]*(?:_[A-Za-z0-9][A-Za-z0-9]*)*$",
            RegexOptions.Compiled);

        // Electrical: 100kVA, 630kW, 11kV, 400A, 3x400A, 100MVA, 50kvar
        private static readonly Regex ElecSize = new(
            @"\d+(?:[xX×]\d+)?(?:MVA|kVA|kW|kV|kA|kvar|MW|VA|A|V|W)\b",
            RegexOptions.Compiled);

        // Flow: 10000Lps, 5000m3h, 200Lpm
        private static readonly Regex FlowSize = new(
            @"\d+(?:Lps|Lpm|m3h|m3s|cfm)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Physical: DN100, DN50 or 300x600mm, 150mm  (min 3 chars to avoid false hits)
        private static readonly Regex PhysSize = new(
            @"\b(?:DN\d+|\d+(?:[xX×]\d+)?(?:mm|cm|m)\b)",
            RegexOptions.Compiled);

        // Keyword table → (lowercase match, CATCODE, canonical TypeWord)
        private static readonly (string Key, string Cat, string Type)[] Keywords =
        {
            // Electrical equipment
            ("transformer",  "ELEC",   "Transformer"),
            ("transfo",      "ELEC",   "Transformer"),
            ("panel",        "ELEC",   "Panel"),
            ("panelboard",   "ELEC",   "Panel"),
            ("switchboard",  "ELEC",   "Switchboard"),
            ("switchgear",   "ELEC",   "Switchgear"),
            ("mcc",          "ELEC",   "MCC"),
            ("ups",          "ELEC",   "UPS"),
            ("inverter",     "ELEC",   "Inverter"),
            ("generator",    "ELEC",   "Generator"),
            ("genset",       "ELEC",   "Generator"),
            ("cabinet",      "ELEC",   "Cabinet"),
            ("db",           "ELEC",   "DB"),
            ("pdu",          "ELEC",   "PDU"),
            ("capacitor",    "ELEC",   "Capacitor"),
            // Lighting
            ("luminaire",    "LIGHT",  "Luminaire"),
            ("downlight",    "LIGHT",  "Downlight"),
            ("spotlight",    "LIGHT",  "Spotlight"),
            ("floodlight",   "LIGHT",  "Floodlight"),
            ("streetlight",  "LIGHT",  "StreetLight"),
            ("troffer",      "LIGHT",  "Troffer"),
            ("batten",       "LIGHT",  "Batten"),
            ("pendant",      "LIGHT",  "Pendant"),
            ("walllight",    "LIGHT",  "WallLight"),
            // Mechanical / HVAC
            ("ahu",          "MECH",   "AHU"),
            ("fcu",          "MECH",   "FCU"),
            ("vav",          "MECH",   "VAV"),
            ("chiller",      "MECH",   "Chiller"),
            ("boiler",       "MECH",   "Boiler"),
            ("pump",         "MECH",   "Pump"),
            ("fan",          "MECH",   "Fan"),
            ("cooler",       "MECH",   "Cooler"),
            ("crac",         "MECH",   "CRAC"),
            ("cooling",      "MECH",   "Cooler"),
            // Plumbing
            ("valve",        "PLUMB",  "Valve"),
            ("fitting",      "PLUMB",  "Fitting"),
            ("pipe",         "PLUMB",  "Pipe"),
            // Fire
            ("sprinkler",    "FIRE",   "Sprinkler"),
            ("detector",     "FIRE",   "Detector"),
            ("hydrant",      "FIRE",   "Hydrant"),
            ("hose",         "FIRE",   "HoseReel"),
        };

        public static List<RenameCandidate> ScanFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath)) return new List<RenameCandidate>();

            return Directory.GetFiles(folderPath, "*.rfa", SearchOption.TopDirectoryOnly)
                .Select(path =>
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    if (CompliantPattern.IsMatch(name))
                        return new RenameCandidate(path, name + ".rfa",
                            IsCompliant: true, NeedsManualInput: false, "Already compliant");

                    var (proposed, needsManual) = ProposeCompliantName(name);
                    return new RenameCandidate(path, proposed + ".rfa",
                        IsCompliant: false, NeedsManualInput: needsManual,
                        needsManual ? "Category unknown — fill in" : "Auto-proposed rename");
                })
                .OrderBy(c => c.IsCompliant)
                .ThenBy(c => Path.GetFileName(c.CurrentPath))
                .ToList();
        }

        /// <summary>
        /// Returns a proposed compliant name and whether manual input is still needed.
        /// NeedsManualInput=true only when the category code cannot be inferred from keywords.
        /// </summary>
        public static (string Proposed, bool NeedsManualInput) ProposeCompliantName(string fileNameWithoutExtension)
        {
            string remaining = fileNameWithoutExtension;

            // 1 — Pull the size token first (before tokenising, to keep "100kVA" intact)
            string sizeToken = "";
            foreach (Regex sizeRx in new[] { ElecSize, FlowSize, PhysSize })
            {
                var m = sizeRx.Match(remaining);
                if (m.Success && m.Length >= 2)
                {
                    sizeToken = m.Value.Trim();
                    remaining = remaining.Remove(m.Index, m.Length);
                    break;
                }
            }

            // 2 — Tokenise remaining text
            string[] words = remaining
                .Replace('_', ' ').Replace('-', ' ').Replace('.', ' ')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 0)
                .ToArray();

            // 3 — Match keywords → catCode + typeWord; everything else = name tokens
            string catCode  = "";
            string typeWord = "";
            var nameWords   = new List<string>();

            foreach (string w in words)
            {
                bool matched = false;
                foreach (var (key, cat, type) in Keywords)
                {
                    if (w.Equals(key, StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrEmpty(catCode)) { catCode = cat; typeWord = type; }
                        matched = true;
                        break;
                    }
                }
                if (!matched)
                    nameWords.Add(CapWord(w));
            }

            bool needsManual = string.IsNullOrEmpty(catCode);
            string cat2 = string.IsNullOrEmpty(catCode) ? "{CAT}" : catCode;

            // 4 — Assemble body: Type_Name_Size
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(typeWord))  parts.Add(typeWord);
            parts.AddRange(nameWords.Where(w => !string.IsNullOrEmpty(w)));
            if (!string.IsNullOrEmpty(sizeToken)) parts.Add(sizeToken);

            string body = parts.Count > 0 ? string.Join("_", parts) : "Family";
            return ($"{cat2}-{body}", needsManual);
        }

        /// <summary>Capitalise first letter; preserve digits-first tokens (e.g. "100kVA").</summary>
        private static string CapWord(string s) =>
            string.IsNullOrEmpty(s) ? s
            : char.IsDigit(s[0]) ? s
            : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

        public static RenameResult BatchRename(List<RenameOperation> operations)
        {
            int renamed = 0, skipped = 0;
            var errors = new List<string>();

            foreach (var op in operations)
            {
                string dir     = Path.GetDirectoryName(op.CurrentPath) ?? "";
                string newPath = Path.Combine(dir, op.ApprovedName);

                if (string.Equals(op.CurrentPath, newPath, StringComparison.OrdinalIgnoreCase))
                { skipped++; continue; }

                if (File.Exists(newPath))
                {
                    errors.Add($"Skipped — name collision: {op.ApprovedName}");
                    skipped++;
                    continue;
                }

                try
                {
                    File.Move(op.CurrentPath, newPath);
                    string logOld = op.CurrentPath + ".log.json";
                    string logNew = newPath        + ".log.json";
                    if (File.Exists(logOld) && !File.Exists(logNew))
                        File.Move(logOld, logNew);
                    renamed++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(op.CurrentPath)}: {ex.Message}");
                    skipped++;
                }
            }

            return new RenameResult(renamed, skipped, errors);
        }
    }
}
