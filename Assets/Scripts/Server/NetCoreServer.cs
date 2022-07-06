using System;
using System.Threading.Tasks;

using Unity.Jobs;
using Unity.Networking.Transport;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using Unity.Networking.Transport.Error;
using System.Runtime.InteropServices;
using Unity.Jobs.LowLevel.Unsafe;
using Higo.NetCode.Common;

namespace Higo.NetCode.Client
{

    [BurstCompile]
    public struct NetCoreServer : IDisposable
    {
        private NetworkDriver m_driver;
        private NativeList<UnclaimedConnection> m_unclaimedConnections;
        private NativeList<RoomInfomation> m_rooms;
        private JobHandle m_jobHandle;

        private NetworkPipeline m_reliablePipeline;
        private NetworkPipeline m_framePipeline;
        private NetCoreTime m_time;

        public bool IsCreated => m_driver.IsCreated;
        public bool Listening => IsCreated ? m_driver.Listening : false;

        public static NetCoreServer Create(ushort port) => Create(port, NetworkEndPoint.AnyIpv4);
        public static NetCoreServer Create(ushort port, NetworkEndPoint endpoint)
        {
            NetCoreServer network;
            endpoint.Port = port;

            NetCodeHelper.CreateNetworkDriver(out network.m_driver, out network.m_reliablePipeline, out network.m_framePipeline);
            network.m_driver.Bind(endpoint);
            network.m_driver.Listen();

            network.m_unclaimedConnections = new NativeList<UnclaimedConnection>(16, Allocator.Persistent);
            network.m_rooms = new NativeList<RoomInfomation>(16, Allocator.Persistent);
            network.m_jobHandle = default;
            network.m_time = default;

            return network;
        }

        public delegate void OnUpdate<T>(ref JobHandle JobHandle, in NativeStream commands, in NativeList<T> foreachCount) where T : unmanaged;
        [BurstCompile]
        public void Update<T>(OnUpdate<T> onUpdate = null) where T : unmanaged
        {
            m_jobHandle.Complete();
            m_time.DeltaTime = Time.deltaTime;
            m_time.ElapsedTime += m_time.DeltaTime;
            var updateConnectionJob = new ServerUpdateConnectionJob()
            {
                driver = m_driver,
                unclaimedConnections = m_unclaimedConnections,
                timeout = 10f,
                time = m_time
            };

            var receiveUnclaimedDataJob = new ServerReceiveUnclaimedDataJob()
            {
                driver = m_driver,
                unclaimedConnections = m_unclaimedConnections,
                repliablePipeline = m_reliablePipeline
            };
            m_jobHandle = m_driver.ScheduleUpdate();
            m_jobHandle = updateConnectionJob.Schedule(m_jobHandle);
            m_jobHandle = receiveUnclaimedDataJob.Schedule(m_jobHandle);

            //m_jobHandle = NativeStream.ScheduleConstruct(out var commands, m_rooms, m_jobHandle, Allocator.TempJob);
            //m_jobHandle = commands.Dispose(m_jobHandle);
        }

        public void Dispose()
        {
            m_jobHandle.Complete();
            m_driver.Dispose();

            var roomsDispose = new ServerRoomsDispose()
            {
                rooms = m_rooms.AsDeferredJobArray()
            };

            m_jobHandle = JobHandle.CombineDependencies(
                m_unclaimedConnections.Dispose(default),
                roomsDispose.ScheduleBatch(m_rooms.Length, 10, default),
                m_jobHandle = m_rooms.Dispose(default)
            );
            m_jobHandle.Complete();
            m_jobHandle = default;
        }

        [BurstCompile]
        public static void ReadStream(ref NativeStream.Reader reader, ref Common.Command cmd)
        {
            cmd = reader.Read<Command>();
        }
    }

    public struct UnclaimedConnection
    {
        public float FirstConnectionTime;
        public NetworkConnection Connection;
    }

    public struct RoomInfomation : IDisposable
    {
        public FixedString32Bytes Id;
        public UnsafeList<NetworkConnection> Connections;

