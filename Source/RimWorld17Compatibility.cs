#if RIMWORLD_1_7
namespace NivarianCommander
{
    // ShaderLab material property values retained by Waylith's Unity shaders.
    internal enum BlendMode { One = 1, SrcAlpha = 5, OneMinusSrcAlpha = 10 }
    internal enum CullMode { Off = 0 }
    internal enum CompareFunction { LessEqual = 4 }

    internal static class RimUnityLinePositions
    {
        internal static void SetPosition(this RimUnity.LineRenderer line, int index, UnityEngine.Vector3 position)
            => line.SetPosition(index, new RimUnity.Vector3(position.x, position.y, position.z));
    }
}
#endif
