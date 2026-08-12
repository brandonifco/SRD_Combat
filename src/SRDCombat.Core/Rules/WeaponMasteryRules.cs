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
/// <b>Executed:</b> Sap, Vex, Topple, Graze, Cleave and Slow — six of the eight,
/// covering 33 of the 38 printed weapons. Sap, Vex and Slow share the per-creature flag
/// with an author's-turn expiry; Topple and Graze ride the attack that already resolves;
/// Cleave is its own second swing, whose damage subtracts the positive ability modifier
/// <c>CombatAttack.AbilityModifier</c> carries for exactly this purpose.
/// </para>
/// <para>
/// <b>Not executed, with reasons.</b> <b>Push</b> is mechanically reachable — forced
/// movement over the grid — but "you can push" is a real choice a player would sometimes
/// decline (pushing an enemy out of your own reach), and this engine models no way to
/// decline; auto-pushing would fire part of a printed sentence wrongly often. <b>Nick</b>
/// is about "the extra attack of the Light property", and two-weapon fighting is not
/// modelled at all, so there is no extra attack to move.
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
        WeaponMastery.Cleave,
        WeaponMastery.Slow,
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
