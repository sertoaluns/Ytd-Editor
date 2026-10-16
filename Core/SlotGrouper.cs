using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace YTDEditor.Core
{
    public class VariantEntry
    {
        public string VariantLetter { get; set; }
        public string FilePath { get; set; }
        public string Stem { get; set; }
        public string GroupKey { get; set; }
        public string SlotNumber { get; set; }
        public string Prefix { get; set; }
        public string Suffix { get; set; }

        public string FileName => Path.GetFileName(FilePath);
    }

    public class SlotGroup
    {
        public string GroupKey { get; set; }
        public string SlotNumber { get; set; }
        public string Prefix { get; set; }
        public string Suffix { get; set; }
        public string Folder { get; set; }
        public SortedDictionary<string, VariantEntry> Variants { get; set; } = new SortedDictionary<string, VariantEntry>(StringComparer.OrdinalIgnoreCase);

        public List<VariantEntry> SortedVariants => Variants.Values.ToList();
        public List<string> VariantLetters => Variants.Keys.Select(k => k.ToLowerInvariant()).ToList();

        public string NextAvailableLetter()
        {
            var used = new HashSet<string>(Variants.Keys.Select(k => k.ToLowerInvariant()));
            for (char c = 'a'; c <= 'z'; c++)
            {
                string s = c.ToString();
                if (!used.Contains(s))
                {
                    string candidateFile = Path.Combine(Folder, BuildVariantStem(s) + ".ytd");
                    if (!File.Exists(candidateFile))
                        return s;
                }
            }
            return "z";
        }

        public string BuildVariantStem(string newLetter, VariantEntry sourceVariant = null)
        {
            string letter = string.IsNullOrWhiteSpace(newLetter) ? "d" : newLetter.Trim().ToLowerInvariant()[0].ToString();
            if (sourceVariant != null)
                return SlotGrouper.ReplaceVariantLetterInName(sourceVariant.Stem, sourceVariant.VariantLetter, letter);
            return $"{Prefix}_{letter}{Suffix}";
        }
    }

    public static class SlotGrouper
    {
        private static readonly Regex SlotRegex = new Regex(
            @"^(?<prefix>.*?(?:diff|tex|normal|spec)?_?(?<slot_num>\d{3}))_(?<variant>[a-z])(?<suffix>_[a-z0-9_]+)?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        private static readonly Regex FallbackSlotRegex = new Regex(
            @"^(?<prefix>.+?_(?<slot_num>\d+))_(?<variant>[a-z])(?<suffix>_.*)?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        public static void ParseYtdStem(string stem, out string groupKey, out string slotNum, out string variant, out string prefix, out string suffix)
        {
            Match m = SlotRegex.Match(stem);
            if (!m.Success)
                m = FallbackSlotRegex.Match(stem);

            if (m.Success)
            {
                prefix = m.Groups["prefix"].Value;
                slotNum = m.Groups["slot_num"].Value;
                variant = m.Groups["variant"].Value.ToLowerInvariant();
                suffix = m.Groups["suffix"].Success ? m.Groups["suffix"].Value : "";
                groupKey = prefix.ToLowerInvariant();
                return;
            }

            groupKey = stem.ToLowerInvariant();
            slotNum = "000";
            variant = "a";
            prefix = stem;
            suffix = "";
        }

        public static string ReplaceVariantLetterInName(string name, string oldLetter, string newLetter)
        {
            char newChar = string.IsNullOrWhiteSpace(newLetter) ? 'd' : char.ToLowerInvariant(newLetter.Trim()[0]);
            Match m = SlotRegex.Match(name);
            if (!m.Success)
                m = FallbackSlotRegex.Match(name);

            if (m.Success)
            {
                string prefix = m.Groups["prefix"].Value;
                string suffix = m.Groups["suffix"].Success ? m.Groups["suffix"].Value : "";
                string origVar = m.Groups["variant"].Value;
                char repChar = (origVar.Length > 0 && char.IsUpper(origVar[0])) ? char.ToUpperInvariant(newChar) : newChar;
                return $"{prefix}_{repChar}{suffix}";
            }

            string pattern = $@"(_\d{{2,4}}_)({Regex.Escape(oldLetter)})(_|$)";
            if (Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase))
            {
                return Regex.Replace(name, pattern, $"${{1}}{newChar}${{3}}", RegexOptions.IgnoreCase);
            }
            return $"{name}_{newChar}";
        }

        public static bool IsNormalOrSpecTexture(string texName, string ytdStem, out string kind)
        {
            string[] tokens = new[] { (texName ?? "").ToLowerInvariant(), (ytdStem ?? "").ToLowerInvariant() };
            foreach (string t in tokens)
            {
                string lastPart = t.Contains("^") ? t.Split('^').Last() : t;
                if (t.EndsWith("_n") || t.Contains("_n_") || t.Contains("_normal_") || t.EndsWith("_normal") || lastPart.Contains("normal"))
                {
                    kind = "normal";
                    return true;
                }
                if (t.EndsWith("_s") || t.Contains("_s_") || t.Contains("_spec_") || t.EndsWith("_spec") || lastPart.Contains("spec"))
                {
                    kind = "spec";
                    return true;
                }
            }
            kind = "diffuse";
            return false;
        }

        public static List<SlotGroup> ScanFolderForSlotGroups(string folderPath, bool recursive = true)
        {
            var groupsMap = new Dictionary<string, SlotGroup>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return new List<SlotGroup>();

            var searchOpt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            string[] files;
            try
            {
                files = Directory.GetFiles(folderPath, "*.ytd", searchOpt);
            }
            catch
            {
                return new List<SlotGroup>();
            }

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            foreach (string file in files)
            {
                string dir = Path.GetDirectoryName(file) ?? "";
                // Skip 'output' subfolder or backup files
                string[] parts = dir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (parts.Any(p => string.Equals(p, "output", StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (file.EndsWith(".bak.ytd", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".gecersiz", StringComparison.OrdinalIgnoreCase))
                    continue;

                string stem = Path.GetFileNameWithoutExtension(file);
                ParseYtdStem(stem, out string groupKey, out string slotNum, out string variant, out string prefix, out string suffix);

                string mapKey = Path.GetFullPath(dir).ToLowerInvariant() + "|" + groupKey;
                if (!groupsMap.TryGetValue(mapKey, out SlotGroup grp))
                {
                    grp = new SlotGroup
                    {
                        GroupKey = groupKey,
                        SlotNumber = slotNum,
                        Prefix = prefix,
                        Suffix = suffix,
                        Folder = dir
                    };
                    groupsMap[mapKey] = grp;
                }

                string varKey = variant;
                int idx = 2;
                while (grp.Variants.ContainsKey(varKey))
                {
                    varKey = $"{variant}{idx}";
                    idx++;
                }

                grp.Variants[varKey] = new VariantEntry
                {
                    VariantLetter = varKey,
                    FilePath = file,
                    Stem = stem,
                    GroupKey = groupKey,
                    SlotNumber = slotNum,
                    Prefix = prefix,
                    Suffix = suffix
                };
            }

            return groupsMap.Values
                .OrderBy(g => g.Folder, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.GroupKey, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
