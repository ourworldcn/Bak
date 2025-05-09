using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using OW.Data;

namespace OW.Data
{
    /// <summary>
    /// ��ʱ�������������Ϣʵ���ࡣ
    /// </summary>
    [Table("OwPersistentTaskEntities")]
    [Comment("��ʱ������������Ϣ")]
    public class OwPersistentTaskEntity : GuidKeyObjectBase
    {
        /// <summary>
        /// ��������
        /// </summary>
        [Required]
        [StringLength(80)]
        [Comment("��������")]
        public string Name { get; set; }

        /// <summary>
        /// ��������
        /// </summary>
        [StringLength(255)]
        [Comment("��������")]
        public string Description { get; set; }

        /// <summary>
        /// �������ʹ���
        /// </summary>
        [StringLength(32)]
        [Comment("�������ʹ���")]
        public string TaskType { get; set; }

        /// <summary>
        /// ����״̬λ��ʶ
        /// 0x01(1): �ȴ���
        /// 0x02(2): ������
        /// 0x04(4): �����
        /// 0x08(8): ʧ��
        /// 0x10(16): ��ȡ��
        /// 0x20(32): �ɱ�ȡ��
        /// 0x40(64): ����ֹ
        /// 0x80(128): ����ͣ
        /// </summary>
        [Comment("����״̬λ��ʶ")]
        public byte StatusFlags { get; set; }

        /// <summary>
        /// ��������λ��ʶ
        /// <list type="bullet">
        /// <item>0x01(1): �ɱ�ȡ��</item>
        /// <item>0x02(2): �ɱ���ͣ</item>
        /// <item>0x04(4): ��Ҫ����</item>
        /// <item>0x08(8): ��Ҫ����</item>
        /// <item>0x10(16): ϵͳ����</item>
        /// <item>0x20(32): ��������</item>
        /// <item>0x40(64): ����</item>
        /// <item>0x80(128): ����</item>
        /// </list>
        /// </summary>
        [Comment("��������λ��ʶ")]
        public byte PropertyFlags { get; set; }

        /// <summary>
        /// ���񴴽�ʱ�䣨UTC������ȷ������
        /// </summary>
        [Comment("����ʱ��(UTC)")]
        [Precision(3)] // ���������뼶��(3λС��)
        public DateTime CreateUtc { get; set; }

        /// <summary>
        /// ����ʼʱ�䣨UTC������ȷ������
        /// </summary>
        [Comment("��ʼʱ��(UTC)")]
        [Precision(3)] // ���������뼶��(3λС��)
        public DateTime? StartUtc { get; set; }

        /// <summary>
        /// �������ʱ�䣨UTC������ȷ������
        /// </summary>
        [Comment("����ʱ��(UTC)")]
        [Precision(3)] // ���������뼶��(3λС��)
        public DateTime? EndUtc { get; set; }

        /// <summary>
        /// ������ȣ�0-100��
        /// </summary>
        [Comment("������ȣ�0-100��")]
        public byte Progress { get; set; }

        /// <summary>
        /// �������ȼ���ԽСԽ�ߣ���0-��ߣ�1-�ߣ�2-�У�3-�ͣ�4-���
        /// </summary>
        [Comment("�������ȼ���0-���~4-���")]
        public byte Priority { get; set; }

        /// <summary>
        /// ������ID
        /// </summary>
        [Comment("������ID")]
        public Guid CreatorId { get; set; }

        /// <summary>
        /// ִ������Ĵ���������
        /// </summary>
        [StringLength(100)]
        [Comment("����������")]
        public string ProcessorClass { get; set; }

        /// <summary>
        /// ���������JSON��ʽ��
        /// </summary>
        [Comment("���������JSON��ʽ��")]
        public string Parameters { get; set; }

        /// <summary>
        /// ������/������Ϣ��JSON��ʽ��
        /// </summary>
        [Comment("������/������Ϣ��JSON��ʽ��")]
        public string ResultData { get; set; }

        /// <summary>
        /// ������Ϣ����4λΪ�����Դ���(0-15)����4λΪ������Դ���(0-15)
        /// </summary>
        [Comment("������Ϣ����4λΪ�����Դ�������4λΪ������Դ���")]
        public byte RetryInfo { get; set; }

        /// <summary>
        /// �´�����ʱ�䣨UTC������ȷ������
        /// </summary>
        [Comment("�´�����ʱ��(UTC)")]
        [Precision(3)] // ���������뼶��(3λС��)
        public DateTime? NextRetryUtc { get; set; }

