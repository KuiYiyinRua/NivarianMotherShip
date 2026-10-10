namespace NivarianCommander
{
    public static class FieldEconomy
    {
        public const int UpkeepInterval = 6000;
        public static bool HasField => CommanderField.Current?.Exists == true;
        public static int PowerCost => HasField ? 100 : 1000;
        public static int VolatileCost => HasField ? 50 : 500;
        public static int CooldownTicks => FieldRecallActions.ResearchFinished ? 60000 : 30000;
        public static int PowerFor(CommanderAbilityDef ability) => ability.number == 1 ? PowerCost : ability.powerCost;
        public static int VolatileFor(CommanderAbilityDef ability) => ability.number == 1 ? VolatileCost : ability.volatileCost;
        public static int CooldownFor(CommanderAbilityDef ability) => ability.number == 1 ? CooldownTicks : ability.cooldownTicks;
    }
}
