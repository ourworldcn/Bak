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
            var entityType = typeof(T); // ��ȡʵ������
            var properties = entityType.GetProperties().Where(c => c.CanRead).ToPooledListBase(); // ��ȡ���пɶ�����

            // ���δ�ṩӳ���Ϊ�գ��򴴽��µ�
            if (fieldMappings == null)
                fieldMappings = new Dictionary<string, string>();

            // ��������ӳ�䣬���ڼ����Щ��������ӳ��
            var reverseMappings = fieldMappings.ToDictionary(kv => kv.Value, kv => kv.Key);

            // Ϊδӳ������Դ���ӳ��
            foreach (var prop in properties)
            {
                // ��������Ѿ���ӳ�䣬������
                if (reverseMappings.ContainsKey(prop.Name))
                    continue;

                // ��������ΪDBF�ֶ������ضϵ�10���ַ���DBF�ֶ����������ƣ�
                string dbfFieldName = prop.Name.Length > 10 ? prop.Name.Substring(0, 10) : prop.Name;

                // �����ֶ����ظ�
                if (!fieldMappings.ContainsKey(dbfFieldName))
                {
                    fieldMappings.Add(dbfFieldName, prop.Name);
                }
                else
                {
                    // �����ֶ�����ͻ��������ֺ�׺
                    int suffix = 1;
                    string newFieldName;
                    do
                    {
                        // ȷ���ܳ��Ȳ�����10���ַ�
                        string baseName = dbfFieldName.Length >= 8 ? dbfFieldName.Substring(0, 8) : dbfFieldName;
                        newFieldName = $"{baseName}_{suffix++}";
                    } while (fieldMappings.ContainsKey(newFieldName) && suffix < 100); // ��������ѭ��

                    if (suffix < 100) // ȷ���ҵ��˿�������
                    {
                        fieldMappings.Add(newFieldName, prop.Name);
                        OwHelper.SetLastErrorAndMessage(400, $"�ֶ��� {dbfFieldName} ��ͻ����������Ϊ {newFieldName}");
                    }
                    else
                    {
                        OwHelper.SetLastErrorAndMessage(400, $"�޷�Ϊ���� {prop.Name} ����Ψһ�ֶ�����������");
                    }
                }
            }

            return fieldMappings; // ����������ӳ���ֵ�
        }
        #endregion

        #region ����д��
        /// <summary>
        /// ��ʵ�弯��д�������������Ա���Ϊ .dbf �ļ���
        /// </summary>
        /// <typeparam name="T">ʵ������</typeparam>
        /// <param name="entities">ʵ�弯��</param>
        /// <param name="stream">Ҫд�����</param>
        /// <param name="fieldMappings">�ֶ�ӳ���ֵ䣬��ΪDBF�ֶ�����ֵΪʵ����������Ϊnull���Զ�����</param>
        /// <param name="customFieldTypes">��ѡ���Զ����ֶ������ֵ䣬��ΪDBF�ֶ�����ֵΪDBF�ֶ�����</param>
        /// <param name="encoding">�ַ����룬Ĭ��ΪGB2312</param>
        public static void WriteToStream<T>(IEnumerable<T> entities, Stream stream,
            Dictionary<string, string> fieldMappings = null,
            Dictionary<string, NativeDbType> customFieldTypes = null,
            System.Text.Encoding encoding = null) where T : class
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream)); // ���������
            if (entities == null) throw new ArgumentNullException(nameof(entities)); // ���ʵ�弯��

            // ���δ�ṩӳ�䣬�򴴽��Զ�ӳ��
            if (fieldMappings == null || fieldMappings.Count == 0)
            {
                fieldMappings = CreateAutoFieldMappings<T>();
            }

            try
            {
                using var writer = new DBFWriter(stream);
                writer.CharEncoding = encoding ?? System.Text.Encoding.GetEncoding("GB2312"); // Ĭ��ʹ��GB2312����

                var entityType = typeof(T);
                var properties = entityType.GetProperties();
                var propertyMap = properties.ToDictionary(p => p.Name, p => p); // �������Բ����ֵ�

                // �����ֶζ���
                var fields = new List<DBFField>();
                foreach (var mapping in fieldMappings)
                {
                    string dbfFieldName = mapping.Key;
                    string propertyName = mapping.Value;

                    // ȷ���ֶ�������DBF���ƣ����10���ַ���
                    if (dbfFieldName.Length > 10)
                    {
                        OwHelper.SetLastErrorAndMessage(400, $"�ֶ��� {dbfFieldName} ����10���ַ������ƣ��ѽض�");
                        dbfFieldName = dbfFieldName.Substring(0, 10);
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

                    // �����������������ʵ����ֶγ��Ⱥ;���
                    int length = 0;
                    int decimalCount = 0;

                    switch (fieldType)
                    {
                        case NativeDbType.Char:
                            length = 254; // ����ַ�����
                            break;
                        case NativeDbType.Numeric:
                            length = 18;
                            decimalCount = 4; // �ʵ�����
                            break;
                        case NativeDbType.Float:
                            length = 18;
                            decimalCount = 6;
                            break;
                        case NativeDbType.Date:
                            length = 8;
                            break;
                        case NativeDbType.Logical:
                            length = 1;
                            break;
                        case NativeDbType.Long:
                            length = 10;
                            break;
                        default:
                            length = 254; // Ĭ����󳤶�
                            break;
                    }

                    fields.Add(new DBFField(dbfFieldName, fieldType, length, decimalCount)); // ����ֶζ���
                }

                writer.Fields = fields.ToArray(); // ����DBF�ļ����ֶνṹ

                // д���¼
                foreach (var entity in entities)
                {
                    if (entity == null)
                    {
                        OwHelper.SetLastErrorAndMessage(400, "ʵ�弯���а���nullֵ��������");
                        continue;
                    }

                    var record = new object[fields.Count];
                    int fieldIndex = 0;

                    foreach (var mapping in fieldMappings)
                    {
                        if (fieldIndex >= fields.Count) break; // ��ֹ����Խ��

                        string propertyName = mapping.Value;
                        if (!propertyMap.TryGetValue(propertyName, out var property))
                        {
                            record[fieldIndex++] = null; // ���Բ����ڣ�ֵΪnull
                            continue;
                        }

                        var value = property.GetValue(entity); // ��ȡ����ֵ

                        // �Կɿ����ͽ��д���
                        if (value != null && property.PropertyType.IsGenericType &&
                            property.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
                        {
                            try
                            {
                                value = Convert.ChangeType(value, Nullable.GetUnderlyingType(property.PropertyType)); // ת���ɿ�����
                            }
                            catch
                            {
                                OwHelper.SetLastErrorAndMessage(400, $"ת������ {propertyName} ��ֵʱ��������");
                                value = null;
                            }
                        }

                        // ������������
                        if (value != null)
                        {
                            if (value is string strValue && strValue.Length > 254)
                            {
                                value = strValue.Substring(0, 254); // �ضϹ����ַ���
                                OwHelper.SetLastErrorAndMessage(400, $"�ַ���ֵ���ض�: {propertyName}");
                            }
                            else if (value is DateTime dateValue && (dateValue == DateTime.MinValue || dateValue.Year < 1900))
                            {
                                value = null; // DBF��֧�ֹ��������
                                OwHelper.SetLastErrorAndMessage(400, $"����ֵ��֧��: {propertyName}");
                            }
                        }

                        record[fieldIndex++] = value; // �洢ֵ����¼
                    }

                    writer.WriteRecord(record); // д���¼��DBF
                }
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

        /// <summary>
        /// ��ʵ�弯��д���ļ��������ڴ���������д��������ķ��ڴ档
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entities"></param>
        /// <param name="filePath"></param>
        /// <param name="fieldMappings"></param>
        /// <param name="customFieldTypes"></param>
        /// <param name="encoding"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public static void WriteLargeFile<T>(IEnumerable<T> entities, string filePath,
            Dictionary<string, string> fieldMappings = null,
            Dictionary<string, NativeDbType> customFieldTypes = null,
            System.Text.Encoding encoding = null) where T : class
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException(nameof(filePath)); // ����ļ�·��
            const int memoryLimit = 1024 * 1024 * 1024; //�ڴ淧�Ż�ȡ1GB�ڴ�
            MemoryStream ms = new MemoryStream(memoryLimit);// �����ڴ���
            try
            {
                using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8 * 1024 * 1024);
                WriteToStream(entities, ms, fieldMappings, customFieldTypes, encoding); // ������д�뷽��
                ms.Position = 0; // �����ڴ���λ��
                ms.CopyTo(stream); // ���ڴ������ݸ��Ƶ��ļ���
            }
            catch (Exception ex)
            {
                OwHelper.SetLastErrorAndMessage(500, $"д�� DBF �ļ� {filePath} ʱ��������: {ex.Message}");
                throw;
            }
            finally
            {
                // �ͷ��ڴ�������֤������ڴ汻��ʱ����
                ms = null;
                GC.Collect(); // ǿ����������
                GC.WaitForPendingFinalizers(); // �ȴ������ս������
                GC.Collect(); // �ٴ�ǿ����������,ȷ��LOB�����ͷ�
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
            if (dataTable == null) throw new ArgumentNullException(nameof(dataTable)); // ������֤
            if (fieldMappings == null || fieldMappings.Count == 0)
            {
                OwHelper.SetLastErrorAndMessage(400, "�ֶ�ӳ�䲻��Ϊ��");
                throw new ArgumentException("�ֶ�ӳ�䲻��Ϊ��", nameof(fieldMappings));
            }

            var result = new List<T>(); // �������
            var entityType = typeof(T); // ʵ������
            var propertyMap = entityType.GetProperties().ToDictionary(p => p.Name, p => p); // �����ֵ䣬���ڿ��ٲ���

            // ����ӳ�䣬��DBF�ֶ���ӳ�䵽������
            var reverseMapping = new Dictionary<string, string>();
            foreach (var mapping in fieldMappings)
            {
                if (!reverseMapping.ContainsKey(mapping.Key.ToUpper()))
                    reverseMapping.Add(mapping.Key.ToUpper(), mapping.Value);
            }

            // ����������������ӳ�䣬�Ա����ز�����
            var columnIndexMap = new Dictionary<string, int>();
            for (int i = 0; i < dataTable.Columns.Count; i++)
            {
                columnIndexMap[dataTable.Columns[i].ColumnName.ToUpper()] = i;
            }

            // ����ÿһ������
            foreach (DataRow row in dataTable.Rows)
            {
                var entity = new T(); // ������ʵ��
                bool hasValidData = false; // ����Ƿ�����Ч����

                // ����������
                foreach (var fieldName in reverseMapping.Keys)
                {
                    string propertyName = reverseMapping[fieldName];
                    if (!propertyMap.TryGetValue(propertyName, out var property))
                    {
                        OwHelper.SetLastErrorAndMessage(400, $"���� {propertyName} ������ {entityType.Name} �в�����");
                        continue;
                    }

                    // ����������
                    if (!columnIndexMap.TryGetValue(fieldName, out int columnIndex))
                    {
                        OwHelper.SetLastErrorAndMessage(400, $"�ֶ� {fieldName} �����ݱ��в�����");
                        continue;
                    }

                    try
                    {
                        var value = row[columnIndex]; // ��ȡ��Ԫ��ֵ

                        if (value == null || value == DBNull.Value)
                            continue; // ������ֵ

                        // ����ת��
                        object convertedValue = null;

                        try
                        {
                            var propertyType = property.PropertyType;
                            bool isNullable = propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Nullable<>);

                            // ����ɿ�����
                            if (isNullable)
                                propertyType = Nullable.GetUnderlyingType(propertyType);

                            // �����ַ����������
                            if (propertyType == typeof(string))
                            {
                                convertedValue = value.ToString().Trim();
                            }
                            // ���������������
                            else if (propertyType == typeof(DateTime))
                            {
                                if (DateTime.TryParse(value.ToString(), out var dt))
                                    convertedValue = dt;
                            }
                            // �������������
                            else if (propertyType == typeof(bool))
                            {
                                string strValue = value.ToString().ToUpper().Trim();
                                convertedValue = strValue == "Y" || strValue == "T" || strValue == "YES" ||
                                               strValue == "TRUE" || strValue == "1";
                            }
                            // ����Guid�������
                            else if (propertyType == typeof(Guid))
                            {
                                if (Guid.TryParse(value.ToString(), out var guid))
                                    convertedValue = guid;
                            }
                            // ��������ת��
                            else
                            {
                                convertedValue = Convert.ChangeType(value, propertyType);
                            }

                            if (convertedValue != null)
                            {
                                property.SetValue(entity, convertedValue); // ��������ֵ
                                hasValidData = true; // �������Ч����
                            }
                        }
                        catch (Exception ex)
                        {
                            OwHelper.SetLastErrorAndMessage(400, $"ת������ {propertyName} ��ֵ {value} ʱ��������: {ex.Message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        OwHelper.SetLastErrorAndMessage(500, $"�����ֶ� {fieldName} ʱ��������: {ex.Message}");
                    }
                }

                // �������Ч���ݵ�ʵ��
                if (hasValidData)
                    result.Add(entity);
            }

            return result;
        }
        #endregion
    }
}
