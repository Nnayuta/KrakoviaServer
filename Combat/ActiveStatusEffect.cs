using System;

/// <summary>
/// Representa uma instância de um efeito de status que está ATIVO em um personagem,
/// guardando sua data de expiração e quem o aplicou.
/// </summary>
public class ActiveStatusEffect
{
    public ServerStatusEffectData Data { get; }
    public DateTime ExpirationTime { get; set; }
    public ICombatEntity Caster { get; }

    public ActiveStatusEffect(ServerStatusEffectData data, ICombatEntity caster, DateTime serverCurrentTime)
    {
        Data = data;
        Caster = caster;
        // Se a duração for 0 ou menos, consideramos como permanente (ou muito longa)
        ExpirationTime = data.Duration > 0 ? serverCurrentTime.AddSeconds(data.Duration) : DateTime.MaxValue;
    }
}