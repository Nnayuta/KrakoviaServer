using System.Collections.Generic;
using System.Numerics;
using System;
public interface ICombatEntity
{
    string Id { get; }
    Vector3 Position { get; }
    bool IsDead { get; }
    CharacterStats? Stats { get; }
    int Level { get; }
    float CurrentHealth { get; set; }
    float MaxHealth { get; }
    float CurrentResource { get; set; }
    float MaxResource { get; }
    float MovementSpeed { get; }
    Dictionary<string, DateTime> AbilityCooldowns { get; }
    void TakeDamage(float amount, ICombatEntity source, UDPServer server);
    void ReceiveHealing(float amount, UDPServer server);
    void ProcessDeath(ICombatEntity killer, UDPServer server);
}