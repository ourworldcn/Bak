#nullable enable
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Concurrent;
using System.Data;
using System.Runtime.InteropServices;
using System.Threading;

namespace OW.Data
{
    /// <summary>
    /// SqlChangeToken �࣬ʵ�� IChangeToken �ӿڣ����ڼ��� SQL ���ݿ�仯��
    /// ���� SqlDependency ʵ�֣����� Windows ƽ̨����ʱ���á�
    /// </summary>
    public class SqlChangeToken : IChangeToken, IDisposable
    {
        #region ��̬�ֶκ͹��캯��
        private static readonly bool _isWindowsPlatform; // �Ƿ�Ϊ Windows ƽ̨

        // ��̬������Դ�����ڹ������ӳغ�ʹ�ü���
        private static readonly ConcurrentDictionary<string, int> _connectionStringUsageCount = new();
        private static readonly ConcurrentDictionary<string, SqlConnection> _connections = new();
        private static object _locker => _connectionStringUsageCount;

        /// <summary>
        /// ��̬���캯�����������ʱƽ̨
        /// </summary>
        static SqlChangeToken()
        {
#if NET6_0_OR_GREATER
            _isWindowsPlatform = OperatingSystem.IsWindows(); // .NET 6+ �Ƽ���ʽ
#else
            _isWindowsPlatform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows); // ���ݾɰ汾
#endif
            if (!_isWindowsPlatform)
            {
                throw new PlatformNotSupportedException(
                    "SqlChangeToken ��Ҫ�� Windows ƽ̨�����С�SqlDependency �� Linux/macOS ƽ̨����֧�֡�" +
                    "�뿼��ʹ����������������綨ʱ��ѯ��Redis ����/���Ļ� CDC + Event Hub��");
            }
        }
        #endregion

        #region ˽���ֶ�
        private readonly string _sqlQuery; // SQL ��ѯ���
        private readonly string _connectionString; // ���ݿ������ַ���
        private SqlDependency? _dependency; // SQL ��������
        private volatile bool _hasChanged = false; // �Ƿ��Ѽ�⵽���
        private readonly ConcurrentBag<(Action<object?> callback, object? state)> _callbacks = new(); // �ص������б�
        private bool _disposed = false; // �Ƿ����ͷ���Դ
        #endregion

        #region ���캯��
        /// <summary>
        /// ���캯������ʼ�� SQL ��ѯ�����ݿ������ַ���
        /// </summary>
        /// <param name="sqlQuery">Ҫ������ SQL ��ѯ�ַ���</param>
        /// <param name="connectionString">���ݿ������ַ���</param>
        public SqlChangeToken(string sqlQuery, string connectionString)
        {
            _sqlQuery = sqlQuery ?? throw new ArgumentNullException(nameof(sqlQuery));
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }
        #endregion

        #region IChangeToken ʵ��
        /// <summary>
        /// ��ȡһ��ֵ����ֵָʾ�Ƿ��Ѽ�⵽���
        /// </summary>
        public bool HasChanged => _hasChanged;

        /// <summary>
        /// ��ȡһ��ֵ����ֵָʾ�Ƿ�Ӧ����ʹ�ûص�����
        /// </summary>
        public bool ActiveChangeCallbacks => true; // ֧�������ص�

