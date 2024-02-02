using System.Runtime.CompilerServices;

namespace DCFApixels.DragonECS
{
    #region IEcsWorldComponent
    public interface IEcsWorldComponent<T>
    {
        void Init(ref T component, EcsWorld world);
        void OnDestroy(ref T component, EcsWorld world);
    }
    public static class EcsWorldComponentHandler<T>
    {
        public static readonly IEcsWorldComponent<T> instance;
        public static readonly bool isHasHandler;
        static EcsWorldComponentHandler()
        {
            T def = default;
            if (def is IEcsWorldComponent<T> intrf)
            {
                instance = intrf;
            }
            else
            {
                instance = new DummyHandler();
            }
        }
        private class DummyHandler : IEcsWorldComponent<T>
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Init(ref T component, EcsWorld world) { }
            public void OnDestroy(ref T component, EcsWorld world) { }
        }
    }
    #endregion

    #region IEcsComponentReset
    public interface IEcsComponentReset<T>
    {
        void Reset(ref T component);
    }
    public static class EcsComponentResetHandler<T>
    {
        public static readonly IEcsComponentReset<T> instance;
        public static readonly bool isHasHandler;
        static EcsComponentResetHandler()
        {
            T def = default;
            if (def is IEcsComponentReset<T> intrf)
            {
                instance = intrf;
            }
            else
            {
                instance = new DummyHandler();
            }
        }
        private sealed class DummyHandler : IEcsComponentReset<T>
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Reset(ref T component) => component = default;
        }
    }
    #endregion

    #region IEcsComponentCopy
    public interface IEcsComponentCopy<T>
    {
        void Copy(ref T from, ref T to);
    }
    public static class EcsComponentCopyHandler<T>
    {
        public static readonly IEcsComponentCopy<T> instance;
        public static readonly bool isHasHandler;
        static EcsComponentCopyHandler()
        {
            T def = default;
            if (def is IEcsComponentCopy<T> intrf)
            {
                instance = intrf;
            }
            else
            {
                instance = new DummyHandler();
            }
        }
        private sealed class DummyHandler : IEcsComponentCopy<T>
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Copy(ref T from, ref T to) => to = from;
        }
    }
    #endregion
}
