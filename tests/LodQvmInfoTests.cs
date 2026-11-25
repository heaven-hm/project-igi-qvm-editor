using System;
using System.Collections.Generic;
using QVM_Editor;

// Host-side tests for the lod.qvm reader. No WinForms/Scintilla dependency:
//   mcs -out:/tmp/lod-tests.exe tests/LodQvmInfoTests.cs QVMEditor/Utils/LodQvmInfo.cs
//   mono /tmp/lod-tests.exe
internal static class LodQvmInfoTests
{
    private static int failures;

    private static void Check(bool condition, string label)
    {
        Console.WriteLine((condition ? "PASS  " : "FAIL  ") + label);
        if (!condition) failures++;
    }

    private static void Near(float actual, float expected, string label)
    {
        Check(Math.Abs(actual - expected) < 0.0001f,
              label + " (got " + actual.ToString("R") + ", expected " + expected.ToString("R") + ")");
    }

    private const string Script =
        "// decompiled LOD.QVM (shape from lod.qvm in the retail install)\n" +
        "Task_New(-1,\"LODSettings\",\"\",100.0,90.0,80.0,20.0,30.0,40.0,1.0,4.0);\n" +
        "Task_New(-1, \"ModelLODSettings\", \"300_01_1\", \"300_01_1\", 70.0, 170.0, 300.0, 350.0, 500.0);\n" +
        "Task_New(-1, \"ModelLODSettings\", \"435_01_1\", \"435_01_1\", 0.0, 25.0, 60.0, 120.0, 250.0);\n" +
        "Task_New(-1, \"ModelLODSettings\", \"broken_1\", \"broken_1\", 1.0, 2.0, 3.0, 4.0);\n" +
        "Task_New(-1, \"EditRigidObj\", \"Some object\", 1.0, 2.0, 3.0, 4.0, 5.0);\n";

    private static int Main()
    {
        List<LodQvmEntry> entries = LodQvmInfo.ParseModelSettings(Script);
        Check(entries.Count == 2, "parses exactly the two complete ModelLODSettings tasks (incomplete task skipped)");
        Check(entries[0].Model == "300_01_1" && entries[1].Model == "435_01_1",
              "model names come from the task name argument");
        Near(entries[0].Distances[0], 70f, "first distance is 'Distance to LOD 1'");
        Near(entries[0].Distances[4], 500f, "fifth distance is 'Distance to cutoff'");

        float[] level;
        Check(LodQvmInfo.TryParseLevelSettings(Script, out level), "level-wide LODSettings task found");
        Check(level.Length == 8, "LODSettings has eight parameters");
        Near(level[0], 100f, "splineDetail parsed");
        Near(level[7], 4f, "range parsed");

        float[] raw = { 70f, 170f, 300f, 350f, 500f };
        float[] world = LodQvmInfo.EngineDistances(raw);
        Near(world[0], 70f * 4096f, "LOD 1 distance scaled by 4096");
        Near(world[4], 500f * 4096f, "cutoff distance scaled by 4096");

        float[] descending = { 100f, 50f, 300f, 20f, 500f };
        float[] clamped = LodQvmInfo.EngineDistances(descending);
        Near(clamped[1], 100f * 4096f, "engine raises a distance below its predecessor");
        Near(clamped[3], 300f * 4096f, "clamp is cumulative, matching ModelLODSettings_Changed");

        string modelTip = LodQvmInfo.DescribeToken("300_01_1", Script);
        Check(modelTip != null && modelTip.Contains("Distance to cutoff: 500") &&
              modelTip.Contains("Engine raw units"), "double-clicking a model explains its five distances");
        string typeTip = LodQvmInfo.DescribeToken("\"ModelLODSettings\"", Script);
        Check(typeTip != null && typeTip.Contains("2 model table(s)"), "task-type calltip explains ModelLODSettings");
        string levelTip = LodQvmInfo.DescribeToken("LODSettings", Script);
        Check(levelTip != null && levelTip.Contains("detail 100/90/80"), "task-type calltip reports level settings");
        Check(LodQvmInfo.DescribeToken("EditRigidObj", Script) == null,
              "non-LOD tokens fall back to the editor's existing calltips");

        Check(LodQvmInfo.ContainsLodTasks(Script), "lod.qvm is detected as a LOD script");
        Check(!LodQvmInfo.ContainsLodTasks("Task_New(-1,\"Building\",\"x\",1.0);"),
              "ordinary level scripts are not LOD scripts");
        Check(LodQvmInfo.DescribeDocument(Script) ==
              "LOD script: 2 ModelLODSettings table(s), 1 level-wide LODSettings task.",
              "load summary counts authored tables");

        Check(LodQvmInfo.ParseModelSettings(null).Count == 0, "null script is handled");
        Check(!LodQvmInfo.TryParseLevelSettings("", out level), "empty script has no level settings");

        Console.WriteLine(failures == 0 ? "ALL TESTS PASSED" : failures + " TEST(S) FAILED");
        return failures == 0 ? 0 : 1;
    }
}
