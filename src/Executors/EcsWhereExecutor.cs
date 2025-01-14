﻿using System.Runtime.CompilerServices;

namespace DCFApixels.DragonECS
{
#if ENABLE_IL2CPP
    using Unity.IL2CPP.CompilerServices;
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
#endif
    public sealed class EcsWhereExecutor<TAspect> : EcsQueryExecutor where TAspect : EcsAspect
    {
        private TAspect _aspect;
        private int[] _filteredEntities;
        private int _filteredEntitiesCount;

        private long _lastWorldVersion;

#if (DEBUG && !DISABLE_DEBUG) || ENABLE_DRAGONECS_ASSERT_CHEKS
        private readonly EcsProfilerMarker _executeMarker = new EcsProfilerMarker("Where");
#endif

        #region Properties
        public TAspect Aspect
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _aspect;
        }
        public sealed override long Version
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _lastWorldVersion;
        }
        #endregion

        #region OnInitialize/OnDestroy
        protected sealed override void OnInitialize()
        {
            _aspect = World.GetAspect<TAspect>();
            _filteredEntities = new int[32];
        }
        protected sealed override void OnDestroy() { }
        #endregion

        #region Methods
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EcsSpan Execute()
        {
            return ExecuteFor(_aspect.World.Entities);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EcsSpan ExecuteFor(EcsSpan span)
        {
#if (DEBUG && !DISABLE_DEBUG) || ENABLE_DRAGONECS_ASSERT_CHEKS
            _executeMarker.Begin();
            if (span.IsNull) throw new System.ArgumentNullException();//TODO составить текст исключения. 
            if (span.WorldID != WorldID) throw new System.ArgumentException();//TODO составить текст исключения. 
#endif
            EcsSpan result;
            if (_lastWorldVersion != World.Version)
            {
                result = _aspect.GetIteratorFor(span).CopyToSpan(ref _filteredEntities);
                _filteredEntitiesCount = result.Count;
                _lastWorldVersion = World.Version;
            }
            else
            {
                result = new EcsSpan(WorldID, _filteredEntities, _filteredEntitiesCount);
            }
#if (DEBUG && !DISABLE_DEBUG) || ENABLE_DRAGONECS_ASSERT_CHEKS
            _executeMarker.End();
#endif
            return result;
        }
        #endregion
    }
}
