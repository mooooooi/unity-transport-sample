using Unity.Collections;

namespace Higo.NetCode.Common
{
    public struct RoomRequestResult
    {
        public int Code;
        public FixedString128Bytes RoomIdOrReason;

        public bool IsSucceed => Code == 0;
    }
}
