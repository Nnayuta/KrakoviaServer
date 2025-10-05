// Servidor/Managers/ExperienceManager.cs
using System.Collections.Generic;

public static class ExperienceManager
{
    public const int MAX_LEVEL = 60; // Defina seu nível máximo aqui

    // Dicionário com a quantidade de XP necessária para passar do Nível X para o Nível X+1.
    private static readonly Dictionary<int, long> _xpForLevel = new Dictionary<int, long>
    {
        // Nível | XP para o próximo
        { 1, 400 },
        { 2, 900 },
        { 3, 1500 },
        { 4, 2300 },
        { 5, 3300 },
        { 6, 4500 },
        { 7, 6000 },
        { 8, 7800 },
        { 9, 9900 },
        { 10, 12400 },
    };

    /// <summary>
    /// Retorna a quantidade de XP necessária para avançar do nível fornecido.
    /// </summary>
    public static long GetExperienceForLevel(int level)
    {
        if (level >= MAX_LEVEL)
        {
            return long.MaxValue; // Se já está no nível máximo, retorna um valor "infinito"
        }
        _xpForLevel.TryGetValue(level, out long requiredXp);
        return requiredXp > 0 ? requiredXp : long.MaxValue;
    }
}