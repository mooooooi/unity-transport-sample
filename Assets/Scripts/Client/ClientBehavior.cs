using System.Collections;
using System.Collections.Generic;
using Unity.Networking.Transport;
using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Networking.Transport.Error;
using System.Threading.Tasks;
using Higo.NetCode.Client;
using Higo.NetCode.Common;

#if !UNITY_SERVER
// legacy animator cinemachine
// burst, jobs -> ecs -> netcode, animation, 
// burst, jobs -> mono -> animations.rigging, unity transport
public class ClientBehavior : MonoBehaviour
{
    public string Address;

    private NetCoreClient m_netcore;

    // Start is called before the first frame update
    async void Start()
    {
        m_netcore = NetCoreClient.Create();

        await m_netcore.Connect(Address);
        m_netcore.Send(new Command() { Sequence = 0, Value = 1 });
        m_netcore.Send(new Command() { Sequence = 1, Value = 3 });
        m_netcore.Send(new Command() { Sequence = 2, Value = 2 });
        m_netcore.Send(new Command() { Sequence = 3, Value = 4 });
    }

    private void OnDestroy()
    {
        m_netcore.Dispose();
    }

    // Update is called once per frame
    void Update()
    {
        m_netcore.Update();
    }
}
#endif