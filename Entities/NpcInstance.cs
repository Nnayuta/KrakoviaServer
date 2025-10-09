// Servidor/Entities/NpcInstance.cs

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Newtonsoft.Json;

public class NpcInstance : ICombatEntity, IWorldEntity
{
    #region IWorldEntity Implementation
    public string GetSpawnMessage()
    {
        string positionStr = string.Join(",",
            Position.X.ToString("F2", CultureInfo.InvariantCulture),
            Position.Y.ToString("F2", CultureInfo.InvariantCulture),
            Position.Z.ToString("F2", CultureInfo.InvariantCulture));

        string rotationStr = string.Join(",",
            Rotation.X.ToString("F2", CultureInfo.InvariantCulture),
            Rotation.Y.ToString("F2", CultureInfo.InvariantCulture),
            Rotation.Z.ToString("F2", CultureInfo.InvariantCulture));

        return $"SPAWN_NPC|{InstanceId}|{BaseData.TypeId}|{positionStr}|{rotationStr}|{CurrentHealth.ToString(CultureInfo.InvariantCulture)}|{MaxHealth.ToString(CultureInfo.InvariantCulture)}";
    }
    #endregion

    #region State & AI Properties
    public string InstanceId { get; }
    public NpcData BaseData { get; private set; }
    public Vector3 Position { get; set; }
    public Vector3 SpawnPosition { get; private set; }
    public DateTime LastDamageTime { get; set; }
    public NpcAiType AiType { get; private set; }
    public Vector3 Rotation { get; set; }

    public Vector3 Destination { get; set; }
    public List<Vector3>? PatrolPath { get; }
    public NpcAiState CurrentState { get; set; }
    public string? TargetPlayerId { get; set; }
    public Dictionary<string, float> ThreatTable { get; } = new();
    public bool IsActive { get; set; } = false;
    public DateTime NextActionTime { get; set; } = DateTime.MinValue;
    public DateTime LastStateChangeTime { get; set; }
    public DateTime NextAutoAttackTime { get; set; } = DateTime.MinValue;
    public DateTime GlobalCooldownEndTime { get; set; } = DateTime.MinValue;
    public int CurrentPatrolIndex { get; set; } = 0;
    public Vector3 LastPosition { get; set; }
    public DateTime TimeAtLastPosition { get; set; }

    public List<ItemStack>? Loot { get; private set; } = null; // Começa nulo
    public DateTime CorpseDespawnTime { get; private set; }
    public bool HasLoot => Loot != null && Loot.Count > 0;
    public bool IsCorpse => CurrentState == NpcAiState.Dead && (Loot != null && Loot.Count > 0);
    public bool IsDespawned => CurrentState == NpcAiState.Dead && (Loot == null || Loot.Count == 0);
    public bool IsInvulnerable { get; set; }
    [JsonIgnore] // Não precisa salvar esta propriedade
    public bool HasStopped { get; set; } = true; // Começa parado


    #endregion

    #region ICombatEntity Implementation
    public string Id => this.InstanceId;
    public int Level => this.BaseData.Level;
    public bool IsDead => this.CurrentState == NpcAiState.Dead;
    public CharacterStats? Stats { get; private set; }
    public float MaxHealth => Stats.GetStatValue(StatType.Health);
    public float MaxResource => Stats.GetStatValue(StatType.Mana);
    public float MovementSpeed => Stats.GetStatValue(StatType.MovementSpeed);
    public float CurrentHealth { get; set; }
    public float CurrentResource { get; set; }
    public Dictionary<string, DateTime> AbilityCooldowns { get; } = new Dictionary<string, DateTime>();
    #endregion


    [JsonIgnore] public INpcBehavior Behavior { get; set; } // (NOVO) Armazena o comportamento da IA
    [JsonIgnore] public Vector3 AggroPosition { get; set; } // (NOVO) Ponto de início do combate para o leash

    public NpcInstance(Vector3 position, Vector3 rotation, NpcAiType aiType, List<Vector3>? patrolPath, NpcData baseData, UDPServer server)
    {
        DateTime currentTime = server.CurrentTimeUtc;

        this.InstanceId = Guid.NewGuid().ToString("N");
        this.BaseData = baseData;
        this.SpawnPosition = position;
        this.Position = position;
        this.Destination = position;
        this.PatrolPath = patrolPath;
        this.LastPosition = position;
        this.CurrentState = NpcAiState.Idle;
        this.AggroPosition = position;

        this.Rotation = rotation;
        this.AiType = aiType;
        this.PatrolPath = patrolPath;

        this.NextActionTime = currentTime;
        this.TimeAtLastPosition = currentTime;
        this.LastStateChangeTime = currentTime;

        InitializeStatsFromData();

        this.CurrentHealth = this.MaxHealth;
        this.CurrentResource = this.MaxResource;
    }

    /// <summary>
    /// NOVO: Método chamado pelo NpcAiManager quando o loot é gerado.
    /// </summary>
    public void SetLoot(List<ItemStack> generatedLoot)
    {
        this.Loot = generatedLoot;
    }

    /// <summary>
    /// NOVO: Limpa o loot (chamado depois que um jogador pega tudo).
    /// </summary>
    public void ClearLoot()
    {
        this.Loot?.Clear();
    }

