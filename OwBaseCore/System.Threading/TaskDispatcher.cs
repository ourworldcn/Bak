using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace System.Threading
{
    /// <summary>
    /// ����������������ִ��һϵ������֧����ͬ��������ϲ�
    /// </summary>
    [OwAutoInjection(ServiceLifetime.Singleton)]
    public class TaskDispatcher : IDisposable
    {
        private readonly ILogger<TaskDispatcher> _logger;
        private readonly IHostApplicationLifetime _applicationLifetime;
        private readonly ConcurrentDictionary<object, TaskItem> _taskQueue = new ConcurrentDictionary<object, TaskItem>();
        // ����׷��ִ��˳��Ķ���
        private readonly ConcurrentQueue<object> _processingOrder = new ConcurrentQueue<object>();
        // ��¼����ʧ�ܵ��������´γ���ʱ��
        private readonly ConcurrentDictionary<object, DateTime> _backoffTasks = new ConcurrentDictionary<object, DateTime>();
        private readonly Thread _executionThread;
        private readonly AutoResetEvent _queueSignal = new AutoResetEvent(false);
        private readonly int _maxQueueSize;
        private bool _disposed;
        // �������ʱ�䣬����Ƶ������
        private readonly TimeSpan _backoffTime = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// ������������캯��
        /// </summary>
        /// <param name="logger">��־��¼��</param>
        /// <param name="applicationLifetime">Ӧ�ó�����������</param>
        /// <param name="maxQueueSize">�������������Ĭ�ϲ�����</param>
        public TaskDispatcher(ILogger<TaskDispatcher> logger, IHostApplicationLifetime applicationLifetime, int maxQueueSize = 0)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _applicationLifetime = applicationLifetime ?? throw new ArgumentNullException(nameof(applicationLifetime));
            _maxQueueSize = maxQueueSize;

            // ���������ȼ�ִ���߳�
            _executionThread = new Thread(ExecuteLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "TaskDispatcher_ExecutionThread"
            };
            _executionThread.Start();

            _logger.LogInformation("TaskDispatcher �ѳ�ʼ��������������: {MaxQueueSize}", _maxQueueSize > 0 ? _maxQueueSize.ToString() : "������");
        }

        /// <summary>
        /// �ύ���񵽶����У���ͬtypeId������ᱻ�ϲ�
        /// </summary>
        /// <param name="typeId">�������ͱ�ʶ</param>
        /// <param name="executeFunc">ִ������ĺ���</param>
        /// <param name="needLock">�Ƿ���Ҫ����</param>
        /// <returns>�Ƿ�ɹ���ӵ�����</returns>
        public bool Enqueue(object typeId, Func<object, bool> executeFunc, bool needLock = true)
        {
            if (typeId == null) throw new ArgumentNullException(nameof(typeId));
            if (executeFunc == null) throw new ArgumentNullException(nameof(executeFunc));
            if (_disposed) return false;
            
            // ��������������
            if (_maxQueueSize > 0 && _taskQueue.Count >= _maxQueueSize)
            {
                _logger.LogWarning("��������Ѵﵽ������� {MaxSize}���޷����������: {TypeId}", _maxQueueSize, typeId);
                return false;
            }

            var taskItem = new TaskItem
            {
                TypeId = typeId,
                ExecuteFunc = executeFunc,
                NeedLock = needLock,
                EnqueueTime = DateTime.UtcNow
            };

            bool isNewTask = !_taskQueue.ContainsKey(typeId);
            
            // ��ӻ��������
            _taskQueue[typeId] = taskItem;
            
            // ������������ʱ����ӵ��������
            if (isNewTask)
            {
                _processingOrder.Enqueue(typeId);
            }
            
            _queueSignal.Set(); // ִ֪ͨ���߳���������
            
            _logger.LogDebug("��������ӵ�����: {TypeId}", typeId);
            return true;
        }

        /// <summary>
        /// �ֶ���������е���������
        /// </summary>
        public void ProcessAll()
        {
            if (_disposed) return;

            _logger.LogDebug("��ʼ�ֶ���������е���������");
            
            // ����һ����ʱ�б�洢��ǰ��������
            var tasksToProcess = _taskQueue.Keys.ToList();
            foreach (var taskId in tasksToProcess)
            {
                ProcessTask(taskId);
            }
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
                    // �ȴ��źŻ�ʱ
                    _queueSignal.WaitOne(200); // ���̵ȴ�ʱ�䣬��Ƶ���ؼ����˵�����
                    
                    // �����������Ƿ��������ִ��
                    CheckBackoffTasks();
                    
                    // ��������е�����
                    ProcessNextTask();
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

        private void CheckBackoffTasks()
        {
            var now = DateTime.UtcNow;
            // �ҳ��������³��Ե�����
            var tasksDueForRetry = _backoffTasks
                .Where(kvp => now >= kvp.Value && _taskQueue.ContainsKey(kvp.Key))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var taskId in tasksDueForRetry)
            {
                // �������ƻش������
                _processingOrder.Enqueue(taskId);
                // �ӻ����б����Ƴ�
                _backoffTasks.TryRemove(taskId, out _);
            }
        }

        private void ProcessNextTask()
        {
            if (_processingOrder.IsEmpty || _taskQueue.IsEmpty)
                return;

            // ���ԴӴ�������л�ȡ��һ������
            if (!_processingOrder.TryDequeue(out var taskId))
                return;

            // ������������ڶ����У�������
            if (_taskQueue.ContainsKey(taskId))
            {
                ProcessTask(taskId);
            }
        }

        private void ProcessTask(object taskId)
        {
            // ����������л�ȡ���񣬵��ݲ��Ƴ�
            if (!_taskQueue.TryGetValue(taskId, out var taskItem))
                return;

            try
            {
                // �����Ҫ���������Ի�ȡ��
                if (taskItem.NeedLock)
                {
                    if (!SingletonLocker.TryEnter(taskItem.TypeId, TimeSpan.FromMilliseconds(10)))
                    {
                        _logger.LogDebug("�޷�����������ӵ������б�: {TypeId}", taskItem.TypeId);
                        // ����ʧ�ܣ���������б��Ժ��ٳ���
                        _backoffTasks[taskItem.TypeId] = DateTime.UtcNow.Add(_backoffTime);
                        return;
                    }
                }

                try
                {
                    // ���ڿ��԰�ȫ�Ƴ�����
                    _taskQueue.TryRemove(taskItem.TypeId, out _);
                    
                    // ִ������
                    bool result = taskItem.ExecuteFunc(taskItem.TypeId);
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
                    if (taskItem.NeedLock)
                    {
                        SingletonLocker.Exit(taskItem.TypeId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "������������з����쳣: {TypeId}", taskItem.TypeId);
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
            _queueSignal.Set(); // ����ִ���߳��Ա��˳�
            
            if (_executionThread.IsAlive && !_executionThread.Join(3000))
            {
                _logger.LogWarning("ִ���߳�δ���ڳ�ʱʱ���ڽ���");
            }
            
            _queueSignal.Dispose();
            _logger.LogInformation("TaskDispatcher ���ͷ���Դ");
        }

        /// <summary>
        /// ��������ݵ����������ͬ�������ڶ�������ʲôҲ����
        /// </summary>
        /// <param name="typeId">�������ͱ�ʶ</param>
        /// <param name="executeFunc">ִ������ĺ���</param>
        /// <param name="needLock">�Ƿ���Ҫ����</param>
        /// <returns>����ɹ���ӻ������Ѵ��ڷ���true�������������false</returns>
        public bool TryAddIdempotent(object typeId, Func<object, bool> executeFunc, bool needLock = true)
        {
            if (_taskQueue.ContainsKey(typeId))
                return true;
                
            return Enqueue(typeId, executeFunc, needLock);
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
            
            /// <summary>�Ƿ���Ҫ����</summary>
            public bool NeedLock { get; set; }
            
            /// <summary>���ʱ��</summary>
            public DateTime EnqueueTime { get; set; }
        }
    }
}
