using System;
using System.Reflection;
namespace NivarianCommander.MultiplayerAdapter
{
    // Only these installed and IL-reviewed companions are accepted. No unknown zero-call fallback.
    internal static class NativeRandomCompatibility
    {
        internal static readonly string[] AfterOwners =
            { "meow.trio.nivarian-simulation-random", "meow.tale-nivarian.multiplayer" };
        internal static bool KnownFloat(MethodInfo method, string name, int parameterCount)
        {
            if (method == null || !method.IsStatic || method.IsGenericMethod ||
                method.Name != name || method.ReturnType != typeof(float)) return false;
            var parameters = method.GetParameters();
            if (parameters.Length != parameterCount) return false;
            foreach (var parameter in parameters) if (parameter.ParameterType != typeof(float)) return false;
            string assembly = method.Module.Assembly.GetName().Name;
            string type = method.DeclaringType?.FullName;
            Guid module = method.Module.ModuleVersionId;
            return (assembly == "Meow.RaceTrioCompatibility" &&
                    type == "Meow.RaceTrioCompatibility.NivarianSimulationRandom" &&
                    module == new Guid("e74263bb-541c-47c7-99a5-3a9e7ed4f748")) ||
                   (assembly == "Meow.TaleNivarianCompatibility" &&
                    type == "Meow.TaleNivarianCompatibility.NivarianSimulationRandom" &&
                    module == new Guid("5e2123ef-6b73-413a-adb3-6d6d1d5eccfd"));
        }
    }
}
