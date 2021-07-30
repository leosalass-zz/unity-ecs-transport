using UnityEngine;
using UnityEngine.Assertions;

using Unity.Entities;
using Unity.Jobs;
using Unity.Collections;
using Unity.Networking.Transport;
using System.IO;
using System.Reflection;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public class TransportServerSystem : SystemBase
{
    public NetworkDriver driver;
    public NativeList<NetworkConnection> connections;
    private NativeList<float> lastKeepAlives;
    private int keepAliveDelay;

    private JobHandle ServerJobHandle;

    protected override void OnCreate()
    {
        driver = NetworkDriver.Create();
        NetworkEndPoint endPoint = NetworkEndPoint.AnyIpv4;
        endPoint.Port = 5522;

        if (IsPortAvailable(driver, endPoint))
        {
            driver.Listen();
            if (driver.Listening)
            {
                Debug.Log("Listening for connections");
            }
        }

        keepAliveDelay = 5;
        int maxConnections = 10;

        connections = new NativeList<NetworkConnection>(maxConnections, Allocator.Persistent);
        lastKeepAlives = new NativeList<float>(maxConnections, Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        // Make sure we run our jobs to completion before exiting.
        ServerJobHandle.Complete();
        driver.Dispose();
        connections.Dispose();
        lastKeepAlives.Dispose();
    }

    protected override void OnUpdate()
    {
        ServerJobHandle.Complete();

        float currentTime = (float)Time.ElapsedTime;

        var serverUpdateMessagePumpJob = new ServerUpdateMessagePumpJob
        {
            driver = driver,
            connections = connections,
            lastKeepAlives = lastKeepAlives,
            currentTime = currentTime
        };

        var serverKeepAliveJob = new ServerKeepAliveJob
        {
            driver = driver.ToConcurrent(),
            connections = connections.AsDeferredJobArray(),
            lastKeepAlives = lastKeepAlives.AsDeferredJobArray(),
            keepAliveDelay = keepAliveDelay,
            currentTime = currentTime
        };

        ServerJobHandle = driver.ScheduleUpdate();
        ServerJobHandle = serverUpdateMessagePumpJob.Schedule(ServerJobHandle);
        ServerJobHandle = serverKeepAliveJob.Schedule(connections, 1, ServerJobHandle);
    }

    private bool IsPortAvailable(NetworkDriver driver, NetworkEndPoint endPoint)
    {
        bool isOpen = (driver.Bind(endPoint) != 0) ? false : true;

        if (!isOpen)
        {
            Debug.Log("There was an error binding to port " + endPoint.Port);
        }

        return isOpen;
    }
}

struct ServerUpdateMessagePumpJob : IJob
{
    public NetworkDriver driver;
    public NativeList<NetworkConnection> connections;
    public NativeList<float> lastKeepAlives;
    public float currentTime;

    public void Execute()
    {
        CleanUpConnections();
        AcceptNewConnections();
    }

    void CleanUpConnections()
    {
        for (int i = 0; i < connections.Length; i++)
        {
            if (!connections[i].IsCreated)
            {
                connections.RemoveAtSwapBack(i);
                lastKeepAlives.RemoveAtSwapBack(i);
                --i;
            }
        }
    }

    void AcceptNewConnections()
    {
        NetworkConnection c;
        while ((c = driver.Accept()) != default(NetworkConnection))
        {
            connections.Add(c);
            lastKeepAlives.Add(currentTime);
            Debug.Log("Accepted a connection");
        }
    }
}

struct ServerKeepAliveJob : IJobParallelForDefer
{
    // Start querying the driver for events
    // that might have happened since the last update(tick).

    public NetworkDriver.Concurrent driver;
    public NativeArray<NetworkConnection> connections;
    public NativeArray<float> lastKeepAlives;
    public int keepAliveDelay;
    public float currentTime;

    public void Execute(int index)
    {
        //Begin by defining a DataStreamReader.
        //This will be used in case any Data event was received.
        //Then we just start looping through all our connections.
        DataStreamReader stream;
        NetworkEvent.Type cmd;

        Assert.IsTrue(connections[index].IsCreated);
        while ((cmd = driver.PopEventForConnection(connections[index], out stream)) != NetworkEvent.Type.Empty)
        {
            if (cmd == NetworkEvent.Type.Data)
            {
                Receive(stream);
            }
            else if (cmd == NetworkEvent.Type.Disconnect)
            {
                Disconnect(index);
            }
        }

        KeepAlive();
    }

    void Receive(DataStreamReader stream)
    {
        byte messageCode = stream.ReadByte();
        Debug.Log("Got " + messageCode + " as message code.");
    }

    void Disconnect(int index)
    {
        Debug.Log("Client disconnected from server");
        connections[index] = default(NetworkConnection);
        lastKeepAlives[index] = 0;
    }

    void KeepAlive()
    {
        for (int i = 0; i < connections.Length; i++)
        {
            if (lastKeepAlives[i] + keepAliveDelay <= currentTime)
            {
                DataStreamWriter writer;
                driver.BeginSend(connections[i], out writer);
                writer.WriteInt(1);
                driver.EndSend(writer);

                lastKeepAlives[i] = currentTime;
            }
        }
    }
}
