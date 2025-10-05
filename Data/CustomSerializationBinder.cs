using Newtonsoft.Json.Serialization;
using System;
using System.Reflection; // Essencial para Assembly.GetExecutingAssembly()

public class CustomSerializationBinder : ISerializationBinder
{
    public Type BindToType(string? assemblyName, string typeName)
    {
        // O assembly atual onde este código está rodando (seu servidor).
        var currentAssembly = Assembly.GetExecutingAssembly();

        // Constrói o nome completo do tipo que estamos procurando no *nosso* assembly.
        // Ex: "Krakovia_Server.ServerWeaponData" (o namespace pode variar)
        // O nome do tipo já vem completo do JSON.
        var typeToFind = typeName;

        // Tenta encontrar o tipo no assembly atual.
        var type = currentAssembly.GetType(typeToFind);

        if (type == null)
        {
            // Fallback: Se o seu servidor tiver múltiplos projetos/assemblies,
            // você pode adicionar lógica aqui para procurar em outros lugares.
            // Por enquanto, isso é suficiente.
            Console.WriteLine($"[AVISO Binder] Não foi possível encontrar o tipo '{typeName}' no assembly '{currentAssembly.GetName().Name}'.");
        }

        // Retorna o tipo encontrado (ou null se não encontrar).
        return type;
    }

    public void BindToName(Type serializedType, out string? assemblyName, out string? typeName)
    {
        // Esta parte é para serialização, não precisamos dela agora.
        assemblyName = null;
        typeName = serializedType.FullName;
    }
}