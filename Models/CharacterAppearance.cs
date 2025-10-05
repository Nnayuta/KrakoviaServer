// Servidor/Models/CharacterAppearance.cs
using System;

[Serializable]
public class CharacterAppearance
{
    // true = Feminino, false = Masculino
    public bool IsFemale { get; set; } = true;

    // As cores são salvas como strings hexadecimais (ex: "#FFFFFF")
    public string SkinColorHex { get; set; } = "#C58C85"; // Cor de pele padrão
    public string HairColorHex { get; set; } = "#514234"; // Cor de cabelo padrão

    // Futuramente, você pode adicionar mais:
    // public int FaceId { get; set; } = 1;
    // public int HairId { get; set; } = 1;
}