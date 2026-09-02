using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;

namespace ShapeProjector
{
    /// <summary>
    /// Named presets (spec §10b): a name → ProjectorParams map in the client-side GLOBAL library at
    /// ModData/shapeprojector/presets.json — available across projectors and worlds, shareable as a
    /// file between players. Loading goes through the normal apply packet, so the server validates a
    /// preset exactly like a manual edit (the preset system never bypasses BE authority).
    ///
    /// Path: string GetOrCreateDataPath(string foldername) — "Returns the path to given foldername
    /// inside the games data folder. Ensures that the folder exists" (api-notes §n.1,
    /// ICoreAPICommon.cs:106-112 → Path.Combine(GamePaths.DataPath, foldername) + CreateDirectory,
    /// APIBase.cs:84-92). No dedicated ModData helper exists (§n.1); ModData/&lt;modid&gt;/ is the
    /// established community convention. IO: plain File.ReadAllText/WriteAllText + Newtonsoft — the
    /// game's own JSON-config pattern (§n.2, APIBase.cs:96-109; Newtonsoft ships with the game).
    ///
    /// Malformed handling (spec §10b "skip bad entries, report, never crash") follows the playbook's
    /// persistence rules: an entry that fails to parse is EXCLUDED from the dropdown but its raw JSON
    /// is KEPT and written back on Store — "the world does not know this item" and "does not know it
    /// YET" are indistinguishable, so skipping must never become deleting. A file whose top level is
    /// unreadable disables Store for the session (refuse to write before a successful read), and a
    /// Store that would replace a non-empty file with an empty library writes a .bak first.
    /// </summary>
    public class ProjectorPresets
    {
        public const string FileName = "presets.json";
        /// <summary>v1 location (spec §4a); migrated from silently, original kept as backup.</summary>
        public const string LegacyConfigFileName = "shapeprojector-presets.json";

        /// <summary>Name → configuration; only entries that parsed. The GUI dropdown reads this.</summary>
        public Dictionary<string, ProjectorParams> Presets = new Dictionary<string, ProjectorParams>();

        /// <summary>Entries that failed to parse, preserved verbatim for the next Store.</summary>
        private readonly Dictionary<string, JToken> malformed = new Dictionary<string, JToken>();

        /// <summary>True when the file existed but its top level was unreadable — Store is refused.</summary>
        public bool LoadFailed { get; private set; }

        /// <summary>How many entries were skipped as malformed (for the §10b report).</summary>
        public int SkippedCount => malformed.Count;

        private static string FilePath(ICoreClientAPI capi)
            => Path.Combine(capi.GetOrCreateDataPath(Path.Combine("ModData", "shapeprojector")), FileName);

        public static ProjectorPresets Load(ICoreClientAPI capi)
        {
            ProjectorPresets result = new ProjectorPresets();
            string path = FilePath(capi);
            try
            {
                if (!File.Exists(path))
                {
                    result.MigrateLegacy(capi, path);
                    return result;
                }

                JObject root = JObject.Parse(File.ReadAllText(path));
                if (root["Presets"] is not JObject entries) return result;   // empty/foreign file: nothing to show, Store allowed

                foreach (JProperty prop in entries.Properties())
                {
                    try
                    {
                        ProjectorParams? p = prop.Value.ToObject<ProjectorParams>();
                        if (p?.Layers == null) throw new JsonSerializationException("no layer list");
                        result.Presets[prop.Name] = p;
                    }
                    catch (Exception e)
                    {
                        // Spec §10b: skip + report, never crash — and keep the raw entry (playbook §7).
                        result.malformed[prop.Name] = prop.Value.DeepClone();
                        capi.Logger.Warning("[shapeprojector] Preset '{0}' in {1} is malformed and was skipped (kept in file): {2}",
                            prop.Name, FileName, e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                result.LoadFailed = true;   // refuse to write over a file we could not read
                capi.Logger.Error("[shapeprojector] Could not read " + path + " — presets unavailable this session, saving disabled to protect the file:");
                capi.Logger.Error(e);
            }
            return result;
        }

        /// <summary>One-time v1 → v2 move: ModConfig/shapeprojector-presets.json → ModData (spec §4a → §10b).</summary>
        private void MigrateLegacy(ICoreClientAPI capi, string newPath)
        {
            try
            {
                // T LoadModConfig<T>(string) — api-notes §d.11; null when the file does not exist.
                var legacy = capi.LoadModConfig<LegacyFile>(LegacyConfigFileName);
                if (legacy?.Presets == null || legacy.Presets.Count == 0) return;
                Presets = legacy.Presets;
                Store(capi);
                capi.Logger.Notification("[shapeprojector] Migrated {0} preset(s) from ModConfig/{1} to {2} (original kept).",
                    Presets.Count, LegacyConfigFileName, newPath);
            }
            catch (Exception e)
            {
                capi.Logger.Warning("[shapeprojector] Could not migrate legacy presets from ModConfig/" + LegacyConfigFileName + ":");
                capi.Logger.Warning(e.Message);
            }
        }

        private class LegacyFile
        {
            // Auto-property, not a field: JsonConvert populates it, and a bare field draws CS0649.
            public Dictionary<string, ProjectorParams>? Presets { get; set; }
        }

        public void Store(ICoreClientAPI capi)
        {
            string path = FilePath(capi);
            if (LoadFailed)
            {
                capi.Logger.Error("[shapeprojector] Not writing " + path + ": the file could not be read this session.");
                return;
            }
            try
            {
                // Full→empty transition gets a backup: emptying is legitimate, but it is also exactly
                // what a failed load looks like from the outside (playbook §7).
                if (Presets.Count == 0 && malformed.Count == 0 && File.Exists(path) && new FileInfo(path).Length > 2)
                {
                    File.Copy(path, path + ".bak", overwrite: true);
                }

                JObject entries = new JObject();
                foreach (var kv in Presets) entries[kv.Key] = JToken.FromObject(kv.Value);
                foreach (var kv in malformed)
                {
                    if (!entries.ContainsKey(kv.Key)) entries[kv.Key] = kv.Value;   // saved-over name supersedes the bad entry
                }
                JObject root = new JObject { ["Presets"] = entries };
                File.WriteAllText(path, root.ToString(Formatting.Indented));
            }
            catch (Exception e)
            {
                capi.Logger.Error("[shapeprojector] Could not write " + path + ":");
                capi.Logger.Error(e);
            }
        }

        /// <summary>Saving over a malformed entry's name replaces it deliberately; deleting removes both.</summary>
        public void Remove(string name)
        {
            Presets.Remove(name);
            malformed.Remove(name);
        }

        /// <summary>Sorted names for the dropdown; stable order across recomposes.</summary>
        public string[] SortedNames()
        {
            string[] names = new string[Presets.Count];
            Presets.Keys.CopyTo(names, 0);
            Array.Sort(names, StringComparer.OrdinalIgnoreCase);
            return names;
        }
    }
}
