﻿using Microsoft.EntityFrameworkCore;
using OW.DDD;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace OW.Data
{
    public static class DbContextExtensions
    {
        /// <summary>
        /// 获取实体的主键值信息
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="dbContext">数据库上下文</param>
        /// <param name="entity">实体对象</param>
        /// <param name="hasEmptyKey">输出参数，标识是否包含空主键</param>
        /// <returns>主键值数组，如果实体类没有定义主键或获取主键值失败则返回null</returns>
        public static object[] GetEntityKeyValues<T>(this DbContext dbContext, T entity, out bool hasEmptyKey) where T : class
        {
            hasEmptyKey = false;

            // 获取主键信息
            var entityType = dbContext.Model.FindEntityType(typeof(T));
            if (entityType == null)
                throw new InvalidOperationException($"找不到实体类型 {typeof(T).Name} 的元数据");

            var keyProperties = entityType.FindPrimaryKey()?.Properties;
            if (keyProperties == null || !keyProperties.Any())
                throw new InvalidOperationException($"实体类型 {typeof(T).Name} 未定义主键");

            // 获取主键值
            var keyValues = new object[keyProperties.Count()];

            for (int index = 0; index < keyProperties.Count(); index++)
            {
                var property = keyProperties.ElementAt(index);
                // 获取属性信息
                var propertyInfo = typeof(T).GetProperty(property.Name);

                if (propertyInfo == null)
                    throw new InvalidOperationException($"无法获取属性 {property.Name} 的反射信息");

                // 获取属性值
                var value = propertyInfo.GetValue(entity);
                keyValues[index] = value;

                // 检查主键是否为默认值
                if (value == null ||
                    (value is Guid guidValue && guidValue == Guid.Empty) ||
                    (value is int intValue && intValue == 0) ||
                    (value is long longValue && longValue == 0) ||
                    (value is string strValue && string.IsNullOrEmpty(strValue)))
                {
                    hasEmptyKey = true;
                    break;
                }
            }

            return keyValues;
        }

#if !NET7_0_OR_GREATER  //EF Core 7.0将支持AddOrUpdate方法
        /// <summary>
        /// 插入或更新一个实体。
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="dbContext">数据库上下文</param>
        /// <param name="obj">要插入或更新的实体对象</param>
        public static void AddOrUpdate<T>(this DbContext dbContext, T obj) where T : class
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj), "实体对象不能为空");

            try
            {
                // 获取实体状态
                var entry = dbContext.Entry(obj);

                // 如果实体已经被跟踪且状态不是Detached，需要根据状态进行处理
                if (entry.State != EntityState.Detached)
                {
                    // 如果实体已经被跟踪，只需要根据需要设置状态
                    if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
                        return; // 实体已经被标记为添加或修改，无需额外操作

                    if (entry.State == EntityState.Unchanged)
                        entry.State = EntityState.Modified; // 将未更改的实体标记为已修改

                    return;
                }

                // 获取主键值
                var keyValues = GetEntityKeyValues(dbContext, obj, out bool hasEmptyKey);

                // 如果主键为空或默认值，则添加实体
                if (hasEmptyKey)
                {
                    dbContext.Set<T>().Add(obj);
                    return;
                }

                // 尝试查找已存在的实体
                var existingEntity = dbContext.Set<T>().Find(keyValues);

                if (existingEntity == null)
                {
                    // 实体不存在，添加新实体
                    dbContext.Set<T>().Add(obj);
                }
                else
                {
                    // 实体已存在，更新实体值
                    dbContext.Entry(existingEntity).CurrentValues.SetValues(obj);
                }
            }
            catch (Exception ex)
            {
                // 记录异常信息，但仍然抛出以便调用者处理
                Debug.WriteLine($"AddOrUpdate 操作异常: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 插入或更新一组实体。
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="dbContext">数据库上下文</param>
        /// <param name="entities">要插入或更新的实体集合</param>
        public static void AddOrUpdate<T>(this DbContext dbContext, IEnumerable<T> entities) where T : class
        {
            if (entities == null)
                throw new ArgumentNullException(nameof(entities), "实体集合不能为空");

            foreach (var entity in entities)
            {
                dbContext.AddOrUpdate(entity);
            }
        }
#endif //!NET7_0_OR_GREATER

        /// <summary>
        /// 扩展方法，用于清空指定的数据库表。
        /// </summary>
        /// <param name="context">数据库上下文实例。</param>
        /// <param name="tableName">要清空的表名。</param>
        public static void TruncateTable(this DbContext context, string tableName)
        {
            // 确保表名有效，防止SQL注入
            if (string.IsNullOrWhiteSpace(tableName))
            {
                throw new ArgumentException("无效的表名。", nameof(tableName));
            }

            // 查询信息架构视图，确保表名存在
            var tableExists = context.Database
                .ExecuteSqlRaw($"SELECT CASE WHEN EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '{tableName}') THEN 1 ELSE 0 END");

            if (tableExists == 0)
            {
                throw new ArgumentException("无效的表名。", nameof(tableName));
            }
            // 执行 TRUNCATE TABLE SQL 命令
            context.Database.ExecuteSqlRaw($"TRUNCATE TABLE {tableName}");
        }

    }
}
