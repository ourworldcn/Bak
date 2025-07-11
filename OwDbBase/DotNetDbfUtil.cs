/*
 * �ļ�����DotNetDbfUtil.cs
 * ���ߣ�OW
 * �������ڣ�2025��5��8��
 * �޸����ڣ�2025��5��8��
 * ���������ļ����� DotNetDBF �������ʵ�֣����ڲ��� DBF �ļ���
 */

using DotNetDBF;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;

namespace OW.Data
{
    /// <summary>
    /// DotNetDBF �����࣬�ṩ DBF �ļ������ĸ���������
    /// </summary>
    public static class DotNetDbfUtil
    {
        #region ����ת��
        /// <summary>
        /// ��ȡ DBF �ֶ����͡�
        /// </summary>
        /// <param name="type">�ֶ����͡�</param>
        /// <returns>DBF �ֶ����͡�</returns>
        public static NativeDbType GetDBFFieldType(Type type)
        {
            // ����ɿ�����
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                type = Nullable.GetUnderlyingType(type);
            }

            return type switch
            {
                // �ַ�������
                Type t when t == typeof(string) => NativeDbType.Char,

                // ��������
                Type t when t == typeof(int) || t == typeof(uint) => NativeDbType.Long,
                Type t when t == typeof(long) || t == typeof(ulong) => NativeDbType.Numeric,
                Type t when t == typeof(short) || t == typeof(ushort) => NativeDbType.Numeric,
                Type t when t == typeof(byte) || t == typeof(sbyte) => NativeDbType.Numeric,

                // ��������
                Type t when t == typeof(float) => NativeDbType.Float,
                Type t when t == typeof(double) => NativeDbType.Double,
                Type t when t == typeof(decimal) => NativeDbType.Numeric,

                // ����ʱ������
                Type t when t == typeof(DateTime) => NativeDbType.Date,
                Type t when t == typeof(DateTimeOffset) => NativeDbType.Date,

                // ��������
                Type t when t == typeof(bool) => NativeDbType.Logical,

                // ��������������
                Type t when t == typeof(byte[]) => NativeDbType.Binary,
#if NET472_OR_GREATER || NETFRAMEWORK
                Type t when t == typeof(System.Data.Linq.Binary) => NativeDbType.Binary,
#endif
                // ��������Ĭ��Ϊ�ַ�����
                _ => NativeDbType.Char
            };
        }

        /// <summary>
        /// ���� DBF �ֶ����ͻ�ȡ DataTable �е����͡�
        /// </summary>
        /// <param name="dbfType">DBF �ֶ����͡�</param>
        /// <returns>DataTable �е����͡�</returns>
        public static Type GetFieldTypeFromDBF(NativeDbType dbfType)
        {
            return dbfType switch
            {
                NativeDbType.Char => typeof(string),        // 'C' �ַ���
                NativeDbType.Numeric => typeof(decimal),    // 'N' ��ֵ��
                NativeDbType.Float => typeof(float),        // 'F' ������
                NativeDbType.Date => typeof(DateTime),      // 'D' ������
                NativeDbType.Logical => typeof(bool),       // 'L' �߼���
                NativeDbType.Memo => typeof(string),        // 'M' ��ע��
                NativeDbType.Binary => typeof(byte[]),      // 'B' ������
                NativeDbType.Long => typeof(int),           // 'I' ������
                NativeDbType.Double => typeof(double),      // 'O' ˫���ȸ�����
                NativeDbType.Autoincrement => typeof(int),  // '+' ������
                NativeDbType.Timestamp => typeof(DateTime), // '@' ʱ���
                NativeDbType.Ole => typeof(byte[]),         // 'G' OLE����
                _ => typeof(string)                         // Ĭ��Ϊ�ַ���
            };
        }
        #endregion