        /// <summary>
        /// ������ʱ�䣨UTC������ȷ������
        /// </summary>
        [Comment("������ʱ��(UTC)")]
        [Precision(3)] // ���������뼶��(3λС��)
        public DateTime LastUpdateUtc { get; set; }

        /// <summary>
        /// ������ID���������������
        /// </summary>
        [Comment("������ID")]
        public Guid? ParentTaskId { get; set; }

        /// <summary>
        /// ����/��������ʶ��
        /// </summary>
        [StringLength(50)]
        [Comment("ִ�л�����ʶ��")]
        public string MachineId { get; set; }

        /// <summary>
        /// ����ʱ����
        /// </summary>
        [Comment("����ʱ����")]
        public short? TimeoutSeconds { get; set; }

        /// <summary>
        /// �⻧ID
        /// </summary>
        [Comment("�⻧ID")]
        public Guid? TenantId { get; set; }

        /// <summary>
        /// ��չ���ݣ�JSON��ʽ��
        /// </summary>
        [Comment("��չ���ݣ�JSON��ʽ��")]
        public string ExtData { get; set; }

        #region ״̬��־λ��������

        /// <summary>
        /// ��������Ϊ�ȴ���״̬
        /// </summary>
        public void SetWaiting()
        {
            StatusFlags = (byte)((StatusFlags & ~0x1E) | (byte)TaskStatusFlags.Waiting); // ��ʽת��Ϊbyte
        }

        /// <summary>
        /// ��������Ϊ������״̬
        /// </summary>
        public void SetRunning()
        {
            StatusFlags = (byte)((StatusFlags & ~0x1F) | (byte)TaskStatusFlags.Running); // ��ʽת��Ϊbyte
        }

        /// <summary>
        /// ��������Ϊ�����״̬
        /// </summary>
        public void SetCompleted()
        {
            StatusFlags = (byte)((StatusFlags & ~0x1B) | (byte)TaskStatusFlags.Completed); // ��ʽת��Ϊbyte
            Progress = 100; // ������Ϊ100%
        }

        /// <summary>
        /// ��������Ϊʧ��״̬
        /// </summary>
        public void SetFailed()
        {
            StatusFlags = (byte)((StatusFlags & ~0x17) | (byte)TaskStatusFlags.Failed); // ��ʽת��Ϊbyte
        }

        /// <summary>
        /// ��������Ϊ��ȡ��״̬
        /// </summary>
        public void SetCancelled()
        {
            StatusFlags = (byte)((StatusFlags & ~0x0F) | (byte)TaskStatusFlags.Cancelled); // ��ʽת��Ϊbyte
        }

        /// <summary>
        /// ��������Ϊ����ͣ״̬
        /// </summary>
        public void SetPaused()
        {
            StatusFlags = (byte)((StatusFlags & ~0x3F) | (byte)TaskStatusFlags.Paused); // ��ʽת��Ϊbyte
        }

        /// <summary>
        /// ��ȡ����ǰ״̬
        /// </summary>
        /// <returns>����״̬</returns>
        public TaskStatusFlags GetStatus()
        {
            if ((StatusFlags & (byte)TaskStatusFlags.Waiting) != 0) return TaskStatusFlags.Waiting;
            if ((StatusFlags & (byte)TaskStatusFlags.Running) != 0) return TaskStatusFlags.Running;
            if ((StatusFlags & (byte)TaskStatusFlags.Completed) != 0) return TaskStatusFlags.Completed;
            if ((StatusFlags & (byte)TaskStatusFlags.Failed) != 0) return TaskStatusFlags.Failed;
            if ((StatusFlags & (byte)TaskStatusFlags.Cancelled) != 0) return TaskStatusFlags.Cancelled;
            if ((StatusFlags & (byte)TaskStatusFlags.Paused) != 0) return TaskStatusFlags.Paused;
            return TaskStatusFlags.Waiting; // Ĭ��Ϊ�ȴ�״̬
        }

        /// <summary>
        /// ��������Ƿ���ָ��״̬
        /// </summary>
        /// <param name="status">Ҫ����״̬</param>
        /// <returns>�������ָ��״̬�򷵻�true�����򷵻�false</returns>
        public bool IsInStatus(TaskStatusFlags status)
        {
            return (StatusFlags & (byte)status) != 0; // ��ʽת��Ϊbyte
        }

