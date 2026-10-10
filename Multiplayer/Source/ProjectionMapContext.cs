using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace NivarianCommander.MultiplayerAdapter
{
    public static class ProjectionMapContext
    {
        static Type asyncType;
        static Func<Map, object> mapTime;
        static MethodInfo before, after;
        static FieldInfo ticking, executing, previousTime, currentMapIndex;
        public static void Install()
        {
            asyncType = AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp");
            var getter = AccessTools.DeclaredMethod(AccessTools.TypeByName("Multiplayer.Client.Extensions"), "AsyncTime", new[] { typeof(Map) });
            if (getter == null || !getter.IsStatic || getter.ReturnType != asyncType)
                throw new MissingMethodException("[NMC S19-MP] Installed official Map.AsyncTime accessor unresolved");
            mapTime = (Func<Map, object>)Delegate.CreateDelegate(typeof(Func<Map, object>), getter);
            before = AccessTools.DeclaredMethod(asyncType, "PreContext"); after = AccessTools.DeclaredMethod(asyncType, "PostContext");
            ticking = AccessTools.Field(asyncType, "tickingMap"); executing = AccessTools.Field(asyncType, "executingCmdMap");
            previousTime = AccessTools.Field(asyncType, "prevTime"); currentMapIndex = AccessTools.Field(typeof(Game), "currentMapIndex");
            if (asyncType == null || before == null || after == null || ticking == null || executing == null || previousTime == null || currentMapIndex == null)
                throw new MissingMemberException("[NMC S19-MP] Installed native map-context scope unresolved");
            ProjectionContext.Mutation = Run;
        }
        static void Run(Map map, Action action)
        {
            if (!MP.IsInMultiplayer || map == null) { action(); return; }
            var context = (Map)ticking.GetValue(null) ?? (Map)executing.GetValue(null);
            if (context == map) { action(); return; }
            var component = mapTime(map);
            if (component == null || component.GetType() != asyncType)
                throw new InvalidOperationException("[NMC S19-MP] Official Map.AsyncTime component missing or type mismatch");
            object oldTicking = ticking.GetValue(null), oldIndex = currentMapIndex.GetValue(Current.Game), oldTime = previousTime.GetValue(component);
            bool entered = false;
            try
            {
                currentMapIndex.SetValue(Current.Game, (sbyte)map.Index); ticking.SetValue(null, map);
                before.Invoke(component, null); entered = true;
                action();
            }
            finally
            {
                try { if (entered) after.Invoke(component, null); }
                finally
                {
                    previousTime.SetValue(component, oldTime);
                    ticking.SetValue(null, oldTicking); currentMapIndex.SetValue(Current.Game, oldIndex);
                }
            }
        }
    }
}
