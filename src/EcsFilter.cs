﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace DCFApixels.DragonECS
{
    public interface IEcsFilter
    {
        public EcsWorld World { get; }
        public int EntitiesCount { get; }
    }


    public abstract class MaskBase
    {
        protected internal static int _typeIDIncrement = 0;
    }
    public abstract class IncBase { }
    public abstract class ExcBase { }

    public sealed class Inc<A> : IncBase { }
    public sealed class Inc<A, b> : IncBase { }
    public sealed class Exc<A> : ExcBase { }
    public sealed class Exc<A, B> : ExcBase { }
    public sealed class Mask<TInc> : MaskBase
        where TInc : IncBase
    {
        public static readonly int typeID = _typeIDIncrement++;
    }
    public sealed class Mask<TInc, TExc> : MaskBase
        where TInc : IncBase
        where TExc : ExcBase
    {
        public static readonly int typeID = _typeIDIncrement++;
    }


    public class EcsFilter : IEcsFilter
    {
        private readonly EcsWorld _source;
        private readonly EcsWorld.Mask _mask;
        private readonly SparseSet _entities;

        private DelayedOp[] _delayedOps;
        private int _delayedOpsCount;

        private int _lockCount;

        #region Properties
        public EcsWorld World => _source;
        public EcsWorld.Mask Mask => _mask;
        public int EntitiesCount => _entities.Count;
        #endregion

        #region Constrcutors
        internal EcsFilter(EcsWorld source, EcsWorld.Mask mask, int capasity)
        {
            _source = source;
            _mask = mask;
            _entities = new SparseSet(capasity);
            _delayedOps = new DelayedOp[512];
            _lockCount = 0;
        }
        #endregion


        internal void Change(int entityID, bool isAdd)
        {
            if (isAdd)
                Add(entityID);
            else
                Remove(entityID);
        }
        internal void Add(int entityID)
        {
            if (_lockCount > 0)
                AddDelayedOp(entityID, true);
            _entities.Add(entityID);
        }

        internal void Remove(int entityID)
        {
            if (_lockCount > 0)
                AddDelayedOp(entityID, false);
            _entities.Remove(entityID);
        }

        private void AddDelayedOp(int entityID, bool isAdd)
        {
            if(_delayedOpsCount >= _delayedOps.Length)
            {
                Array.Resize(ref _delayedOps, _delayedOps.Length << 1);
            }
            ref DelayedOp delayedOd = ref _delayedOps[_delayedOpsCount];
            delayedOd.Entity = entityID;
            delayedOd.Added = isAdd;
        }

        #region GetEnumerator
        private void Unlock()
        {
#if DEBUG
            if (_lockCount <= 0) 
            { 
                throw new Exception($"Invalid lock-unlock balance for {nameof(EcsFilter)}."); 
            }
#endif
            if (--_lockCount <= 0)
            {
                for (int i = 0; i < _delayedOpsCount; i++)
                {
                    ref DelayedOp op = ref _delayedOps[i];
                    if (op.Added)
                    {
                        Add(op.Entity);
                    }
                    else
                    {
                        Remove(op.Entity);
                    }
                }
            }
        }
        public Enumerator GetEnumerator()
        {
            _lockCount++;
            return new Enumerator(this);
        }
        #endregion

        #region Utils
        public ref struct Enumerator
        {
            private readonly EcsFilter _source;
            private readonly SparseSet _entities;
            private int _index;

            public Enumerator(EcsFilter filter)
            {
                _source = filter;
                _entities = filter._entities;
                _index = -1;
            }

            public int Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _entities[_index];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                return ++_index < _entities.Count;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Dispose()
            {
                _source.Unlock();
            }
        }

        private struct DelayedOp
        {
            public bool Added;
            public int Entity;
        }
        #endregion
    }
}
