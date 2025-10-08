// AI/Behaviors/AmbientPassiveBehavior.cs
public class AmbientPassiveBehavior : INpcBehavior
{
    public void Update(NpcInstance npc, float deltaTime)
    {
        // Não faz nada.
    }

    public void OnDamaged(NpcInstance npc, ICombatEntity attacker)
    {
        // Não reage.
    }
}