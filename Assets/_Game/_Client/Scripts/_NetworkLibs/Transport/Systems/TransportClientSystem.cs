
using Unity.Burst;
using UnityEngine;
using UnityEngine.Assertions;

using Unity.Entities;
using Unity.Jobs;
using Unity.Collections;
using Unity.Networking.Transport;


public class TransportClientSystem : SystemBase
{
    public NetworkDriver driver;
    public NetworkConnection server;
    public byte done;

    public JobHandle ClientJobHandle;


    protected override void OnCreate()
    {
        driver = NetworkDriver.Create();
        server = default(NetworkConnection);

        string serverIp = "127.0.0.1";
        ushort serverPort = 5522;
        NetworkEndPoint endPoint = NetworkEndPoint.Parse(serverIp, serverPort);
        
        server = driver.Connect(endPoint);

        Debug.Log("IsCreated: " + server.IsCreated);
    }

    protected override void OnDestroy()
    {
        Debug.LogError("disconected from the server");
        ClientJobHandle.Complete();
        driver.Dispose();
    }

    protected override void OnUpdate()
    {
        ClientJobHandle.Complete();
        var job = new ClientUpdateJob
        {
            driver = driver,
            server = server,
            done = done
        };
        ClientJobHandle = driver.ScheduleUpdate();
        ClientJobHandle = job.Schedule(ClientJobHandle);
    }
}

struct ClientUpdateJob : IJob
{
    public NetworkDriver driver;
    public NetworkConnection server;
    public byte done;

    public void Execute()
    {
        if (!server.IsCreated)
        {
            if (done != 1)
                Debug.Log("Something went wrong during connect, IsCreated: " + server.IsCreated + " done: " + done);
            return;
        }

        DataStreamReader stream;
        NetworkEvent.Type cmd;

        while ((cmd = server.PopEvent(driver, out stream)) != NetworkEvent.Type.Empty)
        {
            if (cmd == NetworkEvent.Type.Connect)
            {
                Debug.Log("We are now connected to the server");
            }
            else if (cmd == NetworkEvent.Type.Disconnect)
            {
                Debug.Log("Client got disconnected from server");
                server = default(NetworkConnection);
            }
        }
    }
}

