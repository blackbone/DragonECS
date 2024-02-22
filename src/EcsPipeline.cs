﻿using DCFApixels.DragonECS.Internal;
using DCFApixels.DragonECS.RunnersCore;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace DCFApixels.DragonECS
{
    public interface IEcsPipelineMember : IEcsSystem
    {
        public EcsPipeline Pipeline { get; set; }
    }
    public sealed class EcsPipeline
    {
        private readonly IEcsPipelineConfig _config;

        private IEcsSystem[] _allSystems;
        private Dictionary<Type, Array> _processes = new Dictionary<Type, Array>();
        private Dictionary<Type, IEcsRunner> _runners = new Dictionary<Type, IEcsRunner>();
        private IEcsRun _runRunnerCache;

        private bool _isInit = false;
        private bool _isDestoryed = false;

        #region Properties
        public IEcsPipelineConfig Config
        {
            get { return _config; }
        }
        public EcsProcess<IEcsSystem> AllSystems
        {
            get { return new EcsProcess<IEcsSystem>(_allSystems); }
        }
        public IReadOnlyDictionary<Type, IEcsRunner> AllRunners
        {
            get { return _runners; }
        }
        public bool IsInit 
        { 
            get { return _isInit; } 
        }
        public bool IsDestoryed 
        { 
            get { return _isDestoryed; } 
        }
        #endregion

        #region Constructors
        private EcsPipeline(IEcsPipelineConfig config, IEcsSystem[] systems)
        {
            _config = config;
            _allSystems = systems;
        }
        #endregion

        #region Get Process/Runner
        public EcsProcess<T> GetProcess<T>() where T : IEcsSystem
        {
            Type type = typeof(T);
            T[] result;
            if(_processes.TryGetValue(type, out Array array))
            {
                result = (T[])array;
            }
            else
            {
                result = _allSystems.OfType<T>().ToArray();
                _processes.Add(type, result);
            }
            return new EcsProcess<T>(result);
        }
#if !REFLECTION_DISABLED
        public T GetRunner<T>() where T : IEcsSystem
        {
            Type interfaceType = typeof(T);
            if (_runners.TryGetValue(interfaceType, out IEcsRunner result) == false)
            {
                result = (IEcsRunner)EcsRunner<T>.Instantiate(this);
                _runners.Add(result.GetType(), result);
                _runners.Add(interfaceType, result);
            }
            return (T)result;
        }
#endif
        public T GetRunnerInstance<T>() where T : IEcsRunner, new()
        {
            Type runnerType = typeof(T);
            if (_runners.TryGetValue(runnerType, out IEcsRunner result) == false)
            {
                result = new T();
                _runners.Add(runnerType, result);
#if !REFLECTION_DISABLED
                _runners.Add(result.Interface, result);
#endif
            }
            return (T)result;
        }
        #endregion

        #region Internal
        internal void OnRunnerDestroy_Internal(IEcsRunner runner)
        {
            _runners.Remove(runner.Interface);
        }
        #endregion

        #region LifeCycle
        public void Init()
        {
            if (_isInit == true)
            {
                EcsDebug.PrintWarning($"This {nameof(EcsPipeline)} has already been initialized");
                return;
            }

            EcsProcess<IEcsPipelineMember> members = GetProcess<IEcsPipelineMember>();
            foreach (var member in members)
            {
                member.Pipeline = this;
            }

            var preInitRunner = GetRunner<IEcsPreInit>();
            preInitRunner.PreInit();
            EcsRunner.Destroy(preInitRunner);
            var initRunner = GetRunner<IEcsInit>();
            initRunner.Init();
            EcsRunner.Destroy(initRunner);

            _runRunnerCache = GetRunner<IEcsRun>();
            _isInit = true;
            GC.Collect();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Run()
        {
#if (DEBUG && !DISABLE_DEBUG) || ENABLE_DRAGONECS_ASSERT_CHEKS
            if (!_isInit) Throw.Pipeline_MethodCalledBeforeInitialisation(nameof(Run));
            if (_isDestoryed) Throw.Pipeline_MethodCalledAfterDestruction(nameof(Run));
#endif
            _runRunnerCache.Run();
        }
        public void Destroy()
        {
#if (DEBUG && !DISABLE_DEBUG) || ENABLE_DRAGONECS_ASSERT_CHEKS
            if (!_isInit) Throw.Pipeline_MethodCalledBeforeInitialisation(nameof(Destroy));
#endif
            if (_isDestoryed)
            {
                EcsDebug.PrintWarning($"This {nameof(EcsPipeline)} has already been destroyed");
                return;
            }
            _isDestoryed = true;
            GetRunner<IEcsDestroy>().Destroy();
        }
        #endregion

        #region Builder
        public static Builder New(IEcsPipelineConfigWriter config = null)
        {
            return new Builder(config);
        }
        public class Builder
        {
            private const int KEYS_CAPACITY = 4;
            private HashSet<Type> _uniqueTypes;
            private readonly Dictionary<string, List<IEcsSystem>> _systems;
            private readonly string _basicLayer;
            public readonly LayerList Layers;
            private readonly IEcsPipelineConfigWriter _config;
            private EcsProfilerMarker _buildBarker = new EcsProfilerMarker("Build Marker");

            public IEcsPipelineConfigWriter Config
            {
                get { return _config; }
            }
            public Builder(IEcsPipelineConfigWriter config = null)
            {
                _buildBarker.Begin();
                if (config == null)
                {
                    config = new EcsPipelineConfig();
                }
                _config = config;
                _basicLayer = EcsConsts.BASIC_LAYER;
                Layers = new LayerList(this, _basicLayer);
                Layers.Insert(EcsConsts.BASIC_LAYER, EcsConsts.PRE_BEGIN_LAYER, EcsConsts.BEGIN_LAYER);
                Layers.InsertAfter(EcsConsts.BASIC_LAYER, EcsConsts.END_LAYER, EcsConsts.POST_END_LAYER);
                _uniqueTypes = new HashSet<Type>();
                _systems = new Dictionary<string, List<IEcsSystem>>(KEYS_CAPACITY);
            }
            public Builder Add(IEcsSystem system, string layerName = null)
            {
                AddInternal(system, layerName, false);
                return this;
            }
            public Builder AddUnique(IEcsSystem system, string layerName = null)
            {
                AddInternal(system, layerName, true);
                return this;
            }
            public Builder Remove<TSystem>()
            {
                _uniqueTypes.Remove(typeof(TSystem));
                foreach (var list in _systems.Values)
                    list.RemoveAll(o => o is TSystem);
                return this;
            }
            private void AddInternal(IEcsSystem system, string layerName, bool isUnique)
            {
                if (layerName == null) layerName = _basicLayer;
                List<IEcsSystem> list;
                if (!_systems.TryGetValue(layerName, out list))
                {
                    list = new List<IEcsSystem> { new SystemsLayerMarkerSystem(layerName.ToString()) };
                    _systems.Add(layerName, list);
                }
                if ((_uniqueTypes.Add(system.GetType()) == false && isUnique))
                    return;
                list.Add(system);

                if (system is IEcsModule module)//если система одновременно явялется и системой и модулем то за один Add будет вызван Add и AddModule
                    AddModule(module);
            }
            public Builder AddModule(IEcsModule module)
            {
                module.Import(this);
                return this;
            }
            public EcsPipeline Build()
            {
                List<IEcsSystem> result = new List<IEcsSystem>(32);
                List<IEcsSystem> basicBlockList = _systems[_basicLayer];
                foreach (var item in _systems)
                {
                    if (!Layers.Contains(item.Key))
                        basicBlockList.AddRange(item.Value);
                }
                foreach (var item in Layers)
                {
                    if (_systems.TryGetValue(item, out var list))
                        result.AddRange(list);
                }
                _buildBarker.End();
                return new EcsPipeline(_config.GetPipelineConfig(), result.ToArray());
            }
            public class LayerList : IEnumerable<string>
            {
                private const string ADD_LAYER = nameof(ADD_LAYER); // автоматический слой нужный только для метода Add

                private Builder _source;
                private List<string> _layers;
                private string _basicLayerName;

                public LayerList(Builder source, string basicLayerName)
                {
                    _source = source;
                    _layers = new List<string>(16) { basicLayerName, ADD_LAYER };
                    _basicLayerName = basicLayerName;
                }

                public Builder Add(string newLayer) => Insert(ADD_LAYER, newLayer);
                public Builder Insert(string targetLayer, string newLayer)
                {
                    if (Contains(newLayer)) return _source;

                    int index = _layers.IndexOf(targetLayer);
                    if (index < 0)
                        throw new KeyNotFoundException($"Layer {targetLayer} not found");
                    _layers.Insert(index, newLayer);
                    return _source;
                }
                public Builder InsertAfter(string targetLayer, string newLayer)
                {
                    if (Contains(newLayer)) return _source;

                    if (targetLayer == _basicLayerName) // нужно чтобы метод Add работал правильно. _basicLayerName и ADD_LAYER считается одним слоем, поэтому Before = _basicLayerName After = ADD_LAYER
                        targetLayer = ADD_LAYER;

                    int index = _layers.IndexOf(targetLayer);
                    if (index < 0)
                        throw new KeyNotFoundException($"Layer {targetLayer} not found");

                    if (++index >= _layers.Count)
                        _layers.Add(newLayer);
                    else
                        _layers.Insert(index, newLayer);
                    return _source;
                }
                public Builder Move(string targetLayer, string movingLayer)
                {
                    _layers.Remove(movingLayer);
                    return Insert(targetLayer, movingLayer);
                }
                public Builder MoveAfter(string targetLayer, string movingLayer)
                {
                    if (targetLayer == _basicLayerName) // нужно чтобы метод Add работал правильно. _basicLayerName и ADD_LAYER считается одним слоем, поэтому Before = _basicLayerName After = ADD_LAYER
                        targetLayer = ADD_LAYER;

                    _layers.Remove(movingLayer);
                    return InsertAfter(targetLayer, movingLayer);
                }

                public Builder Add(params string[] newLayers) => Insert(ADD_LAYER, newLayers);
                public Builder Insert(string targetLayer, params string[] newLayers)
                {
                    int index = _layers.IndexOf(targetLayer);
                    if (index < 0)
                        throw new KeyNotFoundException($"Layer {targetLayer} not found");
                    _layers.InsertRange(index, newLayers.Where(o => !Contains(o)));
                    return _source;
                }
                public Builder InsertAfter(string targetLayer, params string[] newLayers)
                {
                    int index = _layers.IndexOf(targetLayer);
                    if (index < 0)
                        throw new KeyNotFoundException($"Layer {targetLayer} not found");

                    if (targetLayer == _basicLayerName) // нужно чтобы метод Add работал правильно. _basicLayerName и ADD_LAYER считается одним слоем, поэтому Before = _basicLayerName After = ADD_LAYER
                        targetLayer = ADD_LAYER;

                    if (++index >= _layers.Count)
                        _layers.AddRange(newLayers.Where(o => !Contains(o)));
                    else
                        _layers.InsertRange(index, newLayers.Where(o => !Contains(o)));
                    return _source;
                }
                public Builder Move(string targetLayer, params string[] movingLayers)
                {
                    foreach (var movingLayer in movingLayers)
                        _layers.Remove(movingLayer);
                    return Insert(targetLayer, movingLayers);
                }
                public Builder MoveAfter(string targetLayer, params string[] movingLayers)
                {
                    if (targetLayer == _basicLayerName) // нужно чтобы метод Add работал правильно. _basicLayerName и ADD_LAYER считается одним слоем, поэтому Before = _basicLayerName After = ADD_LAYER
                        targetLayer = ADD_LAYER;

                    foreach (var movingLayer in movingLayers)
                        _layers.Remove(movingLayer);
                    return InsertAfter(targetLayer, movingLayers);
                }

                public bool Contains(string layer) => _layers.Contains(layer);

                public List<string>.Enumerator GetEnumerator() => _layers.GetEnumerator();
                IEnumerator<string> IEnumerable<string>.GetEnumerator() => _layers.GetEnumerator();
                IEnumerator IEnumerable.GetEnumerator() => _layers.GetEnumerator();
            }
        }
        #endregion
    }

    public interface IEcsModule
    {
        void Import(EcsPipeline.Builder b);
    }

    #region Extensions
    public static partial class EcsPipelineExtensions
    {
        public static bool IsNullOrDestroyed(this EcsPipeline self)
        {
            return self == null || self.IsDestoryed;
        }
        public static EcsPipeline.Builder Add(this EcsPipeline.Builder self, IEnumerable<IEcsSystem> range, string layerName = null)
        {
            foreach (var item in range)
            {
                self.Add(item, layerName);
            }
            return self;
        }
        public static EcsPipeline.Builder AddUnique(this EcsPipeline.Builder self, IEnumerable<IEcsSystem> range, string layerName = null)
        {
            foreach (var item in range)
            {
                self.AddUnique(item, layerName);
            }
            return self;
        }
        public static EcsPipeline BuildAndInit(this EcsPipeline.Builder self)
        {
            EcsPipeline result = self.Build();
            result.Init();
            return result;
        }
    }
    #endregion

    #region SystemsLayerMarkerSystem
    [MetaTags(MetaTags.HIDDEN)]
    [MetaColor(MetaColor.Black)]
    public class SystemsLayerMarkerSystem : IEcsSystem
    {
        public readonly string name;
        public SystemsLayerMarkerSystem(string name) => this.name = name;
    }
    #endregion

    #region EcsProcess
    public readonly struct EcsProcessRaw : IEnumerable
    {
        private readonly Array _systems;
        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _systems.Length; }
        }
        public IEcsSystem this[int index]
        {
            get { return (IEcsSystem)_systems.GetValue(index); }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal EcsProcessRaw(Array systems)
        {
            _systems = systems;
        }
        public IEnumerator GetEnumerator()
        {
            return _systems.GetEnumerator();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal T[] GetSystems_Internal<T>()
        {
            return (T[])_systems;
        }
    }
    public readonly struct EcsProcess<TProcess> : IReadOnlyCollection<TProcess>
        where TProcess : IEcsSystem
    {
        public readonly static EcsProcess<TProcess> Empty = new EcsProcess<TProcess>(Array.Empty<TProcess>());
        private readonly TProcess[] _systems;
        public bool IsNullOrEmpty
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _systems == null || _systems.Length <= 0; }
        }
        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _systems.Length; }
        }
        int IReadOnlyCollection<TProcess>.Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _systems.Length; }
        }
        public TProcess this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _systems[index]; }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal EcsProcess(TProcess[] systems)
        {
            _systems = systems;
        }
        public static explicit operator EcsProcess<TProcess>(EcsProcessRaw raw)
        {
            return new EcsProcess<TProcess>(raw.GetSystems_Internal<TProcess>());
        }
        public static implicit operator EcsProcessRaw(EcsProcess<TProcess> process)
        {
            return new EcsProcessRaw(process._systems);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator() { return new Enumerator(_systems); }
        IEnumerator<TProcess> IEnumerable<TProcess>.GetEnumerator() { return GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
        public struct Enumerator : IEnumerator<TProcess>
        {
            private readonly TProcess[] _systems;
            private int _index;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Enumerator(TProcess[] systems)
            {
                _systems = systems;
                _index = -1;
            }
            public TProcess Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get { return _systems[_index]; }
            }
            object IEnumerator.Current { get { return Current; } }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext() { return ++_index < _systems.Length; }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Reset() { _index = -1; }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Dispose() { }
        }
    }
    #endregion
}