/*
 * �ļ�����DotNetDBFManager.cs
 * ���ߣ�OW
 * �������ڣ�2025��2��21��
 * �޸����ڣ�2023��2��21��
 * ���������ļ����� DotNetDBFManager ���ʵ�֣����ڲ��� DBF �ļ���
 */

using System;
using System.Data;
using System.IO;
using DotNetDBF;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OW.Data
{
    /// <summary>
    /// DotNetDBFManager �࣬���ڲ��� DBF �ļ���
    /// </summary>
    public class DotNetDBFManager
    {
        private readonly ILogger<DotNetDBFManager> _logger;

        /// <summary>
        /// ���캯������ʼ����־��¼����
        /// </summary>
        /// <param name="logger">��־��¼����</param>
        public DotNetDBFManager(ILogger<DotNetDBFManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// �� DBF �ļ���ȡ���ݵ� DataTable��
        /// </summary>
        /// <param name="filePath">DBF �ļ�·����</param>
        /// <returns>���� DBF ���ݵ� DataTable��</returns>
        public DataTable ReadDBFToDataTable(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                _logger.LogDebug("�ļ�·��Ϊ�ջ�������հ��ַ���");
                throw new ArgumentNullException(nameof(filePath));
            }

            var dataTable = new DataTable();

            try
            {
                using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read))
                using (var reader = new DBFReader(stream))
                {
                    reader.CharEncoding = System.Text.Encoding.UTF8;
                    var fields = reader.Fields;

                    // ���� DataTable ��
                    foreach (var field in fields)
                    {
                        dataTable.Columns.Add(field.Name, GetFieldTypeFromDBF(field.DataType));
                    }

                    // ��ȡ����
                    object[] record;
                    while ((record = reader.NextRecord()) != null)
                    {
                        var dataRow = dataTable.NewRow();
                        for (int i = 0; i < fields.Length; i++)
                        {
                            dataRow[fields[i].Name] = record[i];
                        }
                        dataTable.Rows.Add(dataRow);
                    }
                }
                _logger.LogDebug("�ɹ����ļ���ȡ���ݵ� DataTable��");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "��ȡ DBF �ļ�ʱ��������");
                throw;
            }

            return dataTable;
        }

        /// <summary>
        /// �� DataTable д�� DBF �ļ���
        /// </summary>
        /// <param name="filePath">DBF �ļ�·����</param>
        /// <param name="dataTable">Ҫд��� DataTable��</param>
        public void WriteDataTableToDBF(string filePath, DataTable dataTable)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                _logger.LogDebug("�ļ�·��Ϊ�ջ�������հ��ַ���");
                throw new ArgumentNullException(nameof(filePath));
            }
            if (dataTable == null || dataTable.Columns.Count == 0)
            {
                _logger.LogDebug("DataTable Ϊ�ջ򲻰����κ��С�");
                throw new ArgumentNullException(nameof(dataTable));
            }

            try
            {
                using (var stream = File.Open(filePath, FileMode.Create, FileAccess.Write))
                using (var writer = new DBFWriter(stream))
                {
                    writer.CharEncoding = System.Text.Encoding.UTF8;

                    // ��ȡ�ֶ���Ϣ
                    var fields = new DBFField[dataTable.Columns.Count];
                    for (int i = 0; i < dataTable.Columns.Count; i++)
                    {
                        var column = dataTable.Columns[i];
                        var fieldType = GetDBFFieldType(column.DataType);
                        fields[i] = new DBFField(column.ColumnName, fieldType);
                    }

                    writer.Fields = fields;

                    // д������
                    foreach (DataRow row in dataTable.Rows)
                    {
                        var recordData = new object[fields.Length];
                        for (int i = 0; i < fields.Length; i++)
                        {
                            recordData[i] = row[i];
                        }
                        writer.WriteRecord(recordData);
                    }
                }
                _logger.LogDebug("�ɹ��� DataTable д���ļ���");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "д�� DBF �ļ�ʱ��������");
                throw;
            }
        }

        /// <summary>
        /// ��ȡ DBF �ֶ����͡�
        /// </summary>
        /// <param name="type">�ֶ����͡�</param>
        /// <returns>DBF �ֶ����͡�</returns>
        private NativeDbType GetDBFFieldType(Type type)
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
        private Type GetFieldTypeFromDBF(NativeDbType dbfType)
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


        /// <summary>
        /// ��ʵ�弯��д�������������Ա���Ϊ .dbf �ļ���
        /// </summary>
        /// <typeparam name="T">ʵ������</typeparam>
        /// <param name="stream">Ҫд�����</param>
        /// <param name="entities">ʵ�弯��</param>
        /// <param name="fieldMappings">�ֶ�ӳ���ֵ䣬��ΪDBF�ֶ�����ֵΪʵ��������</param>
        /// <param name="customFieldTypes">��ѡ���Զ����ֶ������ֵ䣬��ΪDBF�ֶ�����ֵΪDBF�ֶ�����</param>
        /// <param name="encoding">�ַ����룬Ĭ��ΪUTF-8</param>
        public void WriteEntitiesToStream<T>(Stream stream, IEnumerable<T> entities,
            Dictionary<string, string> fieldMappings,
            Dictionary<string, NativeDbType> customFieldTypes = null,
            System.Text.Encoding encoding = null) where T : class
        {
            if (stream == null)
            {
                _logger.LogDebug("������Ϊ�ա�");
                throw new ArgumentNullException(nameof(stream));
            }
            if (entities == null)
            {
                _logger.LogDebug("ʵ�弯��Ϊ�ա�");
                throw new ArgumentNullException(nameof(entities));
            }
            if (fieldMappings == null || fieldMappings.Count == 0)
            {
                _logger.LogDebug("�ֶ�ӳ��Ϊ�ջ򲻰����κ�ӳ���ϵ��");
                throw new ArgumentNullException(nameof(fieldMappings));
            }

            try
            {
                using (var writer = new DBFWriter(stream))
                {
                    writer.CharEncoding = encoding ?? System.Text.Encoding.UTF8;

                    var entityType = typeof(T);
                    var properties = entityType.GetProperties();
                    var propertyMap = properties.ToDictionary(p => p.Name, p => p);

                    // �����ֶζ���
                    var fields = new List<DBFField>();
                    foreach (var mapping in fieldMappings)
                    {
                        string dbfFieldName = mapping.Key;
                        string propertyName = mapping.Value;

                        if (!propertyMap.TryGetValue(propertyName, out var property))
                        {
                            _logger.LogWarning($"���� {propertyName} ������ {entityType.Name} �в����ڣ�����������");
                            continue;
                        }

                        NativeDbType fieldType;
                        if (customFieldTypes != null && customFieldTypes.TryGetValue(dbfFieldName, out var customType))
                        {
                            fieldType = customType;
                        }
                        else
                        {
                            try
                            {
                                fieldType = GetDBFFieldType(property.PropertyType);
                            }
                            catch (ArgumentException)
                            {
                                _logger.LogWarning($"���� {propertyName} ������ {property.PropertyType.Name} ��֧�֣�����������");
                                continue;
                            }
                        }

                        // �����������������ʵ����ֶγ���
                        int length = 0;
                        int decimalCount = 0;

                        switch (fieldType)
                        {
                            case NativeDbType.Char:
                                length = 254; // ����ַ�����
                                break;
                            case NativeDbType.Numeric:
                                length = 18;
                                decimalCount = 0;
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
                        }

                        fields.Add(new DBFField(dbfFieldName, fieldType, length, decimalCount));
                    }

                    writer.Fields = fields.ToArray();

                    // д���¼
                    foreach (var entity in entities)
                    {
                        var record = new object[fields.Count];
                        int fieldIndex = 0;

                        foreach (var mapping in fieldMappings)
                        {
                            if (fieldIndex >= fields.Count) break;

                            string propertyName = mapping.Value;
                            if (!propertyMap.TryGetValue(propertyName, out var property))
                            {
                                record[fieldIndex++] = null;
                                continue;
                            }

                            var value = property.GetValue(entity);
                            // �Կɿ����ͽ��д���
                            if (value != null && property.PropertyType.IsGenericType &&
                                property.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
                            {
                                value = Convert.ChangeType(value, Nullable.GetUnderlyingType(property.PropertyType));
                            }

                            record[fieldIndex++] = value;
                        }

                        writer.WriteRecord(record);
                    }
                }
                _logger.LogDebug("�ɹ���ʵ�弯��д������");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "д�� DBF ��ʱ��������");
                throw;
            }
        }

        /// <summary>
        /// ��ʵ�弯��д�� DBF �ļ���
        /// </summary>
        /// <typeparam name="T">ʵ������</typeparam>
        /// <param name="filePath">DBF �ļ�·��</param>
        /// <param name="entities">ʵ�弯��</param>
        /// <param name="fieldMappings">�ֶ�ӳ���ֵ䣬��ΪDBF�ֶ�����ֵΪʵ��������</param>
        /// <param name="customFieldTypes">��ѡ���Զ����ֶ������ֵ䣬��ΪDBF�ֶ�����ֵΪDBF�ֶ�����</param>
        /// <param name="encoding">�ַ����룬Ĭ��ΪUTF-8</param>
        public void WriteEntitiesToDBF<T>(string filePath, IEnumerable<T> entities,
            Dictionary<string, string> fieldMappings,
            Dictionary<string, NativeDbType> customFieldTypes = null,
            System.Text.Encoding encoding = null) where T : class
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                _logger.LogDebug("�ļ�·��Ϊ�ջ�������հ��ַ���");
                throw new ArgumentNullException(nameof(filePath));
            }

            try
            {
                using (var stream = File.Open(filePath, FileMode.Create, FileAccess.Write))
                {
                    WriteEntitiesToStream(stream, entities, fieldMappings, customFieldTypes, encoding);
                }
                _logger.LogDebug($"�ɹ���ʵ�弯��д���ļ� {filePath}��");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, $"д�� DBF �ļ� {filePath} ʱ��������");
                throw;
            }
        }

    }

    public class DotNetDBFManagerTest
    {
        public static void Test()
        {
            var logger = NullLogger<DotNetDBFManager>.Instance;
            var dbfManager = new DotNetDBFManager(logger);
            var filePath = "c:\\test.dbf";

            // �������� DataTable
            var dataTable = new DataTable();
            dataTable.Columns.Add("Field1", typeof(string));
            dataTable.Columns.Add("Field2", typeof(int));
            dataTable.Columns.Add("Field3", typeof(DateTime));

            var row = dataTable.NewRow();
            row["Field1"] = "Test";
            row["Field2"] = 123;
            row["Field3"] = DateTime.Now;
            dataTable.Rows.Add(row);

            // ʹ�� WriteDataTableToDBF д���ļ�
            dbfManager.WriteDataTableToDBF(filePath, dataTable);

            // ʹ�� ReadDBFToDataTable ��������
            var result = dbfManager.ReadDBFToDataTable(filePath);

            // ������
            Console.WriteLine("��ȡ�� DataTable:");
            foreach (DataColumn column in result.Columns)
            {
                Console.Write(column.ColumnName + "\t");
            }
            Console.WriteLine();

            foreach (DataRow dataRow in result.Rows)
            {
                foreach (var item in dataRow.ItemArray)
                {
                    Console.Write(item + "\t");
                }
                Console.WriteLine();
            }

            // ɾ�������ļ�
            File.Delete(filePath);
        }
    }
}



