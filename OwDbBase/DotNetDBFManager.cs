/*
 * �ļ�����DotNetDBFManager.cs
 * ���ߣ�OW
 * �������ڣ�2025��5��8��
 * �޸����ڣ�2025��5��8��
 * ���������ļ����� DotNetDBFManager ���ʵ�֣����ڲ��� DBF �ļ���
 */

using System;
using System.Data;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DotNetDBF;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.ObjectPool;

namespace OW.Data
{
    /// <summary>
    /// DotNetDBFManager �࣬�ṩ��Ч�� DBF �ļ��������ܡ�
    /// </summary>
    public class DotNetDBFManager
    {
        #region �ֶκ�����
        private readonly ILogger<DotNetDBFManager> _logger;
        private readonly ObjectPool<DataTable> _dataTablePool; // ʹ�ö�����������ڴ����
        private static readonly System.Text.Encoding _defaultEncoding = System.Text.Encoding.GetEncoding("GB2312"); // Ĭ�ϱ���
        private const int _maxBatchSize = 5000; // �������С�����������ڴ�����
        #endregion

        #region ���캯��
        /// <summary>
        /// ���캯������ʼ����־��¼���Ͷ���ء�
        /// </summary>
        /// <param name="logger">��־��¼��</param>
        public DotNetDBFManager(ILogger<DotNetDBFManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger)); // ��֤�����ǿ�
            _dataTablePool = new DefaultObjectPool<DataTable>(new DataTablePoolPolicy()); // ��ʼ�������
        }

        /// <summary>
        /// ���캯����ʹ�ÿ���־��¼����
        /// </summary>
        public DotNetDBFManager() : this(NullLogger<DotNetDBFManager>.Instance) { } // Ĭ��ʹ�ÿ���־��¼��
        #endregion

        #region ��ȡ����
        /// <summary>
        /// �� DBF �ļ���ȡ���ݵ� DataTable��
        /// </summary>
        /// <param name="filePath">DBF �ļ�·��</param>
        /// <param name="encoding">�ַ����룬Ĭ��ΪGB2312</param>
        /// <returns>���� DBF ���ݵ� DataTable</returns>
        public DataTable ReadDBFToDataTable(string filePath, System.Text.Encoding encoding = null)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException(nameof(filePath)); // ������֤
            var dataTable = _dataTablePool.Get(); // �Ӷ���ػ�ȡDataTable
            dataTable.Clear(); // ȷ��DataTable�Ǹɾ���

            try
            {
                using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read); // ���ļ���
                using var reader = new DBFReader(stream); // ����DBF��ȡ��
                reader.CharEncoding = encoding ?? _defaultEncoding; // �����ַ�����
                var fields = reader.Fields; // ��ȡ�ֶζ���

                // ���� DataTable ��
                foreach (var field in fields)
                {
                    dataTable.Columns.Add(field.Name, DotNetDbfUtil.GetFieldTypeFromDBF(field.DataType)); // �����
                }

                // ��ȡ����
                object[] record;
                while ((record = reader.NextRecord()) != null)
                {
                    var dataRow = dataTable.NewRow(); // ��������
                    for (int i = 0; i < fields.Length; i++)
                    {
                        dataRow[fields[i].Name] = record[i] ?? DBNull.Value; // �����ֶ�ֵ�������ֵ
                    }
                    dataTable.Rows.Add(dataRow); // �����
                }
                _logger.LogDebug($"�ɹ����ļ� {filePath} ��ȡ {dataTable.Rows.Count} ����¼"); // ��¼�ɹ���־
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"��ȡ DBF �ļ� {filePath} ʱ��������"); // ��¼������־
                _dataTablePool.Return(dataTable); // �����쳣ʱ����DataTable�黹�����
                throw; // �����׳��쳣
            }

            return dataTable; // ��������DataTable
        }

        /// <summary>
        /// �첽�� DBF �ļ���ȡ���ݵ� DataTable��
        /// </summary>
        /// <param name="filePath">DBF �ļ�·��</param>
        /// <param name="encoding">�ַ����룬Ĭ��ΪGB2312</param>
        /// <returns>���� DBF ���ݵ� DataTable</returns>
        public async Task<DataTable> ReadDBFToDataTableAsync(string filePath, System.Text.Encoding encoding = null)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException(nameof(filePath)); // ������֤

            return await Task.Run(() => ReadDBFToDataTable(filePath, encoding)); // ���̳߳����첽ִ��
        }

        #endregion

        #region д�뷽��

        #endregion

        #region ��������
        /// <summary>
        /// �ͷ� DataTable ��Դ�����䷵�ض���ء�
        /// </summary>
        /// <param name="dataTable">Ҫ�ͷŵ� DataTable</param>
        public void ReleaseDataTable(DataTable dataTable)
        {
            if (dataTable != null)
            {
                dataTable.Clear(); // �������
                _dataTablePool.Return(dataTable); // ���س�
            }
        }

        /// <summary>
        /// ��ȡ DBF �ļ����ֶνṹ��
        /// </summary>
        /// <param name="filePath">DBF �ļ�·��</param>
        /// <param name="encoding">�ַ����룬Ĭ��ΪGB2312</param>
        /// <returns>�ֶζ�������</returns>
        public DBFField[] GetDBFStructure(string filePath, System.Text.Encoding encoding = null)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException(nameof(filePath)); // ������֤

            try
            {
                using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read); // ���ļ���
                using var reader = new DBFReader(stream) { CharEncoding = encoding ?? _defaultEncoding }; // ����DBF��ȡ��
                return reader.Fields; // �����ֶζ���
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"��ȡ DBF �ļ� {filePath} �ṹʱ��������"); // ��¼������־
                throw; // �����׳��쳣
            }
        }
        #endregion
    }

    #region �ڲ�������
    /// <summary>
    /// DataTable ����ز����࣬���ڹ��� DataTable ʵ���Ĵ��������á�
    /// </summary>
    internal class DataTablePoolPolicy : IPooledObjectPolicy<DataTable>
    {
        /// <summary>
        /// �����µ� DataTable ʵ����
        /// </summary>
        /// <returns>�µ� DataTable ʵ��</returns>
        public DataTable Create() => new(); // ʹ�� C# 10 ��Ŀ������ new ���ʽ

        /// <summary>
        /// ���� DataTable �Ա����á�
        /// </summary>
        /// <param name="obj">Ҫ���õ� DataTable</param>
        /// <returns>�Ƿ��������</returns>
        public bool Return(DataTable obj)
        {
            try
            {
                obj.Clear(); // ���������
                obj.Columns.Clear(); // ���������
                return true; // ��������
            }
            catch
            {
                return false; // ����ʧ�ܣ���������
            }
        }
    }
    #endregion
}
