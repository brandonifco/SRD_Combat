using SRDCombat.Core.Definitions;

namespace SRDCombat.Core.Rules;

/// <summary>
/// Which mastery properties this engine executes, and the arithmetic they need.
/// </summary>
/// <remarks>
/// <para>
/// <b>The fifth curated allowlist</b>, on the same terms as the others: a printed
/// property is executed only alongside the code that does it, and a draft claiming
/// mastery of a weapon whose property is not executed is <b>refused by name</b> rather
/// than granting a feature that silently does nothing.
/// </para>
/// <para>
/// <b>Executed:</b> Sap, Vex, Topple and Graze — four of the eight, covering 21 of the
/// 38 printed weapons. Sap and Vex needed a per-creature flag with a turn-boundary
/// expiry; Topple and Graze are riders on an attack that already resolves.
/// </para>
/// <para>
/// <b>Not executed, with reasons.</b> <b>Push</b> moves a creature 10 feet, and nothing
/// in this engine moves a creature except on its own turn — forced movement is a model
/// that does not exist. <b>Nick</b> is about "the extra attack of the Light property",
/// and two-weapon fighting is not modelled at all, so there is no extra attack to move.
/// <b>Slow</b> reduces Speed by 10 feet until the start of the attacker's next turn,
/// which needs a speed modifier carrying a turn-boundary expiry — the condition model has
/// expiries but Speed is not a condition. <b>Cleave</b> is the closest to reachable and
/// the most missed, since it is the Greataxe's and therefore the Barbarian's: it needs a
/// second attack whose damage omits the ability modifier, and <c>CombatAttack</c> folds
/// that modifier into its damage where it cannot be taken back out.
/// </para>
/// </remarks>
public static class WeaponMasteryRules
{
    /// <summary>Mastery properties the engine executes.</summary>
    public static IReadOnlyList<WeaponMastery> Executed { get; } =
    [
        WeaponMastery.Sap,
        WeaponMastery.Vex,
        WeaponMastery.Topple,
        WeaponMastery.Graze,
    ];

    /// <summary>Whether the engine executes this property.</summary>
    public static bool Executes(WeaponMastery mastery) => Executed.Contains(mastery);

    /// <summary>
    /// Topple's save DC: "8 plus the ability modifier used to make the attack roll and
    /// your Proficiency Bonus".
    /// </summary>
    public static int ToppleDifficultyClass(int abilityModifier, int proficiencyBonus) =>
        8 + abilityModifier + proficiencyBonus;
}
