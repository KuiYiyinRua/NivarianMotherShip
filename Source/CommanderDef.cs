using System.Collections.Generic;
using Verse;

namespace NivarianCommander
{
    public class CommanderAbilityDef : Def
    {
        public int number, powerCost, volatileCost, cooldownTicks;
        [MustTranslate] public string group;
        [MustTranslate] public string researchLabel;
        [MustTranslate] public string extraCost;
        [MustTranslate] public string durationLabel;
        public string iconPath;
        public string Code => "S" + number.ToString("00");
        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (number < 1 || number > 19) yield return "Ability number must be 1..19";
            if (powerCost < 0 || volatileCost < 0 || cooldownTicks < 0) yield return "Negative ability price or cooldown";
        }
    }
}
