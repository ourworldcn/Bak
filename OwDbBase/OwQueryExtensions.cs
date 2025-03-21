﻿using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace OW.Data
{
    /// <summary>
    /// 子实体表的标记接口。
    /// </summary>
    public interface IOwSubtables
    {
        /// <summary>
        /// 父实体的Id。
        /// </summary>
        public Guid? ParentId { get; set; }
    }

    public static class OwQueryExtensions
    {
        /// <summary>
        /// 按升序对序列的元素进行排序。
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="query"></param>
        /// <param name="fieldName">字段名。支持xxx.xxx语法，如:name.displayname</param>
        /// <param name="isDesc">是否降序排序：true降序排序，false升序排序（省略或默认）。</param>
        /// <returns></returns>
        public static IOrderedQueryable<T> OrderBy<T>(this IQueryable<T> query, string fieldName, bool isDesc = false)
        {
            if (string.IsNullOrWhiteSpace(fieldName)) throw new ArgumentNullException(nameof(fieldName));
            var names = fieldName.Split('.');

            ParameterExpression p = Expression.Parameter(typeof(T));
            //Expression key = Expression.Property(p, fieldName);
            var exprBody = OwExpression.PropertyOrField(p, fieldName, true);
            //var propInfo = GetPropertyInfo(typeof(T), fieldName, true);
            //var expr = GetOrderExpression(typeof(T), propInfo);
            if (isDesc)
            {
                var method = typeof(Queryable).GetMethods().FirstOrDefault(m => m.Name == "OrderByDescending" && m.GetParameters().Length == 2);
                var genericMethod = method.MakeGenericMethod(typeof(T), exprBody.Type);
                return (IOrderedQueryable<T>)genericMethod.Invoke(null, new object[] { query, Expression.Lambda(exprBody, p) });
            }
            else
            {
                var method = typeof(Queryable).GetMethods().FirstOrDefault(m => m.Name == "OrderBy" && m.GetParameters().Length == 2);
                var genericMethod = method.MakeGenericMethod(typeof(T), exprBody.Type);
                return (IOrderedQueryable<T>)genericMethod.Invoke(null, new object[] { query, Expression.Lambda(exprBody, p) });
            }
        }

        /// <summary>
        /// 获取属性反射对象。
        /// </summary>
        /// <param name="objType"></param>
        /// <param name="name"></param>
        /// <param name="ignoreCase"></param>
        /// <returns></returns>
        public static PropertyInfo GetPropertyInfo(Type objType, string name, bool ignoreCase = false)
        {
            var properties = objType.GetProperties();
            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var matchedProperty = properties.FirstOrDefault(p => p.Name.Equals(name, comparison)) ?? throw new ArgumentException("对象不包含指定属性名");
            return matchedProperty;
        }

        /// <summary>
        /// 获取生成表达式
        /// </summary>
        /// <param name="objType"></param>
        /// <param name="pi"></param>
        /// <returns></returns>
        public static LambdaExpression GetOrderExpression(Type objType, PropertyInfo pi)
        {
            var paramExpr = Expression.Parameter(objType);
            var propAccess = Expression.PropertyOrField(paramExpr, pi.Name);
            var expr = Expression.Lambda(propAccess, paramExpr);
            return expr;
        }
    }

    /// <summary>
    /// 实体框架帮助类。
    /// </summary>
    public class EfHelper
    {
        /// <summary>
        /// 获取字符串包含的表达式。
        /// </summary>
        /// <param name="property"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public static Expression StringContains(Expression property, Expression value)
        {
            var method = Expression.Call(property, typeof(string).GetMethod("Contains", new Type[] { typeof(string) }), value);
            return method;
        }

        /// <summary>
        /// 获取介于范围之间的表达式。如果min或max为null，则只应用另一个限制。
        /// </summary>
        /// <param name="property">要比较的属性表达式</param>
        /// <param name="min">最小值表达式，可以为null表示不限制下限</param>
        /// <param name="max">最大值表达式，可以为null表示不限制上限</param>
        /// <returns>组合的表达式，如果min和max都为null则返回一个始终为true的表达式</returns>
        public static Expression Between(Expression property, Expression min, Expression max)
        {
            // 如果两个限制都没有，返回始终为true的表达式
            if (min == null && max == null)
            {
                return Expression.Constant(true);
            }

            // 如果只有最小值限制
            if (max == null)
            {
                return Expression.GreaterThanOrEqual(property, min);
            }

            // 如果只有最大值限制
            if (min == null)
            {
                return Expression.LessThanOrEqual(property, max);
            }

            // 同时有最小值和最大值限制
            var greaterThanOrEqual = Expression.GreaterThanOrEqual(property, min);
            var lessThanOrEqual = Expression.LessThanOrEqual(property, max);
            return Expression.AndAlso(greaterThanOrEqual, lessThanOrEqual);
        }

        /// <summary>
        /// 获取多个条件或关系的表达式。
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="queryable"></param>
        /// <param name="conditional"></param>
        /// <returns></returns>
        public static IQueryable<T> GenerateWhereOr<T>(IQueryable<T> queryable, IDictionary<string, string> conditional) where T : class
        {
            if (conditional == null || !conditional.Any())
                return queryable;

            var type = typeof(T);
            var para = Expression.Parameter(type);
            Expression body = null;

            foreach (var item in conditional)
            {
                if (type.GetProperty(item.Key, BindingFlags.IgnoreCase | BindingFlags.GetProperty | BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy) is null) continue;
                var left = Expression.Property(para, item.Key);
                var values = item.Value.Split(',');
                Expression condition;

                if (values.Length == 1)
                {
                    var right = Constant(values[0], left.Type);
                    if (typeof(string) == left.Type) //对字符串则使用模糊查找
                    {
                        condition = StringContains(left, Constant(values[0], left.Type));
                    }
                    else
                    {
                        condition = Expression.Equal(left, Constant(values[0], left.Type));
                    }
                }
                else if (values.Length == 2)
                {
                    condition = Between(left, Constant(values[0], left.Type), Constant(values[1], left.Type));
                }
                else
                {
                    OwHelper.SetLastErrorAndMessage(404, $"不正确的参数格式——{item.Value}。");
                    return null;
                }

                body = body == null ? condition : Expression.OrElse(body, condition);
            }

            var func = Expression.Lambda<Func<T, bool>>(body, para);
            return queryable.Where(func);
        }

        /// <summary>
        /// 依据条件字典中的条件生成查询表达式。
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="queryable"></param>
        /// <param name="conditional">条件集合，为空集合则立即返回 <paramref name="queryable"/> </param>
        /// <returns>返回的可查询接口，null表示出错。</returns>
        public static IQueryable<T> GenerateWhereAnd<T>(IQueryable<T> queryable, IDictionary<string, string> conditional) where T : class
        {
            IQueryable<T> result = queryable;
            var type = typeof(T);
            var para = Expression.Parameter(type);
            foreach (var item in conditional)
            {
                if (type.GetProperty(item.Key, BindingFlags.IgnoreCase | BindingFlags.GetProperty | BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy) is null) continue;
                var left = Expression.Property(para, item.Key);
                var values = item.Value.Split(',');
                Expression body;

                if (values.Length == 1)
                {
                    // 单值查询处理
                    Expression right;

                    // 检查属性是否为枚举类型
                    if (IsEnumType(left.Type))
                    {
                        right = ParseEnumConstant(values[0], left.Type);
                    }
                    else
                    {
                        right = Constant(values[0], left.Type);
                    }

                    if (right == null) // 转换失败
                        return null;

                    if (typeof(string) == left.Type) // 对字符串则使用模糊查找
                    {
                        body = StringContains(left, right);
                    }
                    else
                        body = Expression.Equal(left, right);
                }
                else if (values.Length == 2)
                {
                    // 处理第一个值（可能为空字符串）
                    Expression minExpr = null;
                    if (!string.IsNullOrEmpty(values[0]))
                    {
                        // 检查属性是否为枚举类型
                        if (IsEnumType(left.Type))
                        {
                            minExpr = ParseEnumConstant(values[0], left.Type);
                        }
                        else
                        {
                            minExpr = Constant(values[0], left.Type);
                        }

                        if (minExpr == null) // 转换失败
                            return null;
                    }

                    // 处理第二个值（可能为空字符串）
                    Expression maxExpr = null;
                    if (!string.IsNullOrEmpty(values[1]))
                    {
                        // 检查属性是否为枚举类型
                        if (IsEnumType(left.Type))
                        {
                            maxExpr = ParseEnumConstant(values[1], left.Type);
                        }
                        else
                        {
                            maxExpr = Constant(values[1], left.Type);
                        }

                        if (maxExpr == null) // 转换失败
                            return null;
                    }

                    // 使用Between方法创建表达式
                    body = Between(left, minExpr, maxExpr);
                }
                else
                {
                    OwHelper.SetLastErrorAndMessage(404, $"不正确的参数格式——{item.Value}。");
                    return null;
                }

                var func = Expression.Lambda<Func<T, bool>>(body, para);
                result = result.Where(func);
            }
            return result;
        }

        /// <summary>
        /// 检查类型是否为枚举或可空枚举类型
        /// </summary>
        /// <param name="type">要检查的类型</param>
        /// <returns>如果是枚举或可空枚举则返回true，否则返回false</returns>
        private static bool IsEnumType(Type type)
        {
            // 检查是否为直接枚举类型
            if (type.IsEnum)
                return true;

            // 检查是否为可空枚举类型
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                return type.GetGenericArguments()[0].IsEnum;
            }

            return false;
        }

        /// <summary>
        /// 解析字符串为枚举常量表达式
        /// </summary>
        /// <param name="value">要解析的字符串值，可以是数字或枚举名称</param>
        /// <param name="enumType">目标枚举类型</param>
        /// <returns>表示枚举值的常量表达式，解析失败则返回null</returns>
        private static Expression ParseEnumConstant(string value, Type enumType)
        {
            // 处理可空类型
            Type underlyingType = enumType;
            bool isNullable = false;

            if (enumType.IsGenericType && enumType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                underlyingType = enumType.GetGenericArguments()[0];
                isNullable = true;
            }

            // 处理null值
            if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
            {
                if (isNullable)
                    return Expression.Constant(null, enumType);
                else
                {
                    OwHelper.SetLastErrorAndMessage(404, $"非可空枚举类型{underlyingType}不能设置为null");
                    return null;
                }
            }

            try
            {
                // 先尝试按名称解析
                if (Enum.TryParse(underlyingType, value, true, out object enumValue))
                {
                    return Expression.Constant(enumValue, isNullable ? enumType : underlyingType);
                }

                // 如果按名称解析失败，尝试按数值解析
                if (int.TryParse(value, out int intValue))
                {
                    if (Enum.IsDefined(underlyingType, intValue))
                    {
                        enumValue = Enum.ToObject(underlyingType, intValue);
                        return Expression.Constant(enumValue, isNullable ? enumType : underlyingType);
                    }
                }

                // 解析失败
                OwHelper.SetLastErrorAndMessage(404, $"无法将值'{value}'解析为枚举类型{underlyingType}");
                return null;
            }
            catch (Exception ex)
            {
                OwHelper.SetLastErrorAndMessage(404, $"解析枚举值出错: {ex.Message}");
                return null;
            }
        }

        public static IQueryable<T> GenerateWhereAndWithEntityName<T>(IQueryable<T> queryable, IDictionary<string, string> conditional) where T : class
        {
            var name = typeof(T).Name;
            var dic = new Dictionary<string, string>(conditional.Where(c => c.Key.StartsWith(name + "."))
                .Select(c => new KeyValuePair<string, string>(c.Key[(name.Length + 1)..], c.Value)));
            var result = GenerateWhereAnd(queryable, dic);
            return result;
        }

        /// <summary>
        /// 分析字符串获得一个指定类型的常量表达式。
        /// </summary>
        /// <param name="value"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public static Expression Constant(string value, Type type)
        {
            Expression result;
            var innerType = type;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))  //可空类型
                innerType = type.GetGenericArguments()[0];
            switch (Type.GetTypeCode(innerType))
            {
                case TypeCode.Empty:
                case TypeCode.DBNull:
                    result = Expression.Constant(null, type);
                    break;
                case TypeCode.Object:
                    if (innerType == typeof(Guid))
                    {
                        if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
                            result = Expression.Constant(null, type);
                        else if (!Guid.TryParse(value, out Guid guidValue))
                        {
                            OwHelper.SetLastErrorAndMessage(404, $"不能转换为类型{type}, {value}");
                            return null;
                        }
                        else
                            result = Expression.Constant(guidValue, type);
                    }
                    else
                    {
                        OwHelper.SetLastErrorAndMessage(404, $"不能转换为类型{type}, {value}");
                        return null;
                    }
                    break;
                case TypeCode.Boolean:
                    if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
                        result = Expression.Constant(null, type);
                    else if (!bool.TryParse(value, out bool boolValue))
                    {
                        OwHelper.SetLastErrorAndMessage(404, $"不能转换为类型{type}, {value}");
                        return null;
                    }
                    else
                        result = Expression.Constant(boolValue, type);
                    break;
                case TypeCode.Char:
                    if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
                        result = Expression.Constant(null, type);
                    else if (!char.TryParse(value, out char charValue))
                    {
                        OwHelper.SetLastErrorAndMessage(404, $"不能转换为类型{type}, {value}");
                        return null;
                    }
                    else result = Expression.Constant(charValue, type);
                    break;
                case TypeCode.SByte:
                case TypeCode.Byte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                case TypeCode.Single:
                case TypeCode.Double:
                case TypeCode.Decimal:
                    if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
                        result = Expression.Constant(null, type);
                    else if (!decimal.TryParse(value, out decimal decimalValue))
                    {
                        OwHelper.SetLastErrorAndMessage(404, $"不能转换为类型{type}, {value}");
                        return null;
                    }
                    else result = Expression.Convert(Expression.Constant(decimalValue), type);
                    break;
                case TypeCode.DateTime:
                    if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
                        result = Expression.Constant(null, type);
                    else if (!DateTime.TryParse(value, out DateTime dateTimeValue))
                    {
                        OwHelper.SetLastErrorAndMessage(404, $"不能转换为类型{type}, {value}");
                        return null;
                    }
                    else result = Expression.Constant(dateTimeValue, type);
                    break;
                case TypeCode.String:
                    if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
                        result = Expression.Constant(null, type);
                    else result = Expression.Constant(value, type);
                    break;
                default:
                    OwHelper.SetLastErrorAndMessage(404, $"不能转换为类型{type}, {value}");
                    return null;
            }
            return result;
        }

        /// <summary>
        /// 设置一个子表的完全集合，不在参数内的都删除，对已有Id的实体更新，对新Id执行添加操作。
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="src"></param>
        /// <param name="context">使用的数据库上下位，不会自动保存，调用者需要自己保存数据。</param>
        public static bool SetChildren<T>(IEnumerable<T> src, Guid parentId, DbContext context, ICollection<T> result = null) where T : GuidKeyObjectBase, IOwSubtables
        {
            var ids = new HashSet<Guid>(src.Select(c => c.Id)); //如果 collection 包含重复项，则集将包含每个唯一元素之一。 不会引发异常。 因此，生成的集的大小与 的大小 collection不同。
            if (!src.TryGetNonEnumeratedCount(out var idCount)) idCount = src.Count();
            if (idCount != ids.Count)
            {
                OwHelper.SetLastErrorAndMessage(400, $"{nameof(src)}中有重复键值。");
                return false;
            }
            if (src.Any(c => c.ParentId != parentId))   //若父实体对象Id非法
            {
                OwHelper.SetLastErrorAndMessage(400, $"{nameof(src)}中父实体对象Id非法——应为{parentId}。");
                return false;
            }
            var exists = context.Set<T>().Where(c => c.ParentId == parentId).ToArray();   //所有已存在的实体
            var removes = exists.Where(c => !ids.Contains(c.Id));   //需要移除的实体
            context.RemoveRange(removes);

            var existsIds = exists.Select(c => c.Id); //已存在的实体Id集合
            var adds = src.Where(c => !existsIds.Contains(c.Id)); //需要添加的实体
            context.AddRange(adds);
            if (result is not null)
                adds.ForEach(c => result.Add(c));

            var modifies = src.Where(c => existsIds.Contains(c.Id));    //要更改的实体
            var set = context.Set<T>();
            foreach (var item in modifies)
            {
                var entity = set.Find(item.Id);
                context.Entry(entity).CurrentValues.SetValues(item);
                result?.Add(item);
            }
            return true;
        }

        /// <summary>
        /// 对子表实体标准化，统一设置父对象Id，如果Id是<see cref="Guid.Empty"/>则生成一个新Id。
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="srcs"></param>
        /// <param name="parentId"></param>
        public static void NormalizeChildren<T>(IEnumerable<T> srcs, Guid parentId) where T : GuidKeyObjectBase, IOwSubtables
        {
            srcs.ForEach(c =>
            {
                c.GenerateIdIfEmpty();
                c.ParentId = parentId;
            });
        }
    }
}
