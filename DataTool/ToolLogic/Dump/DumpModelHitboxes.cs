using System;
using System.Collections.Generic;
using System.IO;
using DataTool.Flag;
using DataTool.Helper;
using DataTool.ToolLogic.List;
using TankLib;
using TankLib.Chunks;
using TankLib.Math;
using TankLib.STU.Types;
using TankLib.STU.Types.Enums;
using static DataTool.Program;

namespace DataTool.ToolLogic.Dump {
    // Dump per-model damage hit volumes (STUModel.m_CB4D298D) as JSON.
    // Enumeration is model-first (type 0xC) — no hero->model chain needed:
    // the caller matches models to heroes by the volume-count signature.
    [Tool("dump-model-hitboxes", Description = "Dump per-model damage hit volumes (spheres/capsules/hulls)", IsSensitive = true, CustomFlags = typeof(ListFlags))]
    class DumpModelHitboxes : ITool {
        static bool IsDamageSet(Enum_7A1887F8 flags) => flags == Enum_7A1887F8.x8437BB52; // 0x1

        static void DumpVec(TextWriter w, string name, teVec3 v) {
            w.Write($"\"{name}\":[{v.X:F6},{v.Y:F6},{v.Z:F6}]");
        }

        public void Parse(ICLIFlags toolFlags) {
            System.Threading.Thread.CurrentThread.CurrentCulture =
                System.Globalization.CultureInfo.InvariantCulture;
            Console.Out.WriteLine("[");
            bool firstModel = true;

            foreach (var guid in Program.TrackedFiles[0xC]) {
                Stream file;
                try {
                    file = IO.OpenFile(guid);
                } catch {
                    continue;
                }
                if (file == null) continue;

                STUModel model;
                try {
                    using (file)
                    using (BinaryReader reader = new BinaryReader(file)) {
                        teChunkedData chunk = new teChunkedData(reader);
                        teModelChunk_STU stuChunk = chunk.GetChunk<teModelChunk_STU>();
                        model = stuChunk?.StructuredData;
                    }
                } catch {
                    continue;
                }

                if (model?.m_CB4D298D == null || model.m_CB4D298D.Length == 0) {
                    continue;
                }

                var spheres = new List<string>();
                var capsules = new List<string>();
                int hulls = 0, meshes = 0;

                foreach (var v in model.m_CB4D298D) {
                    var shape = v.m_B7C8314A;
                    int bone = v.m_30F925F4;
                    bool damage = IsDamageSet(v.m_89AABA94);
                    string tag = damage ? "damage" : "other";

                    if (shape is STU_4614D0E5 sp) {
                        var sb = new StringWriter();
                        sb.Write("{");
                        DumpVec(sb, "center", sp.m_A8D12F28);
                        sb.Write($",\"radius\":{sp.m_radius:F6},\"bone\":{bone},\"tag\":\"{tag}\"");
                        sb.Write("}");
                        spheres.Add(sb.ToString());
                    } else if (shape is STU_4C8D9F5E cap) {
                        var sb = new StringWriter();
                        sb.Write("{");
                        DumpVec(sb, "p1", cap.m_10EFDE90);
                        sb.Write(",");
                        DumpVec(sb, "p2", cap.m_C320C90B);
                        sb.Write($",\"radius\":{cap.m_radius:F6},\"bone\":{bone},\"tag\":\"{tag}\"");
                        sb.Write("}");
                        capsules.Add(sb.ToString());
                    } else if (shape is STU_B3800E70) {
                        hulls++;
                    } else if (shape is STU_537E56E4) {
                        meshes++;
                    }
                }

                if (spheres.Count == 0 && capsules.Count == 0 && hulls == 0) {
                    continue;
                }

                var msb = new StringWriter();
                if (!firstModel) msb.Write(",");
                firstModel = false;
                msb.Write("{\n");
                msb.Write($"  \"guid\":\"{guid:X16}\",\n");
                msb.Write($"  \"spheres\":[{string.Join(",", spheres)}],\n");
                msb.Write($"  \"capsules\":[{string.Join(",", capsules)}],\n");
                msb.Write($"  \"hulls\":{hulls},\n");
                msb.Write($"  \"meshes\":{meshes}\n");
                msb.Write("}");
                Console.Out.WriteLine(msb.ToString());
            }

            Console.Out.WriteLine("]");
        }
    }
}
