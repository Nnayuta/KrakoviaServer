using Newtonsoft.Json.Serialization;
using System;
using System.Linq;
using System.Reflection;

public class CustomSerializationBinder : ISerializationBinder
{
    public Type BindToType(string assemblyName, string typeName)
    {
        // Pega o assembly atual (o código do seu servidor)
        Assembly currentAssembly = Assembly.GetExecutingAssembly();

        // Procura pelo tipo com o nome fornecido DENTRO do assembly do servidor.
        var type = currentAssembly.GetTypes().FirstOrDefault(t => t.Name == typeName);

        return type;
    }

    public void BindToName(Type serializedType, out string assemblyName, out string typeName)
    {
        assemblyName = null; // Não precisamos disso para serializar
        typeName = serializedType.Name;
    }
}