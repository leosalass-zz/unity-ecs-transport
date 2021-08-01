
using Unity.Burst;
using UnityEngine;
using UnityEngine.Assertions;

using Unity.Entities;
using Unity.Jobs;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Transforms;

namespace Client
{
    public class TransportClientSystem : SystemBase
    {
        public NetworkDriver driver;
        public NetworkConnection server;
        public bool done;

        public JobHandle ClientJobHandle;

        private float lastKeepAliveResponseTime;
        private float lastKeepAliveRequestTime;
        private float timeToSendKeepAliveRequest;
        private float keepAliveDelay;
        private float KeepAliveRetryDelay;
        private float currentTime;
        private bool waitingKeepAliveResponse;

        EntitySpawner entitySpawner;

        protected override void OnCreate()
        {
            driver = NetworkDriver.Create();
            server = default(NetworkConnection);

            string serverIp = "127.0.0.1";
            ushort serverPort = 5522;
            NetworkEndPoint endPoint = NetworkEndPoint.Parse(serverIp, serverPort);

            keepAliveDelay = 5;
            KeepAliveRetryDelay = 2f;
            timeToSendKeepAliveRequest = keepAliveDelay;

            server = driver.Connect(endPoint);

            if (driver.IsCreated)
            {
                Debug.Log("Connecting to server...");
                entitySpawner = new EntitySpawner();
            }
        }

        protected override void OnDestroy()
        {
            Debug.Log("disconected from the server");
            driver.Dispose();
        }

        protected override void OnUpdate()
        {
            driver.ScheduleUpdate().Complete();
            currentTime = (float)Time.ElapsedTime;
            UpdateMessagePump();
            CheckAlive();
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
                    entitySpawner.CreateServerEntity();
                }
                else if (cmd == NetworkEvent.Type.Data)
                {
                    NetworkMessages(ref stream);
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    Debug.Log("Client got disconnected from server");
                    server = default(NetworkConnection);
                }
            }
        }

        void NetworkMessages(ref DataStreamReader stream)
        {
            lastKeepAliveResponseTime = currentTime;
            UpdateTimeToSendKeepAliveRequest(keepAliveDelay);

            byte networkMessageCode = stream.ReadByte();
            switch (networkMessageCode)
            {
                case (byte)NetworkMessageCode.KeepAlive:
                    Debug.Log("SERVER IS ALIVE");
                    break;
            }
        }

        private void CheckAlive()
        {
            if (!server.IsCreated)
            {
                Debug.LogError("Something went wrong, lost connection to server");
                return;
            }

            SendKeepAlive();

            if (waitingKeepAliveResponse && currentTime >= lastKeepAliveRequestTime + KeepAliveRetryDelay)
            {
                UpdateTimeToSendKeepAliveRequest(KeepAliveRetryDelay);
            }

        }

        private void SendKeepAlive()
        {

            if (!waitingKeepAliveResponse && currentTime >= timeToSendKeepAliveRequest)
            {
                Debug.Log("sending keep alive to server at " + currentTime);
                DataStreamWriter writer;
                driver.BeginSend(server, out writer);
                writer.WriteByte((byte)NetworkMessageCode.KeepAlive);
                driver.EndSend(writer);

                lastKeepAliveRequestTime = currentTime;
                waitingKeepAliveResponse = true;
            }
        }

        void UpdateTimeToSendKeepAliveRequest(float time)
        {
            timeToSendKeepAliveRequest += time;
            waitingKeepAliveResponse = false;
        }
    }
}