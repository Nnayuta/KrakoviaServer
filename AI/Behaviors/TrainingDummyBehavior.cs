// AI/Behaviors/TrainingDummyBehavior.cs
public class TrainingDummyBehavior : INpcBehavior
{
    private readonly UDPServer _server;

    public TrainingDummyBehavior(UDPServer server)
    {
        _server = server;
    }

    // Dummies não fazem nada no tick normal, exceto regenerar vida.
    public void Update(NpcInstance npc, float deltaTime)
    {
        if (npc.CurrentHealth < npc.MaxHealth && (_server.CurrentTimeUtc - npc.LastDamageTime).TotalSeconds > 10)
        {
            npc.CurrentHealth = npc.MaxHealth;
            _server.NetworkManager.BroadcastMessageToAll($"ENTITY_HEALTH_UPDATE|{npc.Id}|{npc.CurrentHealth}|{npc.MaxHealth}");
        }
    }

    // Dummies não reagem a dano.
    public void OnDamaged(NpcInstance npc, ICombatEntity attacker)
    {
        // Intencionalmente vazio.
    }
}