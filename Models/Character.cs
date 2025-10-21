// Models/Character.cs
using System;
using System.Collections.Generic;

[Serializable]
public class CharacterSummary
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string? Class { get; set; }
    public int Level { get; set; }
    public CharacterAppearance Appearance { get; set; } = new CharacterAppearance();
    public Dictionary<EquipmentSlot, string> EquippedItems { get; set; } = new Dictionary<EquipmentSlot, string>();
}

public class Character
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string ClassID { get; set; }
    public int Level { get; set; }
    public long CurrentExperience { get; set; } = 0;
    public CharacterAppearance Appearance { get; set; } = new CharacterAppearance();

    public Dictionary<EquipmentSlot, string> EquippedItems { get; set; } = new Dictionary<EquipmentSlot, string>();

    public Character()
    {
        Id = Guid.NewGuid().ToString("N");
        Name = string.Empty;
        ClassID = string.Empty;
        Level = 1;
    }

    // O método ToSummary agora será mais completo
    public CharacterSummary ToSummary()
    {
        return new CharacterSummary
        {
            Id = Id,
            Name = Name,
            Class = ClassID,
            Level = Level,
            Appearance = this.Appearance,
            EquippedItems = this.EquippedItems
        };
    }
}