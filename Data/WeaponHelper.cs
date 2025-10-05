using System.Collections.Generic;

// Este helper estático pode ser acessado de qualquer lugar no servidor
public static class WeaponHelper
{
    // Um conjunto (HashSet) para busca ultra-rápida.
    // Ele contém todos os tipos de arma considerados corpo a corpo.
    private static readonly HashSet<WeaponType> MeleeWeaponTypes = new HashSet<WeaponType>
    {
        WeaponType.Sword1H, WeaponType.Axe1H, WeaponType.Mace1H,
        WeaponType.Dagger, WeaponType.Fist,
        WeaponType.Sword2H, WeaponType.Axe2H, WeaponType.Mace2H,
        WeaponType.Polearm, WeaponType.Staff
    };

    // Um conjunto para as armas de longo alcance
    private static readonly HashSet<WeaponType> RangedWeaponTypes = new HashSet<WeaponType>
    {
        WeaponType.Bow, WeaponType.Crossbow, WeaponType.Gun
    };

    public static bool IsMelee(WeaponType type) => MeleeWeaponTypes.Contains(type);
    public static bool IsRanged(WeaponType type) => RangedWeaponTypes.Contains(type);
}