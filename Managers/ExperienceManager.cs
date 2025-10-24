// Servidor/Managers/ExperienceManager.cs
using System;

public static class ExperienceManager
{
    public const int MAX_LEVEL = 60;

    // ===================================================================
    // NOVAS CONSTANTES PARA BALANCEAMENTO (AQUI VOCÊ AJUSTA O JOGO!)
    // ===================================================================

    /// <summary>
    /// Multiplicador global de XP. 1.0f = normal, 2.0f = dobro de XP, etc.
    /// Ótimo para eventos de "Double XP".
    /// </summary>
    public const float SERVER_XP_RATE = 37.5f;

    /// <summary>
    /// A base de XP que um monstro "padrão" do mesmo nível do jogador concede.
    /// Este é o seu principal ponto de ajuste para a velocidade de progressão.
    /// </summary>
    private const int BASE_XP_REWARD = 45;

    /// <summary>
    /// Um valor adicional de XP por nível do monstro, para que monstros mais fortes deem mais XP base.
    /// </summary>
    private const float XP_BONUS_PER_MOB_LEVEL = 2.5f;

    /// <summary>
    /// Multiplicador de XP para monstros marcados como 'IsBoss'.
    /// </summary>
    private const float BOSS_XP_MULTIPLIER = 15.0f;

    /// <summary>
    /// A partir de quantos níveis acima do monstro o jogador para de ganhar XP (ou ganha muito pouco).
    /// </summary>
    private const int GRAY_LEVEL_DIFFERENCE = 8;


    // ===================================================================
    // (NOVO) O MÉTODO PRINCIPAL PARA CALCULAR A RECOMPENSA DE XP
    // ===================================================================
    public static int CalculateExperienceReward(Player player, NpcInstance npc)
    {
        // Passo 1: Calcular a XP base do monstro, independente do jogador.
        // Um monstro nível 10 sempre terá mais XP base que um monstro nível 5.
        float baseNpcXp = BASE_XP_REWARD + (npc.Level * XP_BONUS_PER_MOB_LEVEL);

        // Passo 2: Calcular o modificador baseado na diferença de nível.
        int levelDifference = player.Level - npc.Level;
        float levelDifferenceModifier = 1.0f;

        if (levelDifference > 0)
        {
            // O jogador é mais forte que o monstro. Reduz o XP ganho.
            if (levelDifference >= GRAY_LEVEL_DIFFERENCE)
            {
                return 1; // Monstro "cinza", recompensa mínima.
            }
            // A cada nível acima, a recompensa diminui.
            levelDifferenceModifier = 1.0f - ((float)levelDifference / GRAY_LEVEL_DIFFERENCE);
        }
        else if (levelDifference < 0)
        {
            // O jogador é mais fraco. Aumenta o XP ganho por enfrentar o desafio.
            // Usamos Abs para pegar o valor positivo da diferença.
            // Bônus de 10% por nível de diferença, por exemplo.
            levelDifferenceModifier = 1.0f + (Math.Abs(levelDifference) * 0.1f);
        }
        // Se levelDifference == 0, o modificador continua 1.0f.

        // Garante que o modificador não seja negativo.
        levelDifferenceModifier = Math.Max(0, levelDifferenceModifier);

        // Passo 3: Aplicar o modificador de monstro "Boss".
        float qualityModifier = npc.BaseData.IsWorldBoss ? BOSS_XP_MULTIPLIER : 1.0f;

        // Passo 4: Calcular o XP final.
        float finalXp = baseNpcXp * levelDifferenceModifier * qualityModifier * SERVER_XP_RATE;

        // Retorna o valor arredondado como um inteiro.
        return Math.Max(1, (int)Math.Round(finalXp));
    }


    // ===================================================================
    // (MELHORADO) CURVA DE XP BASEADA EM FÓRMULA
    // ===================================================================
    /// <summary>
    /// Retorna a quantidade total de XP necessária para avançar do nível fornecido.
    /// Agora usa uma fórmula para uma curva suave, em vez de uma tabela manual.
    /// </summary>
    public static long GetExperienceForLevel(int level)
    {
        if (level >= MAX_LEVEL)
        {
            return long.MaxValue;
        }

        // Fórmula de exemplo: XP = (Nível^2 * 100) + (Nível * 300)
        // Isso cria uma curva que exige cada vez mais XP em níveis mais altos.
        // Você pode ajustar os valores 100 e 300 para deixar a progressão mais rápida ou lenta.
        long requiredXp = (long)(Math.Pow(level, 2) * 100) + (level * 300);
        return requiredXp;
    }
}