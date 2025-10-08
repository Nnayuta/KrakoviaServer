// AI/Behaviors/Interfaces/INpcBehavior.cs
public interface INpcBehavior
{
    /// <summary>
    /// O método principal que o NpcAiManager irá chamar a cada tick do servidor.
    /// Contém a lógica de decisão principal (o que fazer quando não está em combate, etc).
    /// </summary>
    void Update(NpcInstance npc, float deltaTime);

    /// <summary>
    /// Método de gatilho para reagir a eventos imediatos, como tomar dano.
    /// Resolve o problema de "lag" da IA, permitindo uma resposta instantânea.
    /// </summary>
    void OnDamaged(NpcInstance npc, ICombatEntity attacker);
}