        public RoomInfomation(string id, int capacity, Allocator allocator)
        {
            Id = new FixedString32Bytes(id);
            Connections = new UnsafeList<NetworkConnection>(capacity, allocator);
        }

        public void Dispose()
        {
            Connections.Dispose();
        }
    }

    [BurstCompile]
    public struct ServerUpdateConnectionJob : IJob
    {
        public NetworkDriver driver;
        public NativeList<UnclaimedConnection> unclaimedConnections;
        public NetCoreTime time;

        public float timeout;
        public void Execute()
        {
            // Cleanup 
            var cur = time.ElapsedTime;
            for (var i = 0; i < unclaimedConnections.Length; i++)
            {
                if (!unclaimedConnections[i].Connection.IsCreated)
                {
                    unclaimedConnections.RemoveAtSwapBack(i);
                    --i;
                }
                else if ((cur - unclaimedConnections[i].FirstConnectionTime) >= timeout)
                {
                    unclaimedConnections[i].Connection.Disconnect(driver);
                    unclaimedConnections.RemoveAtSwapBack(i);
                    --i;
                }
            }

            // Accept new connection
            NetworkConnection c;
            while ((c = driver.Accept()) != default)
            {
                unclaimedConnections.Add(new UnclaimedConnection()
                {
                    FirstConnectionTime = cur,
                    Connection = c
                });
                Debug.Log($"Accept new connection: {c.InternalId}, in time {cur}");
            }
        }
    }

    public enum UnclaimedBehavior : byte
    {
        JoinRoom, CreateRoom
    }

    public unsafe struct ServerReceiveUnclaimedDataJob : IJob
    {
        public NetworkDriver driver;
        public NativeList<UnclaimedConnection> unclaimedConnections;
        public NativeList<RoomInfomation> rooms;
        public NativeParallelHashMap<FixedString128Bytes, int> roomId2index;
        public NetworkPipeline repliablePipeline;

        public void Execute()
        {
            for (var index = 0; index < unclaimedConnections.Length; index++)
            {
                var unclaimed = unclaimedConnections[index];
                var conn = unclaimed.Connection;
                if (!conn.IsCreated)
                    return;
                DataStreamReader reader;
                NetworkEvent.Type evt;
                while ((evt = driver.PopEventForConnection(conn, out reader, out var pipeline)) != NetworkEvent.Type.Empty)
                {
                    if (evt == NetworkEvent.Type.Data)
                    {
                        if (pipeline != repliablePipeline) continue;
                        var type = (UnclaimedBehavior)reader.ReadByte();
                        switch (type)
                        {
                            case UnclaimedBehavior.JoinRoom:
                                var roomId = reader.ReadFixedString128();
                                if (roomId2index.TryGetValue(roomId, out var roomIndex))
                                {
                                    var room = rooms[roomIndex];
                                    room.Connections.Add(conn);
                                    unclaimedConnections[index] = default;

                                    driver.BeginSend(repliablePipeline, conn, out var writer);
                                    var ret = new RoomRequestResult()
                                    {

                                    };
                                    writer.Write(ref ret);
                                    driver.EndSend(writer);
                                }
                                else
                                {

                                }
                                break;
                            case UnclaimedBehavior.CreateRoom:
                                break;
                        }
                    }
                    else if (evt == NetworkEvent.Type.Connect)
                    {
                        Debug.Log($"[Server]connected: {conn.InternalId}, pipeline: {pipeline}");
                    }
                    else if (evt == NetworkEvent.Type.Disconnect)
                    {
                        var reason = (DisconnectReason)reader.ReadByte();
                        Debug.Log($"[Server]disconnected: {conn.InternalId}, pipeline: {pipeline}, reason: {TransportUtility.GetReasonString(reason)}");
                        unclaimedConnections[index] = default;
                    }
                }
            }
        }

        public void OnData()
        {

        }
    }

    public struct ServerRoomsDispose : IJobParallelForBatch
    {
        public NativeArray<RoomInfomation> rooms;
        public void Execute(int startIndex, int count)
        {
            if (count == 0) return;
            for (var i = 0; i < count; i++)
            {
                rooms[i + startIndex].Dispose();
            }
        }
    }
}
