using System.Collections.Generic;
using System.Numerics;
using System;
public interface ICombatEntity
{
    string Id { get; } // Pode ser mantido como o SessionId em string para compatibilidade
    int SessionId { get; } // <<< NOVO: O identificador de rede primário
    string InstanceId { get; } // <<< NOVO: O identificador único e persistente (GUID)
    Vector3 Position { get; }
    bool IsDead { get; }
    CharacterStats? Stats { get; }
    int Level { get; }
    float CurrentHealth { get; set; }
    float MaxHealth { get; }
    float CurrentResource { get; set; }
    float MaxResource { get; }
    float MovementSpeed { get; }
    StatusEffectController StatusEffectController { get; }
    Dictionary<string, DateTime> AbilityCooldowns { get; }
    void TakeDamage(float amount, ICombatEntity source, UDPServer server);
    void ReceiveHealing(float amount, UDPServer server);
    void ProcessDeath(ICombatEntity killer, UDPServer server);
}