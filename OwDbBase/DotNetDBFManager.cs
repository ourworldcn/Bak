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
            if (type == typeof(string))
                return NativeDbType.Char;
            if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
                return NativeDbType.Numeric;
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
                return NativeDbType.Float;
            if (type == typeof(DateTime))
                return NativeDbType.Date;
            if (type == typeof(bool))
                return NativeDbType.Logical;

            throw new ArgumentException("��֧�ֵ���������");
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
                NativeDbType.Char => typeof(string),
                NativeDbType.Numeric => typeof(decimal),
                NativeDbType.Float => typeof(double),
                NativeDbType.Date => typeof(DateTime),
                NativeDbType.Logical => typeof(bool),
                _ => throw new ArgumentException("��֧�ֵ���������"),
            };
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



