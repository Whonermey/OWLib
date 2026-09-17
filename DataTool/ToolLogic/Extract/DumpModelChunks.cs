using System;
using System.IO;
using System.Linq;
using DataTool.Flag;
using DataTool.SaveLogic;
using TankLib;
using TankLib.Chunks;
using TankLib.STU.Types;
using TankLib.Helpers;
using static DataTool.Helper.IO;
using static DataTool.Helper.STUHelper;

namespace DataTool.ToolLogic.Extract;

// Lists chunk tags (4-char IDs) inside map models, so we can find an
// unhandled "collision" chunk. Chunk data for unknown tags is discarded by
// teChunkedData, but ChunkTags still records every tag seen.
[Tool("dump-model-chunks", Description = "List chunk tags inside map models", CustomFlags = typeof(ExtractFlags))]
public class DumpModelChunks : ITool {
    public void Parse(ICLIFlags toolFlags) {
        var flags = (ExtractFlags) toolFlags;
        flags.EnsureOutputDirectory();
        Directory.CreateDirectory(flags.OutputPath);

        using var summary = File.CreateText(Path.Combine(flags.OutputPath, "chunks.txt"));

        foreach (ulong key in Program.TrackedFiles[0x9F]) {
            var mapHeader = GetInstance<STUMapHeader>(key);
            if (mapHeader == null) continue;

            summary.WriteLine($"map {teResourceGUID.Index(key):X8}: m_map={TypeAndIndex(mapHeader.m_map)} m_baseMap={TypeAndIndex(mapHeader.m_baseMap)}");

            // dump chunk tags of m_map and m_baseMap
            DumpChunks(summary, flags.OutputPath, "  m_map", (ulong) mapHeader.m_map);
            DumpChunks(summary, flags.OutputPath, "  m_baseMap", (ulong) mapHeader.m_baseMap);

            // sample a few MODEL_GROUP models
            if (mapHeader.m_D97BC44F == null || mapHeader.m_78715D57 == null) continue;
            int vc = Math.Min(mapHeader.m_D97BC44F.Length, mapHeader.m_78715D57.Length);
            if (vc == 0) continue;
            ulong variantGUID = mapHeader.m_78715D57[0].m_BF231F12;

            teMapPlaceableData pd;
            try { pd = Map.GetPlaceableData(mapHeader, variantGUID, 0x01); } catch { continue; }
            if (pd == null) continue;

            int shown = 0;
            foreach (var placeable in pd.Placeables) {
                if (placeable is not teMapPlaceableModelGroup mg) continue;
                if (shown++ >= 5) break;
                summary.WriteLine($"  modelgroup model={TypeAndIndex(mg.Header.Model)}");
                DumpChunks(summary, flags.OutputPath, "    ", (ulong) mg.Header.Model);
            }
        }

        Logger.Log($"Wrote chunk tags to {flags.OutputPath}/chunks.txt");
    }

    private static string TypeAndIndex(ulong guid) {
        return $"{(guid >> 48):X4}.{teResourceGUID.Index(guid):X3}";
    }

    private static void DumpChunks(TextWriter w, string outDir, string indent, ulong guid) {
        if (guid == 0) return;
        try {
            using Stream stream = OpenFile(guid);
            if (stream == null) { w.WriteLine($"{indent}(no file)"); return; }
            var cd = new teChunkedData(stream, keepOpen: true);
            if (cd.Chunks == null) {
                // not a chunked file — dump full raw for format discovery
                stream.Position = 0;
                byte[] all = new byte[stream.Length];
                stream.Read(all, 0, all.Length);
                string fn = $"raw_{teResourceGUID.Index(guid):X3}_{(guid >> 48):X4}.bin";
                File.WriteAllBytes(Path.Combine(outDir, fn), all);
                w.WriteLine($"{indent}RAW ({all.Length} bytes) -> {fn}: {Convert.ToHexString(all.AsSpan(0, Math.Min(96, all.Length)))}");
                return;
            }
            var tags = string.Join(",", cd.ChunkTags);
            w.WriteLine($"{indent}chunks: {tags}");
        } catch (Exception e) {
            w.WriteLine($"{indent}error: {e.Message}");
        }
    }
}