    /// <summary>
    /// NOVO: Define o temporizador para o corpo desaparecer.
    /// </summary>
    public void SetCorpseDespawnTimer(float seconds, DateTime CurrentTimeUtc)
    {
        this.CorpseDespawnTime = CurrentTimeUtc.AddSeconds(seconds);
        // Adicione este log para ter 100% de certeza
        Console.WriteLine($"[DEBUG-TIMER] Timer de despawn para {this.Id} definido para: {this.CorpseDespawnTime}");
    }

    // =========================================================
    // MÉTODO TOTALMENTE CORRIGIDO E SIMPLIFICADO
    // =========================================================
    private void InitializeStatsFromData()
    {
        // 1. Cria uma "ficha de classe" para o NPC com valores base zerados.
        // Os stats reais virão do JSON.
        var npcClassData = new ServerClassData();

        // 2. Cria a instância do sistema de stats.
        this.Stats = new CharacterStats(npcClassData, this.Level);

        // 3. Itera pelos stats definidos no JSON e aplica TODOS eles.
        // A lógica de if() foi removida pois estava incorreta e bloqueando os stats.
        foreach (var statInfo in BaseData.Stats)
        {
            var modifier = new StatModifier(statInfo.Value, StatModifierType.Flat, "NpcBaseData");
            this.Stats.AddStatModifier(statInfo.Stat, modifier);
        }

        // 4. Garante um valor padrão para a velocidade de movimento caso não tenha sido definida no JSON.
        // Fazemos isso DEPOIS de adicionar os stats do JSON.
        if (this.Stats.GetStatValue(StatType.MovementSpeed) <= 0)
        {
            this.Stats.AddStatModifier(StatType.MovementSpeed, new StatModifier(100, StatModifierType.Flat, "NpcBaseDefault"));
        }

        // 5. Força um recálculo inicial de todos os stats derivados (Vida vinda do Vigor, etc.).
        this.Stats.CalculateAllDerivedStats();
    }

    // Servidor/Entities/NpcInstance.cs

    public void TakeDamage(float amount, ICombatEntity source, UDPServer server)
    {
        if (IsDead) return;

        server.NpcAiManager.OnNpcDamaged(this, source);

        float armorValue = this.Stats?.GetStatValue(StatType.Armor) ?? 0f;

        // Calcula o valor 'K' com base no nível do atacante.
        float kConstant = CombatConstants.ARMOR_K_BASE + (CombatConstants.ARMOR_K_LEVEL_MULTIPLIER * source.Level);

        // Calcula a redução de dano, garantindo que não seja divisão por zero se K for 0.
        float damageReduction = kConstant > 0 ? armorValue / (armorValue + kConstant) : 0f;

        // Aplica o limite máximo de redução de dano (geralmente 75% em MMOs).
        damageReduction = Math.Min(damageReduction, CombatConstants.MAX_ARMOR_DAMAGE_REDUCTION);

        // Calcula o dano final após a redução. O dano mínimo é sempre 1.
        float finalDamage = Math.Max(1, amount * (1 - damageReduction));

        // =================================================================================

        this.CurrentHealth -= finalDamage;

        ThreatTable.TryGetValue(source.Id, out float currentThreat);
        ThreatTable[source.Id] = currentThreat + finalDamage;
        this.NextActionTime = server.CurrentTimeUtc;

        if (this.BaseData.Faction != NpcFaction.Enemy)
        {
            this.TargetPlayerId = source.Id;
        }

        if (this.AiType == NpcAiType.Training_Dummy)
        {
            this.LastDamageTime = server.CurrentTimeUtc;
            if (this.CurrentHealth <= 0)
            {
                this.CurrentHealth = 1; // Training dummy nunca morre.
            }
        }
        else // Para todos os outros NPCs
        {
            if (this.CurrentHealth <= 0)
            {
                this.CurrentHealth = 0;
                ProcessDeath(source, server);
            }
        }

        server.NetworkManager.BroadcastMessageToRelevantPlayers(this.Position, $"ENTITY_HEALTH_UPDATE|{this.Id}|{this.CurrentHealth}|{this.MaxHealth}");
    }

    public void ProcessDeath(ICombatEntity killer, UDPServer server)
    {
        // Prevenção contra chamadas duplas: só executa se não estiver morto.
        if (IsDead) return;

        // A IA é quem gerencia o estado de morte (IsDead é derivado de CurrentState).
        this.ChangeNpcState(NpcAiState.Dead, server.CurrentTimeUtc);

        Console.WriteLine($"[MORTE] NPC {this.BaseData.TypeId} ({this.Id}) foi derrotado por {killer.Id}.");

        server.WorldManager.ProcessNpcDeath(this, killer);
    }

    public void ReceiveHealing(float amount, UDPServer server) // << Adicionado o parâmetro 'server'
    {
        if (IsDead) return;
        this.CurrentHealth += amount;
        if (this.CurrentHealth > this.MaxHealth)
        {
            this.CurrentHealth = this.MaxHealth;
        }

        server.NetworkManager.BroadcastMessageToRelevantPlayers(this.Position, $"ENTITY_HEALTH_UPDATE|{this.Id}|{this.CurrentHealth}|{this.MaxHealth}");
    }

    public void ChangeNpcState(NpcAiState newState, DateTime currentTime)
    {
        if (this.CurrentState == newState) return;
        this.CurrentState = newState;
        this.LastStateChangeTime = currentTime;
        Console.WriteLine($"[NPC STATE] NPC '{this.BaseData.TypeId}' (ID: {this.Id}) mudou para o estado {newState} em {currentTime}.");
    }
}