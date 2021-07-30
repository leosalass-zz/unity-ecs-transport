using Unity.Entities;
using Unity.Networking.Transport;

[GenerateAuthoringComponent]
public class ClientConnectionComponentData : IComponentData
{
    public NetworkConnection connection;
}
