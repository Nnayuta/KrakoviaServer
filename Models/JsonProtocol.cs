using System.Collections.Generic;
using System;
using System.Numerics; // Necessário para [System.Serializable]

// --- ESTRUTURA BASE ---
public class BaseRequest { public string Command { get; set; } }
public class BaseResponse { public string Command { get; set; } public bool Success { get; set; } public string Message { get; set; } }

// --- COMANDOS DE REQUISIÇÃO (Cliente -> Servidor) ---
public class RegisterRequest : BaseRequest { public string Username { get; set; } public string Password { get; set; } }
public class LoginRequest : BaseRequest
{
    public string Username { get; set; }
    public string Password { get; set; }
    public string ClientVersion { get; set; }
}
public class CreateCharacterRequest : BaseRequest
{
    public string Name { get; set; }
    public string ClassID { get; set; }
    public CharacterAppearance Appearance { get; set; }
}
public class SelectCharacterRequest : BaseRequest { public string CharacterId { get; set; } }

public class CharacterListResponse : BaseResponse { public List<CharacterSummary> Characters { get; set; } }

[Serializable]
public class ItemStackSummary
{
    public string InstanceID;
    public string ItemID;
    public int Quantity;
}

// Agora, atualize a classe SelectCharacterResponse
[Serializable]
public class SelectCharacterResponse : BaseResponse
{
    // Informações de Conexão
    public string AccessToken;
    public string WorldServerIp;
    public int WorldServerPort;

    public string CharacterId;
    public string ClassID;
    public int Level;

    // Informações de Inventário e Equipamento
    public List<string> KnownAbilityIDs;
    public List<ItemStackSummary?> Inventory; // << Adicione o '?'
    public Dictionary<EquipmentSlot, ItemStackSummary> Equipment;
    public ActionBarData ActionBar;
}