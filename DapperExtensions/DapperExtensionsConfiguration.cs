﻿using DapperExtensions.Mapper;
using DapperExtensions.Sql;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

namespace DapperExtensions
{
    public interface IDapperExtensionsConfiguration
    {
        Type DefaultMapper { get; }
        IList<Assembly> MappingAssemblies { get; }
        ISqlDialect Dialect { get; }
        IClassMapper GetMap(Type entityType);
        IClassMapper GetMap<T>();
        Type GetMapType(Type entityType);
        void ClearCache();
        Guid GetNextGuid();
        SqlInjection GetOrSetSqlInjection(Type entityType, SqlInjection sqlInjection = null);

        bool CaseSensitiveSearchEnabled { get; }
        void SetCaseSensitiveSearch(bool value);
    }

    public class DapperExtensionsConfiguration : IDapperExtensionsConfiguration
    {
        private readonly ConcurrentDictionary<Type, SqlInjection> _sqlInjections = new ConcurrentDictionary<Type, SqlInjection>();
        private readonly ConcurrentDictionary<Type, IClassMapper> _classMaps = new ConcurrentDictionary<Type, IClassMapper>();

        public DapperExtensionsConfiguration()
            : this(typeof(AutoClassMapper<>), new List<Assembly>(), new SqlServerDialect())
        {
        }

        public DapperExtensionsConfiguration(Type defaultMapper, IList<Assembly> mappingAssemblies, ISqlDialect sqlDialect)
        {
            DefaultMapper = defaultMapper;
            MappingAssemblies = mappingAssemblies ?? new List<Assembly>();
            Dialect = sqlDialect;
        }

        /*-- Properties --*/

        public Type DefaultMapper { get; }
        public IList<Assembly> MappingAssemblies { get; }
        public ISqlDialect Dialect { get; }

        public bool CaseSensitiveSearchEnabled { get; private set; } = false;

        /*-- Methods --*/

        #region -- GetMap(Type) Method --
        public IClassMapper GetMap(Type entityType)
        {
            var mapType = default(Type);

            if (!_classMaps.TryGetValue(entityType, out IClassMapper map))
            {
                mapType = GetMapType(entityType) ?? DefaultMapper.MakeGenericType(entityType);

                try
                {
                    map = (IClassMapper)Activator.CreateInstance(mapType);

                    _classMaps[entityType] = map;

                }
                catch (Exception ex)
                {
                    throw new Exception($"Could not find a class map for entity: {entityType}, mapType: {mapType}, message: {ex.Message}", ex);
                }
            }

            return map;
        }
        #endregion

        #region -- GetMap<T>() Method --
        public IClassMapper GetMap<T>()
        {
            return GetMap(typeof(T));
        }
        #endregion

        #region -- ClearCache() Method --
        [ExcludeFromCodeCoverage]
        public void ClearCache()
        {
            _classMaps.Clear();
        }
        #endregion

        #region -- GetNextGuid() Method --
        public Guid GetNextGuid()
        {
            var b = Guid.NewGuid().ToByteArray();
            var dateTime = new DateTime(1900, 1, 1);
            var now = DateTime.Now;

            var timeSpan = new TimeSpan(now.Ticks - dateTime.Ticks);
            var timeOfDay = now.TimeOfDay;

            var bytes1 = BitConverter.GetBytes(timeSpan.Days);
            var bytes2 = BitConverter.GetBytes((long)(timeOfDay.TotalMilliseconds / 3.333333));

            Array.Reverse(bytes1);
            Array.Reverse(bytes2);

            Array.Copy(bytes1, bytes1.Length - 2, b, b.Length - 6, 2);
            Array.Copy(bytes2, bytes2.Length - 4, b, b.Length - 4, 4);

            return new Guid(b);
        }
        #endregion

        #region -- GetMapType(Type) Method --
        public virtual Type GetMapType(Type entityType)
        {
            Type getType(Assembly a)
            {
                var types = a.GetTypes();

                //Order by to assure that direct implementaion comes first
                //FirstOrDefault to avoid inheritance problems
                return (from type in types
                        let interfaceType = type.GetInterface(typeof(IClassMapper<>).FullName)
                        where
                            interfaceType != null &&
                            (interfaceType.GetGenericArguments()[0] == entityType || interfaceType.GetGenericArguments()[0] == entityType?.BaseType)
                        orderby interfaceType.GetGenericArguments()[0] == entityType descending
                        select type).FirstOrDefault();
            }

            var result = getType(entityType.Assembly);
            if (result != null)
                return result;

            for (var i = 0; i < MappingAssemblies.Count && result == null; i++)
            {
                result = getType(MappingAssemblies[i]);
            }

            return result ?? getType(entityType.Assembly);
        }
        #endregion

        #region -- GetOrSetSqlInjection(Type, SqlInjection) Method --
        public SqlInjection GetOrSetSqlInjection(Type entityType, SqlInjection sqlInjection = null)
        {
            if (!_sqlInjections.TryGetValue(entityType, out SqlInjection value) && sqlInjection != null)
            {
                value = sqlInjection;
                _sqlInjections[entityType] = value;
            }

            return value;
        }
        #endregion

        public void SetCaseSensitiveSearch(bool value) => CaseSensitiveSearchEnabled = value;
    }
}