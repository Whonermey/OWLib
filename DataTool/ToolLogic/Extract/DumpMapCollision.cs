using System;
using System.IO;
using System.Collections.Generic;
using System.Numerics;
using DataTool.Flag;
using DataTool.SaveLogic;
using TankLib;
using TankLib.Chunks;
using TankLib.Math;
using TankLib.STU.Types;
using TankLib.Helpers;
using static DataTool.Helper.IO;
using static DataTool.Helper.STUHelper;

namespace DataTool.ToolLogic.Extract;

// Extracts the map's COLLISION geometry = the placed models' render meshes
// transformed to world space. Output is a binary triangle soup:
//   magic "OWCT" (4 bytes) + uint32 triangleCount + (9 * float32) per triangle.
// The game's static world collision is the visual geometry itself (the Domino
// fixture registry holds only dynamic props, not walls).
[Tool("dump-map-collision", Description = "Extract map collision (render meshes -> world-space triangles)", CustomFlags = typeof(ExtractFlags))]
public class DumpMapCollision : ITool {
    public void Parse(ICLIFlags toolFlags) {
        var flags = (ExtractFlags) toolFlags;
        flags.EnsureOutputDirectory();
        Directory.CreateDirectory(flags.OutputPath);

        teModelChunk_RenderMesh.LoadAssetFunc = guid => OpenFile(guid);

        // optional filter: extra positionals are hex map indices
        var filter = new HashSet<uint>();
        for (int pi = 3; pi < flags.Positionals.Length; pi++) {
            if (uint.TryParse(flags.Positionals[pi], System.Globalization.NumberStyles.HexNumber, null, out uint idx)) filter.Add(idx);
        }

        foreach (ulong key in Program.TrackedFiles[0x9F]) {
            if (filter.Count > 0 && !filter.Contains(teResourceGUID.Index(key))) continue;
            var mapHeader = GetInstance<STUMapHeader>(key);
            if (mapHeader == null || mapHeader.m_D97BC44F == null || mapHeader.m_78715D57 == null) continue;

            int vc = Math.Min(mapHeader.m_D97BC44F.Length, mapHeader.m_78715D57.Length);
            for (int i = 0; i < vc; i++) {
                ulong variantGUID = mapHeader.m_78715D57[i].m_BF231F12;
                if (variantGUID == 0) continue;

                string outPath = Path.Combine(flags.OutputPath, $"collision_{teResourceGUID.Index(key):X8}_v{i}.bin");
                ExtractVariant(outPath, mapHeader, variantGUID);
            }
        }

        Logger.Log($"Collision extraction done -> {flags.OutputPath}");
    }

    private static void ExtractVariant(string outPath, STUMapHeader mapHeader, ulong variantGUID) {
        var tris = new List<float>(1 << 20);

        // MODEL_GROUP (0x01), SINGLE_MODEL (0x02), MODEL (0x08)
        foreach (byte t in new byte[] { 0x01, 0x02, 0x08 }) {
            teMapPlaceableData pd;
            try { pd = Map.GetPlaceableData(mapHeader, variantGUID, t); } catch { continue; }
            if (pd == null) continue;

            foreach (var placeable in pd.Placeables) {
                if (placeable is teMapPlaceableModelGroup mg) {
                    for (int gi = 0; gi < mg.Groups.Length; gi++) {
                        foreach (var entry in mg.Entries[gi]) {
                            AddModelTriangles(tris, mg.Header.Model, entry.Translation, entry.Scale, entry.Rotation);
                        }
                    }
                } else if (placeable is teMapPlaceableSingleModel sm) {
                    AddModelTriangles(tris, sm.Header.Model, sm.Header.Translation, sm.Header.Scale, sm.Header.Rotation);
                } else if (placeable is teMapPlaceableModel m) {
                    AddModelTriangles(tris, m.Header.Model, m.Header.Translation, m.Header.Scale, m.Header.Rotation);
                }
            }
        }

        using var fs = File.Create(outPath);
        using var bw = new BinaryWriter(fs);
        bw.Write(0x5443574F); // "OWCT"
        bw.Write(tris.Count / 9);
        foreach (float f in tris) bw.Write(f);

        Logger.Log($"  {outPath}: {tris.Count / 9} triangles");
    }

    private static void AddModelTriangles(List<float> tris, ulong modelGuid, teVec3 trans, teVec3 scale, teQuat rot) {
        if (modelGuid == 0) return;
        try {
            using Stream stream = OpenFile(modelGuid);
            if (stream == null) return;
            var cd = new teChunkedData(stream);
            var renderMesh = cd.GetChunk<teModelChunk_RenderMesh>();
            if (renderMesh?.Submeshes == null) return;

            var s = (Vector3) scale;
            var q = (Quaternion) rot;
            var t = (Vector3) trans;

            foreach (var sub in renderMesh.Submeshes) {
                if (sub.Vertices == null || sub.Indices == null) continue;
                int n = sub.Vertices.Length;
                var world = new Vector3[n];
                for (int i = 0; i < n; i++) {
                    world[i] = Vector3.Transform((Vector3) sub.Vertices[i] * s, q) + t;
                }
                for (int i = 0; i + 2 < sub.Indices.Length; i += 3) {
                    ushort a = sub.Indices[i], b = sub.Indices[i + 1], c = sub.Indices[i + 2];
                    if (a >= n || b >= n || c >= n) continue;
                    tris.Add(world[a].X); tris.Add(world[a].Y); tris.Add(world[a].Z);
                    tris.Add(world[b].X); tris.Add(world[b].Y); tris.Add(world[b].Z);
                    tris.Add(world[c].X); tris.Add(world[c].Y); tris.Add(world[c].Z);
                }
            }
        } catch {
            // skip malformed model
        }
    }
}
