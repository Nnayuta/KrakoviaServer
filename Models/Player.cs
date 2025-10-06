// Servidor/Player.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Numerics;
using Newtonsoft.Json;

// A classe PlayerState permanece a mesma.
public class PlayerState
{
    public string Position { get; set; } = "174,7,476";
    public string RotationY { get; set; } = "0";
    public string VelocityX { get; set; } = "0";
    public string VelocityY { get; set; } = "0";
}

public class Player : ICombatEntity, IWorldEntity
{
    private readonly UDPServer? _server;

    #region IWorldEntity & Session (Sem mudanças significativas)
    public HashSet<string> KnownPlayerIds { get; } = new HashSet<string>();
    public HashSet<string> KnownNpcIds { get; } = new HashSet<string>();

    public string GetSpawnMessage()
    {
        // Formato: SPAWN_PLAYER | ID | Nome | Posição | RotaçãoY | PayloadEquipamento | JSONAparência | Nível | VidaAtual | VidaMáxima

        string position = this.State.Position;
        string rotationY = this.State.RotationY;
        string equipmentPayload = GetEquipmentPayload();

        string currentHealthStr = this.CurrentHealth.ToString("F0", CultureInfo.InvariantCulture);
        string maxHealthStr = this.MaxHealth.ToString("F0", CultureInfo.InvariantCulture);

        // Serializamos todo o objeto de aparência para JSON.
        // O cliente pode então usar isso para reconstruir a aparência visual completa.
        string appearanceJson = JsonConvert.SerializeObject(this.Appearance);

        // Console.WriteLine($"[GetSpawnMessage] Gerando mensagem de spawn para {this.CharacterName}. PermissionLevel ATUAL: {this.PermissionLevel}");

        // Juntamos tudo em uma única mensagem poderosa.
        return $"SPAWN_PLAYER|{this.Id}|{this.CharacterName}|{this.State.Position}|{this.State.RotationY}|{GetEquipmentPayload()}|{JsonConvert.SerializeObject(this.Appearance)}|{this.Level}|{currentHealthStr}|{maxHealthStr}|{this.PermissionLevel}";
    }

    public string SessionId { get; }
    public IPEndPoint EndPoint { get; }
    public DateTime LastMessageTime { get; set; }
    public PlayerState State { get; }
    #endregion

    #region Identity & Character Data (Sem mudanças)
    public string Username { get; }
    public string CharacterName { get; }
    public string CharacterId { get; }
    public int PermissionLevel { get; private set; } // <<< ADICIONE ESTA LINHA
    public long TotalBronze { get; set; }
    public string ClassID { get; private set; }
    public int Level { get; set; }
    public long CurrentExperience { get; set; }
    public Inventory PlayerInventory { get; private set; }
    public Equipment PlayerEquipment { get; private set; }
    public ActionBarData PlayerActionBar { get; private set; }
    public List<WeaponType> CurrentWeaponProficiencies { get; private set; } = new();
    public PlayerQuestLog QuestLog { get; private set; }
    #endregion

    public DateTime LastCombatTime { get; private set; }
    public DateTime NextRegenTime { get; set; }

    private const float OUT_OF_COMBAT_SECONDS = 7.0f;
    public bool IsInCombat => _server != null && (_server.CurrentTimeUtc - LastCombatTime).TotalSeconds < OUT_OF_COMBAT_SECONDS;

    public CharacterStats Stats { get; private set; }
    public bool IsCasting { get; private set; }
    public DateTime CastEndTime { get; private set; }
    public AbilityData? CurrentCastAbility { get; private set; }
    public string? CurrentCastTargetId { get; private set; }
    public Vector3 CastInitialPosition { get; private set; }

    #region ICombatEntity Implementation (Atualizada)

    public string Id => this.CharacterId;

    public Vector3 Position
    {
        // O getter depende do Vector3Parser, então vamos garantir que o parser também seja robusto.
        get => Vector3Parser.Parse(this.State.Position);
        // O setter já está correto.
        set => this.State.Position = $"{value.X.ToString("F2", CultureInfo.InvariantCulture)},{value.Y.ToString("F2", CultureInfo.InvariantCulture)},{value.Z.ToString("F2", CultureInfo.InvariantCulture)}";
    }
    public bool IsDead { get; private set; } = false;
    public Dictionary<string, DateTime> AbilityCooldowns { get; } = new Dictionary<string, DateTime>();
    public List<string> KnownAbilityIDs { get; set; } = new List<string>();

    // --- Propriedades de Estado de Combate ---
    public float CurrentHealth { get; set; }
    public float CurrentResource { get; set; }

