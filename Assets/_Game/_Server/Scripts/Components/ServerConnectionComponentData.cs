using Unity.Entities;
using Unity.Networking.Transport;

[GenerateAuthoringComponent]
public class ServerConnectionComponentData : IComponentData
{
    public NetworkConnection connection;
}
