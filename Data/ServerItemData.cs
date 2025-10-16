// Data/ServerItemData.cs (NOVO ARQUIVO NO SERVIDOR)
using System.Collections.Generic;

// Enums replicados do cliente
public enum ItemQuality { Common, Uncommon, Rare, Epic, Legendary }
public enum ItemType { Weapon, Shield, Armor, Consumable }
public enum EquipmentSlot { MainHand, OffHand, Head, Chest, Legs, Feet, Hands, Cloak }
public enum WeaponHandType { OneHanded, TwoHanded }
public enum ArmorType { Cloth, Leather, Mail, Plate }
public enum WeaponType
{
    Sword1H, Axe1H, Mace1H, Dagger, Fist,
    Sword2H, Axe2H, Mace2H, Polearm, Staff,
    Bow, Crossbow, Gun
}

public class ServerJunkItemData : ServerItemData
{
    // Intencionalmente vazio.
}

// Seu enum CombatStyle continua existindo, mas agora é apenas para ANIMAÇÃO
public enum CombatStyle { Unarmed, Melee, Ranged, Magic }

// A classe base para todos os itens no servidor
public class ServerItemData
{
    public string itemID { get; set; }
    public string itemName { get; set; }
    public ItemQuality quality { get; set; }
    public int maxStackSize { get; set; } = 1;
    public int requiredLevel { get; set; } = 1;
    public int sellPrice { get; set; } = 0;
    // Propriedade de durabilidade (opcional por item)
    public int? Durability { get; set; }
    // Propriedade terciária
    public bool IsIndestructible { get; set; } = false;
    public List<BaseStatData> Stats { get; set; } = new List<BaseStatData>();

    [Newtonsoft.Json.JsonIgnore] // Garante que o serializador não tente salvar esta propriedade no JSON
    public bool isStackable => maxStackSize > 1;
}

// Classe base para equipamentos
public class ServerEquipmentData : ServerItemData
{
    public EquipmentSlot equipmentSlot { get; set; }
}

// Classes finais e concretas
public class ServerWeaponData : ServerEquipmentData
{
    // Stats específicos de armas
    public float WeaponDamage { get; set; }
    public float WeaponSpeed { get; set; }
    public CombatStyle combatStyle { get; set; }
    public WeaponHandType handType { get; set; }
    public WeaponType weaponType { get; set; }
}

public class ServerArmorData : ServerEquipmentData
{
    public ArmorType armorType { get; set; }
}

public class ServerConsumableData : ServerItemData
{
    // Efeitos Instantâneos (para poções, etc.)
    public int InstantHealthGain { get; set; }
    public int InstantResourceGain { get; set; }

    // Efeito de Status Aplicado (para comidas, elixires, etc.)
    // Armazena o ID do StatusEffect a ser buscado e aplicado pelo servidor.
    // Ex: "buff_well_fed", "elixir_of_fortitude"
    public string StatusEffectID { get; set; }
}
