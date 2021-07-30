
using Unity.Burst;
using UnityEngine;
using UnityEngine.Assertions;

using Unity.Entities;
using Unity.Jobs;
using Unity.Collections;
using Unity.Networking.Transport;


namespace Client
{
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

            var updateMessagePumpJob = new UpdateMessagePumpJob
            {
                driver = driver,
                server = server,
                done = done
            };

            var keepAliveJob = new KeepAliveJob
            {
                driver = driver,
                server = server,
                lastKeepAlive = lastKeepAlive,
                keepAliveDelay = keepAliveDelay,
                currentTime = (float)Time.ElapsedTime
            };

            ClientJobHandle = driver.ScheduleUpdate();
            ClientJobHandle = updateMessagePumpJob.Schedule(ClientJobHandle);
            ClientJobHandle = keepAliveJob.Schedule(ClientJobHandle);
        }
    }

    struct UpdateMessagePumpJob : IJob
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
                    connected();
                }
                else if (cmd == NetworkEvent.Type.Data)
                {
                    NetworkMessages(ref stream);
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    disconnected();
                }
            }
        }

        void connected()
        {
            Debug.Log("We are now connected to the server");
        }

        void disconnected()
        {
            Debug.Log("Client got disconnected from server");
            server = default(NetworkConnection);
        }

        void NetworkMessages(ref DataStreamReader stream)
        {
            byte networkMessageCode = stream.ReadByte();

            switch (networkMessageCode)
            {
                case (byte)NetworkMessageCode.KeepAlive:
                    Debug.Log("SERVER IS ALIVE");
                    break;
            }
        }
    }


    struct KeepAliveJob : IJob
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

            keepAlive();
        }

        void keepAlive()
        {
            if (lastKeepAlive[0] + keepAliveDelay <= currentTime)
            {
                lastKeepAlive[0] = currentTime;

                DataStreamWriter writer;
                driver.BeginSend(server, out writer);
                writer.WriteByte((byte)NetworkMessageCode.KeepAlive);
                driver.EndSend(writer);
            }
        }
    }
}