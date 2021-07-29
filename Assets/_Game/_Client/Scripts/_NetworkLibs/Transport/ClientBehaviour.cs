using UnityEngine;

using Unity.Networking.Transport;

public class ClientBehaviour : MonoBehaviour
{
    public NetworkDriver driver;
    protected NetworkConnection server;

    private string serverIp;
    private ushort serverPort;
    private float lastKeepAlive;
    private int sendKeepAliveDelay;

    private void Start()
    {
        driver = NetworkDriver.Create();
        server = default(NetworkConnection);

        serverIp = "127.0.0.1";
        serverPort = 5522;
        NetworkEndPoint endPoint = NetworkEndPoint.Parse(serverIp, serverPort);

        server = driver.Connect(endPoint);

        sendKeepAliveDelay = 5;
        lastKeepAlive = 0;
    }

    private void OnDestroy()
    {
        driver.Dispose();
    }

    private void Update()
    {
        driver.ScheduleUpdate().Complete();
        UpdateMessagePump();
        CheckAlive();
    }

    private void CheckAlive()
    {
        if (!server.IsCreated)
        {
            Debug.LogError("Something went wrong, lost connection to server");
            return;
        }

        KeepAlive();
    }

    private void KeepAlive()
    {
        float currentTime = Time.timeSinceLevelLoad;
        if (lastKeepAlive + sendKeepAliveDelay <= currentTime)
        {
            DataStreamWriter writer;
            driver.BeginSend(server, out writer);
            writer.WriteInt(1);
            driver.EndSend(writer);

            lastKeepAlive = currentTime;
        }
    }

    private void UpdateMessagePump()
    {
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
                Debug.Log("Got the value = " + value + " back  from the server, " + Time.timeSinceLevelLoad);
            }
            else if (cmd == NetworkEvent.Type.Disconnect)
            {
                Debug.Log("Client got disconnected from server");
                server = default(NetworkConnection);
            }
        }

    }
}