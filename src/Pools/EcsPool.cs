using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DCFApixels.DragonECS
{
    /// <summary>Pool for IEcsComponent components</summary>
    public sealed class EcsPool<T> : IEcsPoolImplementation<T>, IEcsStructPool<T>, IEnumerable<T> //IEnumerable<T> - IntelliSense hack
        where T : struct, IEcsComponent
    {
        private EcsWorld _source;
        private int _componentTypeID;
        private EcsMaskChunck _maskBit;

        private int[] _mapping;// index = entityID / value = itemIndex;/ value = 0 = no entityID
        private T[] _items; //dense
        private int _itemsCount;
        private int[] _recycledItems;
        private int _recycledItemsCount;

        private IEcsComponentReset<T> _componentResetHandler = EcsComponentResetHandler<T>.instance;
        private IEcsComponentCopy<T> _componentCopyHandler = EcsComponentCopyHandler<T>.instance;

        private List<IEcsPoolEventListener> _listeners = new List<IEcsPoolEventListener>();

        private EcsWorld.PoolsMediator _mediator;

        #region Properites
        public int Count => _itemsCount;
        public int Capacity => _items.Length;
        public int ComponentID => _componentTypeID;
        public Type ComponentType => typeof(T);
        public EcsWorld World => _source;
        #endregion

        #region Methods
        public ref T Add(int entityID)
        {
            ref int itemIndex = ref _mapping[entityID];
#if (DEBUG && !DISABLE_DEBUG) || ENABLE_DRAGONECS_ASSERT_CHEKS
            if (itemIndex > 0) EcsPoolThrowHalper.ThrowAlreadyHasComponent<T>(entityID);
#endif
            if (_recycledItemsCount > 0)
            {
                itemIndex = _recycledItems[--_recycledItemsCount];
                _itemsCount++;
            }
            else
            {
                itemIndex = ++_itemsCount;
                if (itemIndex >= _items.Length)
                    Array.Resize(ref _items, _items.Length << 1);
            }
            _mediator.RegisterComponent(entityID, _componentTypeID, _maskBit);
            _listeners.InvokeOnAddAndGet(entityID);
            return ref _items[itemIndex];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Get(int entityID)
        {
#if (DEBUG && !DISABLE_DEBUG) || ENABLE_DRAGONECS_ASSERT_CHEKS
            if (!Has(entityID)) EcsPoolThrowHalper.ThrowNotHaveComponent<T>(entityID);
#endif
            _listeners.InvokeOnGet(entityID);
            return ref _items[_mapping[entityID]];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref readonly T Read(int entityID)
        {
#if (DEBUG && !DISABLE_DEBUG) || ENABLE_DRAGONECS_ASSERT_CHEKS
            if (!Has(entityID)) EcsPoolThrowHalper.ThrowNotHaveComponent<T>(entityID);
#endif
            return ref _items[_mapping[entityID]];
        }
        public ref T TryAddOrGet(int entityID)
        {
            ref int itemIndex = ref _mapping[entityID];
            if (itemIndex <= 0)
            {
                if (_recycledItemsCount > 0)
                {
                    itemIndex = _recycledItems[--_recycledItemsCount];
                    _itemsCount++;
                }
                else
                {
                    itemIndex = ++_itemsCount;
                    if (itemIndex >= _items.Length)
                        Array.Resize(ref _items, _items.Length << 1);
                }
                _mediator.RegisterComponent(entityID, _componentTypeID, _maskBit);
                _listeners.InvokeOnAdd(entityID);
            }
            _listeners.InvokeOnGet(entityID);
            return ref _items[itemIndex];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Has(int entityID)
        {
            return _mapping[entityID] > 0;
        }
        public void Del(int entityID)
        {
#if (DEBUG && !DISABLE_DEBUG) || ENABLE_DRAGONECS_ASSERT_CHEKS
            if (!Has(entityID)) EcsPoolThrowHalper.ThrowNotHaveComponent<T>(entityID);
#endif
            ref int itemIndex = ref _mapping[entityID];
            _componentResetHandler.Reset(ref _items[itemIndex]);
            if (_recycledItemsCount >= _recycledItems.Length)
                Array.Resize(ref _recycledItems, _recycledItems.Length << 1);
            _recycledItems[_recycledItemsCount++] = itemIndex;
            _mapping[entityID] = 0;
            _itemsCount--;
            _mediator.UnregisterComponent(entityID, _componentTypeID, _maskBit);
            _listeners.InvokeOnDel(entityID);
        }
        public void TryDel(int entityID)
        {
            if (Has(entityID)) Del(entityID);
        }
        public void Copy(int fromEntityID, int toEntityID)
        {
#if (DEBUG && !DISABLE_DEBUG) || ENABLE_DRAGONECS_ASSERT_CHEKS
            if (!Has(fromEntityID)) EcsPoolThrowHalper.ThrowNotHaveComponent<T>(fromEntityID);
#endif
            _componentCopyHandler.Copy(ref Get(fromEntityID), ref TryAddOrGet(toEntityID));
        }
        public void Copy(int fromEntityID, EcsWorld toWorld, int toEntityID)
        {
#if (DEBUG && !DISABLE_DEBUG) || ENABLE_DRAGONECS_ASSERT_CHEKS
            if (!Has(fromEntityID)) EcsPoolThrowHalper.ThrowNotHaveComponent<T>(fromEntityID);
#endif
            _componentCopyHandler.Copy(ref Get(fromEntityID), ref toWorld.GetPool<T>().TryAddOrGet(toEntityID));
        }
        #endregion

        #region Callbacks
        void IEcsPoolImplementation.OnInit(EcsWorld world, EcsWorld.PoolsMediator mediator, int componentTypeID)
        {
            _source = world;
            _mediator = mediator;
            _componentTypeID = componentTypeID;
            _maskBit = EcsMaskChunck.FromID(componentTypeID);

            _mapping = new int[world.Capacity];
            _recycledItems = new int[world.Config.Get_PoolRecycledComponentsCapacity()];
            _recycledItemsCount = 0;
            _items = new T[world.Config.Get_PoolComponentsCapacity()];
            _itemsCount = 0;
        }
        void IEcsPoolImplementation.OnWorldResize(int newSize)
        {
            Array.Resize(ref _mapping, newSize);
        }
        void IEcsPoolImplementation.OnWorldDestroy() { }
        void IEcsPoolImplementation.OnReleaseDelEntityBuffer(ReadOnlySpan<int> buffer)
        {
            foreach (var entityID in buffer)
                TryDel(entityID);
        }
        #endregion

        #region Other
        void IEcsPool.AddRaw(int entityID, object dataRaw) => Add(entityID) = (T)dataRaw;
        object IEcsPool.GetRaw(int entityID) => Read(entityID);
        void IEcsPool.SetRaw(int entityID, object dataRaw) => Get(entityID) = (T)dataRaw;
        ref readonly T IEcsStructPool<T>.Read(int entityID) => ref Read(entityID);
        ref T IEcsStructPool<T>.Get(int entityID) => ref Get(entityID);
        #endregion

        #region Listeners
        public void AddListener(IEcsPoolEventListener listener)
        {
            if (listener == null) { throw new ArgumentNullException("listener is null"); }
            _listeners.Add(listener);
        }
        public void RemoveListener(IEcsPoolEventListener listener)
        {
            if (listener == null) { throw new ArgumentNullException("listener is null"); }
            _listeners.Remove(listener);
        }
        #endregion

        #region IEnumerator - IntelliSense hack
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => throw new NotImplementedException();
        IEnumerator IEnumerable.GetEnumerator() => throw new NotImplementedException();
        #endregion
    }
    /// <summary>Standard component</summary>
    public interface IEcsComponent { }
    public static class EcsPoolExt
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EcsPool<TComponent> GetPool<TComponent>(this EcsWorld self) where TComponent : struct, IEcsComponent
        {
            return self.GetPool<EcsPool<TComponent>>();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EcsPool<TComponent> GetPoolUnchecked<TComponent>(this EcsWorld self) where TComponent : struct, IEcsComponent
        {
            return self.GetPoolUnchecked<EcsPool<TComponent>>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EcsPool<TComponent> Include<TComponent>(this EcsAspectBuilderBase self) where TComponent : struct, IEcsComponent
        {
            return self.Include<EcsPool<TComponent>>();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EcsPool<TComponent> Exclude<TComponent>(this EcsAspectBuilderBase self) where TComponent : struct, IEcsComponent
        {
            return self.Exclude<EcsPool<TComponent>>();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EcsPool<TComponent> Optional<TComponent>(this EcsAspectBuilderBase self) where TComponent : struct, IEcsComponent
        {
            return self.Optional<EcsPool<TComponent>>();
        }
    }
}
