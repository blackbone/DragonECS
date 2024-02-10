﻿using System.Runtime.CompilerServices;

namespace DCFApixels.DragonECS
{
    public sealed class EcsWhereToGroupExecutor<TAspect> : EcsQueryExecutor where TAspect : EcsAspect
    {
        private TAspect _aspect;
        private EcsGroup _filteredGroup;

        private long _version;

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
            get => _version;
        }
        #endregion

        #region OnInitialize/OnDestroy
        protected sealed override void OnInitialize()
        {
            _aspect = World.GetAspect<TAspect>();
            _filteredGroup = EcsGroup.New(World);
        }
        protected sealed override void OnDestroy()
        {
            _filteredGroup.Dispose();
        }
        #endregion

        #region Methods
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EcsReadonlyGroup Execute() => ExecuteFor(_aspect.World.Entities);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EcsReadonlyGroup ExecuteFor(EcsSpan span)
        {
#if (DEBUG && !DISABLE_DEBUG) || ENABLE_DRAGONECS_ASSERT_CHEKS
            _executeMarker.Begin();
            if (span.IsNull) throw new System.ArgumentNullException();//TODO составить текст исключения. 
            if (span.WorldID != WorldID) throw new System.ArgumentException();//TODO составить текст исключения. 
#endif
            unchecked { _version++; }
            _aspect.GetIteratorFor(span).CopyTo(_filteredGroup);
#if (DEBUG && !DISABLE_DEBUG) || ENABLE_DRAGONECS_ASSERT_CHEKS
            _executeMarker.End();
#endif
            return _filteredGroup.Readonly;
        }
        #endregion
    }
}