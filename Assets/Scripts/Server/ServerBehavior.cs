using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;

using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Error;
using Unity.Jobs;
using Unity.Burst;
using Higo.NetCode.Client;
using System;
using Higo.NetCode.Common;

#if UNITY_SERVER || UNITY_EDITOR
public class ServerBehavior : MonoBehaviour
{
    public NetCoreServer m_netcore;

    public ushort Port;
    public ServerProcessCommand processCommandJob;

    // Start is called before the first frame update
    void Start()
    {
        m_netcore = NetCoreServer.Create(Port);
    }

    private void OnDestroy()
    {
        m_netcore.Dispose();
    }

    // Update is called once per frame
    void Update()
    {
        m_netcore.Update<RoomInfomation>(OnNetworkUpdate);
    }

    private unsafe void OnNetworkUpdate(ref JobHandle JobHandle, in NativeStream commands, in NativeList<RoomInfomation> foreachCount)
    {
        processCommandJob.commandsReader = commands.AsReader();
        processCommandJob.rooms = foreachCount;
        JobHandle = processCommandJob.Schedule(JobHandle);
    }
}

public struct ServerProcessCommand : IJob
{
    [ReadOnly]
    public NativeStream.Reader commandsReader;
    [ReadOnly]  
    public NativeList<RoomInfomation> rooms;

    public void Execute()
    {
        var forEachLength = rooms.Length;
        if (forEachLength == 0) return;

        Command cmd = default;
        for (var index = 0; index < forEachLength; index++)
        {
            commandsReader.BeginForEachIndex(index);
            while (commandsReader.RemainingItemCount > 0)
            {
                NetCoreServer.ReadStream(ref commandsReader, ref cmd);
                Debug.Log($"{index}: {rooms[index].Id} {cmd.Sequence} {cmd.Value}");
            }
            commandsReader.EndForEachIndex();
        }
    }
}
#endif