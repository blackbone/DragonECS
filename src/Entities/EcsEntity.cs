﻿using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DCFApixels.DragonECS
{

    /// <summary>Permanent relation entity identifier</summary>
    [StructLayout(LayoutKind.Explicit, Pack = 2, Size = 8)]
    public readonly partial struct EcsEntity : IEquatable<long>, IEquatable<EcsEntity>
    {
        public static readonly EcsEntity NULL = default;
        // uniqueID - 32 bits
        // gen - 16 bits
        // world - 16 bits
        [FieldOffset(0)]
        internal readonly long full; //Union
        [FieldOffset(3)]
        public readonly int id;
        [FieldOffset(1)]
        public readonly short gen;
        [FieldOffset(0)]
        public readonly short world;

        public ent Ent
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new ent(id);
        }

        #region Constructors
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EcsEntity(int id, short gen, short world) : this()
        {
            this.id = id;
            this.gen = gen;
            this.world = world;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal EcsEntity(long full) : this()
        {
            this.full = full;
        }
        #endregion

        #region Equals
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(EcsEntity other) => full == other.full;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(long other) => full == other;
        #endregion

        #region Object
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => unchecked((int)full) ^ (int)(full >> 32);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString() => $"Entity(id:{id} gen:{gen} world:{world})";
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj) => obj is EcsEntity other && full == other.full;
        #endregion

        #region operators
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(in EcsEntity a, in EcsEntity b) => a.full == b.full;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(in EcsEntity a, in EcsEntity b) => a.full != b.full;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator long(in EcsEntity a) => a.full;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator ent(in EcsEntity a) => a.Ent;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator EcsEntity(in long a) => new EcsEntity(a);
        #endregion
    }

    public readonly partial struct EcsEntity
    {
        private static EcsProfilerMarker _IsNullMarker = new EcsProfilerMarker("EcsEntity.IsNull");
        private static EcsProfilerMarker _ReadMarker = new EcsProfilerMarker("EcsEntity.Read");
        private static EcsProfilerMarker _WriteMarker = new EcsProfilerMarker("EcsEntity.Write");
        private static EcsProfilerMarker _HasMarker = new EcsProfilerMarker("EcsEntity.Has");
        private static EcsProfilerMarker _DelMarker = new EcsProfilerMarker("EcsEntity.Del");

        public bool IsNull
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                //using (_IsNullMarker.Auto())
                return this == NULL;
            }
        }

        public ref readonly T Read<T>()
            where T : struct
        {
            //using (_ReadMarker.Auto())
            return ref EcsWorld.Worlds[world].GetPool<T>().Read(id);
        }

        public ref T Add<T>()
            where T : struct
        {
            return ref EcsWorld.Worlds[world].GetPool<T>().Add(id);
        }
        public ref T Write<T>()
            where T : struct
        {
            //using (_WriteMarker.Auto())
            return ref EcsWorld.Worlds[world].GetPool<T>().Write(id);
        }
        public bool Has<T>()
            where T : struct
        {
            //using (_HasMarker.Auto())
            return EcsWorld.Worlds[world].GetPool<T>().Has(id);
        }
        public bool NotHas<T>()
            where T : struct
        {
            //using (_HasMarker.Auto())
            return EcsWorld.Worlds[world].GetPool<T>().Has(id);
        }
        public void Del<T>()
            where T : struct
        {
            //using (_DelMarker.Auto())
            EcsWorld.Worlds[world].GetPool<T>().Del(id);
        }
    } 

    public static partial class entExtensions
    {
        private static EcsProfilerMarker _IsAliveMarker = new EcsProfilerMarker("EcsEntity.IsAlive");

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAlive(this ref EcsEntity self)
        {
            //using (_IsAliveMarker.Auto())
            //{
                bool result = EcsWorld.Worlds[self.world].EntityIsAlive(self.id, self.gen);
                if (!result) self = EcsEntity.NULL;
                return result;
            //}
        }
    }
}