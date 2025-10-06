using System.Collections.Generic;
using System.Numerics;
using System;

// A interface foi simplificada para expor o sistema de stats, em vez de stats individuais.
public interface ICombatEntity
{
    // Identificação e Estado
    string Id { get; }
    Vector3 Position { get; }
    bool IsDead { get; }
    int Level { get; }

    // ** A MUDANÇA PRINCIPAL **
    // Agora, toda entidade de combate TEM um componente CharacterStats.
    // Esta é a fonte única da verdade para todos os atributos.
    CharacterStats Stats { get; }

    // Atributos de Combate (ainda úteis como atalhos, mas agora lêem do sistema de stats)
    float CurrentHealth { get; set; }
    float MaxHealth { get; }
    float CurrentResource { get; set; }
    float MaxResource { get; }
    float MovementSpeed { get; }

    // Cooldowns
    Dictionary<string, DateTime> AbilityCooldowns { get; }

    // Métodos de Interação
    void TakeDamage(float amount, ICombatEntity source, UDPServer server);
    void ReceiveHealing(float amount);
}