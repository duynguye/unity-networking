using UnityEngine;

using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;
using Unity.Collections;
using Unity.Jobs;

using NetworkConnection = Unity.Networking.Transport.NetworkConnection;
using UnityEngine.Assertions;
using System;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Collections.LowLevel.Unsafe;

struct ServerUpdateConnectionsJob : IJob
{
    public UdpNetworkDriver driver;
    public NativeList<NetworkConnection> connections;

    public void Execute()
    {
        // Clean up the connections
        for (int i = 0; i < connections.Length; i++)
        {
            if (!connections[i].IsCreated)
            {
                connections.RemoveAtSwapBack(i);
                --i;
            }
        }

        NetworkConnection c;
        while ((c = driver.Accept()) != default)
        {
            connections.Add(c);
            Debug.Log("Accepted a connection");
        }
    }
}

struct ServerUpdateJob : IJobParallelFor
{
    public UdpNetworkDriver.Concurrent driver;
    public NativeArray<NetworkConnection> connections;
    public NativeArray<NetworkPipeline> pipeline;

    public void Execute(int index)
    {
        DataStreamReader stream;
        if (!connections[index].IsCreated)
            Assert.IsTrue(true);

        NetworkEvent.Type command;
        while ((command = driver.PopEventForConnection(connections[index], out stream)) != NetworkEvent.Type.Empty)
        {
            if (command == NetworkEvent.Type.Data)
            {
                var readerCtx = default(DataStreamReader.Context);
                uint number = stream.ReadUInt(ref readerCtx);

                Debug.Log("Got " + number + " from the Client adding +2 to it");
                number += 2;

                using (var writer = new DataStreamWriter(4, Allocator.Temp))
                {
                    writer.Write(number);
                    driver.Send(pipeline[0], connections[index], writer);
                }
            }
            else if (command == NetworkEvent.Type.Disconnect)
            {
                Debug.Log("Client disconnected from server");
                connections[index] = default;
            }
        }
    }
}

public class JobifiedServerBehavior : MonoBehaviour
{
    public UdpNetworkDriver m_Driver;
    public NativeList<NetworkPipeline> m_Pipeline;
    public NetworkEndPoint m_Endpoint;
    private NativeList<NetworkConnection> m_Connections;
    private JobHandle ServerJobHandle;

    // Start is called before the first frame update
    private void Start()
    {
        m_Connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);
        m_Driver = new UdpNetworkDriver(new SimulatorUtility.Parameters { MaxPacketSize = 256, MaxPacketCount = 30, PacketDelayMs = 100 });
        m_Pipeline = new NativeList<NetworkPipeline>(16, Allocator.Persistent);
        m_Pipeline.Add(m_Driver.CreatePipeline(typeof(UnreliableSequencedPipelineStage), typeof(SimulatorPipelineStage)));

        m_Endpoint = new NetworkEndPoint();
        m_Endpoint = NetworkEndPoint.Parse("0.0.0.0", 9000);

        if (m_Driver.Bind(m_Endpoint) != 0)
        {
            Debug.Log("Failed to bind to port 9000");
        }
        else
        {
            m_Driver.Listen();
        }
    }

    private void OnDestroy()
    {
        ServerJobHandle.Complete();

        m_Driver.Dispose();
        m_Pipeline.Dispose();
        m_Connections.Dispose();
    }

    // Update is called once per frame
    private void Update()
    {
        ServerJobHandle.Complete();

        var connectionJob = new ServerUpdateConnectionsJob
        {
            driver = m_Driver,
            connections = m_Connections
        };

        var serverUpdateJob = new ServerUpdateJob
        {
            driver = m_Driver.ToConcurrent(),
            connections = m_Connections.AsDeferredJobArray(),
            pipeline = m_Pipeline.AsDeferredJobArray()
        };

        ServerJobHandle = m_Driver.ScheduleUpdate();
        ServerJobHandle = connectionJob.Schedule(ServerJobHandle);

        ServerJobHandle.Complete();
        ServerJobHandle = serverUpdateJob.Schedule(m_Connections.Length, 1, ServerJobHandle);
    }
}
