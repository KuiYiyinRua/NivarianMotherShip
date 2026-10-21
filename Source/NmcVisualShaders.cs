using UnityEngine;

namespace NivarianCommander
{
    internal static class NmcVisualShaders
    {
        internal static Shader Colored()
        {
#if RIMWORLD_1_7
            return Native("NMC_VertexAlpha");
#else
            return Shader.Find("Hidden/Internal-Colored");
#endif
        }
        internal static Shader ForBlend(Shader original, bool additive)
        {
#if RIMWORLD_1_7
            return additive ? Native("NMC_VertexAdditive") : original;
#else
            return original;
#endif
        }
#if RIMWORLD_1_7
        static Shader Native(string name) => Waylith.Rendering.ShaderRegistry.TryResolve(name, out var shader)
            ? Shader.FromEngineShader(name, shader) : null;
#endif
    }
}
