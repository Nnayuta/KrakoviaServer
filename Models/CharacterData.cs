// Servidor/Models/CharacterData.cs
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

/// <summary>
/// Armazena os dados persistentes de um �nico personagem no servidor.
/// </summary>
public class CharacterData
{
    public string CharacterId { get; set; }
    public string ClassID { get; set; }
    public int Level { get; set; }
    public long CurrentExperience { get; set; }
    public long TotalBronze { get; set; }
    public string Position { get; set; }
    public Vector3 PositionVec { get; set; }
    public int InventorySize { get; set; }

    public Inventory PlayerInventory { get; set; }
    public Equipment PlayerEquipment { get; set; }
    public ActionBarData PlayerActionBar { get; set; }
    public CharacterAppearance Appearance { get; set; }
    public PlayerQuestLog QuestLog { get; set; }

    // Construtor que recebe os dados de inicializa��o.
    public CharacterData(string characterId, string classId, int level, CharacterAppearance appearance)
    {
        this.CharacterId = characterId;
        this.ClassID = classId;
        this.Level = level;
        this.TotalBronze = 0;
        this.CurrentExperience = 0;
        this.Appearance = appearance;
        this.QuestLog = new PlayerQuestLog();

        this.Position = "160,70,944";
        string[] splitPos = this.Position.Split(',');

        this.PositionVec = new Vector3(
            float.Parse(splitPos[0], CultureInfo.InvariantCulture),
            float.Parse(splitPos[1], CultureInfo.InvariantCulture),
            float.Parse(splitPos[2], CultureInfo.InvariantCulture));

        // Inicializa invent�rio, equipamento e barras de a��o vazios.
        // Eles ser�o preenchidos pelo CharacterDatabase se for a primeira vez.
        this.InventorySize = 20; // Tamanho inicial padrão
        this.PlayerInventory = new Inventory(this.InventorySize);
        this.PlayerEquipment = new Equipment();
        this.PlayerActionBar = new ActionBarData(12);
    }
}