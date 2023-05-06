using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DCFApixels.DragonECS
{
    using static EcsPoolThrowHalper;
    /// <summary>Pool for IEcsComponent components</summary>
    public sealed class EcsPool<T> : IEcsPoolImplementation<T>, IEnumerable<T> //IntelliSense hack
        where T : struct, IEcsComponent
    {
        private EcsWorld _source;
        private int _id;

        private int[] _mapping;// index = entityID / value = itemIndex;/ value = 0 = no entityID
        private T[] _items; //dense
        private int _itemsCount;
        private int[] _recycledItems;
        private int _recycledItemsCount;

        private IEcsComponentReset<T> _componentResetHandler;
        private IEcsComponentCopy<T> _componentCopyHandler;
        private PoolRunners _poolRunners;

        #region Properites
        public int Count => _itemsCount;
        public int Capacity => _items.Length;
        public int ComponentID => _id;
        public Type ComponentType => typeof(T);
        public EcsWorld World => _source;
        #endregion

        #region Init
        void IEcsPoolImplementation.OnInit(EcsWorld world, int componentID)
        {
            _source = world;
            _id = componentID; 

            const int capacity = 512;

            _mapping = new int[world.Capacity];
            _recycledItems = new int[128];
            _recycledItemsCount = 0;
            _items = new T[capacity];
            _itemsCount = 0;

            _componentResetHandler = EcsComponentResetHandler<T>.instance;
            _componentCopyHandler = EcsComponentCopyHandler<T>.instance;
            _poolRunners = new PoolRunners(world.Pipeline);
        }
        #endregion

        #region Methods
        public ref T Add(int entityID)
        {
            // using (_addMark.Auto())
            //  {
            ref int itemIndex = ref _mapping[entityID];
#if (DEBUG && !DISABLE_DEBUG) || !DRAGONECS_NO_SANITIZE_CHECKS
            if (itemIndex > 0) ThrowAlreadyHasComponent<T>(entityID);
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
            this.IncrementEntityComponentCount(entityID);
            _poolRunners.add.OnComponentAdd<T>(entityID);
            _poolRunners.write.OnComponentWrite<T>(entityID);
            return ref _items[itemIndex];
            // }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Write(int entityID)
        {
            //   using (_writeMark.Auto())
#if (DEBUG && !DISABLE_DEBUG) || !DRAGONECS_NO_SANITIZE_CHECKS
            if (!Has(entityID)) ThrowNotHaveComponent<T>(entityID);
#endif
            _poolRunners.write.OnComponentWrite<T>(entityID);
            return ref _items[_mapping[entityID]];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref readonly T Read(int entityID)
        {
            //  using (_readMark.Auto())
#if (DEBUG && !DISABLE_DEBUG) || !DRAGONECS_NO_SANITIZE_CHECKS
            if (!Has(entityID)) ThrowNotHaveComponent<T>(entityID);
#endif
            return ref _items[_mapping[entityID]];
        }
        public ref T TryAddOrWrite(int entityID)
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
                this.IncrementEntityComponentCount(entityID);
                _poolRunners.add.OnComponentAdd<T>(entityID);
            }
            _poolRunners.write.OnComponentWrite<T>(entityID);
            return ref _items[itemIndex];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Has(int entityID)
        {
            return _mapping[entityID] > 0;
        }
        public void Del(int entityID)
        {
#if (DEBUG && !DISABLE_DEBUG) || !DRAGONECS_NO_SANITIZE_CHECKS
            if (!Has(entityID)) ThrowNotHaveComponent<T>(entityID);
#endif
            ref int itemIndex = ref _mapping[entityID];
            _componentResetHandler.Reset(ref _items[itemIndex]);
            if (_recycledItemsCount >= _recycledItems.Length)
                Array.Resize(ref _recycledItems, _recycledItems.Length << 1);
            _recycledItems[_recycledItemsCount++] = itemIndex;
            _mapping[entityID] = 0;
            _itemsCount--;
            this.DecrementEntityComponentCount(entityID);
            _poolRunners.del.OnComponentDel<T>(entityID);
        }
        public void TryDel(int entityID)
        {
            if (Has(entityID)) Del(entityID);
        }
        public void Copy(int fromEntityID, int toEntityID)
        {
#if (DEBUG && !DISABLE_DEBUG) || !DRAGONECS_NO_SANITIZE_CHECKS
            if (!Has(fromEntityID)) ThrowNotHaveComponent<T>(fromEntityID);
#endif
            if (Has(toEntityID))
                _componentCopyHandler.Copy(ref Write(fromEntityID), ref Write(toEntityID));
            else
                _componentCopyHandler.Copy(ref Write(fromEntityID), ref Add(toEntityID));
        }
        #endregion

        #region Callbacks
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
        void IEcsPool.SetRaw(int entityID, object dataRaw) => Write(entityID) = (T)dataRaw;
        ref readonly T IEcsPool<T>.Read(int entityID) => ref Read(entityID);
        ref T IEcsPool<T>.Write(int entityID) => ref Write(entityID);
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
        public static EcsPool<TComponent> GetPool<TComponent>(this EcsWorld self) where TComponent : struct, IEcsComponent
        {
            return self.GetPool<TComponent, EcsPool<TComponent>>();
        }

        public static EcsPool<TComponent> Include<TComponent>(this EcsSubjectBuilderBase self) where TComponent : struct, IEcsComponent
        {
            return self.Include<TComponent, EcsPool<TComponent>>();
        }
        public static EcsPool<TComponent> Exclude<TComponent>(this EcsSubjectBuilderBase self) where TComponent : struct, IEcsComponent
        {
            return self.Exclude<TComponent, EcsPool<TComponent>>();
        }
        public static EcsPool<TComponent> Optional<TComponent>(this EcsSubjectBuilderBase self) where TComponent : struct, IEcsComponent
        {
            return self.Optional<TComponent, EcsPool<TComponent>>();
        }
    }
}
