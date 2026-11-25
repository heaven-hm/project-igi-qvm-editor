using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace QVM_Editor
{
    /// <summary>
    /// One authored <c>ModelLODSettings</c> task from a decompiled lod.qvm / lod.qsc.
    /// The five distances are the raw values in metres, in file order:
    /// "Distance to LOD 1" .. "Distance to LOD 4", "Distance to cutoff".
    /// </summary>
    internal sealed class LodQvmEntry
    {
        internal LodQvmEntry(string model, float[] distances)
        {
            Model = model;
            Distances = distances;
        }

        internal string Model { get; private set; }
        internal float[] Distances { get; private set; }
    }

    /// <summary>
    /// Dependency-free reader for the two LOD task types the game's LOD container
    /// runs out of LOCAL:lod.qsc (compiled as lod.qvm). Kept separate from the
    /// WinForms editor so it can be exercised by a plain console test runner.
    ///
    /// The engine's own definition (decompiled retail IGI.exe,
    /// src/animtrig/lodsettings.c) is:
    ///   LODSettings        { splineDetail, rigidDetail, boneDetail,
    ///                        splineCull, rigidCull, boneCull, blend, range }
    ///   ModelLODSettings   { char name[16]; float dist[5]; }
    /// ModelLODSettings_Changed (0x4EDB60) clamps dist[] to be non-decreasing and
    /// writes dist[i] * 4096 onto the model; lod_select uses dist[4] as the cull
    /// distance. See docs/LOD_QVM.md for the full trace.
    /// </summary>
    internal static class LodQvmInfo
    {
        internal const string LevelTaskName = "LODSettings";
        internal const string ModelTaskName = "ModelLODSettings";
        internal const float WorldUnitsPerMetre = 4096f;

        private static readonly string[] DistanceNames =
        {
            "Distance to LOD 1",
            "Distance to LOD 2",
            "Distance to LOD 3",
            "Distance to LOD 4",
            "Distance to cutoff"
        };

        /// <summary>
        /// Splits the arguments of a decompiled Task_New(...) line, trimming
        /// whitespace and optional surrounding double quotes.
        /// </summary>
        private static List<string> SplitArguments(string line)
        {
            var result = new List<string>();
            int start = line.IndexOf('(');
            int end = line.LastIndexOf(')');
            if (start < 0 || end <= start) return result;

            foreach (string raw in line.Substring(start + 1, end - start - 1).Split(','))
            {
                string token = raw.Trim();
                if (token.Length >= 2 && token[0] == '"' && token[token.Length - 1] == '"')
                    token = token.Substring(1, token.Length - 2);
                result.Add(token);
            }
            return result;
        }

        private static int FindTypeIndex(List<string> tokens, string typeName)
        {
            for (int i = 0; i < tokens.Count; i++)
            {
                if (string.Equals(tokens[i], typeName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private static bool TryFloat(string token, out float value)
        {
            return float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// True when the script contains either of the two LOD task types.
        /// </summary>
        internal static bool ContainsLodTasks(string script)
        {
            if (string.IsNullOrEmpty(script)) return false;
            return script.IndexOf(ModelTaskName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   script.IndexOf(LevelTaskName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Reads every ModelLODSettings task in a decompiled script, in file
        /// order. Lines that do not carry five numeric distances are skipped,
        /// matching the engine, which cannot apply an incomplete table.
        /// </summary>
        internal static List<LodQvmEntry> ParseModelSettings(string script)
        {
            var entries = new List<LodQvmEntry>();
            if (string.IsNullOrEmpty(script)) return entries;

            foreach (string line in script.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                if (line.IndexOf("Task_New", StringComparison.OrdinalIgnoreCase) < 0) continue;
                List<string> tokens = SplitArguments(line);
                int typeAt = FindTypeIndex(tokens, ModelTaskName);
                if (typeAt < 0 || typeAt + 1 >= tokens.Count) continue;

                string model = tokens[typeAt + 1];
                if (string.IsNullOrEmpty(model)) continue;

                var distances = new List<float>();
                for (int i = typeAt + 2; i < tokens.Count && distances.Count < 5; i++)
                {
                    float value;
                    if (TryFloat(tokens[i], out value)) distances.Add(value);
                }
                if (distances.Count != 5) continue;

                entries.Add(new LodQvmEntry(model, distances.ToArray()));
            }
            return entries;
        }

        /// <summary>
        /// Reads the single level-wide LODSettings task, if present. The eight
        /// values are the two sets of three percentages, blend and range.
        /// </summary>
        internal static bool TryParseLevelSettings(string script, out float[] values)
        {
            values = null;
            if (string.IsNullOrEmpty(script)) return false;

            foreach (string line in script.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                if (line.IndexOf("Task_New", StringComparison.OrdinalIgnoreCase) < 0) continue;
                List<string> tokens = SplitArguments(line);
                int typeAt = FindTypeIndex(tokens, LevelTaskName);
                if (typeAt < 0) continue;

                var parsed = new List<float>();
                for (int i = typeAt + 1; i < tokens.Count && parsed.Count < 8; i++)
                {
                    float value;
                    if (TryFloat(tokens[i], out value)) parsed.Add(value);
                }
                if (parsed.Count != 8) continue;

                values = parsed.ToArray();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Mirrors ModelLODSettings_Changed: raise each distance to its
        /// predecessor, then scale into the engine's raw world units.
        /// </summary>
        internal static float[] EngineDistances(float[] authored)
        {
            var result = new float[5];
            for (int i = 0; i < 5; i++)
            {
                float value = authored[i];
                if (i > 0 && value < result[i - 1] / WorldUnitsPerMetre)
                    value = result[i - 1] / WorldUnitsPerMetre;
                result[i] = value * WorldUnitsPerMetre;
            }
            return result;
        }

        /// <summary>
        /// Human-readable explanation for an LOD token double-clicked in a
        /// decompiled lod.qvm. Returns null when the token is not LOD-related,
        /// so the editor's existing calltip behaviour is unchanged.
        /// </summary>
        internal static string DescribeToken(string token, string script)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(script)) return null;
            string name = token.Trim().Trim('"');

            if (string.Equals(name, ModelTaskName, StringComparison.OrdinalIgnoreCase))
            {
                List<LodQvmEntry> all = ParseModelSettings(script);
                return ModelTaskName + " is one authored LOD table per model.\n" +
                       "Five distances (metres): LOD 1, LOD 2, LOD 3, LOD 4, cutoff.\n" +
                       "The engine raises any distance below its predecessor, then\n" +
                       "scales by 4096; past the cutoff the model is not drawn.\n" +
                       "This file defines " + all.Count + " model table(s).";
            }

            if (string.Equals(name, LevelTaskName, StringComparison.OrdinalIgnoreCase))
            {
                float[] values;
                string text = LevelTaskName + " is the level-wide LOD fallback used when\n" +
                              "a model has no authored table: spline/rigid/bone detail\n" +
                              "and cull percentages, blend and range.\n";
                if (TryParseLevelSettings(script, out values))
                    text += "Values: detail " + values[0] + "/" + values[1] + "/" + values[2] +
                            ", cull " + values[3] + "/" + values[4] + "/" + values[5] +
                            ", blend " + values[6] + ", range " + values[7];
                else
                    text += "No LODSettings task was found in this script.";
                return text;
            }

            foreach (LodQvmEntry entry in ParseModelSettings(script))
            {
                if (!string.Equals(entry.Model, name, StringComparison.OrdinalIgnoreCase)) continue;
                var text = new StringBuilder();
                text.Append("LOD switch distances for '").Append(entry.Model).Append("' (metres):\n");
                for (int i = 0; i < 5; i++)
                    text.Append(DistanceNames[i]).Append(": ").Append(entry.Distances[i].ToString(CultureInfo.InvariantCulture)).Append('\n');
                text.Append("Engine raw units: ").Append(string.Join(", ", Array.ConvertAll(EngineDistances(entry.Distances), d => d.ToString(CultureInfo.InvariantCulture))));
                return text.ToString();
            }
            return null;
        }

        /// <summary>
        /// Short summary used when a lod.qvm script is loaded, so the editor can
        /// report what the file actually contains instead of showing raw script.
        /// </summary>
        internal static string DescribeDocument(string script)
        {
            List<LodQvmEntry> entries = ParseModelSettings(script);
            float[] values;
            bool level = TryParseLevelSettings(script, out values);
            return "LOD script: " + entries.Count + " ModelLODSettings table(s), " +
                   (level ? "1" : "no") + " level-wide LODSettings task.";
        }
    }
}
