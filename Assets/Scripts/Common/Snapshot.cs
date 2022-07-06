namespace Higo.NetCode.Common
{
    public struct Snapshot : Sequencable
    {
        private uint sequence;
        public uint Value;
        public uint Sequence => sequence;
    }
}
