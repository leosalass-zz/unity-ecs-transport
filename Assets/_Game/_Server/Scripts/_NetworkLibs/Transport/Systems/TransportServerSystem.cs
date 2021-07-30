using UnityEngine;
using UnityEngine.Assertions;

using Unity.Entities;
using Unity.Jobs;
using Unity.Collections;
using Unity.Networking.Transport;
using System.IO;
using System.Reflection;

namespace Server
{

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

            var updateConnectionsJob = new UpdateConnectionsJob
            {
                driver = driver,
                connections = connections,
                lastKeepAlives = lastKeepAlives,
                currentTime = currentTime
            };

            var updateMessagePumpJump = new UpdateMessagePumpJob
            {
                driver = driver.ToConcurrent(),
                connections = connections.AsDeferredJobArray(),
                lastKeepAlives = lastKeepAlives.AsDeferredJobArray(),
            };

            var keepAliveJob = new KeepAliveJob
            {
                driver = driver.ToConcurrent(),
                connections = connections.AsDeferredJobArray(),
                lastKeepAlives = lastKeepAlives.AsDeferredJobArray(),
                keepAliveDelay = keepAliveDelay,
                currentTime = currentTime
            };

            ServerJobHandle = driver.ScheduleUpdate();
            ServerJobHandle = updateConnectionsJob.Schedule(ServerJobHandle);
            ServerJobHandle = updateMessagePumpJump.Schedule(connections, 1, ServerJobHandle);
            ServerJobHandle = keepAliveJob.Schedule(connections, 1, ServerJobHandle);
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

    struct UpdateConnectionsJob : IJob
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

    struct UpdateMessagePumpJob : IJobParallelForDefer
    {
        // Start querying the driver for events
        // that might have happened since the last update(tick).

        public NetworkDriver.Concurrent driver;
        public NativeArray<NetworkConnection> connections;
        public NativeArray<float> lastKeepAlives;

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
                    NetworkMessages(ref stream, index);
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    Disconnect(index);
                }
            }
        }

        void NetworkMessages(ref DataStreamReader stream, int index)
        {
            byte networkMessageCode = stream.ReadByte();

            switch (networkMessageCode)
            {
                case (byte)NetworkMessageCode.KeepAlive:
                    Debug.Log("CLIENT " + connections[index].InternalId + " IS ALIVE");
                    break;
            }
        }

        void Disconnect(int index)
        {
            Debug.Log("Client disconnected from server");
            connections[index] = default(NetworkConnection);
            lastKeepAlives[index] = 0;
        }
    }

    struct KeepAliveJob : IJobParallelForDefer
    {
        public NetworkDriver.Concurrent driver;
        public NativeArray<NetworkConnection> connections;
        public NativeArray<float> lastKeepAlives;
        public int keepAliveDelay;
        public float currentTime;
        public byte done;

        public void Execute(int index)
        {
            if (!connections[index].IsCreated)
            {
                if (done != 1)
                    Debug.Log("Something went wrong during connect, IsCreated: " + connections[index].IsCreated + " done: " + done);
                return;
            }

            keepAlive(index);
        }

        void keepAlive(int index)
        {
            if (lastKeepAlives[index] + keepAliveDelay <= currentTime)
            {
                lastKeepAlives[0] = currentTime;

                DataStreamWriter writer;
                driver.BeginSend(connections[index], out writer);
                writer.WriteByte(1);
                driver.EndSend(writer);
            }
        }
    }
}
