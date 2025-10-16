// Servidor/Entities/NpcInstance.cs

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Newtonsoft.Json;

public class NpcInstance : ICombatEntity, IWorldEntity
{
    #region State & AI Properties
    public bool IsStationary => AiType == NpcAiType.Stationary_Guard || AiType == NpcAiType.Training_Dummy;
    public int SessionId { get; private set; }
    public string InstanceId { get; }
    public NpcData BaseData { get; private set; }
    public Vector3 Position { get; set; }
    public Vector3 SpawnPosition { get; private set; }
    public DateTime LastDamageTime { get; set; }
    public NpcAiType AiType { get; private set; }
    public Vector3 Rotation { get; set; }
    [JsonIgnore] // Não precisa salvar
    public AiUpdateTier UpdateTier { get; set; } = AiUpdateTier.Slow;

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
    public float LastKnownTargetDistance { get; set; } = float.MaxValue;
    public DateTime TimeAtLastKnownTargetDistance { get; set; }

    public List<ItemStack>? Loot { get; private set; } = null; // Começa nulo
    public DateTime CorpseDespawnTime { get; private set; }
    public bool HasLoot => Loot != null && Loot.Count > 0;
    public bool IsCorpse => CurrentState == NpcAiState.Dead && (Loot != null && Loot.Count > 0);
    public bool IsDespawned => CurrentState == NpcAiState.Dead && (Loot == null || Loot.Count == 0);
    public bool IsInvulnerable { get; set; }
    [JsonIgnore] // Não precisa salvar esta propriedade
    public bool HasStopped { get; set; } = true; // Começa parado

    /// <summary>
    /// A última posição real do NPC no mundo do jogo, conforme reportado pelo cliente "dono".
    /// </summary>
    public Vector3 LastReportedClientPosition { get; set; }

    /// <summary>
    /// O momento em que a LastReportedClientPosition foi recebida.
    /// </summary>
    public DateTime TimeAtLastReportedClientPosition { get; set; }


    #endregion

    #region ICombatEntity Implementation
    public string Id => this.InstanceId;
    public int Level => this.BaseData.Level;
    public bool IsDead { get; set; } = false;
    public DateTime RespawnTime { get; set; } = DateTime.MaxValue;
    public CharacterStats? Stats { get; private set; }
    public float MaxHealth => Stats.GetStatValue(StatType.Health);
    public float MaxResource => Stats.GetStatValue(StatType.Mana);
    public float MovementSpeed => Stats.GetStatValue(StatType.MovementSpeed);
    public float CurrentHealth { get; set; }
    public float CurrentResource { get; set; }
    public Dictionary<string, DateTime> AbilityCooldowns { get; } = new Dictionary<string, DateTime>();
    public bool IsCasting { get; private set; } = false;
    public DateTime CastingEndTime { get; private set; }
    public AbilityData? CastingAbility { get; private set; }
    public string? CastingTargetId { get; private set; }

    #endregion


    [JsonIgnore] public INpcBehavior Behavior { get; set; } // (NOVO) Armazena o comportamento da IA
    [JsonIgnore] public Vector3 AggroPosition { get; set; } // (NOVO) Ponto de início do combate para o leash

    public NpcInstance(Vector3 position, Vector3 rotation, NpcAiType aiType, List<Vector3>? patrolPath, NpcData baseData, UDPServer server)
    {
        DateTime currentTime = server.CurrentTimeUtc;

        this.SessionId = server.GetNextNpcSessionId();
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
        this.TimeAtLastKnownTargetDistance = currentTime;
        this.LastReportedClientPosition = position;
        this.TimeAtLastReportedClientPosition = currentTime;

        InitializeStatsFromData();

        this.CurrentHealth = this.MaxHealth;
        this.CurrentResource = this.MaxResource;
    }

    public void StartCasting(AbilityData ability, string targetId, DateTime serverCurrentTime)
    {
        if (IsCasting) return;

        IsCasting = true;
        CastingAbility = ability;
        CastingTargetId = targetId;
        CastingEndTime = serverCurrentTime.AddSeconds(ability.CastTime);

        // Muda o estado da IA para Casting para que o Behavior possa reagir
        ChangeNpcState(NpcAiState.Casting, serverCurrentTime);
    }

