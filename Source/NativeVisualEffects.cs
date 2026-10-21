using LumiParticle.Data;
using LumiParticle.Effects;
using LumiParticle.Items;
using UnityEngine;
using Verse;
#if RIMWORLD_1_7
using LumiParticle.Core.Lifetime;
#endif

namespace NivarianCommander
{
    internal static class NativeVisualEffects
    {
        internal static LumiAnimatorItem SpawnAnimator(Map map, LumiAnimatorData data, Thing follow)
        {
#if RIMWORLD_1_7
            return LumiParticleEffects.SpawnAnimator(map, data, Renewable(), follow)?.Item;
#else
            return LumiParticleEffects.SpawnAnimator(map, data, follow);
#endif
        }

        internal static LumiParticleItem SpawnParticle(Map map, LumiParticleData data, Thing follow)
        {
#if RIMWORLD_1_7
            return LumiParticleEffects.SpawnParticle(map, data, Renewable(), follow)?.Item;
#else
            return LumiParticleEffects.SpawnParticle(map, data, follow);
#endif
        }

        internal static LumiLaserLineItem SpawnLine(Map map, Vector3 start, Vector3 end, LumiLaserLineData data)
        {
#if RIMWORLD_1_7
            var lifetime = LumiVisualLifetime.OneShot(data.fadeIn + data.hold + data.fadeOut, LumiGameClock.Instance);
            lifetime.SourceTag = "NMC.PrismBeam";
            return LumiParticleEffects.SpawnLine(map, start, end, data.fadeIn, data.hold, data.fadeOut,
                lifetime, data.prefabName, data.startColor, data.endColor)?.Item;
#else
            return LumiParticleEffects.SpawnLine(map, start, end, data);
#endif
        }

#if RIMWORLD_1_7
        static LumiVisualLifetime Renewable()
        {
            var lifetime = LumiVisualLifetime.Renewable(2f, LumiGameClock.Instance);
            lifetime.SourceTag = "NMC.Formation";
            return lifetime;
        }
#endif
    }
}
