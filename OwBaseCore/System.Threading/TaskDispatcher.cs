using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;


namespace System.Threading
{
    /// <summary>
    /// �����������ѡ��
    /// </summary>
    public class TaskDispatcherOptions : IOptions<TaskDispatcherOptions>
    {
        /// <summary>
        /// �������������Ĭ�ϲ�����
        /// </summary>
        public int MaxQueueSize { get; set; }

        /// <summary>
        /// ִ���̼߳���������룩
        /// </summary>
        public int CheckIntervalMs { get; set; } = 200;

        /// <summary>
        /// �����ȴ���ʱ�����룩
        /// </summary>
        /// <value>Ĭ��0�����ȴ�</value>
        public int LockTimeoutMs { get; set; } = 0;

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        public TaskDispatcherOptions Value => this;
    }

    /// <summary>
    /// ����������������ִ��һϵ������֧����ͬ��������ϲ�
    /// </summary>
    [OwAutoInjection(ServiceLifetime.Singleton)]
    public class TaskDispatcher : IDisposable
    {
        private readonly ILogger<TaskDispatcher> _logger;
        private readonly IHostApplicationLifetime _applicationLifetime;
        private readonly ConcurrentDictionary<object, TaskItem> _taskQueue = new ConcurrentDictionary<object, TaskItem>();
        private readonly Thread _executionThread;
        private readonly TaskDispatcherOptions _options;
        private bool _disposed;

        /// <summary>
        /// ������������캯��
        /// </summary>
        /// <param name="logger">��־��¼��</param>
        /// <param name="applicationLifetime">Ӧ�ó�����������</param>
        /// <param name="options">����ѡ��</param>
        public TaskDispatcher(ILogger<TaskDispatcher> logger, IHostApplicationLifetime applicationLifetime,
            IOptions<TaskDispatcherOptions> options)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _applicationLifetime = applicationLifetime ?? throw new ArgumentNullException(nameof(applicationLifetime));
            _options = options?.Value ?? new TaskDispatcherOptions();

            // ���������ȼ�ִ���߳�
            _executionThread = new Thread(ExecuteLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "TaskDispatcher_ExecutionThread"
            };
            _executionThread.Start();

            _logger.LogInformation("TaskDispatcher �ѳ�ʼ��������������: {MaxQueueSize}",
                _options.MaxQueueSize > 0 ? _options.MaxQueueSize.ToString() : "������");
        }

        /// <summary>
        /// �ύ���񵽶����У���ͬtypeId������ᱻ�ϲ�
        /// </summary>
        /// <param name="typeId">�������ͱ�ʶ</param>
        /// <param name="executeFunc">ִ������ĺ���</param>
        /// <param name="parameter">�������</param>
        /// <param name="needLock">�Ƿ���Ҫ����</param>
        /// <returns>�Ƿ�ɹ���ӵ�����</returns>
        public bool Enqueue(object typeId, Func<object, bool> executeFunc, object parameter = null, bool needLock = true)
        {
            if (typeId == null) throw new ArgumentNullException(nameof(typeId));
            if (executeFunc == null) throw new ArgumentNullException(nameof(executeFunc));
            if (_disposed) return false;

            // ��������������
            if (_options.MaxQueueSize > 0 && _taskQueue.Count >= _options.MaxQueueSize)
            {
                _logger.LogWarning("��������Ѵﵽ������� {MaxSize}���޷����������: {TypeId}",
                    _options.MaxQueueSize, typeId);
                return false;
            }

            var taskItem = new TaskItem
            {
                TypeId = typeId,
                ExecuteFunc = executeFunc,
                Parameter = parameter,
                NeedLock = needLock,
                EnqueueTime = DateTime.UtcNow
            };

            // ��ӻ��������
            _taskQueue[typeId] = taskItem;

            // ������Ҫͨ���ź�ִ֪ͨ���߳�

            _logger.LogDebug("��������ӵ�����: {TypeId}", typeId);
            return true;
        }

        /// <summary>
        /// �ֶ���������е��������񣬻���9���ڲ�������ֱ���ֵ�Ϊ��
        /// </summary>
        /// <returns>�Ƿ����������Ѵ������</returns>
        public bool ProcessAll()
        {
            if (_disposed) return false;

            _logger.LogDebug("��ʼ�ֶ���������е���������");

            // ���ó�ʱʱ��
            DateTime startTime = DateTime.UtcNow;
            TimeSpan timeout = TimeSpan.FromSeconds(9);

            // �������Դ�������ֱ������Ϊ�ջ�ʱ
            while (!_taskQueue.IsEmpty && DateTime.UtcNow - startTime < timeout && !_taskQueue.IsEmpty)
            {
                // ��ȡ��ǰ��������
                var tasksToProcess = _taskQueue.Keys.ToList();
                // ����ǰ��������
                foreach (var taskId in tasksToProcess)
                {
                    ProcessTask(taskId);
                }

                // ϵͳ��ֹʱ��Ӧ������ɴ���
                if (_taskQueue.IsEmpty) break;
                Thread.Yield();
            }

            bool allTasksProcessed = _taskQueue.IsEmpty;

            if (allTasksProcessed)
            {
                _logger.LogDebug("���������Ѵ������");
            }
            else if (DateTime.UtcNow - startTime >= timeout)
            {
                _logger.LogWarning("������ʱ������ {RemainingTasks} ������δ����", _taskQueue.Count);
            }

            return allTasksProcessed;
        }

        /// <summary>
        /// ��ȡ��ǰ��������������
        /// </summary>
        public int Count => _taskQueue.Count;