    // --- Propriedades de Conveniência (Lêem do sistema de Stats) ---
    public float MaxHealth => Stats.GetStatValue(StatType.Health);
    public float MaxResource => Stats.GetStatValue(StatType.Mana);
    public float MovementSpeed => Stats.GetStatValue(StatType.MovementSpeed);
    public CharacterAppearance Appearance { get; private set; }


    private bool _isProcessingEquipmentChange = false;

    // Implementação explícita da interface para Level
    int ICombatEntity.Level => this.Level;

    #endregion

    public Player(IPEndPoint endPoint, AuthenticatedPlayerInfo authInfo, UDPServer server, CharacterData characterData)
    {
        _server = server;

        SessionId = Guid.NewGuid().ToString("N");
        EndPoint = endPoint;
        LastMessageTime = server.CurrentTimeUtc;
        LastCombatTime = server.CurrentTimeUtc;
        NextRegenTime = server.CurrentTimeUtc;
        QuestLog = new PlayerQuestLog(this);
        PermissionLevel = authInfo.PermissionLevel;

        Console.WriteLine($"[Player CONSTRUTOR] Objeto Player criado para {authInfo.CharacterName}. PermissionLevel atribuído: {this.PermissionLevel}");

        Username = authInfo.Username;
        CharacterName = authInfo.CharacterName;
        CharacterId = authInfo.CharacterId;

        // Atribuição de dados a partir do CharacterData carregado
        State = new PlayerState { Position = characterData.Position };
        this.ClassID = characterData.ClassID;
        this.Level = characterData.Level;
        this.TotalBronze = characterData.TotalBronze;
        this.CurrentExperience = characterData.CurrentExperience;
        this.Appearance = characterData.Appearance ?? new CharacterAppearance();
        this.PlayerInventory = characterData.PlayerInventory;
        this.PlayerEquipment = characterData.PlayerEquipment;
        this.PlayerActionBar = characterData.PlayerActionBar;

        this.PlayerEquipment.OnEquipmentChanged += OnEquipmentChanged;

        InitializeCharacter();
    }

    private void InitializeCharacter()
    {
        this.KnownAbilityIDs = CalculateKnownAbilities();

        // RebuildStats agora irá recalcular stats E proficiências.
        RebuildStats();

        this.CurrentHealth = this.MaxHealth;
        this.CurrentResource = this.MaxResource;
        Console.WriteLine($"[PlayerInit] '{this.Username}' inicializado com {this.MaxHealth} de vida.");

        SendFullStateToClient();
    }

    public void RebuildStats()
    {
        if (!DataManager.Classes.TryGetValue(this.ClassID, out var classData)) return;

        // 1. Cria uma nova instância de CharacterStats
        this.Stats = new CharacterStats(classData, this.Level);

        // 2. Aplica os stats dos itens equipados
        foreach (ItemStack equippedStack in this.PlayerEquipment.equippedItems.Values)
        {
            if (equippedStack != null && DataManager.Items.TryGetValue(equippedStack.ItemID, out var itemData))
            {
                ApplyItemStats_Internal(itemData);
            }
        }

        // 3. Recalcula os stats derivados
        this.Stats.CalculateAllDerivedStats();

        this.CurrentHealth = Math.Min(this.CurrentHealth, this.MaxHealth);
        this.CurrentResource = Math.Min(this.CurrentResource, this.MaxResource);

        // 4. Recalcula as proficiências, pois elas podem depender do nível (habilidades passivas).
        RecalculateProficiencies();
    }

    // Método auxiliar renomeado para uso interno
    private void ApplyItemStats_Internal(ServerItemData itemData)
    {
        foreach (var statInfo in itemData.Stats)
        {
            var modifier = new StatModifier(statInfo.Value, StatModifierType.Flat, itemData.itemID);
            this.Stats.AddStatModifier(statInfo.Stat, modifier);
        }
    }

    public void EnterCombat()
    {
        LastCombatTime = _server.CurrentTimeUtc;
    }

    /// <summary>
    /// NOVO: Método que reage a mudanças de equipamento de forma eficiente.
    /// </summary>
    private void OnEquipmentChanged(ServerItemData oldItem, ServerItemData newItem)
    {
        // Se já estamos no meio de um processamento, ignora os disparos subsequentes
        // para evitar broadcasts múltiplos.
        if (_isProcessingEquipmentChange) return;

        try
        {
            // Trava o processamento
            _isProcessingEquipmentChange = true;

            // 1. A lógica de reconstrução de stats permanece a mesma.
            RebuildStats();

            // 2. Notifica o PRÓPRIO jogador sobre as mudanças de stats e inventário/equipamento.
            _server?.NetworkManager.SendVitalsUpdate(this);
            _server?.NetworkManager.SendStatsUpdate(this);
            SendFullStateToClient();

            // 3. Transmite a MUDANÇA VISUAL apenas para os OUTROS jogadores.
            if (_server != null)
            {
                string message = $"VISUAL_EQUIPMENT_UPDATE|{this.Id}|{GetEquipmentPayload()}";
                // Usamos o BroadcastMessageToOthers que criamos antes
                _server.NetworkManager.BroadcastMessageToOthers(this, message);
            }

            Console.WriteLine($"[Sync Equip] Transmitindo atualização de equipamento do jogador {this.Id} para os outros. (Disparo Único)");
        }
        finally
        {
            // Garante que a trava seja liberada, mesmo que ocorra um erro.
            _isProcessingEquipmentChange = false;
        }
    }

