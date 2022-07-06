using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Higo.NetCode.Common
{
    public interface Sequencable
    {
        public uint Sequence { get; }
    }

    public struct NativeRingBuffer<T> : INativeDisposable where T : unmanaged, Sequencable
    {
        private NativeArray<T> datas;
        private NativeArray<int> indices;
        public readonly int Length;
        public bool IsCreated => datas.IsCreated;

        public NativeRingBuffer(int length, Allocator allocator)
        {
            Length = length;
            datas = new NativeArray<T>(length, allocator, NativeArrayOptions.UninitializedMemory);
            indices = new NativeArray<int>(length, allocator, NativeArrayOptions.UninitializedMemory);
            for (var i = 0; i < length; i++)
            {
                indices[i] = -1;
            }
        }

        public JobHandle Dispose(JobHandle inputDeps)
        {
            inputDeps = datas.Dispose(inputDeps);
            inputDeps = datas.Dispose(inputDeps);
            return inputDeps;
        }

        public void Dispose()
        {
            datas.Dispose();
            indices.Dispose();
        }

        public T Get(uint sequence)
        {
            var localIndex = sequence % Length;
            return indices[(int)localIndex] == sequence ? datas[(int)localIndex] : default;
        }

        public bool TryGet(uint sequence, out T value)
        {
            var localIndex = sequence % Length;
            var isGreateEqualThan = indices[(int)localIndex] == sequence;
            value = isGreateEqualThan ? datas[(int)localIndex] : default;
            return isGreateEqualThan;
        }

        [WriteAccessRequired]
        public void Set(T value)
        {
            var sequence = (int)value.Sequence;
            var localIndex = sequence % Length;
            var recordSequence = indices[localIndex];
            indices[localIndex] = sequence >= recordSequence ? sequence : recordSequence;
            datas[localIndex] = sequence >= recordSequence ? value : default;
        }

        public bool ContainKey(int sequence)
        {
            var localIndex = sequence % Length;
            return indices[localIndex] == sequence;
        }
    }
}