        private void ExecuteLoop()
        {
            _logger.LogInformation("����ִ���߳�������");

            try
            {
                while (!_applicationLifetime.ApplicationStopped.IsCancellationRequested)
                {
                    // ��������е���������
                    if (!_taskQueue.IsEmpty)
                    {
                        // ���Ƶ�ǰ�����б��Ա������ʱ���޸�����
                        var currentTasks = _taskQueue.Keys.ToList();
                        foreach (var taskId in currentTasks)
                        {
                            // ÿ�δ�������ǰ���Ӧ���Ƿ�Ҫֹͣ
                            if (_applicationLifetime.ApplicationStopped.IsCancellationRequested)
                                break;

                            ProcessTask(taskId);
                        }
                    }

                    // ������ӦӦ��ֹͣ
                    if (_applicationLifetime.ApplicationStopped.WaitHandle.WaitOne(_options.CheckIntervalMs))
                        break;
                }

                // Ӧ��ֹͣ���Դ���ʣ������
                _logger.LogInformation("Ӧ�ó�������ֹͣ������ʣ������");
                ProcessAll();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "����ִ���̷߳����쳣");
            }

            _logger.LogInformation("����ִ���߳���ֹͣ");
        }

        private void ProcessTask(object taskId)
        {
            // ֱ�ӳ��ԴӶ������Ƴ�����
            if (!_taskQueue.TryRemove(taskId, out var taskItem))
                return;

            try
            {
                // �����Ҫ���������Ի�ȡ��
                var isLock = taskItem.NeedLock;
                if (isLock)
                {
                    if (!SingletonLocker.TryEnter(taskItem.TypeId, TimeSpan.FromMilliseconds(_options.LockTimeoutMs)))
                    {
                        _logger.LogDebug("�޷��������񣬽����¼�������Ժ�����: {TypeId}", taskItem.TypeId);
                        // ����ʧ�ܣ����������¼�����У�������������Ѵ�����ͬ�����������
                        _taskQueue.TryAdd(taskItem.TypeId, taskItem);
                        return;
                    }
                }

                try
                {
                    // ִ������ʹ�������������������ID
                    bool result = taskItem.ExecuteFunc(taskItem.Parameter ?? taskItem.TypeId);
                    if (result)
                    {
                        _logger.LogDebug("����ִ�гɹ�: {TypeId}", taskItem.TypeId);
                    }
                    else
                    {
                        _logger.LogWarning("����ִ�з���ʧ��: {TypeId}", taskItem.TypeId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "����ִ��ʱ�����쳣: {TypeId}", taskItem.TypeId);
                }
                finally
                {
                    // �ͷ���
                    if (isLock)
                    {
                        SingletonLocker.Exit(taskItem.TypeId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "������������з����쳣: {TypeId}", taskItem.TypeId);
                // ���½�������ӵ�����
                _taskQueue.TryAdd(taskItem.TypeId, taskItem);
            }
        }

        /// <summary>
        /// ���ָ�����͵������Ƿ��ڶ�����
        /// </summary>
        /// <param name="typeId">��������ID</param>
        /// <returns>������ڷ���true�����򷵻�false</returns>
        public bool Contains(object typeId)
        {
            return _taskQueue.ContainsKey(typeId);
        }

        /// <summary>
        /// ȷ��ָ�����������ڶ�������ɻ򲻴���
        /// </summary>
        /// <param name="typeId">��������ID</param>
        /// <param name="timeout">�ȴ���ʱʱ��</param>
        /// <returns>���������ɻ򲻴��ڷ���true����ʱ����false</returns>
        public bool EnsureCompleteIdempotent(object typeId, TimeSpan timeout)
        {
            if (typeId == null) throw new ArgumentNullException(nameof(typeId));

            if (!Contains(typeId))
                return true;

            DateTime startTime = DateTime.UtcNow;
            while (DateTime.UtcNow - startTime < timeout)
            {
                if (!Contains(typeId))
                    return true;

                Thread.Sleep(10);
            }

            return !Contains(typeId);
        }

        /// <summary>
        /// �ͷ���Դ
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            if (_executionThread.IsAlive && !_executionThread.Join(10000))
            {
                _logger.LogWarning("ִ���߳�δ���ڳ�ʱʱ���ڽ���");
            }

            _logger.LogInformation("TaskDispatcher ���ͷ���Դ");
        }

        /// <summary>
        /// ��������ݵ����������ͬ�������ڶ�������ʲôҲ����
        /// </summary>
        /// <param name="typeId">�������ͱ�ʶ</param>
        /// <param name="executeFunc">ִ������ĺ���</param>
        /// <param name="parameter">�������</param>
        /// <param name="needLock">�Ƿ���Ҫ����</param>
        /// <returns>����ɹ���ӻ������Ѵ��ڷ���true�������������false</returns>
        public bool TryAddIdempotent(object typeId, Func<object, bool> executeFunc, object parameter = null, bool needLock = true)
        {
            if (_taskQueue.ContainsKey(typeId))
                return true;

            return Enqueue(typeId, executeFunc, parameter, needLock);
        }

        /// <summary>
        /// ��ʾ�����е�������
        /// </summary>
        private class TaskItem
        {
            /// <summary>�������ͱ�ʶ</summary>
            public object TypeId { get; set; }

            /// <summary>ִ�к���</summary>
            public Func<object, bool> ExecuteFunc { get; set; }

            /// <summary>�������</summary>
            public object Parameter { get; set; }

            /// <summary>�Ƿ���Ҫ����</summary>
            public bool NeedLock { get; set; }

            /// <summary>���ʱ��</summary>
            public DateTime EnqueueTime { get; set; }
        }
    }

    /// <summary>��չ������</summary>
    public static class TaskDispatcherExtensions
    {
        /// <summary>��������ӵ�����</summary>
        public static IServiceCollection AddTaskDispatcher(this IServiceCollection services)
        {
            return services.AddSingleton<TaskDispatcher>();
        }
    }
}
