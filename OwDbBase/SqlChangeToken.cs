/*
 * �ļ�����SqlChangeToken.cs
 * ���ߣ�OW
 * �������ڣ�2023��10��25��
 * ���������ļ����� SqlChangeToken ���ʵ�֣����ڼ�� SQL ���ݿ�仯��
 */

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Concurrent;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace OW.Data
{
    /// <summary>
    /// SqlChangeToken �࣬ʵ�� IChangeToken �ӿڣ����ڼ�� SQL ���ݿ�仯��
    /// </summary>
    public class SqlChangeToken : IChangeToken
    {
        private readonly string _sqlQuery;
        private readonly string _connectionString;
        private readonly ILogger<SqlChangeToken> _logger;
        private SqlDependency _dependency;
        private CancellationTokenSource _cts;
        private static readonly ConcurrentDictionary<string, int> _connectionStringUsageCount = new();
        private static readonly ConcurrentDictionary<string, SqlConnection> _connections = new();
        private static readonly object _locker = new();

        /// <summary>
        /// ���캯������ʼ�� SQL ��ѯ�����ݿ������ַ�����
        /// </summary>
        /// <param name="sqlQuery">Ҫ���� SQL ��ѯ�ַ�����</param>
        /// <param name="connectionString">���ݿ������ַ�����</param>
        /// <param name="logger">��־��¼������ѡ������</param>
        public SqlChangeToken(string sqlQuery, string connectionString, ILogger<SqlChangeToken> logger = null)
        {
            _sqlQuery = sqlQuery ?? throw new ArgumentNullException(nameof(sqlQuery));
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger;
        }

        /// <summary>
        /// ��ʼ��� SQL ���ݿ�仯��
        /// </summary>
        public void Start()
        {
            _cts = new CancellationTokenSource();
            lock (_locker)
            {
                if (_connectionStringUsageCount.AddOrUpdate(_connectionString, 1, (_, count) => count + 1) == 1)
                {
                    var builder = new SqlConnectionStringBuilder(_connectionString);
                    EnsureDatabaseEnabledBroker(_connectionString, builder.InitialCatalog ?? builder["Database"].ToString());
                    SqlDependency.Start(_connectionString);
                }
            }

            var connection = _connections.GetOrAdd(_connectionString, connStr => new SqlConnection(connStr));
            if (connection.State == ConnectionState.Closed)
            {
                lock (connection)
                    connection.Open();
            }
            using var command = new SqlCommand(_sqlQuery, connection);
            _dependency = new SqlDependency(command);
            _dependency.OnChange += OnDependencyChange;

            try
            {
                lock (connection)
                {
                    command.ExecuteReader();
                    //using var reader = command.ExecuteReader();
                    //while (reader.Read()) { }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "ִ�� SqlCommand ʱ����");
                connection.Close();
                throw;
            }
        }

        /// <summary>
        /// ȷ�����ݿ������� Broker��
        /// </summary>
        /// <param name="connectionString">���ݿ������ַ�����</param>
        /// <param name="databaseName">���ݿ����ơ�</param>
        private static void EnsureDatabaseEnabledBroker(string connectionString, string databaseName)
        {
            const string enableBrokerFormatString = "USE master; \r\n IF EXISTS (SELECT is_broker_enabled FROM sys.databases WHERE name = '{0}' AND is_broker_enabled = 0)\r\nBEGIN\r\n    ALTER DATABASE [{0}] SET ENABLE_BROKER;\r\nEND\r\n";
            const string enableBroker = "ALTER DATABASE {0} SET NEW_BROKER WITH ROLLBACK IMMEDIATE;";
            using var connection = new SqlConnection(connectionString);
            connection.Open();
            var commandText = string.Format(enableBrokerFormatString, databaseName);
            using var command = new SqlCommand(commandText, connection);
            command.ExecuteNonQuery();

            commandText = string.Format(enableBroker, databaseName);
            using var command2 = new SqlCommand(commandText, connection);
            command2.ExecuteNonQuery();

            commandText = string.Format("GRANT SUBSCRIBE QUERY NOTIFICATIONS TO {0};", "zc");
            using var command3 = new SqlCommand(commandText, connection);
            //command3.ExecuteNonQuery();
        }

        /// <summary>
        /// SqlDependency �仯�¼�����
        /// </summary>
        private void OnDependencyChange(object sender, SqlNotificationEventArgs e)
        {
            if (e.Type == SqlNotificationType.Change)
            {
                try
                {
                    _cts?.Cancel();
                    _logger?.LogDebug("����������仯���Ѵ����ص�������");
                    Start(); // �Զ���ע��
                }
                catch (Exception excp)
                {
                    _logger?.LogDebug(excp, "����������仯���Ѵ����ص�����,����������");
                }
            }

            lock (_locker)
            {
                if (_connectionStringUsageCount.AddOrUpdate(_connectionString, 0, (_, count) => count - 1) == 0)
                {
                    SqlDependency.Stop(_connectionString);
                    if (_connections.TryRemove(_connectionString, out var connection))
                    {
                        connection.Close();
                    }
                }
            }
        }

        #region IChangeToken ʵ��
        /// <summary>
        /// ��ȡһ��ֵ����ֵָʾ�Ƿ��Ѽ�⵽���ġ�
        /// </summary>
        public bool HasChanged => _cts?.IsCancellationRequested ?? false;

        /// <summary>
        /// ��ȡһ��ֵ����ֵָʾ�Ƿ�Ӧ����ʹ�ûص���
        /// </summary>
        public bool ActiveChangeCallbacks => true;

        /// <summary>
        /// ע����Ļص���
        /// </summary>
        /// <param name="callback">�ص�������</param>
        /// <param name="state">�ص�������״̬����</param>
        /// <returns>һ�� IDisposable ��������ȡ���ص���</returns>
        public IDisposable RegisterChangeCallback(Action<object> callback, object state)
        {
            return _cts.Token.Register(callback, state);
        }
        #endregion
    }
}