        #region �ֶ�ӳ��
        /// <summary>
        /// Ϊ������ʵ�����ʹ����Զ��ֶ�ӳ��
        /// </summary>
        /// <typeparam name="T">ʵ������</typeparam>
        /// <param name="fieldMappings">���е��ֶ�ӳ�䣬û��ָ�������Խ��Զ�����ӳ�䣻Ϊnullʱ�����µ�ӳ��</param>
        /// <returns>�ֶ�ӳ���ֵ䣬��ΪDBF�ֶ���(���10�ַ�)��ֵΪʵ��������</returns>
        public static Dictionary<string, string> CreateAutoFieldMappings<T>(Dictionary<string, string> fieldMappings = null) where T : class
        {
            var entityType = typeof(T);
            
            try
            {
                // ��ȡ���пɶ��Ĺ�������
                var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanRead && p.GetIndexParameters().Length == 0) // �ų�����������
                    .ToArray();

                if (properties.Length == 0)
                {
                    OwHelper.SetLastErrorAndMessage(400, $"���� {entityType.Name} û�п��õ���������DBFӳ��");
                    return fieldMappings ?? new Dictionary<string, string>();
                }

                // ��ʼ��ӳ���ֵ�
                fieldMappings ??= new Dictionary<string, string>();

                // ��������ӳ�䣬���ڼ����Щ��������ӳ��
                var reverseMappings = fieldMappings.ToDictionary(kv => kv.Value, kv => kv.Key);

                // Ϊδӳ������Դ���ӳ��
                foreach (var prop in properties)
                {
                    // ��������ӳ�������
                    if (reverseMappings.ContainsKey(prop.Name))
                        continue;

                    // ����DBF�ֶ��������10���ַ���
                    var dbfFieldName = GenerateUniqueDbfFieldName(prop.Name, fieldMappings);
                    if (!string.IsNullOrEmpty(dbfFieldName))
                    {
                        fieldMappings[dbfFieldName] = prop.Name;
                    }
                }

                return fieldMappings;
            }
            catch (Exception ex)
            {
                OwHelper.SetLastErrorAndMessage(500, $"�����ֶ�ӳ��ʱ��������: {ex.Message}");
                return fieldMappings ?? new Dictionary<string, string>();
            }
        }

        /// <summary>
        /// ����Ψһ��DBF�ֶ���
        /// </summary>
        /// <param name="propertyName">������</param>
        /// <param name="existingMappings">����ӳ��</param>
        /// <returns>Ψһ��DBF�ֶ���������޷������򷵻�null</returns>
        private static string GenerateUniqueDbfFieldName(string propertyName, Dictionary<string, string> existingMappings)
        {
            // �ضϵ�10���ַ�
            var baseName = propertyName.Length > 10 ? propertyName[..10] : propertyName;
            
            // ���û�г�ͻ��ֱ�ӷ���
            if (!existingMappings.ContainsKey(baseName))
                return baseName;

            // �����ͻ��������ֺ�׺
            for (int suffix = 1; suffix < 100; suffix++)
            {
                var suffixStr = suffix.ToString();
                var maxBaseLength = 10 - suffixStr.Length - 1; // ��ȥ��׺���Ⱥ��»���
                if (maxBaseLength <= 0) break;

                var truncatedBase = baseName.Length > maxBaseLength ? baseName[..maxBaseLength] : baseName;
                var newFieldName = $"{truncatedBase}_{suffixStr}";
                
                if (!existingMappings.ContainsKey(newFieldName))
                    return newFieldName;
            }

            // �޷�����Ψһ����
            OwHelper.SetLastErrorAndMessage(400, $"�޷�Ϊ���� {propertyName} ����Ψһ��DBF�ֶ���");
            return null;
        }
        #endregion

        #region ����д��
        /// <summary>
        /// ��ʵ�弯��д�뵽���У����Խ����Զ�ת��Ϊ .dbf �ļ���
        /// ʹ�ø���ȫ��DBFд������������NullReferenceException
        /// </summary>
        /// <typeparam name="T">ʵ������</typeparam>
        /// <param name="entities">ʵ�弯��</param>
        /// <param name="stream">Ҫд��������������غ�����λ�ò���Ԥ֪�����ᱣ�ִ�״̬��</param>
        /// <param name="fieldMappings">�ֶ�ӳ���ֵ䣬��ΪDBF�ֶ�����ֵΪʵ����������Ϊnull���Զ�����</param>
        /// <param name="customFieldTypes">��ѡ���Զ����ֶ������ֵ䣬��ΪDBF�ֶ�����ֵΪDBF�ֶ�����</param>
        /// <param name="encoding">�ַ����룬Ĭ��ΪGB2312</param>
        public static void WriteToStream<T>(IEnumerable<T> entities, Stream stream,
            Dictionary<string, string> fieldMappings = null,
            Dictionary<string, NativeDbType> customFieldTypes = null,
            System.Text.Encoding? encoding = null) where T : class
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(entities);

            // ת��Ϊ�б��Ա���ö�ٺͼ���
            var entityList = entities as List<T> ?? entities.ToList();
            if (!entityList.Any())
            {
                OwHelper.SetLastErrorAndMessage(400, "ʵ�弯��Ϊ�գ��޷�����DBF�ļ�");
                throw new InvalidOperationException("ʵ�弯��Ϊ�գ��޷�����DBF�ļ�");
            }

            // ���δ�ṩӳ�䣬�򴴽��Զ�ӳ��
            fieldMappings ??= CreateAutoFieldMappings<T>();
            
            if (fieldMappings == null || !fieldMappings.Any())
            {
                throw new InvalidOperationException("�޷�������Ч���ֶ�ӳ��");
            }

            var recordsWritten = 0;
            
            try
            {
                using var wapper = new WrapperStream(stream, true); // ʹ�ð�װ������ȷ����ȷ�ͷ���Դ
                using var writer = new DBFWriter(wapper);   // ʹ�� using ���ȷ�� DBFWriter ��ȷ�ͷ�
                writer.CharEncoding = encoding ?? System.Text.Encoding.GetEncoding("GB2312"); // Ĭ�ϱ���

                var entityType = typeof(T);
                var properties = entityType.GetProperties();
                var propertyMap = properties.ToDictionary(p => p.Name, p => p); // �����������ֵ�

                // �����ֶζ���
                var fields = new List<DBFField>();
                var validMappings = new List<KeyValuePair<string, string>>(); // �洢��Ч��ӳ��
                
                foreach (var mapping in fieldMappings)
                {
                    string dbfFieldName = mapping.Key;
                    string propertyName = mapping.Value;

                    // ȷ���ֶ�������DBF�淶
                    if (dbfFieldName.Length > 10)
                    {
                        OwHelper.SetLastErrorAndMessage(400, $"�ֶ��� {dbfFieldName} ����10���ַ����ƣ��ѽض�");
                        dbfFieldName = dbfFieldName[..10]; // ʹ�÷�Χ������򻯽ضϲ���
                    }

                    if (!propertyMap.TryGetValue(propertyName, out var property))
                    {
                        OwHelper.SetLastErrorAndMessage(400, $"���� {propertyName} ������ {entityType.Name} �в����ڣ���������");
                        continue;
                    }

                    NativeDbType fieldType;
                    if (customFieldTypes != null && customFieldTypes.TryGetValue(dbfFieldName, out var customType))
                    {
                        fieldType = customType; // ʹ���Զ�������
                    }
                    else
                    {
                        try
                        {
                            fieldType = GetDBFFieldType(property.PropertyType); // ������������ȷ��DBF����
                        }
                        catch (ArgumentException)
                        {
                            OwHelper.SetLastErrorAndMessage(400, $"���� {propertyName} ������ {property.PropertyType.Name} ��֧�֣���������");
                            continue;
                        }
                    }

                    // �����ֶ����ʹ���DBFField - �޸�������Ƿ�֧�ֳ�������
                    try
                    {
                        DBFField field = fieldType switch
                        {
                            NativeDbType.Date => new DBFField(dbfFieldName, fieldType), // �������Ͳ���Ҫ���ó���
                            NativeDbType.Logical => new DBFField(dbfFieldName, fieldType), // �߼����Ͳ���Ҫ���ó���
                            NativeDbType.Char => new DBFField(dbfFieldName, fieldType, 254, 0), // �ַ��������ó���
                            NativeDbType.Numeric => new DBFField(dbfFieldName, fieldType, 18, 4), // �����������ó��Ⱥ�С��λ
                            NativeDbType.Float => new DBFField(dbfFieldName, fieldType, 18, 6), // �����������ó��Ⱥ�С��λ
                            NativeDbType.Long => new DBFField(dbfFieldName, fieldType, 10, 0), // ���������ó���
                            NativeDbType.Double => new DBFField(dbfFieldName, fieldType, 18, 8), // ˫���ȸ��������ó��Ⱥ�С��λ
                            _ => new DBFField(dbfFieldName, fieldType) // �������ͳ���Ĭ�Ϲ���
                        };

                        fields.Add(field);
                        validMappings.Add(new KeyValuePair<string, string>(dbfFieldName, propertyName));
                    }
                    catch (Exception ex)
                    {
                        OwHelper.SetLastErrorAndMessage(400, $"����DBF�ֶ� {dbfFieldName} (����: {fieldType}) ʱ��������: {ex.Message}����������");
                        continue;
                    }
                }

                // ȷ��������һ���ֶ�
                if (fields.Count == 0)
                {
                    throw new InvalidOperationException("û����Ч���ֶζ��壬�޷�����DBF�ļ�");
                }

                // �����ֶνṹ - ���ǹؼ����裬������д���¼֮ǰ���
                writer.Fields = fields.ToArray();

                // д���¼
                foreach (var entity in entityList)
                {
                    if (entity is null)
                    {
                        OwHelper.SetLastErrorAndMessage(400, "ʵ�弯���а���nullֵ����������");
                        continue;
                    }

                    var record = new object?[fields.Count];
                    
                    for (int fieldIndex = 0; fieldIndex < validMappings.Count; fieldIndex++)
                    {
                        var mapping = validMappings[fieldIndex];
                        string propertyName = mapping.Value;
                        
                        if (!propertyMap.TryGetValue(propertyName, out var property))
                        {
                            record[fieldIndex] = null; // ���Բ�����ʱʹ��null
                            continue;
                        }

                        var value = property.GetValue(entity); // ��ȡ����ֵ

                        // ����ɿ�����
                        if (value is not null && property.PropertyType.IsGenericType &&
                            property.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
                        {
                            try
                            {
                                value = Convert.ChangeType(value, Nullable.GetUnderlyingType(property.PropertyType)!);
                            }
                            catch
                            {
                                OwHelper.SetLastErrorAndMessage(400, $"ת������ {propertyName} ��ֵʱ��������");
                                value = null;
                            }
                        }

                        // ��������ֵ������
                        value = value switch
                        {
                            string strValue when strValue.Length > 254 => strValue[..254], // �ضϹ����ַ���
                            DateTime dateValue when dateValue == DateTime.MinValue || dateValue.Year < 1900 => null, // DBF��֧�ֹ��������
                            _ => value
                        };

                        record[fieldIndex] = value; // �洢ֵ����¼
                    }

                    writer.WriteRecord(record); // д���¼��DBF
                    recordsWritten++; // ��¼��д��ļ�¼��
                }

                // ȷ������д����һ����¼
                if (recordsWritten == 0)
                {
                    OwHelper.SetLastErrorAndMessage(400, "û����Ч�ļ�¼��д��DBF�ļ�");
                    throw new InvalidOperationException("û����Ч�ļ�¼��д��DBF�ļ�");
                }

                // DBFWriter ���� using ������ʱ�Զ����� Dispose �������д��
            }
            catch (Exception ex)
            {
                OwHelper.SetLastErrorAndMessage(500, $"д�� DBF ��ʱ��������: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// ��ʵ�弯��д��DBF�ļ�
        /// </summary>
        /// <typeparam name="T">ʵ������</typeparam>
        /// <param name="entities">ʵ�弯��</param>
        /// <param name="filePath">�ļ�·��</param>
        /// <param name="fieldMappings">�ֶ�ӳ���ֵ䣬��ΪDBF�ֶ�����ֵΪʵ����������Ϊnull���Զ�����</param>
        /// <param name="customFieldTypes">��ѡ���Զ����ֶ������ֵ䣬��ΪDBF�ֶ�����ֵΪDBF�ֶ�����</param>
        /// <param name="encoding">�ַ����룬Ĭ��ΪGB2312</param>
        public static void WriteToFile<T>(IEnumerable<T> entities, string filePath,
            Dictionary<string, string> fieldMappings = null,
            Dictionary<string, NativeDbType> customFieldTypes = null,
            System.Text.Encoding encoding = null) where T : class
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException(nameof(filePath)); // ����ļ�·��

            try
            {
                using var stream = File.Open(filePath, FileMode.Create, FileAccess.Write);
                WriteToStream(entities, stream, fieldMappings, customFieldTypes, encoding); // ������д�뷽��
            }
            catch (Exception ex)
            {
                OwHelper.SetLastErrorAndMessage(500, $"д�� DBF �ļ� {filePath} ʱ��������: {ex.Message}");
                throw;
            }
        }
        #endregion

        #region ���ݶ�ȡ
        /// <summary>
        /// ��DataTableת��Ϊʵ�弯��
        /// </summary>
        /// <typeparam name="T">Ŀ��ʵ������</typeparam>
        /// <param name="dataTable">Դ���ݱ�</param>
        /// <param name="fieldMappings">�ֶ�ӳ�䣬��ΪDBF�ֶ�����ֵΪʵ��������</param>
        /// <returns>ʵ�弯��</returns>
        public static IEnumerable<T> ConvertDataTableToEntities<T>(DataTable dataTable,
            Dictionary<string, string> fieldMappings) where T : class, new()
        {
            ArgumentNullException.ThrowIfNull(dataTable);
            
            if (fieldMappings == null || fieldMappings.Count == 0)
            {
                OwHelper.SetLastErrorAndMessage(400, "�ֶ�ӳ�䲻��Ϊ��");
                throw new ArgumentException("�ֶ�ӳ�䲻��Ϊ��", nameof(fieldMappings));
            }

            var result = new List<T>();
            var entityType = typeof(T);
            var propertyMap = entityType.GetProperties().ToDictionary(p => p.Name, p => p);

            // ����ӳ�䣬��DBF�ֶ���ӳ�䵽�������������ִ�Сд��
            var reverseMapping = fieldMappings.ToDictionary(
                kv => kv.Key.ToUpperInvariant(), 
                kv => kv.Value, 
                StringComparer.OrdinalIgnoreCase);

            // ����������������ӳ�䣨�����ִ�Сд��
            var columnIndexMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < dataTable.Columns.Count; i++)
            {
                columnIndexMap[dataTable.Columns[i].ColumnName] = i;
            }

            // ����ÿһ������
            foreach (DataRow row in dataTable.Rows)
            {
                try
                {
                    var entity = new T();
                    bool hasValidData = false;

                    foreach (var kvp in reverseMapping)
                    {
                        var fieldName = kvp.Key;
                        var propertyName = kvp.Value;

                        // ��������Ƿ����
                        if (!propertyMap.TryGetValue(propertyName, out var property))
                        {
                            OwHelper.SetLastErrorAndMessage(400, $"���� {propertyName} ������ {entityType.Name} �в�����");
                            continue;
                        }

                        // ������Ƿ����
                        if (!columnIndexMap.TryGetValue(fieldName, out int columnIndex))
                        {
                            OwHelper.SetLastErrorAndMessage(400, $"�ֶ� {fieldName} �����ݱ��в�����");
                            continue;
                        }

                        try
                        {
                            var cellValue = row[columnIndex];
                            if (cellValue == null || cellValue == DBNull.Value)
                                continue;

                            // ת������������ֵ
                            var convertedValue = ConvertValue(cellValue, property.PropertyType);
                            if (convertedValue != null)
                            {
                                property.SetValue(entity, convertedValue);
                                hasValidData = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            OwHelper.SetLastErrorAndMessage(400, $"ת���ֶ� {fieldName} ������ {propertyName} ʱ��������: {ex.Message}");
                        }
                    }

                    // ֻ�������Ч���ݵ�ʵ��
                    if (hasValidData)
                        result.Add(entity);
                }
                catch (Exception ex)
                {
                    OwHelper.SetLastErrorAndMessage(500, $"����������ʱ��������: {ex.Message}");
                }
            }

            return result;
        }

        /// <summary>
        /// ת��ֵ��Ŀ������
        /// </summary>
        /// <param name="value">ԭʼֵ</param>
        /// <param name="targetType">Ŀ������</param>
        /// <returns>ת�����ֵ��ʧ�ܷ���null</returns>
        private static object ConvertValue(object value, Type targetType)
        {
            try
            {
                // ����ɿ�����
                var isNullable = targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>);
                var actualType = isNullable ? Nullable.GetUnderlyingType(targetType) : targetType;

                // ����Ŀ�����ͽ������⴦��
                return actualType.Name switch
                {
                    nameof(String) => value.ToString().Trim(),
                    nameof(DateTime) => DateTime.TryParse(value.ToString(), out var dt) ? dt : null,
                    nameof(Boolean) => ConvertToBoolean(value.ToString()),
                    nameof(Guid) => Guid.TryParse(value.ToString(), out var guid) ? guid : null,
                    _ => Convert.ChangeType(value, actualType) // ��������ת��
                };
            }
            catch
            {
                return null; // ת��ʧ�ܷ���null
            }
        }

        /// <summary>
        /// ת���ַ���������ֵ
        /// </summary>
        /// <param name="value">�ַ���ֵ</param>
        /// <returns>����ֵ</returns>
        private static bool ConvertToBoolean(string value)
        {
            var upperValue = value.ToUpperInvariant().Trim();
            return upperValue is "Y" or "T" or "YES" or "TRUE" or "1";
        }
        #endregion
    }
}
