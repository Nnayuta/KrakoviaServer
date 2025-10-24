using System;
using System.Collections.Generic;
using System.Globalization; // Necessário para formatar os números
using System.Linq;
using System.Text; // Necessário para o StringBuilder

public class StatusEffectController
{
    private readonly ICombatEntity _owner;
    private readonly UDPServer _server;
    private readonly List<ActiveStatusEffect> _activeEffects = new();

    public StatusEffectController(ICombatEntity owner, UDPServer server)
    {
        _owner = owner;
        _server = server;
    }

    public void ApplyEffect(string effectID, ICombatEntity caster)
    {
        if (!DataManager.StatusEffects.TryGetValue(effectID, out var effectData))
        {
            Console.WriteLine($"[StatusEffect] TENTATIVA FALHOU: Efeito com ID '{effectID}' não encontrado.");
            return;
        }

        var existingEffect = _activeEffects.FirstOrDefault(e => e.Data.EffectID == effectID);
        if (existingEffect != null)
        {
            existingEffect.ExpirationTime = _server.CurrentTimeUtc.AddSeconds(effectData.Duration);
            Console.WriteLine($"[StatusEffect] Efeito '{effectID}' REATIVADO em {_owner.Id}.");
        }
        else
        {
            var newActiveEffect = new ActiveStatusEffect(effectData, caster, _server.CurrentTimeUtc);
            _activeEffects.Add(newActiveEffect);

            foreach (var modifierDef in effectData.StatModifiers)
            {
                var modifier = new StatModifier(modifierDef.value, modifierDef.type, effectData.EffectID);
                _owner.Stats.AddStatModifier(modifierDef.targetStat, modifier);
            }
            Console.WriteLine($"[StatusEffect] Efeito '{effectID}' APLICADO em {_owner.Id}.");
        }

        // Sempre que um efeito é aplicado ou atualizado, enviamos a lista completa.
        SendFullEffectListToClient();
    }

    public void Update()
    {
        if (!_activeEffects.Any()) return;

        for (int i = _activeEffects.Count - 1; i >= 0; i--)
        {
            var effect = _activeEffects[i];
            if (_server.CurrentTimeUtc >= effect.ExpirationTime)
            {
                // RemoveEffect já chama SendFullEffectListToClient()
                RemoveEffect(effect);
            }
        }
    }

    private void RemoveEffect(ActiveStatusEffect effectToRemove)
    {
        _owner.Stats.RemoveAllStatModifiersFromSource(effectToRemove.Data.EffectID);
        _activeEffects.Remove(effectToRemove);
        Console.WriteLine($"[StatusEffect] Efeito '{effectToRemove.Data.EffectID}' EXPIRADO de {_owner.Id}.");

        // Sempre que um efeito é removido, enviamos a lista completa.
        SendFullEffectListToClient();
    }

    /// <summary>
    /// Compila a lista atual de efeitos ativos em uma única mensagem de rede e a envia ao cliente.
    /// Formato: STATUS_EFFECT_LIST_UPDATE|id1,duração1,éBuff1;id2,duração2,éBuff2;...
    /// </summary>
    public void SendFullEffectListToClient()
    {
        // Só faz sentido enviar para jogadores.
        if (_owner is not Player playerOwner) return;

        // Se não há efeitos, enviamos uma mensagem vazia para que o cliente limpe a HUD.
        if (!_activeEffects.Any())
        {
            _server.NetworkManager.SendMessageToPlayer(playerOwner, "STATUS_EFFECT_LIST_UPDATE|");
            return;
        }

        // Usamos StringBuilder para performance, pois é mais eficiente que concatenar strings.
        var payloadBuilder = new StringBuilder();

        foreach (var effect in _activeEffects)
        {
            // Calcula a duração restante em segundos.
            float remainingDuration = (float)(effect.ExpirationTime - _server.CurrentTimeUtc).TotalSeconds;
            // Garante que não enviamos durações negativas.
            remainingDuration = Math.Max(0, remainingDuration);

            // Converte booleano para 1 ou 0.
            int isBuffFlag = effect.Data.IsBuff ? 1 : 0;

            // Adiciona a string do efeito ao payload.
            // Usamos InvariantCulture para garantir que o '.' seja o separador decimal.
            payloadBuilder.Append($"{effect.Data.EffectID},{remainingDuration.ToString("F1", CultureInfo.InvariantCulture)},{isBuffFlag};");
        }

        // Remove o último ';' para deixar a string limpa.
        if (payloadBuilder.Length > 0)
        {
            payloadBuilder.Length--;
        }

        string finalMessage = $"STATUS_EFFECT_LIST_UPDATE|{payloadBuilder.ToString()}";
        _server.NetworkManager.SendMessageToPlayer(playerOwner, finalMessage);
    }
}