using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OW.EntityFrameworkCore;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OW.Data
{
    /// <summary>����״̬ö�٣�֧��λ��־���</summary>
    [Flags]
    public enum OwTaskStatus : byte
    {
        /// <summary>�մ���</summary>
        Created = 0,
        /// <summary>������</summary>
        Pending = 1,
        /// <summary>ִ����</summary>
        Running = 2,
        /// <summary>�����</summary>
        Completed = 4,
        /// <summary>ʧ��</summary>
        Failed = 8,
    }

    /// <summary>
    /// ��ʱ����������Ĵ洢ʵ�壬���ڳ־û�������Ϣ��״̬
    /// </summary>
    [Comment("��ʱ����������Ĵ洢ʵ��")]
    [Index(nameof(CreatorId))]
    [Index(nameof(TenantId))]
    [Index(nameof(ServiceTypeName), nameof(MethodName))]
    [Index(nameof(StatusValue))]
    public class OwTaskStore : GuidKeyObjectBase
    {
        /// <summary>Ҫִ�еķ������͵��������ƣ����ڷ������</summary>
        [Comment("Ҫִ�еķ������͵���������")]
        public string ServiceTypeName { get; set; }

        /// <summary>Ҫִ�еķ������ƣ���Ϸ����������ڷ������</summary>
        [Comment("Ҫִ�еķ�������")]
        public string MethodName { get; set; }

        /// <summary>���������JSON�ַ�����ʾ���洢�����ݿ���</summary>
        [Comment("���������JSON��ʽ")]
        public string ParametersJson { get; set; }

        /// <summary>
        /// ����������ֵ���ʽ�����洢�����ݿ��У�ͨ��JSON���л�/�����л�ת��
        /// </summary>
        [NotMapped]
        public Dictionary<string, string> Parameters
        {
            get => string.IsNullOrEmpty(ParametersJson)
                ? new Dictionary<string, string>()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(ParametersJson);
            set => ParametersJson = value != null ? JsonSerializer.Serialize(value) : null;
        }

        /// <summary>����ǰִ��״̬���ֽ�ֵ���洢�����ݿ���</summary>
        [Comment("����ǰִ��״̬")]
        public byte StatusValue { get; set; }

        /// <summary>
        /// ����״̬��ö����ʽ�����洢�����ݿ��У�ͨ��StatusValueת��
        /// </summary>
        [NotMapped]
        public OwTaskStatus Status
        {
            get => (OwTaskStatus)StatusValue;
            set => StatusValue = (byte)value;
        }

        /// <summary>���񴴽�ʱ�䣬UTC��ʽ�����ȵ�����</summary>
        [Comment("���񴴽�ʱ�䣬UTC��ʽ")]
        [Precision(3)]
        public DateTime CreatedUtc { get; set; }

        /// <summary>����ʼִ��ʱ�䣬UTC��ʽ�����ȵ����룬��Ϊnull</summary>
        [Comment("����ʼִ��ʱ�䣬UTC��ʽ")]
        [Precision(3)]
        public DateTime? StartUtc { get; set; }

        /// <summary>�������ʱ�䣬UTC��ʽ�����ȵ����룬��Ϊnull</summary>
        [Comment("�������ʱ�䣬UTC��ʽ")]
        [Precision(3)]
        public DateTime? CompletedUtc { get; set; }

        /// <summary>����ִ�н����JSON�ַ�����ʾ���洢�����ݿ���</summary>
        [Comment("����ִ�н����JSON��ʽ")]
        public string ResultJson { get; set; }

        /// <summary>
        /// ����ִ�н�����ֵ���ʽ�����洢�����ݿ��У�ͨ��JSON���л�/�����л�ת��
        /// </summary>
        [NotMapped]
        public Dictionary<string, string> Result
        {
            get => string.IsNullOrEmpty(ResultJson)
                ? new Dictionary<string, string>()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(ResultJson);
            set => ResultJson = value != null ? JsonSerializer.Serialize(value) : null;
        }

        /// <summary>����ִ��ʧ��ʱ�Ĵ�����Ϣ�������������쳣��ջ</summary>
        [Comment("����ִ��ʧ��ʱ�Ĵ�����Ϣ")]
        public string ErrorMessage { get; set; }

        /// <summary>������������û�ID������Ȩ�޿��ƺ����</summary>
        [Comment("������������û�ID")]
        public Guid CreatorId { get; set; }

        /// <summary>�����������⻧ID��֧�ֶ��⻧��������Ϊnull</summary>
        [Comment("�����������⻧ID")]
        public Guid? TenantId { get; set; }
    }

    /// <summary>
    /// ͨ�ó�ʱ������������������࣬���ṩ������ִ������ĺ��Ĺ���
    /// ʹ��.NET 6��׼��BackgroundServiceģʽ��֧�ֲ������ƺ���Դ����
    /// ��ǿ�쳣����ȷ��ִ������ĺ����׳����쳣�ܹ�������¼������ջ��Ϣ
    /// </summary>
    /// <typeparam name="TDbContext">���ݿ����������ͣ�����̳���OwDbContext</typeparam>
    public class OwTaskService<TDbContext> : BackgroundService where TDbContext : OwDbContext
    {
        #region �ֶκ�����

        private readonly IServiceProvider _serviceProvider; // �����ṩ�ߣ����ڴ�������Χ
        private readonly ILogger<OwTaskService<TDbContext>> _logger; // ��־��¼��
        private readonly IDbContextFactory<TDbContext> _dbContextFactory; // ���ݿ������Ĺ���
        private readonly ConcurrentQueue<Guid> _pendingTaskIds = new(); // ��ִ���������
        private readonly SemaphoreSlim _semaphore; // �ź��������ڿ��Ʋ�����

        /// <summary>��ǰ����ִ�е�����������ͨ���ź�������</summary>
        public int CurrentRunningTaskCount => Environment.ProcessorCount - _semaphore.CurrentCount;

        /// <summary>��ǰ�ȴ������е���������</summary>
        public int PendingTaskCount => _pendingTaskIds.Count;

        #endregion

        #region ���캯��

        /// <summary>
        /// ��ʼ���������ʵ��������������Ͳ�������
        /// </summary>
        /// <param name="serviceProvider">�����ṩ�ߣ���������ע��</param>
        /// <param name="logger">��־��¼��</param>
        /// <param name="dbContextFactory">���ݿ������Ĺ���</param>
        /// <exception cref="ArgumentNullException">���κβ���Ϊnullʱ�׳�</exception>
        public OwTaskService(IServiceProvider serviceProvider, ILogger<OwTaskService<TDbContext>> logger, IDbContextFactory<TDbContext> dbContextFactory)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
            _semaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount); // ������������CPU������

            _logger.LogInformation("OwTaskService �ѳ�ʼ�������ݿ�������: {DbContext}", typeof(TDbContext).Name);
        }

        #endregion

        #region BackgroundService ʵ��

        /// <summary>
        /// <inheritdoc />
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public override Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("OwTaskService ��������...");
            // ��δ��ɵ���������ݿ���ص��ڴ������
            try
            {
                using var dbContext = _dbContextFactory.CreateDbContext();
                var pendingTasks = dbContext.Set<OwTaskStore>()
                    .Where(t => t.StatusValue == (byte)OwTaskStatus.Pending || t.StatusValue == (byte)OwTaskStatus.Created)
                    .Select(t => t.Id)
                    .ToList();
                foreach (var taskId in pendingTasks)
                {
                    _pendingTaskIds.Enqueue(taskId);
                }
                _logger.LogInformation("�Ѽ��� {Count} �����������񵽶���", pendingTasks.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "���ش���������ʱ��������");
            }
            return base.StartAsync(cancellationToken);
        }

        /// <summary>
        /// ִ�к�̨������ѭ������������������в��ַ�ִ��
        /// ʹ��ͬ��ģʽ����async/await������
        /// </summary>
        /// <param name="stoppingToken">ȡ�����ƣ���������ֹͣ����</param>
        /// <returns>��ʾ�첽������Task</returns>
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            return Task.Run(() =>
            {
                _logger.LogInformation("OwTaskService ��̨����������");

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        if (_pendingTaskIds.TryDequeue(out var taskId))
                        {
                            _semaphore.Wait(stoppingToken); // ʹ��ͬ���ȴ�

                            _ = Task.Run(() => // �ڶ����߳���ִ������
                            {
                                try
                                {
                                    ProcessTask(taskId);
                                }
                                finally
                                {
                                    _semaphore.Release();
                                }
                            }, stoppingToken);
                        }
                        else
                        {
                            // ʹ��ͬ���ӳٷ�ʽ
                            if (!stoppingToken.IsCancellationRequested)
                            {
                                Thread.Sleep(500); // ����Ϊ��ʱ�ȴ�
                            }
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break; // ����ȡ��
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "������ѭ���з�������");
                        if (!stoppingToken.IsCancellationRequested)
                        {
                            Thread.Sleep(1000); // ����ʱ�ȴ�������
                        }
                    }
                }

                _logger.LogInformation("OwTaskService ��̨�����ѽ���");
            }, stoppingToken);
        }

        #endregion

        #region �����ӿ�

        /// <summary>
        /// �������ύ������ִ�ж���
        /// </summary>
        /// <param name="serviceType">Ҫִ�еķ������ͣ�������ע�ᵽDI����</param>
        /// <param name="methodName">Ҫ���õķ������ƣ������ǹ�������</param>
        /// <param name="parameters">���������ֵ䣬��Ϊ��������ֵΪ����ֵ���ַ�����ʾ</param>
        /// <param name="creatorId">�������û�ID��������ƺ�Ȩ�޿���</param>
        /// <param name="tenantId">�⻧ID����ѡ�����ڶ��⻧����</param>
        /// <returns>�´��������Ψһ��ʶID</returns>
        /// <exception cref="ArgumentNullException">��serviceTypeΪnullʱ�׳�</exception>
        /// <exception cref="ArgumentException">��methodNameΪ�ջ�nullʱ�׳�</exception>
        public Guid CreateTask(Type serviceType, string methodName, Dictionary<string, string> parameters, Guid creatorId, Guid? tenantId = null)
        {
            if (serviceType == null) throw new ArgumentNullException(nameof(serviceType));
            if (string.IsNullOrWhiteSpace(methodName)) throw new ArgumentException("�������Ʋ���Ϊ��", nameof(methodName));

            var taskId = Guid.NewGuid();

            try
            {
                using var dbContext = _dbContextFactory.CreateDbContext();
                var taskEntity = new OwTaskStore
                {
                    Id = taskId,
                    ServiceTypeName = serviceType.FullName,
                    MethodName = methodName,
                    Parameters = parameters ?? new Dictionary<string, string>(),
                    CreatedUtc = DateTime.UtcNow,
                    StatusValue = (byte)OwTaskStatus.Pending,
                    CreatorId = creatorId,
                    TenantId = tenantId
                };

                dbContext.Set<OwTaskStore>().Add(taskEntity);
                dbContext.SaveChanges();

                _pendingTaskIds.Enqueue(taskId); // ����ִ�ж���

                _logger.LogDebug("���� {TaskId} �Ѵ���������: {Service}������: {Method}", taskId, serviceType.Name, methodName);
                return taskId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "��������ʧ�ܣ�����: {Service}������: {Method}", serviceType.Name, methodName);
                throw;
            }
        }

        /// <summary>
        /// �������ύ������ķ������ذ汾���ṩ����ʱ���Ͱ�ȫ
        /// </summary>
        /// <typeparam name="TService">Ҫִ�еķ������ͣ�����ʱȷ��</typeparam>
        /// <param name="methodName">Ҫ���õķ�������</param>
        /// <param name="parameters">���������ֵ�</param>
        /// <param name="creatorId">�������û�ID</param>
        /// <param name="tenantId">�⻧ID����ѡ</param>
        /// <returns>�´��������Ψһ��ʶID</returns>
        public Guid CreateTask<TService>(string methodName, Dictionary<string, string> parameters, Guid creatorId, Guid? tenantId = null)
        {
            return CreateTask(typeof(TService), methodName, parameters, creatorId, tenantId);
        }

        #endregion

        #region �ڲ�ʵ��

        /// <summary>
        /// ����ָ������ʹ��ͬ����ʽ�ͷ�Χ�����װȷ����Դ����
        /// ��ǿ�쳣����ȷ��������¼�쳣��Ϣ�Ͷ�ջ����
        /// </summary>
        /// <param name="taskId">Ҫִ�е�����ID</param>
        private void ProcessTask(Guid taskId)
        {
            OwTaskStore taskEntity = null;
            string currentStep = "��ʼ��";
            bool taskStarted = false; // ��������Ƿ��ѿ�ʼ������ȷ��״̬���µ���ȷ��

            try
            {
                currentStep = "��������";
                // ��ȡ���񲢸���״̬Ϊִ����
                using (var dbContext = _dbContextFactory.CreateDbContext())
                {
                    taskEntity = dbContext.Set<OwTaskStore>().Find(taskId);
                    if (taskEntity == null)
                    {
                        _logger.LogWarning("δ�ҵ����� {TaskId}", taskId);
                        return;
                    }

                    currentStep = "��������״̬Ϊִ����";
                    taskEntity.StatusValue = (byte)OwTaskStatus.Running;
                    taskEntity.StartUtc = DateTime.UtcNow;
                    dbContext.SaveChanges();
                    taskStarted = true; // ��������ѿ�ʼ

                    _logger.LogDebug("���� {TaskId} ״̬�Ѹ���Ϊִ���У���ʼʱ��: {StartTime}", taskId, taskEntity.StartUtc);
                }

                _logger.LogDebug("��ʼִ������ {TaskId}������: {Service}������: {Method}", taskId, taskEntity.ServiceTypeName, taskEntity.MethodName);

                currentStep = "��֤���������Ϣ";
                // ��֤��������
                if (string.IsNullOrWhiteSpace(taskEntity.ServiceTypeName))
                    throw new InvalidOperationException("����ķ�����������Ϊ��");
                if (string.IsNullOrWhiteSpace(taskEntity.MethodName))
                    throw new InvalidOperationException("����ķ�������Ϊ��");

                currentStep = "���ҷ�������";
                // �Ľ������Ͳ��һ���
                var serviceType = FindTypeByName(taskEntity.ServiceTypeName);
                if (serviceType == null)
                    throw new InvalidOperationException($"�޷��ҵ�����: {taskEntity.ServiceTypeName}");

                _logger.LogDebug("���� {TaskId} �ҵ���������: {ServiceType}", taskId, serviceType.FullName);

                currentStep = "���ҷ���";
                var methodInfo = serviceType.GetMethod(taskEntity.MethodName, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
                if (methodInfo == null)
                    throw new InvalidOperationException($"�ڷ��� {taskEntity.ServiceTypeName} ���Ҳ������� {taskEntity.MethodName}");

                _logger.LogDebug("���� {TaskId} �ҵ�����: {Method}, �Ƿ�̬: {IsStatic}", taskId, methodInfo.Name, methodInfo.IsStatic);

                currentStep = "׼����������";
                object result;
                object[] parameters;
                
                try
                {
                    parameters = PrepareMethodParameters(methodInfo, taskEntity.Parameters);
                    _logger.LogDebug("���� {TaskId} ����׼����ɣ���������: {ParameterCount}", taskId, parameters?.Length ?? 0);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"׼����������ʱ��������: {ex.Message}", ex);
                }

                currentStep = "ִ�з���";
                if (methodInfo.IsStatic)
                {
                    // ��̬�������ã�����Ƿ���Ҫע������ṩ��
                    if (HasServiceProviderParameter(methodInfo))
                    {
                        currentStep = "��������������ִ�о�̬����";
                        // Ϊ��̬����ע������ṩ��
                        using var scope = _serviceProvider.CreateScope();
                        var scopedProvider = scope.ServiceProvider;
                        if (scopedProvider == null)
                            throw new InvalidOperationException("�޷���������������");

                        _logger.LogDebug("���� {TaskId} ��ʼִ�о�̬�������������ṩ�ߣ�", taskId);
                        
                        result = InvokeMethodWithExceptionHandling(methodInfo, null, parameters, scopedProvider, taskId);
                    }
                    else
                    {
                        currentStep = "ִ�о�̬����";
                        _logger.LogDebug("���� {TaskId} ��ʼִ�о�̬����", taskId);
                        
                        result = InvokeMethodWithExceptionHandling(methodInfo, null, parameters, null, taskId);
                    }
                }
                else
                {
                    currentStep = "�������������򲢽�������ʵ��";
                    // ʵ����������
                    using var scope = _serviceProvider.CreateScope();
                    var scopedProvider = scope.ServiceProvider;
                    if (scopedProvider == null)
                        throw new InvalidOperationException("�޷���������������");

                    var service = scopedProvider.GetService(serviceType);
                    if (service == null)
                        throw new InvalidOperationException($"�޷���DI������������: {taskEntity.ServiceTypeName}");

                    currentStep = "ִ��ʵ������";
                    _logger.LogDebug("���� {TaskId} ��ʼִ��ʵ������", taskId);
                    
                    result = InvokeMethodWithExceptionHandling(methodInfo, service, parameters, scopedProvider, taskId);
                }

                currentStep = "�����������״̬";
                _logger.LogDebug("���� {TaskId} ִ����ɣ���ʼ����״̬", taskId);
                UpdateTaskCompletion(taskId, result);

                _logger.LogDebug("���� {TaskId} ִ�гɹ�", taskId);
            }
            catch (Exception ex)
            {
                // ��ǿ�Ĵ�����Ϣ��������ǰִ�в�����������쳣��Ϣ
                var contextualError = $"����ִ��ʧ�ܣ���ǰ����: {currentStep}";
                if (taskEntity != null)
                {
                    contextualError += $"\n��������: ID={taskId}, ����={taskEntity.ServiceTypeName}, ����={taskEntity.MethodName}";
                    contextualError += $"\n�������: {taskEntity.ParametersJson ?? "��"}";
                }
                
                // ��¼��ϸ���쳣��Ϣ����־
                _logger.LogWarning(ex, "���� {TaskId} �ڲ��� '{CurrentStep}' ִ��ʧ��", taskId, currentStep);

                // ����������������Ϣ���쳣������ԭʼ�쳣��Ϊ�ڲ��쳣
                var wrappedException = new InvalidOperationException(contextualError, ex);
                
                // ��Ӷ������������Ϣ���쳣������
                wrappedException.Data["TaskId"] = taskId;
                wrappedException.Data["ExecutionStep"] = currentStep;
                wrappedException.Data["TaskStarted"] = taskStarted;
                wrappedException.Data["ExecutionTime"] = DateTime.UtcNow;
                
                if (taskEntity != null)
                {
                    wrappedException.Data["ServiceTypeName"] = taskEntity.ServiceTypeName;
                    wrappedException.Data["MethodName"] = taskEntity.MethodName;
                    wrappedException.Data["TaskCreatedUtc"] = taskEntity.CreatedUtc;
                    wrappedException.Data["TaskStartUtc"] = taskEntity.StartUtc;
                    wrappedException.Data["CreatorId"] = taskEntity.CreatorId;
                    wrappedException.Data["TenantId"] = taskEntity.TenantId;
                }
                
                // ��ȡ�����Ĵ�����Ϣ��������ջ���٣�
                var errorMessage = GetCompleteExceptionMessage(wrappedException);

                // ȷ������״̬����ȷ����Ϊʧ��
                try
                {
                    if (taskStarted)
                    {
                        UpdateTaskFailure(taskId, errorMessage);
                        _logger.LogDebug("���� {TaskId} ʧ��״̬�Ѹ���", taskId);
                    }
                    else
                    {
                        _logger.LogWarning("���� {TaskId} �ڿ�ʼǰ��ʧ���ˣ�״̬������Ҫ�ֶ����", taskId);
                        UpdateTaskFailure(taskId, errorMessage);
                    }
                }
                catch (Exception updateEx)
                {
                    _logger.LogError(updateEx, "�������� {TaskId} ʧ��״̬ʱ��������", taskId);
                }
            }
        }

        /// <summary>
        /// �Ľ��ķ������ã������������쳣����Ͷ�ջ��Ϣ����
        /// </summary>
        /// <param name="methodInfo">������Ϣ</param>
        /// <param name="service">����ʵ������̬����Ϊnull��</param>
        /// <param name="parameters">��������</param>
        /// <param name="serviceProvider">�����ṩ��</param>
        /// <param name="taskId">����ID</param>
        /// <returns>����ִ�н��</returns>
        private object InvokeMethodWithExceptionHandling(MethodInfo methodInfo, object service, object[] parameters, IServiceProvider serviceProvider, Guid taskId)
        {
            try
            {
                // ����Ǿ�̬��������Ҫ�����ṩ�ߣ�ע������ṩ��
                if (methodInfo.IsStatic && serviceProvider != null && HasServiceProviderParameter(methodInfo))
                {
                    return InvokeStaticMethodWithServiceProvider(methodInfo, parameters, serviceProvider, taskId);
                }
                else
                {
                    return methodInfo.Invoke(service, parameters);
                }
            }
            catch (TargetInvocationException tie)
            {
                // ��ȡ�ڲ��쳣��ͬʱ����ԭʼ�쳣��������Ϣ
                var innerException = tie.InnerException ?? tie;
                var enhancedException = new InvalidOperationException(
                    $"����ִ��ʱ�����쳣: {innerException.Message}\nԭʼ�����쳣: {tie.Message}", 
                    innerException);
                
                // ��ԭʼTargetInvocationException����Ϣ��ӵ�Data��
                enhancedException.Data["OriginalTargetInvocationException"] = tie.ToString();
                enhancedException.Data["OriginalStackTrace"] = tie.StackTrace;
                enhancedException.Data["TargetMethod"] = methodInfo.Name;
                enhancedException.Data["TargetType"] = methodInfo.DeclaringType?.FullName;
                enhancedException.Data["IsStatic"] = methodInfo.IsStatic;
                if (service != null)
                {
                    enhancedException.Data["ServiceInstance"] = service.GetType().FullName;
                }
                
                throw enhancedException;
            }
            catch (Exception ex)
            {
                // Ϊ�����쳣�����������Ϣ
                var enhancedException = new InvalidOperationException(
                    $"����ִ��ʱ�����쳣: {ex.Message}", ex);
                
                enhancedException.Data["TargetMethod"] = methodInfo.Name;
                enhancedException.Data["TargetType"] = methodInfo.DeclaringType?.FullName;
                enhancedException.Data["IsStatic"] = methodInfo.IsStatic;
                enhancedException.Data["MethodParameters"] = string.Join(", ", parameters?.Select(p => p?.ToString() ?? "null") ?? Array.Empty<string>());
                if (service != null)
                {
                    enhancedException.Data["ServiceInstance"] = service.GetType().FullName;
                }
                
                throw enhancedException;
            }
        }

        /// <summary>
        /// ���쳣������ȡ�����Ĵ�����Ϣ�������ڲ��쳣���Ͷ�ջ��Ϣ
        /// ȷ�������쳣��Ϣ������������õ��쳣�����ܱ�������¼
        /// </summary>
        /// <param name="ex">�쳣����</param>
        /// <returns>��ʽ��������������Ϣ�ַ�����������ջ����</returns>
        private static string GetCompleteExceptionMessage(Exception ex)
        {
            if (ex == null)
                return "δ֪�쳣";

            var errorDetails = new List<string>();
            var currentEx = ex;
            var exceptionLevel = 0;

            // �����쳣�����ռ������쳣��Ϣ
            while (currentEx != null)
            {
                var exceptionInfo = new List<string>();

                // �쳣������Ϣ
                exceptionInfo.Add($"�쳣����: {exceptionLevel}");
                exceptionInfo.Add($"�쳣����: {currentEx.GetType().FullName}");
                exceptionInfo.Add($"�쳣��Ϣ: {currentEx.Message}");

                // �����Ŀ��վ����Ϣ�������
                if (currentEx.TargetSite != null)
                {
                    exceptionInfo.Add($"Ŀ�귽��: {currentEx.TargetSite.DeclaringType?.FullName}.{currentEx.TargetSite.Name}");
                    
                    // ��ȡĿ�귽���Ĳ�����Ϣ
                    var parameters = currentEx.TargetSite.GetParameters();
                    if (parameters.Length > 0)
                    {
                        var parameterInfo = string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
                        exceptionInfo.Add($"Ŀ�귽������: {parameterInfo}");
                    }
                }

                // �����Դ��Ϣ�������
                if (!string.IsNullOrEmpty(currentEx.Source))
                {
                    exceptionInfo.Add($"�쳣Դ: {currentEx.Source}");
                }

                // ��¼HResult��������ã�
                if (currentEx.HResult != 0)
                {
                    exceptionInfo.Add($"HResult: 0x{currentEx.HResult:X8}");
                }

                // ��ջ������Ϣ - ��������Ҫ�Ĳ���
                if (!string.IsNullOrEmpty(currentEx.StackTrace))
                {
                    exceptionInfo.Add($"��ջ����:\n{currentEx.StackTrace}");
                }
                else
                {
                    // ���û�ж�ջ���٣����Ի�ȡ��ǰ����ջ
                    try
                    {
                        var stackTrace = new System.Diagnostics.StackTrace(currentEx, true);
                        if (stackTrace.FrameCount > 0)
                        {
                            exceptionInfo.Add($"��ջ���٣�ͨ��StackTrace��ȡ��:\n{stackTrace}");
                        }
                    }
                    catch
                    {
                        // ����޷���ȡ��ջ���٣����ټ�¼��һ��
                        exceptionInfo.Add("��ջ����: �޷���ȡ��ջ������Ϣ");
                    }
                }

                // ����и������ݣ������
                if (currentEx.Data != null && currentEx.Data.Count > 0)
                {
                    var dataEntries = new List<string>();
                    foreach (var key in currentEx.Data.Keys)
                    {
                        try
                        {
                            dataEntries.Add($"  {key}: {currentEx.Data[key]}");
                        }
                        catch
                        {
                            dataEntries.Add($"  {key}: <�޷����л�>");
                        }
                    }
                    exceptionInfo.Add($"��������:\n{string.Join("\n", dataEntries)}");
                }

                // ���TargetInvocationException�����⴦��
                if (currentEx is TargetInvocationException tie)
                {
                    exceptionInfo.Add("ע��: ����һ����������쳣���������쳣��InnerException��");
                    if (tie.InnerException != null)
                    {
                        exceptionInfo.Add($"�ڲ��쳣Ԥ��: {tie.InnerException.GetType().Name} - {tie.InnerException.Message}");
                    }
                }

                // ���AggregateException�����⴦��
                if (currentEx is AggregateException aggEx)
                {
                    exceptionInfo.Add($"�ۺ��쳣���� {aggEx.InnerExceptions.Count} ���ڲ��쳣");
                    for (int i = 0; i < aggEx.InnerExceptions.Count; i++)
                    {
                        var innerEx = aggEx.InnerExceptions[i];
                        exceptionInfo.Add($"  �ۺ��쳣[{i}]: {innerEx.GetType().Name} - {innerEx.Message}");
                    }
                }

                errorDetails.Add($"=== �쳣 {exceptionLevel} ===\n{string.Join("\n", exceptionInfo)}");

                currentEx = currentEx.InnerException;
                exceptionLevel++;
            }

            // ���ʱ����ͻ�����Ϣ
            var environmentInfo = new List<string>
            {
                $"�쳣����ʱ��: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC",
                $"����ʱ��: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}",
                $"��������: {Environment.MachineName}",
                $"�û���: {Environment.UserDomainName}",
                $"�û���: {Environment.UserName}",
                $"����ϵͳ: {Environment.OSVersion}",
                $"����ID: {Environment.ProcessId}",
                $"�߳�ID: {Thread.CurrentThread.ManagedThreadId}",
                $"�߳�����: {Thread.CurrentThread.Name ?? "δ����"}",
                $"Ӧ�ó�����: {AppDomain.CurrentDomain.FriendlyName}",
                $"����Ŀ¼: {Environment.CurrentDirectory}",
                $"CLR�汾: {Environment.Version}",
                $"����������: {Environment.ProcessorCount}",
                $"ϵͳ����ʱ��: {Environment.TickCount}ms"
            };

            // ��ӵ�ǰ����ջ��������ã�
            try
            {
                var currentStackTrace = new System.Diagnostics.StackTrace(true);
                if (currentStackTrace.FrameCount > 0)
                {
                    environmentInfo.Add($"��ǰ����ջ:\n{currentStackTrace}");
                }
            }
            catch
            {
                environmentInfo.Add("��ǰ����ջ: �޷���ȡ");
            }

            var fullErrorMessage = new List<string>
            {
                "=== ����ִ���쳣���� ===",
                string.Join("\n", environmentInfo),
                "",
                string.Join("\n\n", errorDetails),
                "=== �쳣������� ==="
            };

            return string.Join("\n", fullErrorMessage);
        }

        /// <summary>
        /// �Ľ������Ͳ��ҷ�����֧���������Ѽ��س����в�������
        /// </summary>
        /// <param name="typeName">��������������</param>
        /// <returns>�ҵ������ͣ����δ�ҵ��򷵻�null</returns>
        private static Type FindTypeByName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            // ���ȳ��Ա�׼��Type.GetType()
            var type = Type.GetType(typeName);
            if (type != null)
                return type;

            // ���ʧ�ܣ�������ǰӦ�����е����г���
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = assembly.GetType(typeName);
                    if (type != null)
                        return type;

                    // Ҳ���Բ����ִ�Сд��ƥ��
                    var types = assembly.GetTypes().Where(t =>
                        string.Equals(t.FullName, typeName, StringComparison.OrdinalIgnoreCase)).ToArray();

                    if (types.Length == 1)
                        return types[0];
                    else if (types.Length > 1)
                        throw new InvalidOperationException($"�ҵ����ƥ�������: {typeName}");
                }
                catch (ReflectionTypeLoadException)
                {
                    // ĳЩ���򼯿����޷������������ͣ�������Щ�쳣
                    continue;
                }
                catch (Exception)
                {
                    // ���������쳣������������һ������
                    continue;
                }
            }

            return null;
        }

        /// <summary>
        /// ��鷽���Ƿ��з����ṩ�߲���
        /// </summary>
        /// <param name="methodInfo">������Ϣ</param>
        /// <returns>���������Ҫ�����ṩ���򷵻�true</returns>
        private static bool HasServiceProviderParameter(MethodInfo methodInfo)
        {
            var parameters = methodInfo.GetParameters();
            return parameters.Any(p => p.ParameterType == typeof(IServiceProvider));
        }

        /// <summary>
        /// Ϊ��̬��������ע������ṩ�ߺ�����ID
        /// ��ǿ�����������ע�루taskId, serviceProvider��
        /// </summary>
        /// <param name="methodInfo">������Ϣ</param>
        /// <param name="parameters">ԭʼ��������</param>
        /// <param name="serviceProvider">�����ṩ��</param>
        /// <param name="taskId">����ID</param>
        /// <returns>����ִ�н��</returns>
        private object InvokeStaticMethodWithServiceProvider(MethodInfo methodInfo, object[] parameters, IServiceProvider serviceProvider, Guid taskId)
        {
            var methodParams = methodInfo.GetParameters();
            var enhancedParameters = new object[methodParams.Length];

            // ����ԭ�в���
            Array.Copy(parameters, enhancedParameters, Math.Min(parameters.Length, enhancedParameters.Length));

            // ���Ҳ�ע���������
            for (int i = 0; i < methodParams.Length; i++)
            {
                // ע������ṩ�߲���
                if (methodParams[i].ParameterType == typeof(IServiceProvider))
                {
                    enhancedParameters[i] = serviceProvider;
                }
                // ע������ID����
                else if (methodParams[i].ParameterType == typeof(Guid) && 
                         string.Equals(methodParams[i].Name, "taskId", StringComparison.OrdinalIgnoreCase))
                {
                    enhancedParameters[i] = taskId;
                }
            }

            return methodInfo.Invoke(null, enhancedParameters);
        }

        /// <summary>
        /// ���ݷ�����Ϣ�Ͳ����ֵ�׼���������ò�������
        /// ��ǿ�����������ƥ�䣬�ر��Ƕ���Dictionary<string, string>���͵Ĳ���
        /// </summary>
        /// <param name="methodInfo">Ŀ�귽���ķ�����Ϣ</param>
        /// <param name="parameterDict">������ֵ���ֵ�</param>
        /// <returns>׼���õĲ������飬������ǩ��˳������</returns>
        private static object[] PrepareMethodParameters(MethodInfo methodInfo, Dictionary<string, string> parameterDict)
        {
            var methodParams = methodInfo.GetParameters();
            var parameters = new object[methodParams.Length];

            for (int i = 0; i < methodParams.Length; i++)
            {
                var param = methodParams[i];
                
                // ���⴦���������������Dictionary<string, string>�Ҳ�����Ϊ"parameters"
                // ֱ�Ӵ�������parameterDict����������ϵͳ��Լ��
                if (param.ParameterType == typeof(Dictionary<string, string>) && 
                    string.Equals(param.Name, "parameters", StringComparison.OrdinalIgnoreCase))
                {
                    parameters[i] = parameterDict ?? new Dictionary<string, string>();
                    continue;
                }
                
                // ���⴦���������������Guid�Ҳ�����Ϊ"taskId"
                // �����ֵ��в��ң������ں�����InvokeStaticMethodWithServiceProvider����
                if (param.ParameterType == typeof(Guid) && 
                    string.Equals(param.Name, "taskId", StringComparison.OrdinalIgnoreCase))
                {
                    parameters[i] = Guid.Empty; // ��ʱֵ������InvokeStaticMethodWithServiceProvider�б���ȷ����
                    continue;
                }
                
                // ���⴦���������������IServiceProvider�Ҳ�����Ϊ"serviceProvider"
                // �����ֵ��в��ң������ں�����InvokeStaticMethodWithServiceProvider����
                if (param.ParameterType == typeof(IServiceProvider) && 
                    string.Equals(param.Name, "serviceProvider", StringComparison.OrdinalIgnoreCase))
                {
                    parameters[i] = null; // ��ʱֵ������InvokeStaticMethodWithServiceProvider�б���ȷ����
                    continue;
                }

                // �����������
                if (parameterDict?.TryGetValue(param.Name, out var paramValue) == true && !string.IsNullOrEmpty(paramValue))
                {
                    try
                    {
                        // ����������͵�ת��
                        if (param.ParameterType == typeof(Guid) || param.ParameterType == typeof(Guid?))
                        {
                            if (param.ParameterType == typeof(Guid?))
                            {
                                parameters[i] = string.IsNullOrEmpty(paramValue) ? (Guid?)null : Guid.Parse(paramValue);
                            }
                            else
                            {
                                parameters[i] = Guid.Parse(paramValue);
                            }
                        }
                        else if (param.ParameterType == typeof(Dictionary<string, string>))
                        {
                            parameters[i] = JsonSerializer.Deserialize<Dictionary<string, string>>(paramValue);
                        }
                        else
                        {
                            parameters[i] = Convert.ChangeType(paramValue, param.ParameterType);
                        }
                    }
                    catch (Exception)
                    {
                        parameters[i] = param.ParameterType.IsValueType ? Activator.CreateInstance(param.ParameterType) : null;
                    }
                }
                else
                {
                    parameters[i] = param.ParameterType.IsValueType ? Activator.CreateInstance(param.ParameterType) : null;
                }
            }

            return parameters;
        }

        /// <summary>
        /// ��������Ϊ���״̬��������ִ�н��
        /// </summary>
        /// <param name="taskId">����ID</param>
        /// <param name="result">����ִ�н��</param>
        private void UpdateTaskCompletion(Guid taskId, object result)
        {
            try
            {
                using var dbContext = _dbContextFactory.CreateDbContext();
                var task = dbContext.Set<OwTaskStore>().Find(taskId);
                if (task != null)
                {
                    task.StatusValue = (byte)OwTaskStatus.Completed;
                    task.CompletedUtc = DateTime.UtcNow;

                    if (result != null)
                    {
                        try
                        {
                            // ���Խ�������л�ΪJSON
                            var resultJson = JsonSerializer.Serialize(result);
                            task.Result = new Dictionary<string, string> { { "Result", resultJson } };
                        }
                        catch (Exception serializationEx)
                        {
                            _logger.LogWarning(serializationEx, "���� {TaskId} ������л�ʧ�ܣ�ʹ��ToString()����", taskId);
                            try
                            {
                                // ������л�ʧ�ܣ�ʹ��ToString()
                                task.Result = new Dictionary<string, string> { { "Result", result.ToString() } };
                            }
                            catch (Exception toStringEx)
                            {
                                _logger.LogWarning(toStringEx, "���� {TaskId} ���ToString()Ҳʧ�ܣ�ʹ����������", taskId);
                                // ���ToString()Ҳʧ�ܣ����ټ�¼������Ϣ
                                task.Result = new Dictionary<string, string> { 
                                    { "Result", $"<���л�ʧ��: {result.GetType().FullName}>" },
                                    { "SerializationError", serializationEx.Message }
                                };
                            }
                        }
                    }

                    dbContext.SaveChanges();
                    _logger.LogDebug("���� {TaskId} ���״̬���³ɹ�", taskId);
                }
                else
                {
                    _logger.LogWarning("���Ը������� {TaskId} ���״̬ʱ��δ�ҵ�������", taskId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "�������� {TaskId} ���״̬ʧ��", taskId);
                
                // ���Լ򻯵�״̬����
                try
                {
                    using var dbContext = _dbContextFactory.CreateDbContext();
                    var task = dbContext.Set<OwTaskStore>().Find(taskId);
                    if (task != null)
                    {
                        task.StatusValue = (byte)OwTaskStatus.Completed;
                        task.CompletedUtc = DateTime.UtcNow;
                        // ����������ֻ����״̬
                        dbContext.SaveChanges();
                        _logger.LogDebug("���� {TaskId} ���״̬���³ɹ����򻯰汾��δ��������", taskId);
                    }
                }
                catch (Exception retryEx)
                {
                    _logger.LogError(retryEx, "���Ը������� {TaskId} ���״̬ʱҲ��������", taskId);
                }
            }
        }

        /// <summary>
        /// ��������Ϊʧ��״̬�������������Ĵ�����Ϣ
        /// </summary>
        /// <param name="taskId">����ID</param>
        /// <param name="errorMessage">�����Ĵ�����Ϣ��������ջ����</param>
        private void UpdateTaskFailure(Guid taskId, string errorMessage)
        {
            try
            {
                using var dbContext = _dbContextFactory.CreateDbContext();
                var task = dbContext.Set<OwTaskStore>().Find(taskId);
                if (task != null)
                {
                    task.StatusValue = (byte)OwTaskStatus.Failed;
                    task.CompletedUtc = DateTime.UtcNow;
                    task.ErrorMessage = errorMessage;
                    
                    // ���Ա������
                    dbContext.SaveChanges();
                    _logger.LogDebug("���� {TaskId} ʧ��״̬���³ɹ�", taskId);
                }
                else
                {
                    _logger.LogWarning("���Ը������� {TaskId} ʧ��״̬ʱ��δ�ҵ�������", taskId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "�������� {TaskId} ʧ��״̬ʱ��������", taskId);
                
                // ������ݿ����ʧ�ܣ����Լ�¼����־��
                _logger.LogError("���� {TaskId} �Ĵ�����Ϣ�������ݿ����ʧ�ܶ���¼����־����\n{ErrorMessage}", taskId, errorMessage);
                
                // ���Լ򻯵�״̬����
                try
                {
                    using var dbContext = _dbContextFactory.CreateDbContext();
                    var task = dbContext.Set<OwTaskStore>().Find(taskId);
                    if (task != null)
                    {
                        task.StatusValue = (byte)OwTaskStatus.Failed;
                        task.CompletedUtc = DateTime.UtcNow;
                        // ���������Ϣ̫�����ض���
                        task.ErrorMessage = errorMessage.Length > 8000 ? 
                            errorMessage.Substring(0, 8000) + "\n...[������Ϣ�ѽضϣ�������Ϣ��鿴��־]" : 
                            errorMessage;
                        
                        dbContext.SaveChanges();
                        _logger.LogDebug("���� {TaskId} ʧ��״̬���³ɹ����򻯰汾��", taskId);
                    }
                }
                catch (Exception retryEx)
                {
                    _logger.LogError(retryEx, "���Ը������� {TaskId} ʧ��״̬ʱҲ��������", taskId);
                }
            }
        }

        #endregion

        #region �ͷ���Դ

        /// <summary>
        /// �ͷ��й���Դ�������ź�����
        /// </summary>
        public override void Dispose()
        {
            _semaphore?.Dispose();
            base.Dispose();
        }

        #endregion
    }

    /// <summary>
    /// OwTaskService����չ���������ڼ򻯷���ע��
    /// </summary>
    public static class OwTaskServiceExtensions
    {
        /// <summary>
        /// ����񼯺����OwTaskService��ʹ��.NET 6��׼��HostedServiceģʽ
        /// </summary>
        /// <typeparam name="TDbContext">���ݿ�����������</typeparam>
        /// <param name="services">���񼯺�</param>
        /// <returns>���º�ķ��񼯺ϣ�֧����ʽ����</returns>
        public static IServiceCollection AddOwTaskService<TDbContext>(this IServiceCollection services)
            where TDbContext : OwDbContext
        {
            services.AddHostedService<OwTaskService<TDbContext>>();
            services.TryAddSingleton(provider =>
                (OwTaskService<TDbContext>)provider.GetRequiredService<IEnumerable<IHostedService>>().First(c => c is OwTaskService<TDbContext>));
            return services;
        }

        /// <summary>
        /// ����񼯺����OwTaskService��ͬʱ����DbContextFactory
        /// </summary>
        /// <typeparam name="TDbContext">���ݿ�����������</typeparam>
        /// <param name="services">���񼯺�</param>
        /// <param name="optionsAction">���ݿ�����������ί��</param>
        /// <returns>���º�ķ��񼯺ϣ�֧����ʽ����</returns>
        public static IServiceCollection AddOwTaskService<TDbContext>(this IServiceCollection services, Action<DbContextOptionsBuilder> optionsAction)
            where TDbContext : OwDbContext
        {
            services.AddDbContextFactory<TDbContext>(optionsAction);
            return services.AddOwTaskService<TDbContext>();
        }
    }
}