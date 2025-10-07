using System;
using Newtonsoft.Json;

[Serializable]
public class CharacterAppearance
{
    // --- Gênero e Cores Base ---
    public bool IsFemale { get; set; }
    public string SkinColorHex { get; set; } = "#C58C85";
    public string HairColorHex { get; set; } = "#3B332A";

    // =================================================================================
    // >> NOVAS OPÇÕES DE CUSTOMIZAÇÃO <<
    // =================================================================================

    // --- Estilos de Mesh ---
    public int FaceStyleIndex { get; set; } = 0;
    public int HairStyleIndex { get; set; } = 0;
    public int BeardStyleIndex { get; set; } = 0; // Será ignorado se for mulher

    // --- Novas Cores ---
    public string EyeColorHex { get; set; } = "#6A4C38";   // Castanho padrão
    public string ScleraColorHex { get; set; } = "#FFFFFF"; // Branco padrão
    public string LipsColorHex { get; set; } = "#B4736A";  // Cor de lábio natural
    public string ScarColorHex { get; set; } = "#A56B61";   // Cor de cicatriz sutil
}