using DCFApixels.DragonECS.PoolsCore;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
#if ENABLE_IL2CPP
using Unity.IL2CPP.CompilerServices;
#endif

namespace DCFApixels.DragonECS
{
    /// <summary>Component without data</summary>
    public interface IEcsTagComponent : IEcsComponentType { }

#if ENABLE_IL2CPP
    [Il2CppSetOption (Option.NullChecks, false)]
#endif
    [MetaColor(MetaColor.DragonRose)]
    [MetaGroup(EcsConsts.FRAMEWORK_NAME)]
    /// <summary>Pool for IEcsTagComponent components</summary>
    public sealed class EcsTagPool<T> : IEcsPoolImplementation<T>, IEcsStructPool<T>, IEnumerable<T> //IEnumerable<T> - IntelliSense hack
        where T : struct, IEcsTagComponent
    {
        private EcsWorld _source;
        private int _componentTypeID;
        private EcsMaskChunck _maskBit;

        private bool[] _mapping;// index = entityID / value = itemIndex;/ value = 0 = no entityID
        private int _count = 0;

#if !DISABLE_POOLS_EVENTS
        private List<IEcsPoolEventListener> _listeners = new List<IEcsPoolEventListener>();
        private int _listenersCachedCount = 0;
#endif

        private T _fakeComponent;
        private EcsWorld.PoolsMediator _mediator;

        #region CheckValide
#if DEBUG
        private static bool _isInvalidType;
        static EcsTagPool()
        {
#pragma warning disable IL2090 // 'this' argument does not satisfy 'DynamicallyAccessedMembersAttribute' in call to target method. The generic parameter of the source method or type does not have matching annotations.
            _isInvalidType = typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length > 0;
#pragma warning restore IL2090
        }
        public EcsTagPool()
        {
            if (_isInvalidType)
            {
                throw new EcsFrameworkException($"{typeof(T).Name} type must not contain any data.");
            }
        }
#endif
        #endregion

        #region Properites
        public int Count
        {
            get { return _count; }
        }
        public int ComponentTypeID
        {
            get { return _componentTypeID; }
        }
        public Type ComponentType
        {
            get { return typeof(T); }
        }
        public EcsWorld World
        {
            get { return _source; }
        }
        public bool IsReadOnly
        {
            get { return false; }
        }
        #endregion

        #region Method
        public void Add(int entityID)
        {
#if (DEBUG && !DISABLE_DEBUG) || ENABLE_DRAGONECS_ASSERT_CHEKS
            if (Has(entityID)) { EcsPoolThrowHalper.ThrowAlreadyHasComponent<T>(entityID); }
#endif
            _count++;
            _mapping[entityID] = true;
            _mediator.RegisterComponent(entityID, _componentTypeID, _maskBit);
#if !DISABLE_POOLS_EVENTS
            _listeners.InvokeOnAdd(entityID, _listenersCachedCount);
#endif
        }
        public void TryAdd(int entityID)
        {
            if (Has(entityID) == false)
            {
                Add(entityID);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Has(int entityID)
        {
            return _mapping[entityID];
        }
        public void Del(int entityID)
        {
#if (DEBUG && !DISABLE_DEBUG) || ENABLE_DRAGONECS_ASSERT_CHEKS
            if (!Has(entityID)) { EcsPoolThrowHalper.ThrowNotHaveComponent<T>(entityID); }
#endif
            _mapping[entityID] = false;
            _count--;
            _mediator.UnregisterComponent(entityID, _componentTypeID, _maskBit);
#if !DISABLE_POOLS_EVENTS
            _listeners.InvokeOnDel(entityID, _listenersCachedCount);
#endif
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void TryDel(int entityID)
        {
            if (Has(entityID))
            {
                Del(entityID);
            }
        }
        public void Copy(int fromEntityID, int toEntityID)
        {
#if (DEBUG && !DISABLE_DEBUG) || ENABLE_DRAGONECS_ASSERT_CHEKS
            if (!Has(fromEntityID)) { EcsPoolThrowHalper.ThrowNotHaveComponent<T>(fromEntityID); }
#endif
            TryAdd(toEntityID);
        }
        public void Copy(int fromEntityID, EcsWorld toWorld, int toEntityID)
        {
#if (DEBUG && !DISABLE_DEBUG) || ENABLE_DRAGONECS_ASSERT_CHEKS
            if (!Has(fromEntityID)) { EcsPoolThrowHalper.ThrowNotHaveComponent<T>(fromEntityID); }
#endif
            toWorld.GetPool<T>().TryAdd(toEntityID);
        }
        public void Set(int entityID, bool isHas)
        {
            if (isHas != Has(entityID))
            {
                if (isHas)
                {
                    Add(entityID);
                }
                else
                {
                    Del(entityID);
                }
            }
        }
        public void Toggle(int entityID)
        {
            if (Has(entityID))
            {
                Del(entityID);
            }
            else
            {
                Add(entityID);
            }
        }

        public void ClearAll()
        {
            var span = _source.Where(out SingleAspect<EcsTagPool<T>> _);
            _count = 0;
            foreach (var entityID in span)
            {
                _mapping[entityID] = false;
                _mediator.UnregisterComponent(entityID, _componentTypeID, _maskBit);
#if !DISABLE_POOLS_EVENTS
                _listeners.InvokeOnDel(entityID, _listenersCachedCount);
#endif
            }
        }
        #endregion

        #region Callbacks
        void IEcsPoolImplementation.OnInit(EcsWorld world, EcsWorld.PoolsMediator mediator, int componentTypeID)
        {
            _source = world;
            _mediator = mediator;
            _componentTypeID = componentTypeID;
            _maskBit = EcsMaskChunck.FromID(componentTypeID);

            _mapping = new bool[world.Capacity];
        }
        void IEcsPoolImplementation.OnWorldResize(int newSize)
        {
            Array.Resize(ref _mapping, newSize);
        }
        void IEcsPoolImplementation.OnWorldDestroy() { }

        void IEcsPoolImplementation.OnReleaseDelEntityBuffer(ReadOnlySpan<int> buffer)
        {
            if (_count <= 0)
            {
                return;
            }
            foreach (var entityID in buffer)
            {
                TryDel(entityID);
            }
        }
        #endregion

        #region Other
        void IEcsPool.AddRaw(int entityID, object dataRaw) { Add(entityID); }
        object IEcsReadonlyPool.GetRaw(int entityID)
        {
#if (DEBUG && !DISABLE_DEBUG) || ENABLE_DRAGONECS_ASSERT_CHEKS
            if (Has(entityID) == false) { EcsPoolThrowHalper.ThrowNotHaveComponent<T>(entityID); }
#endif
            return _fakeComponent;
        }
        void IEcsPool.SetRaw(int entityID, object dataRaw)
        {
#if (DEBUG && !DISABLE_DEBUG) || ENABLE_DRAGONECS_ASSERT_CHEKS
            if (Has(entityID) == false) { EcsPoolThrowHalper.ThrowNotHaveComponent<T>(entityID); }
#endif
        }
        ref T IEcsStructPool<T>.Add(int entityID)
        {
            Add(entityID);
            return ref _fakeComponent;
        }
        ref readonly T IEcsStructPool<T>.Read(int entityID)
        {
#if (DEBUG && !DISABLE_DEBUG) || ENABLE_DRAGONECS_ASSERT_CHEKS
            if (Has(entityID) == false) { EcsPoolThrowHalper.ThrowNotHaveComponent<T>(entityID); }
#endif
            return ref _fakeComponent;
        }
        ref T IEcsStructPool<T>.Get(int entityID)
        {
#if (DEBUG && !DISABLE_DEBUG) || ENABLE_DRAGONECS_ASSERT_CHEKS
            if (Has(entityID) == false) { EcsPoolThrowHalper.ThrowNotHaveComponent<T>(entityID); }
#endif
            return ref _fakeComponent;
        }
        #endregion

        #region Listeners
#if !DISABLE_POOLS_EVENTS
        public void AddListener(IEcsPoolEventListener listener)
        {
            if (listener == null) { EcsPoolThrowHalper.ThrowNullListener(); }
            _listeners.Add(listener);
            _listenersCachedCount++;
        }
        public void RemoveListener(IEcsPoolEventListener listener)
        {
            if (listener == null) { EcsPoolThrowHalper.ThrowNullListener(); }
            if (_listeners.Remove(listener))
            {
                _listenersCachedCount--;
            }
        }
#endif
        #endregion

        #region IEnumerator - IntelliSense hack
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => throw new NotImplementedException();
        IEnumerator IEnumerable.GetEnumerator() => throw new NotImplementedException();
        #endregion

        #region MarkersConverter
        public static implicit operator EcsTagPool<T>(IncludeMarker a) { return a.GetInstance<EcsTagPool<T>>(); }
        public static implicit operator EcsTagPool<T>(ExcludeMarker a) { return a.GetInstance<EcsTagPool<T>>(); }
        public static implicit operator EcsTagPool<T>(OptionalMarker a) { return a.GetInstance<EcsTagPool<T>>(); }
        #endregion
    }
    public static class EcsTagPoolExt
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EcsTagPool<TTagComponent> GetPool<TTagComponent>(this EcsWorld self) where TTagComponent : struct, IEcsTagComponent
        {
            return self.GetPoolInstance<EcsTagPool<TTagComponent>>();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EcsTagPool<TTagComponent> GetPoolUnchecked<TTagComponent>(this EcsWorld self) where TTagComponent : struct, IEcsTagComponent
        {
            return self.GetPoolInstanceUnchecked<EcsTagPool<TTagComponent>>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EcsTagPool<TTagComponent> Include<TTagComponent>(this EcsAspect.Builder self) where TTagComponent : struct, IEcsTagComponent
        {
            return self.IncludePool<EcsTagPool<TTagComponent>>();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EcsTagPool<TTagComponent> Exclude<TTagComponent>(this EcsAspect.Builder self) where TTagComponent : struct, IEcsTagComponent
        {
            return self.ExcludePool<EcsTagPool<TTagComponent>>();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EcsTagPool<TTagComponent> Optional<TTagComponent>(this EcsAspect.Builder self) where TTagComponent : struct, IEcsTagComponent
        {
            return self.OptionalPool<EcsTagPool<TTagComponent>>();
        }

        //---------------------------------------------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EcsTagPool<TTagComponent> GetTagPool<TTagComponent>(this EcsWorld self) where TTagComponent : struct, IEcsTagComponent
        {
            return self.GetPoolInstance<EcsTagPool<TTagComponent>>();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EcsTagPool<TTagComponent> GetTagPoolUnchecked<TTagComponent>(this EcsWorld self) where TTagComponent : struct, IEcsTagComponent
        {
            return self.GetPoolInstanceUnchecked<EcsTagPool<TTagComponent>>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EcsTagPool<TTagComponent> IncludeTag<TTagComponent>(this EcsAspect.Builder self) where TTagComponent : struct, IEcsTagComponent
        {
            return self.IncludePool<EcsTagPool<TTagComponent>>();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EcsTagPool<TTagComponent> ExcludeTag<TTagComponent>(this EcsAspect.Builder self) where TTagComponent : struct, IEcsTagComponent
        {
            return self.ExcludePool<EcsTagPool<TTagComponent>>();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EcsTagPool<TTagComponent> OptionalTag<TTagComponent>(this EcsAspect.Builder self) where TTagComponent : struct, IEcsTagComponent
        {
            return self.OptionalPool<EcsTagPool<TTagComponent>>();
        }
    }
}
