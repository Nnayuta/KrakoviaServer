// NOVO ARQUIVO: Server/Utilities/ItemLevelConverter.cs

using System.Collections.Generic;
using System.Linq;

public static class ItemLevelConverter
{
    // Mapeia o NÍVEL DA CRIATURA para o NÍVEL DO ITEM (iLvl)
    private static readonly Dictionary<int, (int min, int max)> creatureLevelToItemLevel = new Dictionary<int, (int, int)>
    {
        // Exemplo: Um NPC nível 10 pode dropar itens com iLvl entre 26 e 30
        { 1, (1, 5) },    { 2, (6, 10) },   { 3, (11, 15) },  { 4, (14, 18) },  { 5, (16, 20) },
        { 6, (18, 22) },  { 7, (20, 24) },  { 8, (21, 25) },  { 9, (23, 28) },  { 10, (26, 30) },
        { 11, (28, 32) }, { 12, (30, 34) }, { 13, (31, 35) }, { 14, (33, 38) }, { 15, (36, 40) },
        { 16, (38, 42) }, { 17, (40, 44) }, { 18, (41, 45) }, { 19, (43, 48) }, { 20, (45, 50) }
    };

    // Mapeia o NÍVEL DO ITEM (iLvl) para o NÍVEL REQUERIDO para equipar
    private static readonly SortedList<int, int> itemLevelToRequiredLevel = new SortedList<int, int>
    {
        // iLvl <= X -> ReqLvl Y
        { 5, 1 },   { 10, 2 },  { 15, 3 },  { 20, 5 },  { 25, 8 },
        { 30, 10 }, { 35, 13 }, { 40, 16 }, { 45, 20 },
        { 999, 20 } // Um valor máximo para garantir que sempre encontre um teto
    };

    private static readonly Random _random = new Random();

    /// <summary>
    /// Gera um iLvl aleatório com base no nível da criatura que dropou o item.
    /// </summary>
    public static int GetItemLevelForCreature(int creatureLevel)
    {
        if (creatureLevelToItemLevel.TryGetValue(creatureLevel, out var range))
        {
            return _random.Next(range.min, range.max + 1);
        }
        // Fallback: se o nível da criatura não estiver na tabela, o iLvl é o dobro do nível.
        return creatureLevel * 2;
    }

    /// <summary>
    /// Determina o nível requerido para equipar um item com base no seu iLvl.
    /// </summary>
    public static int GetRequiredLevelForItemLevel(int itemLevel)
    {
        foreach (var pair in itemLevelToRequiredLevel)
        {
            if (itemLevel <= pair.Key)
            {
                return pair.Value;
            }
        }
        return 1; // Fallback
    }
}