using UnityEngine;

using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

using NetworkConnection = Unity.Networking.Transport.NetworkConnection;

struct ClientUpdateJob : IJob
{
    public UdpNetworkDriver driver;
    public NativeArray<NetworkConnection> connection;
    public NativeArray<byte> done;

    public void Execute()
    {
        if (!connection[0].IsCreated)
        {
            if (done[0] != 1)
                Debug.Log("Something went wrong during the connect");

            return;
        }

        DataStreamReader stream;
        NetworkEvent.Type command;

        while ((command = connection[0].PopEvent(driver, out stream)) != NetworkEvent.Type.Empty)
        {
            if (command == NetworkEvent.Type.Connect)
            {
                Debug.Log("We are now connected to the server");

                var value = 1;
                using (var writer = new DataStreamWriter(4, Allocator.Temp))
                {
                    writer.Write(value);
                    connection[0].Send(driver, writer);
                }
            }
            else if (command == NetworkEvent.Type.Data)
            {
                var readerCtx = default(DataStreamReader.Context);
                uint value = stream.ReadUInt(ref readerCtx);

                Debug.Log("Got the value = " + value + " back from the server");

                done[0] = 1;
                connection[0].Disconnect(driver);
                connection[0] = default;
            }
            else if (command == NetworkEvent.Type.Disconnect)
            {
                Debug.Log("Client got disconnected from server");
            }
        }
    }
}

public class JobifiedClientBehavior : MonoBehaviour
{
    public UdpNetworkDriver m_Driver;
    public NetworkPipeline m_Pipeline;
    public NativeArray<NetworkConnection> m_Connection;
    public NetworkEndPoint m_Endpoint;
    public NativeArray<byte> m_Done;
    public JobHandle ClientJobHandle;

    // Start is called before the first frame update
    private void Start()
    {
        m_Driver = new UdpNetworkDriver(new SimulatorUtility.Parameters { MaxPacketSize = 256, MaxPacketCount = 30, PacketDelayMs = 100 });
        m_Pipeline = m_Driver.CreatePipeline(typeof(UnreliableSequencedPipelineStage), typeof(SimulatorPipelineStage));
        m_Connection = new NativeArray<NetworkConnection>(1, Allocator.Persistent);
        m_Done = new NativeArray<byte>(1, Allocator.Persistent);

        m_Endpoint = new NetworkEndPoint();
        m_Endpoint = NetworkEndPoint.Parse("127.0.0.1", 9000);

        m_Connection[0] = m_Driver.Connect(m_Endpoint);
    }

    private void OnDestroy()
    {
        ClientJobHandle.Complete();

        m_Connection.Dispose();
        m_Driver.Dispose();
        m_Done.Dispose();
    }

    // Update is called once per frame
    private void Update()
    {
        ClientJobHandle.Complete();

        var job = new ClientUpdateJob
        {
            driver = m_Driver,
            connection = m_Connection,
            done = m_Done
        };

        ClientJobHandle = m_Driver.ScheduleUpdate();
        ClientJobHandle = job.Schedule(ClientJobHandle);
    }
}