    public void SendFullStateToClient()
    {
        // Se _server for nulo (como no caso do tempPlayer), este método não faz nada.
        if (_server == null) return;

        // Envia atualização de inventário
        string invPayload = string.Join("|", this.PlayerInventory.slots.Select(s => s == null ? "null" : $"{s.InstanceID},{s.ItemID},{s.Quantity}"));
        _server.NetworkManager.SendMessageToClient($"INVENTORY_UPDATE|{invPayload}", this.EndPoint);

        // Envia atualização de equipamento
        string eqPayload = string.Join("|", this.PlayerEquipment.equippedItems.Select(kvp => $"{kvp.Key},{(kvp.Value == null ? "null" : $"{kvp.Value.InstanceID},{kvp.Value.ItemID},{kvp.Value.Quantity}")}"));
        _server.NetworkManager.SendMessageToClient($"EQUIPMENT_UPDATE|{eqPayload}", this.EndPoint);

        _server.NetworkManager.SendVitalsUpdate(this);
        _server.NetworkManager.SendStatsUpdate(this);
    }

    public List<string> CalculateKnownAbilities()
    {
        var known = new List<string>();
        if (!DataManager.Classes.TryGetValue(this.ClassID, out var classData)) return known;
        for (int i = 1; i <= this.Level; i++)
        {
            if (classData.BaseAbilityUnlocks.TryGetValue(i, out var abilitiesToLearn))
            {
                known.AddRange(abilitiesToLearn);
            }
        }
        return known.Distinct().ToList(); // Adicionado Distinct() por segurança
    }

    /// <summary>
    /// Verifica se o jogador conhece uma habilidade passiva específica.
    /// </summary>
    /// <param name="abilityId">O ID da habilidade passiva a ser verificada.</param>
    /// <returns>True se o jogador conhece a habilidade.</returns>
    public bool HasPassive(string abilityId)
    {
        // A lógica é simples: apenas verifica se a ID existe na lista de habilidades conhecidas.
        return this.KnownAbilityIDs.Contains(abilityId);
    }

    /// <summary>
    /// Atualiza o estado derivado do personagem, como proficiências.
    /// Nota: A atualização de stats já é tratada pelo evento OnEquipmentChanged.
    /// </summary>
    public void UpdateCharacterState()
    {
        // Recalcula quais tipos de armas o jogador pode usar, pois isso pode ter mudado.
        RecalculateProficiencies();

        // Embora os stats já tenham sido atualizados pelo evento, é seguro
        // recalcular os derivados novamente para garantir consistência.
        this.Stats.CalculateAllDerivedStats();

        // Garante que a vida/mana não excedam os novos máximos.
        if (this.CurrentHealth > this.MaxHealth) this.CurrentHealth = this.MaxHealth;
        if (this.CurrentResource > this.MaxResource) this.CurrentResource = this.MaxResource;
    }


    public void RecalculateProficiencies()
    {
        if (!DataManager.Classes.TryGetValue(this.ClassID, out var classData))
        {
            this.CurrentWeaponProficiencies = new List<WeaponType>(); // Garante que a lista esteja vazia se a classe não for encontrada
            return;
        }

        // Começa com a lista de proficiências base da classe
        var proficiencies = new List<WeaponType>(classData.WeaponProficiencies);

        // Adiciona proficiências concedidas por habilidades passivas que o jogador conhece
        foreach (string abilityID in this.KnownAbilityIDs)
        {
            if (DataManager.Abilities.TryGetValue(abilityID, out var ability) && ability.Type == AbilityType.Passive && ability.GrantsWeaponProficiency != WeaponType.Sword1H) // Exemplo, ajuste conforme seus enums
            {
                // Adiciona a proficiência se a habilidade passiva conceder uma.
                // TODO: Seu `AbilityData` precisa de um campo como `public WeaponType GrantsWeaponProficiency`.
                // proficiencies.Add(ability.GrantsWeaponProficiency);
            }
        }
        this.CurrentWeaponProficiencies = proficiencies.Distinct().ToList();
    }

    #region Métodos de Combate (Atualizados)