        #endregion

        #region ���Ա�־λ��������

        /// <summary>
        /// �����������Ա�־
        /// </summary>
        /// <param name="flag">Ҫ���õı�־</param>
        /// <param name="value">��־ֵ</param>
        public void SetPropertyFlag(TaskPropertyFlags flag, bool value)
        {
            if (value)
                PropertyFlags |= (byte)flag; // ��ʽת��Ϊbyte
            else
                PropertyFlags &= (byte)~(byte)flag; // ��ʽת��Ϊbyte
        }

        /// <summary>
        /// ��������Ƿ���ָ�����Ա�־
        /// </summary>
        /// <param name="flag">Ҫ���ı�־</param>
        /// <returns>�����ָ����־�򷵻�true�����򷵻�false</returns>
        public bool HasPropertyFlag(TaskPropertyFlags flag)
        {
            return (PropertyFlags & (byte)flag) != 0; // ��ʽת��Ϊbyte
        }

        /// <summary>
        /// ���������Ƿ�ɱ�ȡ��
        /// </summary>
        /// <param name="value">�Ƿ�ɱ�ȡ��</param>
        public void SetCancellable(bool value)
        {
            SetPropertyFlag(TaskPropertyFlags.Cancellable, value);
        }

        #endregion

        #region ������Ϣ��������

        /// <summary>
        /// ��ȡ��ǰ���Դ���
        /// </summary>
        /// <returns>��ǰ���Դ���</returns>
        public int GetRetryCount()
        {
            return RetryInfo & 0x0F; // ��4λ
        }

        /// <summary>
        /// ��ȡ������Դ���
        /// </summary>
        /// <returns>������Դ���</returns>
        public int GetMaxRetries()
        {
            return (RetryInfo >> 4) & 0x0F; // ��4λ
        }

        /// <summary>
        /// ���õ�ǰ���Դ���
        /// </summary>
        /// <param name="count">���Դ���(0-15)</param>
        public void SetRetryCount(int count)
        {
            if (count < 0) count = 0;
            if (count > 15) count = 15;
            RetryInfo = (byte)((RetryInfo & 0xF0) | count);
        }

        /// <summary>
        /// ����������Դ���
        /// </summary>
        /// <param name="maxRetries">������Դ���(0-15)</param>
        public void SetMaxRetries(int maxRetries)
        {
            if (maxRetries < 0) maxRetries = 0;
            if (maxRetries > 15) maxRetries = 15;
            RetryInfo = (byte)((RetryInfo & 0x0F) | (maxRetries << 4));
        }

        /// <summary>
        /// �������Դ���
        /// </summary>
        /// <returns>���Ӻ�����Դ���</returns>
        public int IncrementRetryCount()
        {
            int currentRetries = GetRetryCount();
            if (currentRetries < 15) // ȷ�����������ֵ
                SetRetryCount(currentRetries + 1);
            return GetRetryCount();
        }

        #endregion
    }

    /// <summary>
    /// ����״̬��־λö��
    /// </summary>
    [Flags]
    public enum TaskStatusFlags : byte
    {
        /// <summary>
        /// �ȴ���
        /// </summary>
        Waiting = 0x01,

        /// <summary>
        /// ������
        /// </summary>
        Running = 0x02,

        /// <summary>
        /// �����
        /// </summary>
        Completed = 0x04,

        /// <summary>
        /// ʧ��
        /// </summary>
        Failed = 0x08,

        /// <summary>
        /// ��ȡ��
        /// </summary>
        Cancelled = 0x10,

        /// <summary>
        /// ����ͣ
        /// </summary>
        Paused = 0x80
    }

    /// <summary>
    /// �������Ա�־λö��
    /// </summary>
    [Flags]
    public enum TaskPropertyFlags : byte
    {
        /// <summary>
        /// �ɱ�ȡ��
        /// </summary>
        Cancellable = 0x01,

        /// <summary>
        /// �ɱ���ͣ
        /// </summary>
        Pausable = 0x02,

        /// <summary>
        /// ��Ҫ����
        /// </summary>
        Important = 0x04,

        /// <summary>
        /// ��Ҫ����
        /// </summary>
        RequireReport = 0x08,

        /// <summary>
        /// ϵͳ����
        /// </summary>
        SystemTask = 0x10,

        /// <summary>
        /// ��������
        /// </summary>
        Recurring = 0x20
    }
}
