using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Error;
using System.Threading.Tasks;
using System;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Higo.NetCode.Common;

namespace Higo.NetCode.Client
{
    [BurstCompile]
    unsafe struct ClientUpdateJob : IJob
    {
        public NetworkDriver driver;
        public NetworkConnection connection;
        public NativeReference<(byte, byte)> connectResult;
        public NativeRingBuffer<Snapshot> snapshots;
        public void Execute()
        {
            if (!connection.IsCreated)
            {
                return;
            }

            DataStreamReader reader;
            NetworkEvent.Type cmd;
            while ((cmd = connection.PopEvent(driver, out reader)) != NetworkEvent.Type.Empty)
            {
                if (cmd == NetworkEvent.Type.Connect)
                {
                    if (connectResult.IsCreated)
                        connectResult.Value = (1, 0);
                }
                else if (cmd == NetworkEvent.Type.Data)
                {
                    var size = UnsafeUtility.SizeOf<Snapshot>();
                    var align = UnsafeUtility.AlignOf<Snapshot>();
                    var ptr = UnsafeUtility.Malloc(size, align, Allocator.Temp);
                    reader.ReadBytes((byte*)ptr, 1);
                    snapshots.Set(UnsafeUtility.AsRef<Snapshot>(ptr));
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    var reasonByte = reader.ReadByte();
                    connection = default;
                    if (connectResult.IsCreated)
                        connectResult.Value = (2, reasonByte);
                }
            }
        }
    }

    public unsafe struct NetCoreClient : IDisposable, INetCoreClient
    {
        private NetworkDriver m_driver;
        private NetworkConnection m_connection;
        private NativeArray<byte> m_done;
        private JobHandle m_jobHandle;
        private NativeRingBuffer<Snapshot> m_snapshots;
        private NetworkPipeline m_reliablePipeline;
        private NetworkPipeline m_framePipeline;

        public NativeReference<(byte code, byte reason)> m_connectResult;
        private TaskCompletionSource<object> m_connectDeferred;


        // Start is called before the first frame update
        public static NetCoreClient Create()
        {
            var network = new NetCoreClient();

            NetCodeHelper.CreateNetworkDriver(out network.m_driver, out network.m_reliablePipeline, out network.m_framePipeline);
            network.m_connection = default;
            network.m_done = new NativeArray<byte>(1, Allocator.Persistent);
            network.m_jobHandle = default;
            network.m_connectResult = new NativeReference<(byte, byte)>(Allocator.Persistent);
            network.m_snapshots = new NativeRingBuffer<Snapshot>(5, Allocator.Persistent);

            return network;
        }

        public Task Connect(string ipWithPort)
        {
            var members = ipWithPort.Split(':');
            if (members.Length != 2 || string.IsNullOrWhiteSpace(members[0]) || string.IsNullOrWhiteSpace(members[1]) || !ushort.TryParse(members[1], out var port))
                return Task.FromException(new Exception("NetworkEndPoint parse failed."));
            return Connect(members[0], port);
        }

        public Task Connect(string ip, ushort port)
        {
            if (!NetworkEndPoint.TryParse(ip, port, out var endpoint))
                return Task.FromException(new Exception("NetworkEndPoint parse failed."));
            m_connection = m_driver.Connect(endpoint);

            m_connectDeferred = new TaskCompletionSource<object>();
            return m_connectDeferred.Task;
        }

        public Task Disconnect()
        {
            if (!m_connection.IsCreated) return Task.CompletedTask;
            m_connection.Disconnect(m_driver);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            m_jobHandle.Complete();

            m_driver.Dispose();
            m_done.Dispose();
            m_connectResult.Dispose();
            m_snapshots.Dispose();
        }

        // Update is called once per frame
        public void Update()
        {
            m_jobHandle.Complete();

            if (m_connectDeferred != null && m_connectResult.Value != default)
            {
                var ret = m_connectResult.Value;
                if (ret.code == 1)
                {
                    m_connectDeferred.SetResult(null);
                }
                else if (ret.code == 2)
                {
                    m_connectDeferred.SetException(new Exception($"Connect fail, reason: {TransportUtility.GetReasonString((DisconnectReason)ret.reason)}"));
                }
                m_connectDeferred = null;
                m_connectResult.Value = default;
            }

            if (m_connection.IsCreated)
            {
                var job = new ClientUpdateJob()
                {
                    driver = m_driver,
                    connection = m_connection,
                    connectResult = m_connectResult,
                    snapshots = m_snapshots
                };

                m_jobHandle = m_driver.ScheduleUpdate();
                m_jobHandle = job.Schedule(m_jobHandle);
            }
        }

        public void Send(Command cmd)
        {
            m_driver.BeginSend(m_reliablePipeline, m_connection, out var writer);
            writer.WriteBytes((byte*)UnsafeUtility.AddressOf(ref cmd), UnsafeUtility.SizeOf<Command>());
            m_driver.EndSend(writer);
        }
    }
}
