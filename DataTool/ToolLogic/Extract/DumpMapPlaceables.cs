using System;
using System.IO;
using DataTool.Flag;
using DataTool.SaveLogic;
using TankLib;
using TankLib.STU.Types;
using TankLib.Helpers;
using static DataTool.Helper.IO;
using static DataTool.Helper.STUHelper;

namespace DataTool.ToolLogic.Extract;

// Dumps raw map placeable chunks so we can discover the collision format.
// Overwatch maps are 0x0BC files composed of placeable chunks keyed by
// (variantGUID & ~0xFFFFFFFF00000000) | 0x0DD0000100000000 | (type << 32).
// OWLib parses the known types (MODEL_GROUP..SEQUENCE); the static collision
// lives in an UNPARSED type (likely 0x12-0x17). This tool emits a summary of
// every present type + raw bytes for the unknown ones.
[Tool("dump-map-placeables", Description = "Dump raw map placeable chunks (discover collision format)", CustomFlags = typeof(ExtractFlags))]
public class DumpMapPlaceables : ITool {
    public void Parse(ICLIFlags toolFlags) {
        var flags = (ExtractFlags) toolFlags;
        flags.EnsureOutputDirectory();

        int mapsSeen = 0;
        foreach (ulong key in Program.TrackedFiles[0x9F]) {
            var mapHeader = GetInstance<STUMapHeader>(key);
            if (mapHeader == null || mapHeader.m_D97BC44F == null || mapHeader.m_78715D57 == null) continue;

            int variantCount = Math.Min(mapHeader.m_D97BC44F.Length, mapHeader.m_78715D57.Length);

            for (int i = 0; i < variantCount; i++) {
                var variantResultingMap = mapHeader.m_78715D57[i];
                ulong variantGUID = variantResultingMap.m_BF231F12;
                if (variantGUID == 0) continue;

                string mapDir = Path.Combine(flags.OutputPath, $"map_{teResourceGUID.Index(key):X8}", $"variant_{i}");
                Directory.CreateDirectory(mapDir);

                using (var summary = File.CreateText(Path.Combine(mapDir, "_summary.txt"))) {
                    summary.WriteLine($"map_index={teResourceGUID.Index(key):X8} variant={i} variantGUID={variantGUID:X16}");

                    for (int t = 0; t <= 0x20; t++) {
                        teMapPlaceableData pd;
                        try {
                            pd = Map.GetPlaceableData(mapHeader, variantGUID, (byte) t);
                        } catch (Exception) {
                            continue; // chunk type has a different/non-placeable format
                        }
                        if (pd == null || pd.Header.PlaceableCount == 0) continue;

                        bool known = teMapPlaceableData.Manager.Types.ContainsKey((TankLib.Enums.teMAP_PLACEABLE_TYPE) t);
                        summary.WriteLine($"type 0x{t:X2} ({NameForType(t)}) count={pd.Header.PlaceableCount} known={known}");

                        if (known) continue; // only raw-dump unknown types

                        string typeDir = Path.Combine(mapDir, $"type_{t:X2}");
                        Directory.CreateDirectory(typeDir);

                        int actualCount = Math.Min(pd.CommonStructures.Length, pd.Placeables.Length);
                        int dumpLimit = Math.Min(3, actualCount);
                        long totalBytes = 0;
                        for (int p = 0; p < actualCount; p++) {
                            var cs = pd.CommonStructures[p];
                            byte[] raw = (pd.Placeables[p] is teMapPlaceableDummy dummy) ? dummy.Data : Array.Empty<byte>();
                            totalBytes += raw.Length;
                            if (p >= dumpLimit) continue;
                            string fn = Path.Combine(typeDir, $"{p:D4}_{cs.UUID.Value:N}.bin");
                            File.WriteAllBytes(fn, raw);
                        }
                        summary.WriteLine($"  total_raw_bytes={totalBytes} dumped_first={dumpLimit}");
                    }
                }
            }

            mapsSeen++;
        }

        Logger.Log($"Dumped {mapsSeen} maps to {flags.OutputPath}");
    }

    private static string NameForType(int t) {
        return t switch {
            0x01 => "MODEL_GROUP",
            0x02 => "SINGLE_MODEL",
            0x03 => "OCCLUDER",
            0x04 => "REFLECTIONPOINT",
            0x05 => "CLUTTER",
            0x06 => "LABEL",
            0x07 => "TEXT",
            0x08 => "MODEL",
            0x09 => "LIGHT",
            0x0A => "AREA",
            0x0B => "ENTITY",
            0x0C => "SOUND",
            0x0D => "EFFECT",
            0x0E => "FOG",
            0x0F => "INDOORBOX",
            0x10 => "POSTPROCESSING",
            0x11 => "PLANAR_REFLECTION_SURFACE",
            0x12 => "UNKNOWN_12",
            0x13 => "UNKNOWN_13",
            0x14 => "UNKNOWN_14 (COLLISION?)",
            0x15 => "UNKNOWN_15 (PATHING?)",
            0x16 => "UNKNOWN_16",
            0x17 => "UNKNOWN_17",
            0x18 => "SEQUENCE",
            _ => "UNKNOWN",
        };
    }
}
