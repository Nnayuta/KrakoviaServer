// Provavelmente em um arquivo como Servidor/Models/ActionBarData.cs
using System;
using System.Collections.Generic;

[Serializable]
public class ActionBarData
{
    // NOVO: Adicionado um setter para que possa ser modificado externamente (pelo JsonConvert)
    public List<ActionBarSlotData> Slots { get; set; }

    // Construtor usado ao criar uma nova barra
    public ActionBarData(int size)
    {
        Slots = new List<ActionBarSlotData>(size);
        for (int i = 0; i < size; i++)
        {
            Slots.Add(new ActionBarSlotData());
        }
    }

    // =================================================================================
    // O MÉTODO QUE FALTA: Limpa todos os slots.
    // =================================================================================
    public void Clear()
    {
        foreach (var slot in Slots)
        {
            slot.ContentType = ActionBarContentType.None;
            slot.ContentID = string.Empty;
            slot.FallbackItemID = string.Empty;
        }
    }

    // NOVO: Construtor vazio necessário para a desserialização do JSON a partir do cliente
    public ActionBarData()
    {
        Slots = new List<ActionBarSlotData>();
    }
}

[Serializable]
public class ActionBarSlotData
{
    // As propriedades precisam de getters E setters para a serialização funcionar
    public ActionBarContentType ContentType { get; set; } = ActionBarContentType.None;
    public string ContentID { get; set; } = string.Empty;
    public string FallbackItemID { get; set; } = string.Empty;
}

public enum ActionBarContentType { None, Item, Ability }