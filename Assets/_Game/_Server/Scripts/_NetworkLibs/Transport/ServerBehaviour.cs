using UnityEngine;
using Unity.Networking.Transport;
using Unity.Collections;

public class ServerBehaviour : MonoBehaviour
{
    public NetworkDriver driver;
    protected NativeList<NetworkConnection> connections;
    private NativeList<float> lastKeepAlives;
    private int sendKeepAliveDelay;

    private int maximunConnections;

    private void Start()
    {
        driver = NetworkDriver.Create();

        /**
         * who can connect to us?
         */
        NetworkEndPoint endPoint = NetworkEndPoint.AnyIpv4;
        endPoint.Port = 5522;

        if (isPortAvailable(driver, endPoint))
        {
            driver.Listen();
            if (driver.Listening)
            {
                Debug.Log("Listening for connections");
            }
        }

        sendKeepAliveDelay = 5;

        maximunConnections = 10;
        connections = new NativeList<NetworkConnection>(maximunConnections, Allocator.Persistent);
        lastKeepAlives = new NativeList<float>(maximunConnections, Allocator.Persistent);

    }

    private bool isPortAvailable(NetworkDriver driver, NetworkEndPoint endPoint)
    {
        bool isOpen = (driver.Bind(endPoint) != 0) ? false : true;

        if (!isOpen)
        {
            Debug.Log("There was an error binding to port " + endPoint.Port);
        }

        return isOpen;
    }

    private void OnDestroy()
    {
        driver.Dispose();
        connections.Dispose();
    }

    private void Update()
    {
        driver.ScheduleUpdate().Complete();
        CleanupConnections();
        AcceptNewConnections();
        UpdateMessagePump();
        KeepAlive();
    }

    private void CleanupConnections()
    {
        for (int i = 0; i < connections.Length; i++)
        {
            if (!connections[i].IsCreated)
            {
                connections.RemoveAtSwapBack(i);
                --i;
            }
        }
    }

    private void AcceptNewConnections()
    {
        NetworkConnection c;
        while ((c = driver.Accept()) != default(NetworkConnection))
        {
            connections.Add(c);
            lastKeepAlives.Add(Time.timeSinceLevelLoad);
            Debug.Log("Accepted a connection");
        }
    }

    private void UpdateMessagePump()
    {
        DataStreamReader stream;
        for (int i = 0; i < connections.Length; i++)
        {
            NetworkEvent.Type cmd;
            while ((cmd = driver.PopEventForConnection(connections[i], out stream)) != NetworkEvent.Type.Empty)
            {
                if (cmd == NetworkEvent.Type.Data)
                {
                    uint number = stream.ReadUInt();

                    Debug.Log("Got " + number + " from the Client, " + Time.timeSinceLevelLoad);
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    Debug.Log("Client disconnected from server");
                    connections[i] = default(NetworkConnection);
                    lastKeepAlives[i] = 0f;
                }
            }
        }
    }

    private void KeepAlive()
    {
        float currentTime = Time.timeSinceLevelLoad;
        for (int i = 0; i < connections.Length; i++)
        {
            if (lastKeepAlives[i] + sendKeepAliveDelay <= currentTime)
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