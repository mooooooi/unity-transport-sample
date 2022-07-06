using Higo.NetCode.Common;
using System.Threading.Tasks;

namespace Higo.NetCode.Client
{
    public interface INetCoreClient
    {
        Task Connect(string ipWithPort);
        Task Connect(string ip, ushort port);
        Task Disconnect();
        void Send(Command cmd);
    }
}
