/*
 * �ļ�����SqlDependencyManager.cs
 * ���ߣ�OW
 * �������ڣ�2023��10��25��
 * ���������ļ����� SqlDependencyManager �����ʵ�֣����ڼ��һ�� IQueryable<T> �Ľ�����仯��
 * ��ǰ�ļ����ݸ�����
 * - SqlDependencyManager�����ڼ��һ�� IQueryable<T> �Ľ�����仯��
 */

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OW.Data
{
    /// <summary>
    /// SqlDependency �����������ڼ��һ�� SQL �ַ����Ľ�����仯��
    /// </summary>
    public class SqlDependencyManager : BackgroundService
    {
        #region ˽���ֶ�
        public const string EnableBrokerFormatString = "USE master; \r\n IF EXISTS (SELECT is_broker_enabled FROM sys.databases WHERE name = '{0}' AND is_broker_enabled = 0)\r\nBEGIN\r\n    ALTER DATABASE [{0}] SET ENABLE_BROKER;\r\nEND\r\n";
        private readonly ILogger<SqlDependencyManager> _Logger;
        private readonly ConcurrentDictionary<string, int> _ConnectionStringUsageCount = new();
        private readonly ConcurrentDictionary<string, SqlConnection> _Connections = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _SqlCancellationTokenSources = new();
        public object Locker => _ConnectionStringUsageCount;
        #endregion ˽���ֶ�

        #region ��̬����
        /// <summary>
        /// ���� SQL Broker��
        /// </summary>
        /// <param name="connectionString">���ݿ������ַ�����</param>
        /// <param name="databaseName">���ݿ����ơ�</param>
        public static void EnableSqlBroker(string connectionString, string databaseName)
        {
            using var connection = new SqlConnection(connectionString);
            connection.Open();
            var commandText = string.Format(EnableBrokerFormatString, databaseName);
            using var command = new SqlCommand(commandText, connection);
            command.ExecuteNonQuery();
        }

        static HashSet<string> _EnabledBrokerDatabases = new();
        /// <summary>
        /// ȷ�����ݿ������� Broker��
        /// </summary>
        /// <param name="connectionString">���ݿ������ַ�����</param>
        /// <param name="databaseName">���ݿ����ơ�</param>
        public static void EnsureDatabaseEnabledBroker(string connectionString, string databaseName)
        {
            if (_EnabledBrokerDatabases.Add(databaseName))
                EnableSqlBroker(connectionString, databaseName);
        }
        #endregion ��̬����

        #region ���캯��
        /// <summary>
        /// ���캯������ʼ����־��¼����
        /// </summary>
        /// <param name="logger">��־��¼����</param>
        public SqlDependencyManager(ILogger<SqlDependencyManager> logger)
        {
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        #endregion ���캯��

        #region ��������
        /// <summary>
        /// ע�� SqlDependency ������ע����������ڽ���������仯ʱ�Զ���ע�ᡣ
        /// </summary>
        /// <param name="sqlQuery">Ҫ���� SQL ��ѯ�ַ�����</param>
        /// <param name="connectionString">Ҫʹ�õ����ݿ������ַ�����</param>
        /// <returns>һ����ȡ�������ڼ�⵽�仯ʱ�رա�</returns>
        public CancellationTokenSource RegisterSqlDependency(string sqlQuery, string connectionString)
        {
            var cts = new CancellationTokenSource();
            lock (Locker)
                if (_ConnectionStringUsageCount.AddOrUpdate(connectionString, 1, (_, count) => count + 1) == 1)
                {
                    var builder = new SqlConnectionStringBuilder(connectionString);
                    EnsureDatabaseEnabledBroker(connectionString, builder.InitialCatalog ?? builder["Database"].ToString());
                    SqlDependency.Start(connectionString);
                }

            var connection = _Connections.GetOrAdd(connectionString, connStr => new SqlConnection(connStr));
            using var command = new SqlCommand(sqlQuery, connection);
            var dependency = new SqlDependency(command);
            dependency.OnChange += (sender, e) => OnDependencyChange(sender, e, cts, connectionString, sqlQuery);

            try
            {
                if (connection.State == ConnectionState.Closed)
                {
                    connection.Open();
                }
                using var reader = command.ExecuteReader();
                // ��ȡ�����Դ��� SqlDependency
                while (reader.Read()) { }
            }
            catch (Exception ex)
            {
                _Logger.LogError(ex, "ִ�� SqlCommand ʱ����");
                connection.Close();
                throw;
            }

            _SqlCancellationTokenSources[sqlQuery] = cts;
            return cts;
        }

        /// <summary>
        /// ָֹͣ�� SQL ��ѯ�����ݿ�������
        /// </summary>
        /// <param name="sqlQuery">Ҫֹͣ������ SQL ��ѯ�ַ�����</param>
        public void StopListening(string sqlQuery)
        {
            if (_SqlCancellationTokenSources.TryRemove(sqlQuery, out var cts))
            {
                cts.Cancel();
                _Logger.LogDebug("��ֹͣ SQL ��ѯ {SqlQuery} �����ݿ�������", sqlQuery);
            }
        }
        #endregion ��������

        #region ˽�з���
        /// <summary>
        /// SqlDependency �仯�¼�����
        /// </summary>
        private void OnDependencyChange(object sender, SqlNotificationEventArgs e, CancellationTokenSource cts, string connectionString, string sqlQuery)
        {
            if (e.Type == SqlNotificationType.Change)
            {
                try
                {
                    cts?.Cancel();
                    RegisterSqlDependency(sqlQuery, connectionString); // �Զ���ע��
                }
                catch (Exception excp)
                {
                    _Logger.LogDebug(excp, "����������仯���Ѵ����ص�����,����������");
                }
            }

            if (_ConnectionStringUsageCount.AddOrUpdate(connectionString, 0, (_, count) => count - 1) == 0)
            {
                SqlDependency.Stop(connectionString);
                if (_Connections.TryRemove(connectionString, out var connection))
                {
                    connection.Close();
                }
            }
        }

        /// <summary>
        /// ֹͣ���ݿ�������
        /// </summary>
        private void StopDatabaseListening()
        {
            foreach (var sqlQuery in _SqlCancellationTokenSources.Keys)
            {
                StopListening(sqlQuery);
            }

            foreach (var connectionString in _ConnectionStringUsageCount.Keys)
            {
                SqlDependency.Stop(connectionString);
                if (_Connections.TryRemove(connectionString, out var connection))
                {
                    connection.Close();
                }
            }

            _Logger.LogDebug("��ֹͣ�������ݿ�������");
        }
        #endregion ˽�з���

        #region BackgroundService ����
        /// <summary>
        /// ִ�к�̨����
        /// </summary>
        /// <param name="stoppingToken">ֹͣ���ơ�</param>
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            stoppingToken.Register(StopDatabaseListening);
            return Task.CompletedTask;
        }
        #endregion BackgroundService ����

        #region �ͷ���Դ
        /// <summary>
        /// �ͷ���Դ��
        /// </summary>
        public override void Dispose()
        {
            StopDatabaseListening();
            base.Dispose();
        }
        #endregion �ͷ���Դ
    }

    /// <summary>
    /// SqlDependencyManager ��չ������
    /// </summary>
    public static class SqlDependencyManagerExtensions
    {
        /// <summary>
        /// ��� SqlDependencyManager ����
        /// </summary>
        /// <param name="services"></param>
        /// <returns></returns>
        public static IServiceCollection AddSqlDependencyManager(this IServiceCollection services)
        {
            services.AddSingleton<SqlDependencyManager>();
            return services;
        }
    }
}





