namespace Higo.NetCode.Common
{
    public struct Command : Sequencable
    {
        private uint sequence;
        public uint Value;
        public uint Sequence { get => sequence; set => sequence = value; }
        public override string ToString()
        {
            return $"{Value}";
        }
    }
}