        /// <summary>
        /// ע�����ص�����
        /// </summary>
        /// <param name="callback">�ص�����</param>
        /// <param name="state">�ص�������״̬����</param>
        /// <returns>һ�� IDisposable ��������ȡ���ص�</returns>
        /// <exception cref="ObjectDisposedException">���������ͷ�ʱ�׳�</exception>
        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SqlChangeToken));

            if (_hasChanged)
            {
                // ����Ѿ������仯������ִ�лص�
                callback(state);
                return new NoOpDisposable();
            }

            _callbacks.Add((callback, state));
            return new CallbackDisposable(this, callback, state);
        }
        #endregion

        #region ������̬����
        /// <summary>
        /// ���� SQL ��������
        /// </summary>
        /// <param name="connectionString">���ݿ������ַ���</param>
        /// <exception cref="InvalidOperationException">������ʧ��ʱ�׳�</exception>
        public static void Start(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
                throw new ArgumentNullException(nameof(connectionString));

            try
            {
                lock (_locker)
                {
                    // ����ǵ�һ��ʹ�ø������ַ����������� SqlDependency
                    if (_connectionStringUsageCount.AddOrUpdate(connectionString, 1, (_, count) => count + 1) == 1)
                    {
                        var builder = new SqlConnectionStringBuilder(connectionString);
                        var databaseName = builder.InitialCatalog;
                        if (string.IsNullOrEmpty(databaseName))
                        {
                            databaseName = builder["Database"]?.ToString();
                        }

                        if (string.IsNullOrEmpty(databaseName))
                        {
                            throw new InvalidOperationException("�޷��������ַ�������ȡ���ݿ�����");
                        }

                        EnsureDatabaseEnabledBroker(connectionString, databaseName);
                        SqlDependency.Start(connectionString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"���� SqlDependency ʧ��: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// ֹͣ SQL �����������ͷ���Դ
        /// </summary>
        /// <param name="connectionString">���ݿ������ַ���</param>
        public static void Stop(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString)) return;

            lock (_locker)
            {
                // ���������ַ���ʹ�ü��������Ϊ0��ֹͣ SqlDependency
                if (_connectionStringUsageCount.TryGetValue(connectionString, out var count) && count > 0)
                {
                    var newCount = _connectionStringUsageCount.AddOrUpdate(connectionString, 0, (_, c) => Math.Max(0, c - 1));
                    if (newCount == 0)
                    {
                        try
                        {
                            SqlDependency.Stop(connectionString);
                            if (_connections.TryRemove(connectionString, out var connection))
                            {
                                connection.Close();
                                connection.Dispose();
                            }
                        }
                        catch
                        {
                            // ����ֹͣʱ���쳣
                        }
                    }
                }
            }
        }

        /// <summary>
        /// ȷ�����ݿ������� Service Broker
        /// </summary>
        /// <param name="connectionString">���ݿ������ַ���</param>
        /// <param name="databaseName">���ݿ�����</param>
        /// <exception cref="InvalidOperationException">������ Service Broker ʧ��ʱ�׳�</exception>
        public static void EnsureDatabaseEnabledBroker(string connectionString, string databaseName)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentNullException(nameof(connectionString));

            if (string.IsNullOrWhiteSpace(databaseName))
                throw new ArgumentNullException(nameof(databaseName));

            const string checkBrokerSql = "SELECT is_broker_enabled FROM sys.databases WHERE name = @DatabaseName";
            const string enableBrokerSql = "ALTER DATABASE [{0}] SET ENABLE_BROKER WITH ROLLBACK IMMEDIATE";

            try
            {
                using var connection = new SqlConnection(connectionString);
                connection.Open();

                // ����Ƿ������� Service Broker
                using (var checkCommand = new SqlCommand(checkBrokerSql, connection))
                {
                    checkCommand.Parameters.AddWithValue("@DatabaseName", databaseName);
                    var result = checkCommand.ExecuteScalar();

                    if (result is bool isBrokerEnabled && isBrokerEnabled)
                    {
                        return; // Service Broker �����ã��������
                    }
                }

                // ���� Service Broker
                var enableCommand = string.Format(enableBrokerSql, databaseName);
                using var command = new SqlCommand(enableCommand, connection);
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"�������ݿ� '{databaseName}' �� Service Broker ʧ��: {ex.Message}", ex);
            }
        }
        #endregion

        #region ʵ������
        /// <summary>
        /// �������� SQL ���ݿ�仯
        /// </summary>
        /// <exception cref="ObjectDisposedException">���������ͷ�ʱ�׳�</exception>
        /// <exception cref="InvalidOperationException">������ʧ��ʱ�׳�</exception>
        public void StartInstance()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SqlChangeToken));

            try
            {
                // ������̬ SqlDependency
                Start(_connectionString);

                var connection = _connections.GetOrAdd(_connectionString, connStr => new SqlConnection(connStr));

                // ȷ�������Ǵ򿪵�
                if (connection.State == ConnectionState.Closed)
                {
                    lock (connection)
                    {
                        if (connection.State == ConnectionState.Closed)
                            connection.Open();
                    }
                }

                using var command = new SqlCommand(_sqlQuery, connection);
                _dependency = new SqlDependency(command);
                _dependency.OnChange += OnDependencyChange;

                lock (connection)
                {
                    using var reader = command.ExecuteReader();
                    // ִ�в�ѯ�Խ���������ϵ��������Ҫ��ȡ����
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"���� SqlChangeToken ʵ��ʧ��: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// ֹͣʵ���������ͷ���Դ
        /// </summary>
        public void StopInstance()
        {
            if (_disposed) return;

            if (_dependency != null)
            {
                _dependency.OnChange -= OnDependencyChange;
                _dependency = null;
            }

            // ֹͣ��̬ SqlDependency
            Stop(_connectionString);
        }
        #endregion

        #region ˽�з���
        /// <summary>
        /// SqlDependency �仯�¼�������
        /// </summary>
        private void OnDependencyChange(object? sender, SqlNotificationEventArgs e)
        {
            if (_disposed) return;

            if (e.Type == SqlNotificationType.Change && e.Source == SqlNotificationSource.Data)
            {
                // ���Ϊ�ѱ仯
                _hasChanged = true;

                // ִ������ע��Ļص�����
                foreach (var (callback, state) in _callbacks)
                {
                    try
                    {
                        callback(state);
                    }
                    catch
                    {
                        // ���Իص�ִ��ʱ���쳣
                    }
                }

                // �Զ�����ע�ᣨ�����Ҫ����������
                try
                {
                    if (!_disposed)
                    {
                        // ����״̬����������
                        _hasChanged = false;
                        StartInstance();
                    }
                }
                catch
                {
                    // ��������ע��ʱ���쳣
                }
            }
            else if (e.Type == SqlNotificationType.Subscribe && e.Info == SqlNotificationInfo.Error)
            {
                // SqlDependency ����ʧ�ܣ����Ϊ�ѱ仯��֪ͨ������
                _hasChanged = true;
            }
        }

        /// <summary>
        /// �Ƴ�ָ���Ļص�����
        /// </summary>
        private void RemoveCallback(Action<object?> callback, object? state)
        {
            // ConcurrentBag ��֧��ֱ���Ƴ�������Ϊ�˼�ʵ�֣�ʵ���ϲ�ִ���Ƴ�����
            // ��ʵ��ʹ���У������Ҫ��ȷ�Ļص���������ʹ���������ݽṹ
        }
        #endregion

        #region IDisposable ʵ��
        /// <summary>
        /// �ͷ���Դ
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            StopInstance();
            _dependency = null;
            _disposed = true;
        }
        #endregion

        #region Ƕ������
        /// <summary>
        /// �޲����� IDisposable ʵ��
        /// </summary>
        private class NoOpDisposable : IDisposable
        {
            public void Dispose() { }
        }

        /// <summary>
        /// �ص������� IDisposable ʵ��
        /// </summary>
        private class CallbackDisposable : IDisposable
        {
            private readonly SqlChangeToken _token;
            private readonly Action<object?> _callback;
            private readonly object? _state;

            public CallbackDisposable(SqlChangeToken token, Action<object?> callback, object? state)
            {
                _token = token;
                _callback = callback;
                _state = state;
            }

            public void Dispose()
            {
                _token.RemoveCallback(_callback, _state);
            }
        }
        #endregion
    }
}
#nullable restore