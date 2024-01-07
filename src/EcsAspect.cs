﻿using DCFApixels.DragonECS.Internal;
using DCFApixels.DragonECS.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DCFApixels.DragonECS
{
    public abstract class EcsAspect
    {
        internal EcsWorld _source;
        internal EcsMask _mask;
        private bool _isInit;

        internal UnsafeArray<int> _sortIncBuffer;
        internal UnsafeArray<int> _sortExcBuffer;
        internal UnsafeArray<EcsMaskChunck> _sortIncChunckBuffer;
        internal UnsafeArray<EcsMaskChunck> _sortExcChunckBuffer;

        #region Properties
        public EcsMask Mask => _mask;
        public EcsWorld World => _source;
        public bool IsInit => _isInit;
        #endregion

        #region Methods
        public bool IsMatches(int entityID) => _source.IsMatchesMask(_mask, entityID);
        #endregion

        #region Builder
        protected virtual void Init(Builder b) { }
        public sealed class Builder : EcsAspectBuilderBase
        {
            private EcsWorld _world;
            private HashSet<int> _inc;
            private HashSet<int> _exc;
            private List<Combined> _combined;

            public EcsWorld World => _world;

            private Builder(EcsWorld world)
            {
                _world = world;
                _combined = new List<Combined>();
                _inc = new HashSet<int>();
                _exc = new HashSet<int>();
            }
            internal static unsafe TAspect Build<TAspect>(EcsWorld world) where TAspect : EcsAspect
            {
                Builder builder = new Builder(world);
                Type aspectType = typeof(TAspect);
                ConstructorInfo constructorInfo = aspectType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(Builder) }, null);
                EcsAspect newAspect;
                if (constructorInfo != null)
                {
                    newAspect = (EcsAspect)constructorInfo.Invoke(new object[] { builder });
                }
                else
                {
                    newAspect = (EcsAspect)Activator.CreateInstance(typeof(TAspect));
                    newAspect.Init(builder);
                }
                newAspect._source = world;
                builder.End(out newAspect._mask);
                newAspect._isInit = true;

                newAspect._sortIncBuffer = new UnsafeArray<int>(newAspect._mask.inc.Length, true);
                newAspect._sortExcBuffer = new UnsafeArray<int>(newAspect._mask.exc.Length, true);
                newAspect._sortIncChunckBuffer = new UnsafeArray<EcsMaskChunck>(newAspect._mask.incChunckMasks.Length, true);
                newAspect._sortExcChunckBuffer = new UnsafeArray<EcsMaskChunck>(newAspect._mask.excChunckMasks.Length, true);

                for (int i = 0; i < newAspect._sortIncBuffer.Length; i++)
                {
                    newAspect._sortIncBuffer.ptr[i] = newAspect._mask.inc[i];
                }
                for (int i = 0; i < newAspect._sortExcBuffer.Length; i++)
                {
                    newAspect._sortExcBuffer.ptr[i] = newAspect._mask.exc[i];
                }

                for (int i = 0; i < newAspect._sortIncChunckBuffer.Length; i++)
                {
                    newAspect._sortIncChunckBuffer.ptr[i] = newAspect._mask.incChunckMasks[i];
                }
                for (int i = 0; i < newAspect._sortExcChunckBuffer.Length; i++)
                {
                    newAspect._sortExcChunckBuffer.ptr[i] = newAspect._mask.excChunckMasks[i];
                }

                return (TAspect)newAspect;
            }

            #region Include/Exclude/Optional
            public sealed override TPool Include<TPool>()
            {
                IncludeImplicit(typeof(TPool).GetGenericArguments()[0]);
                return _world.GetPool<TPool>();
            }
            public sealed override TPool Exclude<TPool>()
            {
                ExcludeImplicit(typeof(TPool).GetGenericArguments()[0]);
                return _world.GetPool<TPool>();
            }
            public sealed override TPool Optional<TPool>()
            {
                return _world.GetPool<TPool>();
            }
            private void IncludeImplicit(Type type)
            {
                int id = _world.GetComponentID(type);
#if (DEBUG && !DISABLE_DEBUG) || ENABLE_DRAGONECS_ASSERT_CHEKS
                if (_inc.Contains(id) || _exc.Contains(id)) Throw.ConstraintIsAlreadyContainedInMask(type);
#endif
                _inc.Add(id);
            }
            private void ExcludeImplicit(Type type)
            {
                int id = _world.GetComponentID(type);
#if (DEBUG && !DISABLE_DEBUG) || ENABLE_DRAGONECS_ASSERT_CHEKS
                if (_inc.Contains(id) || _exc.Contains(id)) Throw.ConstraintIsAlreadyContainedInMask(type);
#endif
                _exc.Add(id);
            }
            #endregion

            #region Combine
            public TOtherAspect Combine<TOtherAspect>(int order = 0) where TOtherAspect : EcsAspect
            {
                var result = _world.GetAspect<TOtherAspect>();
                _combined.Add(new Combined(result, order));
                return result;
            }
            #endregion

            public EcsWorldCmp<T> GetWorldData<T>() where T : struct
            {
                return new EcsWorldCmp<T>(_world.id);
            }

            private void End(out EcsMask mask)
            {
                HashSet<int> maskInc;
                HashSet<int> maskExc;
                if (_combined.Count > 0)
                {
                    maskInc = new HashSet<int>();
                    maskExc = new HashSet<int>();
                    _combined.Sort((a, b) => a.order - b.order);
                    foreach (var item in _combined)
                    {
                        EcsMask submask = item.aspect._mask;
                        maskInc.ExceptWith(submask.exc);//удаляю конфликтующие ограничения
                        maskExc.ExceptWith(submask.inc);//удаляю конфликтующие ограничения
                        maskInc.UnionWith(submask.inc);
                        maskExc.UnionWith(submask.exc);
                    }
                    maskInc.ExceptWith(_exc);//удаляю конфликтующие ограничения
                    maskExc.ExceptWith(_inc);//удаляю конфликтующие ограничения
                    maskInc.UnionWith(_inc);
                    maskExc.UnionWith(_exc);
                }
                else
                {
                    maskInc = _inc;
                    maskExc = _exc;
                }

                Dictionary<int, int> r = new Dictionary<int, int>();
                foreach (var id in maskInc)
                {
                    var bit = EcsMaskChunck.FromID(id);
                    if (!r.TryAdd(bit.chankIndex, bit.mask))
                        r[bit.chankIndex] = r[bit.chankIndex] | bit.mask;
                }
                EcsMaskChunck[] incMasks = r.Select(o => new EcsMaskChunck(o.Key, o.Value)).ToArray();
                r.Clear();
                foreach (var id in maskExc)
                {
                    var bit = EcsMaskChunck.FromID(id);
                    if (!r.TryAdd(bit.chankIndex, bit.mask))
                        r[bit.chankIndex] = r[bit.chankIndex] | bit.mask;
                }
                EcsMaskChunck[] excMasks = r.Select(o => new EcsMaskChunck(o.Key, o.Value)).ToArray();

                var inc = maskInc.ToArray();
                Array.Sort(inc);
                var exc = maskExc.ToArray();
                Array.Sort(exc);

                mask = new EcsMask(0, _world.id, inc, exc, incMasks, excMasks);
                _world = null;
                _inc = null;
                _exc = null;
            }

            #region SupportReflectionHack
#if UNITY_2020_3_OR_NEWER
            [UnityEngine.Scripting.Preserve]
#endif
            private void SupportReflectionHack<TPool>() where TPool : IEcsPoolImplementation, new()
            {
                Include<TPool>();
                Exclude<TPool>();
                Optional<TPool>();
                IncludeImplicit(null);
                ExcludeImplicit(null);
            }
            #endregion
        }
        #endregion

        #region Destructor
        unsafe ~EcsAspect()
        {
            _sortIncBuffer.Dispose();
            _sortExcBuffer.Dispose();
            _sortIncChunckBuffer.Dispose();
            _sortExcChunckBuffer.Dispose();
        }
        #endregion

        #region Iterator
        public EcsAspectIterator GetIterator()
        {
            return new EcsAspectIterator(this, _source.Entities);
        }
        public EcsAspectIterator GetIteratorFor(EcsSpan span)
        {
            return new EcsAspectIterator(this, span);
        }
        #endregion

        #region Combined
        private readonly struct Combined
        {
            public readonly EcsAspect aspect;
            public readonly int order;
            public Combined(EcsAspect aspect, int order)
            {
                this.aspect = aspect;
                this.order = order;
            }
        }
        #endregion
    }

    #region BuilderBase
    public abstract class EcsAspectBuilderBase
    {
        public abstract TPool Include<TPool>() where TPool : IEcsPoolImplementation, new();
        public abstract TPool Exclude<TPool>() where TPool : IEcsPoolImplementation, new();
        public abstract TPool Optional<TPool>() where TPool : IEcsPoolImplementation, new();
    }
    #endregion

    #region Iterator
    public ref struct EcsAspectIterator
    {
        public readonly int worldID;
        public readonly EcsAspect aspect;
        private EcsSpan _span;

        public EcsAspectIterator(EcsAspect aspect, EcsSpan span)
        {
            worldID = aspect.World.id;
            _span = span;
            this.aspect = aspect;
        }
        public void CopyTo(EcsGroup group)
        {
            group.Clear();
            var enumerator = GetEnumerator();
            while (enumerator.MoveNext())
                group.AddInternal(enumerator.Current);
        }
        public int CopyTo(ref int[] array)
        {
            var enumerator = GetEnumerator();
            int count = 0;
            while (enumerator.MoveNext())
            {
                if(array.Length <= count)
                    Array.Resize(ref array, array.Length << 1);
                array[count++] = enumerator.Current;
            }
            return count;
        }
        public EcsSpan CopyToSpan(ref int[] array)
        {
            var enumerator = GetEnumerator();
            int count = 0;
            while (enumerator.MoveNext())
            {
                if (array.Length <= count)
                    Array.Resize(ref array, array.Length << 1);
                array[count++] = enumerator.Current;
            }
            return new EcsSpan(worldID, array, count);
        }

        #region object
        public override string ToString()
        {
            List<int> ints = new List<int>();
            foreach (var e in this)
            {
                ints.Add(e);
            }
            return CollectionUtility.EntitiesToString(ints, "it");
        }
        #endregion

        #region Enumerator
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator() => new Enumerator(_span, aspect);

        public unsafe ref struct Enumerator
        {
            private readonly struct IncCountComparer : IComparerX<int>
            {
                public readonly int[] counts;
                public IncCountComparer(int[] counts)
                {
                    this.counts = counts;
                }
                public int Compare(int a, int b)
                {
                    return counts[a] - counts[b];
                }
            }
            private readonly struct ExcCountComparer : IComparerX<int>
            {
                public readonly int[] counts;
                public ExcCountComparer(int[] counts)
                {
                    this.counts = counts;
                }
                public int Compare(int a, int b)
                {
                    return counts[b] - counts[a];
                }
            }
            private ReadOnlySpan<int>.Enumerator _span;
            private readonly int[][] _entitiesComponentMasks;

            private static EcsMaskChunck* _preSortedIncBuffer;
            private static EcsMaskChunck* _preSortedExcBuffer;

            private UnsafeArray<EcsMaskChunck> _sortIncChunckBuffer;
            private UnsafeArray<EcsMaskChunck> _sortExcChunckBuffer;

            private EcsAspect aspect;

            public unsafe Enumerator(EcsSpan span, EcsAspect aspect)
            {
                this.aspect = aspect;
                _span = span.GetEnumerator();
                _entitiesComponentMasks = span.World._entitiesComponentMasks;

                EcsMask mask = aspect.Mask;

                UnsafeArray<int> _sortIncBuffer = aspect._sortIncBuffer;
                UnsafeArray<int> _sortExcBuffer = aspect._sortExcBuffer;
                _sortIncChunckBuffer = aspect._sortIncChunckBuffer;
                _sortExcChunckBuffer = aspect._sortExcChunckBuffer;

                int[] counts = mask.World._poolComponentCounts;



                IncCountComparer incComparer = new IncCountComparer(counts);
                ExcCountComparer excComparer = new ExcCountComparer(counts);

                #region Sort
                UnsafeArraySortHalperX<int>.InsertionSort(_sortIncBuffer.ptr, _sortIncBuffer.Length, ref incComparer);
                UnsafeArraySortHalperX<int>.InsertionSort(_sortExcBuffer.ptr, _sortExcBuffer.Length, ref excComparer);

                //if (_sortIncBuffer.Length > 1)
                //{
                //    //if (_sortIncBufferLength == 2)
                //    //{
                //    //    if (counts[_sortIncBuffer[0]] > counts[_sortIncBuffer[1]])
                //    //    {
                //    //        int tmp = _sortIncBuffer[0];
                //    //        _sortIncBuffer[0] = _sortIncBuffer[1];
                //    //        _sortIncBuffer[1] = tmp;
                //    //    }
                //    //    //...
                //    //}
                //    //else
                //    {
                //        for (int i = 0, n = _sortIncBuffer.Length - 1; i < n; i++)
                //        {
                //            //int counti = counts[_sortIncBuffer[i]];
                //            //if (counti <= 0)
                //            //{
                //            //    _span = ReadOnlySpan<int>.Empty.GetEnumerator();
                //            //    goto skip1;
                //            //}
                //            bool noSwaped = true; 
                //            for (int j = 0; j < n - i; )
                //            {
                //                ref int j0 = ref _sortIncBuffer.ptr[j++];
                //                if (counts[j0] > counts[_sortIncBuffer.ptr[j]])
                //                {
                //                    int tmp = _sortIncBuffer.ptr[j];
                //                    _sortIncBuffer.ptr[j] = j0;
                //                    j0 = tmp;
                //                    noSwaped = false;
                //                }
                //            }
                //            if (noSwaped)
                //                break;
                //        }
                //    }
                //}
                //skip1:;
                //if (_sortExcBuffer.Length > 1)
                //{
                //    //if (_sortExcBufferLength == 2)
                //    //{
                //    //    if (counts[_sortExcBuffer[0]] < counts[_sortExcBuffer[1]])
                //    //    {
                //    //        int tmp = _sortExcBuffer[0];
                //    //        _sortExcBuffer[0] = _sortExcBuffer[1];
                //    //        _sortExcBuffer[1] = tmp;
                //    //    }
                //    //    //...
                //    //}
                //    //else
                //    {
                //        for (int i = 0, n = _sortExcBuffer.Length - 1; i < n; i++)
                //        {
                //            //int counti = counts[_sortExcBuffer[i]];
                //            //if (counti <= 0)
                //            //{
                //            //    _excChunckMasks = ReadOnlySpan<EcsMaskBit>.Empty;
                //            //    goto skip2;
                //            //}
                //            bool noSwaped = true;
                //            for (int j = 0; j < n - i;)
                //            {
                //                ref int j0 = ref _sortExcBuffer.ptr[j++];
                //                if (counts[j0] < counts[_sortExcBuffer.ptr[j]])
                //                {
                //                    int tmp = _sortExcBuffer.ptr[j];
                //                    _sortExcBuffer.ptr[j] = j0;
                //                    j0 = tmp;
                //                    noSwaped = false;
                //                }
                //            }
                //            if (noSwaped)
                //                break;
                //        }
                //    }
                //}
                //skip2:;



                if (_preSortedIncBuffer == null)
                {
                    _preSortedIncBuffer = UnmanagedArrayUtility.New<EcsMaskChunck>(256);
                    _preSortedExcBuffer = UnmanagedArrayUtility.New<EcsMaskChunck>(256);
                }


                for (int i = 0; i < _sortIncBuffer.Length; i++)
                {
                    _preSortedIncBuffer[i] = EcsMaskChunck.FromID(_sortIncBuffer.ptr[i]);
                }
                for (int i = 0; i < _sortExcBuffer.Length; i++)
                {
                    _preSortedExcBuffer[i] = EcsMaskChunck.FromID(_sortExcBuffer.ptr[i]);
                }

                //int _sortedIncBufferLength = mask.inc.Length;
                //int _sortedExcBufferLength = mask.exc.Length;

                //if (_sortIncChunckBuffer.Length > 1)//перенести этот чек в начала сортировки, для _incChunckMasks.Length == 1 сортировка не нужна
                if (_sortIncBuffer.Length > 1)
                {
                    for (int i = 0, ii = 0; ii < _sortIncChunckBuffer.Length; ii++)
                    {
                        EcsMaskChunck bas = _preSortedIncBuffer[i];
                        int chankIndexX = bas.chankIndex;
                        int maskX = bas.mask;

                        for (int j = i + 1; j < _sortIncBuffer.Length; j++)
                        {
                            if (_preSortedIncBuffer[j].chankIndex == chankIndexX)
                            {
                                maskX |= _preSortedIncBuffer[j].mask;
                            }
                        }
                        _sortIncChunckBuffer.ptr[ii] = new EcsMaskChunck(chankIndexX, maskX);
                        while (++i < _sortIncBuffer.Length && _preSortedIncBuffer[i].chankIndex == chankIndexX)
                        {
                            // skip
                        }
                    }
                }
                else
                {
                    _sortIncChunckBuffer.ptr[0] = _preSortedIncBuffer[0];
                }

                //if (_sortExcChunckBuffer.Length > 1)//перенести этот чек в начала сортировки, для _excChunckMasks.Length == 1 сортировка не нужна
                if (_sortExcBuffer.Length > 1)
                {
                    for (int i = 0, ii = 0; ii < _sortExcChunckBuffer.Length; ii++)
                    {
                        EcsMaskChunck bas = _preSortedExcBuffer[i];
                        int chankIndexX = bas.chankIndex;
                        int maskX = bas.mask;

                        for (int j = i + 1; j < _sortExcBuffer.Length; j++)
                        {
                            if (_preSortedExcBuffer[j].chankIndex == chankIndexX)
                            {
                                maskX |= _preSortedExcBuffer[j].mask;
                            }
                        }
                        _sortExcChunckBuffer.ptr[ii] = new EcsMaskChunck(chankIndexX, maskX);
                        while (++i < _sortExcBuffer.Length && _preSortedExcBuffer[i].chankIndex == chankIndexX)
                        {
                            // skip
                        }
                    }
                }
                else
                {
                    _sortExcChunckBuffer.ptr[0] = _preSortedExcBuffer[0];
                }
                #endregion
            }
            public int Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _span.Current;
            }
            public entlong CurrentLong
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => aspect.World.GetEntityLong(_span.Current);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                while (_span.MoveNext())
                {
                    int e = _span.Current;
                    for (int i = 0; i < _sortIncChunckBuffer.Length; i++)
                    {
                        var bit = _sortIncChunckBuffer.ptr[i];
                        if ((_entitiesComponentMasks[e][bit.chankIndex] & bit.mask) != bit.mask)
                            goto skip;
                    }
                    for (int i = 0; i < _sortExcChunckBuffer.Length; i++)
                    {
                        var bit = _sortExcChunckBuffer.ptr[i];
                        if ((_entitiesComponentMasks[e][bit.chankIndex] & bit.mask) > 0)
                            goto skip;
                    }
                    return true;
                    skip: continue;
                }
                return false;
            }
        }
        #endregion
    }
    #endregion
}
