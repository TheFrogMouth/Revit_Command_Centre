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

    public static class FamilyRenameService
    {
        // Convention: [Type]-[Subtype]-[Dimensions]-v[N].rfa  e.g. Door-Single-0900x2100-v1.rfa
        private static readonly Regex CompliantPattern = new(
            @"^[A-Z][A-Za-z]+-[A-Z][A-Za-z]+-\d+x\d+-v\d+$",
            RegexOptions.Compiled);

        private static readonly Regex DimensionToken = new(@"^\d+x\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex VersionToken   = new(@"^v\d+$",    RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
                        needsManual ? "Missing dimension segment — fill in manually" : "Auto-proposed rename");
                })
                .OrderBy(c => c.IsCompliant)
                .ThenBy(c => Path.GetFileName(c.CurrentPath))
                .ToList();
        }

        public static (string Proposed, bool NeedsManualInput) ProposeCompliantName(string fileNameWithoutExtension)
        {
            // Split on common separators and capitalise each token
            string[] raw = fileNameWithoutExtension
                .Replace('_', ' ')
                .Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);

            if (raw.Length == 0) return (fileNameWithoutExtension, true);

            string[] tokens = raw.Select(t =>
                char.ToUpperInvariant(t[0]) + t.Substring(1).ToLowerInvariant()
            ).ToArray();

            bool hasDimension = tokens.Any(t => DimensionToken.IsMatch(t));
            bool hasVersion   = tokens.Any(t => VersionToken.IsMatch(t));
            string version    = hasVersion ? tokens.First(t => VersionToken.IsMatch(t)).ToLowerInvariant() : "v1";

            var mainTokens = tokens
                .Where(t => !VersionToken.IsMatch(t))
                .ToList();

            if (!hasDimension)
            {
                // Cannot auto-complete — caller must supply dimensions
                string partial = string.Join("-", mainTokens) + "-{WxH}-" + version;
                return (partial, true);
            }

            return (string.Join("-", mainTokens) + "-" + version, false);
        }

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

                    // Rename .log.json sidecar if present
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