    public void FinishCasting()
    {
        IsCasting = false;
        CastingAbility = null;
        CastingTargetId = null;
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
        if (source.Id == this.Id) return;
        if (IsDead) return;
        server.NpcAiManager.OnNpcDamaged(this, source);

        float finalDamage = amount; // Começa com o dano bruto

        // --- Passo 1: Redução por Armadura (lógica existente) ---
        float armorValue = this.Stats?.GetStatValue(StatType.Armor) ?? 0f;
        float kConstant = CombatConstants.ARMOR_K_BASE + (CombatConstants.ARMOR_K_LEVEL_MULTIPLIER * source.Level);
        float damageReduction = kConstant > 0 ? armorValue / (armorValue + kConstant) : 0f;
        damageReduction = Math.Min(damageReduction, CombatConstants.MAX_ARMOR_DAMAGE_REDUCTION);
        finalDamage *= (1 - damageReduction);

        // --- (NOVA LÓGICA) Passo 2: Modificador por Diferença de Nível ---
        int levelDifference = source.Level - this.Level;

        // Limita a diferença de nível para o cálculo, para evitar bônus/penalidades absurdos
        levelDifference = Math.Clamp(levelDifference, -CombatConstants.MAX_LEVEL_DIFFERENCE_MOD, CombatConstants.MAX_LEVEL_DIFFERENCE_MOD);

        // Calcula o modificador (ex: +2 níveis = 1.2x, -3 níveis = 0.7x)
        float levelModifier = 1.0f + (levelDifference * CombatConstants.DAMAGE_MOD_PER_LEVEL);

        finalDamage *= levelModifier;

        // Garante que o dano final seja no mínimo 1
        finalDamage = Math.Max(1, finalDamage);

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

        string currentHpStr = this.CurrentHealth.ToString("F2", CultureInfo.InvariantCulture);
        string maxHpStr = this.MaxHealth.ToString("F2", CultureInfo.InvariantCulture);
        server.NetworkManager.BroadcastMessageToRelevantPlayers(this.Position, $"ENTITY_HEALTH_UPDATE|{this.SessionId}|{currentHpStr}|{maxHpStr}");
    }


    public void ReceiveHealing(float amount, UDPServer server) // << Adicionado o parâmetro 'server'
    {
        if (IsDead) return;
        this.CurrentHealth += amount;
        if (this.CurrentHealth > this.MaxHealth)
        {
            this.CurrentHealth = this.MaxHealth;
        }

        string currentHpStr = this.CurrentHealth.ToString("F2", CultureInfo.InvariantCulture);
        string maxHpStr = this.MaxHealth.ToString("F2", CultureInfo.InvariantCulture);

        server.NetworkManager.BroadcastMessageToRelevantPlayers(this.Position, $"ENTITY_HEALTH_UPDATE|{this.Id}|{currentHpStr}|{maxHpStr}");
    }

    public void ProcessDeath(ICombatEntity killer, UDPServer server)
    {
        // Prevenção contra chamadas duplas: só executa se não estiver morto.
        if (this.IsDead) return;

        // Agora, nós definimos diretamente.
        this.IsDead = true;

        // Mantemos a mudança de estado da IA para fins de animação e comportamento
        this.ChangeNpcState(NpcAiState.Dead, server.CurrentTimeUtc);

        Console.WriteLine($"[MORTE] NPC {this.BaseData.TypeId} ({this.Id}) foi derrotado por {killer.Id}.");

        server.WorldManager.ProcessNpcDeath(this, killer);
    }

    public void ChangeNpcState(NpcAiState newState, DateTime currentTime)
    {
        if (this.CurrentState == newState) return;
        this.CurrentState = newState;
        this.LastStateChangeTime = currentTime;
        // Console.WriteLine($"[NPC STATE] NPC '{this.BaseData.TypeId}' (ID: {this.Id}) mudou para o estado {newState} em {currentTime}.");
    }


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

        string currentHpStr = CurrentHealth.ToString("F2", CultureInfo.InvariantCulture);
        string maxHpStr = MaxHealth.ToString("F2", CultureInfo.InvariantCulture);

        string Stationary = IsStationary ? "1" : "0";
        return $"SPAWN_NPC|{SessionId}|{InstanceId}|{BaseData.TypeId}|{positionStr}|{rotationStr}|{currentHpStr}|{maxHpStr}|{Stationary}";
    }
    #endregion
}