
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

    private NativeList<float> lastKeepAlive;
    private int keepAliveDelay;


    protected override void OnCreate()
    {
        driver = NetworkDriver.Create();
        server = default(NetworkConnection);

        string serverIp = "127.0.0.1";
        ushort serverPort = 5522;
        NetworkEndPoint endPoint = NetworkEndPoint.Parse(serverIp, serverPort);

        keepAliveDelay = 5;
        lastKeepAlive = new NativeList<float>(1, Allocator.Persistent);

        server = driver.Connect(endPoint);

        Debug.Log("IsCreated: " + server.IsCreated);
        if (driver.IsCreated)
        {
            lastKeepAlive.Add(0);
        }
    }

    protected override void OnDestroy()
    {
        Debug.LogError("disconected from the server");
        ClientJobHandle.Complete();
        driver.Dispose();
        lastKeepAlive.Dispose();
    }

    protected override void OnUpdate()
    {
        ClientJobHandle.Complete();

        var job = new ClientUpdateJob
        {
            driver = driver,
            server = server,
            lastKeepAlive = lastKeepAlive,
            keepAliveDelay = keepAliveDelay,
            currentTime = (float)Time.ElapsedTime,
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
    public NativeList<float> lastKeepAlive;
    public int keepAliveDelay;
    public float currentTime;
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
            else if (cmd == NetworkEvent.Type.Data)
            {
                uint value = stream.ReadByte();
                Debug.Log("Got the value = " + value + " back  from the server");
            }
            else if (cmd == NetworkEvent.Type.Disconnect)
            {
                Debug.Log("Client got disconnected from server");
                server = default(NetworkConnection);
            }
        }


        //keepAlive
        if (lastKeepAlive[0] + keepAliveDelay <= currentTime)
        {
            lastKeepAlive[0] = currentTime;

            DataStreamWriter writer;
            driver.BeginSend(server, out writer);
            writer.WriteInt(1);
            driver.EndSend(writer);

        }
    }
}