    public void StartCasting(AbilityData ability, string? targetId, DateTime currentTime)
    {
        IsCasting = true;
        CurrentCastAbility = ability;
        CurrentCastTargetId = targetId;
        CastEndTime = currentTime.AddSeconds(ability.CastTime);
        CastInitialPosition = this.Position;

        Console.WriteLine($"[Casting-Server] '{Username}' iniciou casting de '{ability.ID}' em {CastInitialPosition}. Termina em: {CastEndTime}");
    }

    /// <summary>
    /// Interrompe o processo de casting atual.
    /// </summary>
    /// <param name="notifyClient">Se verdadeiro, enviará uma mensagem de confirmação de cancelamento para o cliente.</param>
    /// <param name="networkManager">A referência ao NetworkManager, necessária se notifyClient for true.</param>
    public void InterruptCasting(bool notifyClient = false, NetworkManager networkManager = null)
    {
        if (!IsCasting) return;

        var abilityName = CurrentCastAbility?.Name ?? "desconhecida";
        Console.WriteLine($"[Casting-Server] Casting de '{Username}' para a habilidade '{abilityName}' interrompido.");

        IsCasting = false;
        CurrentCastAbility = null;
        CurrentCastTargetId = null;
        CastEndTime = DateTime.MinValue;

        // NOVO: Envia a confirmação para o cliente se solicitado
        if (notifyClient && networkManager != null)
        {
            networkManager.SendMessageToClient("CAST_CANCELED", this.EndPoint);
            networkManager.BroadcastMessageToOthers(this, $"ENTITY_CAST_CANCEL|{this.Id}");
        }
    }

    public void TakeDamage(float amount, ICombatEntity source, UDPServer server)
    {
        if (IsDead) return;

        EnterCombat();
        // Pega o valor da armadura do sistema de stats.
        float armorValue = this.Stats.GetStatValue(StatType.Armor);

        // Calcula o valor 'K' com base no nível do atacante (source).
        float kConstant = CombatConstants.ARMOR_K_BASE + (CombatConstants.ARMOR_K_LEVEL_MULTIPLIER * source.Level);

        // Calcula a redução de dano.
        float damageReduction = kConstant > 0 ? armorValue / (armorValue + kConstant) : 0f;

        // Aplica o limite máximo de redução de dano (cap).
        damageReduction = Math.Min(damageReduction, CombatConstants.MAX_ARMOR_DAMAGE_REDUCTION);

        // Calcula o dano final.
        float finalDamage = Math.Max(1, amount * (1 - damageReduction));

        this.CurrentHealth -= finalDamage;
        if (this.CurrentHealth <= 0)
        {
            this.CurrentHealth = 0;
            this.IsDead = true;
        }

        _server?.NetworkManager.SendVitalsUpdate(this);
    }

    public void Respawn(Vector3 position)
    {
        this.Position = position;
        this.IsDead = false;

        // Não precisa mais recalcular tudo, apenas restaurar vida/mana.
        this.CurrentHealth = this.MaxHealth * 0.5f;
        this.CurrentResource = this.MaxResource * 0.5f;
    }

    public void ReceiveHealing(float amount)
    {
        if (IsDead) return;
        this.CurrentHealth += amount;
        if (this.CurrentHealth > this.MaxHealth)
        {
            this.CurrentHealth = this.MaxHealth;
        }

        _server?.NetworkManager.SendVitalsUpdate(this);
    }

    #endregion

    /// <summary>
    /// Gera uma string formatada contendo os IDs dos itens equipados, para sincronização visual.
    /// Formato: "Slot1,ItemID1;Slot2,ItemID2;..."
    /// </summary>
    /// <returns>Uma string com os dados de equipamento para a rede.</returns>
    public string GetEquipmentPayload()
    {
        // Usamos um List<string> para construir as partes e depois juntamos com Join, que é eficiente.
        var payloadParts = new List<string>();

        foreach (var pair in PlayerEquipment.equippedItems)
        {
            // Só nos importamos com o slot e o ID do item para a aparência.
            // Se o slot estiver vazio (null), enviamos "null" para que o cliente saiba que deve desequipar o visual.
            string itemID = pair.Value?.ItemID ?? "null";
            payloadParts.Add($"{(int)pair.Key},{itemID}");
        }

        return string.Join(";", payloadParts);
    }

    public CharacterData GetCharacterDataForSaving()
    {
        // Retorna um objeto CharacterData com o estado atual do jogador.
        // É basicamente o processo inverso do construtor.
        return new CharacterData(this.CharacterId, this.ClassID, this.Level, this.Appearance)
        {
            Position = this.State.Position,
            CurrentExperience = this.CurrentExperience,
            TotalBronze = this.TotalBronze,
            PlayerInventory = this.PlayerInventory,
            PlayerEquipment = this.PlayerEquipment,
            PlayerActionBar = this.PlayerActionBar
        };
    }
}