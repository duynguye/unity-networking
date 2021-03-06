﻿using UnityEngine;

using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

using NetworkConnection = Unity.Networking.Transport.NetworkConnection;

public class ClientBehavior : MonoBehaviour
{
    public UdpNetworkDriver m_Driver;
    public NetworkPipeline m_Pipeline;
    public NetworkConnection m_Connection;
    public NetworkEndPoint m_Endpoint;
    public bool Done;

    // Start is called before the first frame update
    private void Start()
    {
        m_Driver = new UdpNetworkDriver(new SimulatorUtility.Parameters { MaxPacketSize = 256, MaxPacketCount = 30, PacketDelayMs = 100 });
        m_Pipeline = m_Driver.CreatePipeline(typeof(UnreliableSequencedPipelineStage), typeof(SimulatorPipelineStage));
        m_Connection = default;

        m_Endpoint = new NetworkEndPoint();
        m_Endpoint = NetworkEndPoint.Parse("127.0.0.1", 9000);

        m_Connection = m_Driver.Connect(m_Endpoint);
    }

    private  void OnDestroy()
    {
        m_Driver.Dispose();
    }

    // Update is called once per frame
    private void Update()
    {
        m_Driver.ScheduleUpdate().Complete();

        if (!m_Connection.IsCreated)
        {
            if (!Done)
            {
                Debug.Log("Something went wrong during connection");
            }

            return;
        }

        NetworkEvent.Type cmd;

        while ((cmd = m_Connection.PopEvent(m_Driver, out DataStreamReader stream)) != NetworkEvent.Type.Empty)
        {
            switch (cmd)
            {
                case NetworkEvent.Type.Connect:
                    Debug.Log("We are now connected to the server.");
                    var value = 1;

                    using (var writer = new DataStreamWriter(4, Allocator.Temp))
                    {
                        writer.Write(value);
                        m_Connection.Send(m_Driver, writer);
                    }

                    break;

                case NetworkEvent.Type.Data:
                    var readerCtx = default(DataStreamReader.Context);
                    uint streamValue = stream.ReadUInt(ref readerCtx);

                    Debug.Log("Got the value = " + streamValue + " back from the server.");

                    Done = true;
                    m_Connection.Disconnect(m_Driver);
                    m_Connection = default;

                    break;

                case NetworkEvent.Type.Disconnect:
                    Debug.Log("Client got disconnected from the server.");
                    m_Connection = default;

                    break;

                case NetworkEvent.Type.Empty:
                    break;
            }
        }
    }
}
