using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;

namespace Higo.NetCode.Common
{
    public static class NetCodeHelper
    {
        public static void CreateNetworkDriver(out NetworkDriver driver, out NetworkPipeline reliable, out NetworkPipeline frame)
        {
            var settings = new NetworkSettings();
            settings.WithReliableStageParameters();
            
            settings.WithPipelineParameters(2);
            driver = NetworkDriver.Create(settings);
            reliable = driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            frame = driver.CreatePipeline(typeof(UnreliableSequencedPipelineStage));
        }
    }
}
