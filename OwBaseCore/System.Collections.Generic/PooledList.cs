using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System.Collections.Generic
{
    /// <summary>
    /// һ������ List&lt;T&gt; �ķ��ͼ����࣬���ڲ��洢����ͨ�� ArrayPool&lt;T&gt;.Shared ���гػ���������գ�
    /// �Լ���Ƶ���������������� GC ѹ�����ڴ���Ƭ�������ڸ����ܳ����´�����ʱ���ݵ��ռ��봦��
    /// </summary>
    /// <remarks>
    /// ע�⣺����������ڶ��������ڳ��������ʺϳ��ڳ��С�ʹ����Ϻ������� Dispose �����黹��Դ��
    /// �����÷����� using �����ʹ�ô����ʵ����
    /// </remarks>
    /// <typeparam name="T">������Ԫ�ص�����</typeparam>
    public sealed class PooledList<T> : PooledListBase<T>
    {
        private const int DefaultCapacity = 8; // Ĭ�ϵĳ�ʼ����

        #region ���캯��
        /// <summary>��ʼ�� PooledList&lt;T&gt; �����ʵ��������ָ���ĳ�ʼ������</summary>
        /// <param name="capacity">��ʼ������Ĭ��Ϊ 8</param>
        /// <exception cref="ArgumentOutOfRangeException">capacity С�� 0</exception>
        public PooledList(int capacity = DefaultCapacity) : base(Math.Max(capacity, DefaultCapacity))
        {
        }

        /// <summary>��ʼ�� PooledList&lt;T&gt; �����ʵ������ʵ��������ָ�����ϸ��Ƶ�Ԫ��</summary>
        /// <param name="collection">һ�����ϣ���Ԫ�ر����Ƶ����б���</param>
        /// <exception cref="ArgumentNullException">collection Ϊ null</exception>
        public PooledList(IEnumerable<T> collection) : base(collection is ICollection<T> c ? c.Count : DefaultCapacity)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            AddRange(collection);
        }
        #endregion

        #region ��ǿ��List<T>���ݷ���
        /// <summary>������ָ��ν��ƥ���Ԫ�أ����������� PooledList&lt;T&gt; �е�һ��ƥ��Ԫ�صĴ��㿪ʼ������</summary>
        /// <param name="match">����Ҫ������Ԫ�ص�������ν��</param>
        /// <returns>����ҵ���һ����ָ��ν��ƥ���Ԫ�أ���Ϊ��Ԫ�صĴ��㿪ʼ������������Ϊ -1</returns>
        /// <exception cref="ArgumentNullException">match Ϊ null</exception>
        public int FindIndex(Predicate<T> match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            return FindIndex(0, Count, match);
        }

        /// <summary>��ָ����������ʼ������ָ��ν��ƥ���Ԫ�أ����������� PooledList&lt;T&gt; �е�һ��ƥ��Ԫ�صĴ��㿪ʼ������</summary>
        /// <param name="startIndex">���㿪ʼ��������ʼ����</param>
        /// <param name="match">����Ҫ������Ԫ�ص�������ν��</param>
        /// <returns>����ҵ���һ����ָ��ν��ƥ���Ԫ�أ���Ϊ��Ԫ�صĴ��㿪ʼ������������Ϊ -1</returns>
        /// <exception cref="ArgumentNullException">match Ϊ null</exception>
        /// <exception cref="ArgumentOutOfRangeException">startIndex ������Χ</exception>
        public int FindIndex(int startIndex, Predicate<T> match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            if (startIndex < 0 || startIndex > Count) throw new ArgumentOutOfRangeException(nameof(startIndex));
            return FindIndex(startIndex, Count - startIndex, match);
        }

        /// <summary>��ָ����������ʼ����ָ��������Ԫ������ָ��ν��ƥ���Ԫ�أ����������� PooledList&lt;T&gt; �е�һ��ƥ��Ԫ�صĴ��㿪ʼ������</summary>
        /// <param name="startIndex">���㿪ʼ��������ʼ����</param>
        /// <param name="count">Ҫ�����Ĳ����е�Ԫ����</param>
        /// <param name="match">����Ҫ������Ԫ�ص�������ν��</param>
        /// <returns>����ҵ���һ����ָ��ν��ƥ���Ԫ�أ���Ϊ��Ԫ�صĴ��㿪ʼ������������Ϊ -1</returns>
        /// <exception cref="ArgumentNullException">match Ϊ null</exception>
        /// <exception cref="ArgumentOutOfRangeException">startIndex �� count ������Χ</exception>
        public int FindIndex(int startIndex, int count, Predicate<T> match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            if (startIndex < 0 || startIndex > Count) throw new ArgumentOutOfRangeException(nameof(startIndex));
            if (count < 0 || startIndex + count > Count) throw new ArgumentOutOfRangeException(nameof(count));

            int endIndex = startIndex + count;
            for (int i = startIndex; i < endIndex; i++)
            {
                if (match(this[i])) return i;
            }
            return -1;
        }

        /// <summary>������ָ��ν�ʶ����������ƥ���Ԫ�أ������� PooledList&lt;T&gt; �еĵ�һ��ƥ��Ԫ��</summary>
        /// <param name="match">����Ҫ������Ԫ�ص�������ν��</param>
        /// <returns>����ҵ���ָ��ν�ʶ����������ƥ��ĵ�һ��Ԫ�أ���Ϊ��Ԫ�أ�����Ϊ���� T ��Ĭ��ֵ</returns>
        /// <exception cref="ArgumentNullException">match Ϊ null</exception>
        public T Find(Predicate<T> match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            for (int i = 0; i < Count; i++)
            {
                if (match(this[i])) return this[i];
            }
            return default;
        }

        /// <summary>������ָ��ν�ʶ����������ƥ�������Ԫ��</summary>
        /// <param name="match">����Ҫ������Ԫ�ص�������ν��</param>
        /// <returns>����ҵ���ָ��ν�ʶ����������ƥ���Ԫ�أ���Ϊ��ЩԪ����ɵ� PooledList&lt;T&gt;������Ϊ�յ� PooledList&lt;T&gt;</returns>
        /// <exception cref="ArgumentNullException">match Ϊ null</exception>
        public PooledList<T> FindAll(Predicate<T> match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            PooledList<T> list = new PooledList<T>();
            for (int i = 0; i < Count; i++)
            {
                if (match(this[i])) list.Add(this[i]);
            }
            return list;
        }

        /// <summary>������ָ��ν��ƥ���Ԫ�أ����������� PooledList&lt;T&gt; �����һ��ƥ��Ԫ�صĴ��㿪ʼ������</summary>
        /// <param name="match">����Ҫ������Ԫ�ص�������ν��</param>
        /// <returns>����ҵ����һ����ָ��ν��ƥ���Ԫ�أ���Ϊ��Ԫ�صĴ��㿪ʼ������������Ϊ -1</returns>
        /// <exception cref="ArgumentNullException">match Ϊ null</exception>
        public int FindLastIndex(Predicate<T> match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            return FindLastIndex(Count - 1, Count, match);
        }

        /// <summary>��ָ����������ʼ��ǰ������ָ��ν��ƥ���Ԫ�أ����������� PooledList&lt;T&gt; �����һ��ƥ��Ԫ�صĴ��㿪ʼ������</summary>
        /// <param name="startIndex">���㿪ʼ�������������ʼ����</param>
        /// <param name="match">����Ҫ������Ԫ�ص�������ν��</param>
        /// <returns>����ҵ����һ����ָ��ν��ƥ���Ԫ�أ���Ϊ��Ԫ�صĴ��㿪ʼ������������Ϊ -1</returns>
        /// <exception cref="ArgumentNullException">match Ϊ null</exception>
        /// <exception cref="ArgumentOutOfRangeException">startIndex ������Χ</exception>
        public int FindLastIndex(int startIndex, Predicate<T> match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            if (Count == 0) return -1;
            if (startIndex >= Count) throw new ArgumentOutOfRangeException(nameof(startIndex));
            return FindLastIndex(startIndex, startIndex + 1, match);
        }

        /// <summary>��ָ��������ʼ�������ָ��������Ԫ������ָ��ν��ƥ���Ԫ�أ����������� PooledList&lt;T&gt; �����һ��ƥ��Ԫ�صĴ��㿪ʼ������</summary>
        /// <param name="startIndex">���㿪ʼ�������������ʼ����</param>
        /// <param name="count">Ҫ�����Ĳ����е�Ԫ����</param>
        /// <param name="match">����Ҫ������Ԫ�ص�������ν��</param>
        /// <returns>����ҵ����һ����ָ��ν��ƥ���Ԫ�أ���Ϊ��Ԫ�صĴ��㿪ʼ������������Ϊ -1</returns>
        /// <exception cref="ArgumentNullException">match Ϊ null</exception>
        /// <exception cref="ArgumentOutOfRangeException">startIndex �� count ������Χ</exception>
        public int FindLastIndex(int startIndex, int count, Predicate<T> match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            if (Count == 0)
            {
                if (startIndex != -1) throw new ArgumentOutOfRangeException(nameof(startIndex));
                return -1;
            }
            if (startIndex < 0 || startIndex >= Count) throw new ArgumentOutOfRangeException(nameof(startIndex));
            if (count < 0 || startIndex - count + 1 < 0) throw new ArgumentOutOfRangeException(nameof(count));

            int endIndex = startIndex - count + 1;
            for (int i = startIndex; i >= endIndex; i--)
            {
                if (match(this[i])) return i;
            }
            return -1;
        }

        /// <summary>������ָ��ν�ʶ����������ƥ���Ԫ�أ������� PooledList&lt;T&gt; �е����һ��ƥ��Ԫ��</summary>
        /// <param name="match">����Ҫ������Ԫ�ص�������ν��</param>
        /// <returns>����ҵ���ָ��ν�ʶ����������ƥ������һ��Ԫ�أ���Ϊ��Ԫ�أ�����Ϊ���� T ��Ĭ��ֵ</returns>
        /// <exception cref="ArgumentNullException">match Ϊ null</exception>
        public T FindLast(Predicate<T> match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            for (int i = Count - 1; i >= 0; i--)
            {
                if (match(this[i])) return this[i];
            }
            return default;
        }

        /// <summary>ȷ�� PooledList&lt;T&gt; �е�ÿ��Ԫ���Ƿ���ָ��ν�ʶ��������ƥ��</summary>
        /// <param name="match">����Ҫ������Ԫ�ص�������ν��</param>
        /// <returns>��� PooledList&lt;T&gt; �е�ÿ��Ԫ�ض���ָ��ν�ʶ��������ƥ�䣬��Ϊ true������Ϊ false</returns>
        /// <exception cref="ArgumentNullException">match Ϊ null</exception>
        public bool TrueForAll(Predicate<T> match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            for (int i = 0; i < Count; i++)
            {
                if (!match(this[i])) return false;
            }
            return true;
        }

        /// <summary>�� PooledList&lt;T&gt; �е�ÿ��Ԫ��ִ��ָ������</summary>
        /// <param name="action">Ҫ�� PooledList&lt;T&gt; ��ÿ��Ԫ��ִ�е�ί��</param>
        /// <exception cref="ArgumentNullException">action Ϊ null</exception>
        public void ForEach(Action<T> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            for (int i = 0; i < Count; i++) action(this[i]);
        }

        /// <summary>��ָ��������ʼ���� PooledList&lt;T&gt; ���������󣬲����ص�һ��ƥ����Ĵ��㿪ʼ������</summary>
        /// <param name="item">Ҫ�� PooledList&lt;T&gt; �ж�λ�Ķ��󣬶����������ͣ���ֵ����Ϊ null</param>
        /// <param name="index">���㿪ʼ��������ʼ����</param>
        /// <returns>�� index ��ʼ������� PooledList&lt;T&gt; ���ҵ� item����Ϊ����ĵ�һ��ƥ����Ĵ��㿪ʼ������������Ϊ -1</returns>
        /// <exception cref="ArgumentOutOfRangeException">index ������Χ</exception>
        public int IndexOf(T item, int index)
        {
            if (index < 0 || index > Count) throw new ArgumentOutOfRangeException(nameof(index));
            return Array.IndexOf(Items, item, index, Count - index);
        }

        /// <summary>�� PooledList&lt;T&gt; �ڣ���ָ����������ʼ������ָ��������Ԫ�أ��������󣬲����ص�һ��ƥ����Ĵ��㿪ʼ������</summary>
        /// <param name="item">Ҫ�� PooledList&lt;T&gt; �ж�λ�Ķ��󣬶����������ͣ���ֵ����Ϊ null</param>
        /// <param name="index">���㿪ʼ��������ʼ����</param>
        /// <param name="count">Ҫ�����������е�Ԫ����</param>
        /// <returns>�� index ��ʼ���� count ��Ԫ�ط�Χ�ڣ������ PooledList&lt;T&gt; ���ҵ� item����Ϊ����ĵ�һ��ƥ����Ĵ��㿪ʼ������������Ϊ -1</returns>
        /// <exception cref="ArgumentOutOfRangeException">index �� count ������Χ</exception>
        public int IndexOf(T item, int index, int count)
        {
            if (index < 0 || index > Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (count < 0 || index + count > Count) throw new ArgumentOutOfRangeException(nameof(count));
            return Array.IndexOf(Items, item, index, count);
        }

        /// <summary>�� PooledList&lt;T&gt; ���Ƴ���ָ��ν�ʶ����������ƥ�������Ԫ��</summary>
        /// <param name="match">����Ҫ�Ƴ���Ԫ�ص�������ν��</param>
        /// <returns>�� PooledList&lt;T&gt; ���Ƴ���Ԫ����Ŀ</returns>
        /// <exception cref="ArgumentNullException">match Ϊ null</exception>
        public int RemoveAll(Predicate<T> match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));

            int freeIndex = 0;
            // �ҵ���һ��Ҫɾ����Ԫ��
            while (freeIndex < Count && !match(this[freeIndex])) freeIndex++;
            if (freeIndex >= Count) return 0;

            int current = freeIndex + 1;
            while (current < Count)
            {
                while (current < Count && match(this[current])) current++;
                if (current < Count) this[freeIndex++] = this[current++];
            }

            int removed = Count - freeIndex;
            // �Ƴ�Ԫ��
            for (int i = 0; i < removed; i++)
                RemoveAt(Count - 1);

            return removed;
        }

        /// <summary>�� PooledList&lt;T&gt; ���Ƴ�ָ����Χ��Ԫ��</summary>
        /// <param name="index">Ҫ�Ƴ��ĵ�һ��Ԫ�صĴ��㿪ʼ������</param>
        /// <param name="count">Ҫ�Ƴ���Ԫ����</param>
        /// <exception cref="ArgumentOutOfRangeException">index �� count ������Χ</exception>
        public void RemoveRange(int index, int count)
        {
            if (index < 0 || index > Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (count < 0 || index + count > Count) throw new ArgumentOutOfRangeException(nameof(count));

            // �������ǰɾ������������Ҫ�ƶ�Ԫ��
            for (int i = index + count - 1; i >= index; i--)
                RemoveAt(i);
        }

        /// <summary>��ָ���������������ϵ�Ԫ�ز��� PooledList&lt;T&gt;</summary>
        /// <param name="index">Ӧ�ڴ˴�������Ԫ�صĴ��㿪ʼ������</param>
        /// <param name="collection">Ҫ����ļ��ϣ����ϱ�����Ϊ null���������԰���Ϊ null ��Ԫ��</param>
        /// <exception cref="ArgumentNullException">collection Ϊ null</exception>
        /// <exception cref="ArgumentOutOfRangeException">index С�� 0 ����� Count</exception>
        public void InsertRange(int index, IEnumerable<T> collection)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if ((uint)index > (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));

            foreach (var item in collection)
                Insert(index++, item);
        }

        /// <summary>��ָ��������Ԫ�ش�Դ���鸴�Ƶ� PooledList&lt;T&gt;</summary>
        /// <param name="source">Ҫ���и���Ԫ�ص�Դ����</param>
        /// <param name="sourceIndex">Դ�����п�ʼ���Ƶ�����</param>
        /// <param name="destinationIndex">PooledList&lt;T&gt; �п�ʼճ��������</param>
        /// <param name="count">Ҫ���Ƶ�Ԫ����</param>
        /// <exception cref="ArgumentNullException">source Ϊ null</exception>
        /// <exception cref="ArgumentOutOfRangeException">sourceIndex��destinationIndex �� count ������Χ</exception>
        /// <exception cref="ArgumentException">Դ������û���㹻��Ԫ�ؿɸ���</exception>
        public void CopyFrom(T[] source, int sourceIndex, int destinationIndex, int count)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (sourceIndex < 0) throw new ArgumentOutOfRangeException(nameof(sourceIndex));
            if (destinationIndex < 0 || destinationIndex > Count) throw new ArgumentOutOfRangeException(nameof(destinationIndex));
            if (count < 0 || sourceIndex + count > source.Length || destinationIndex + count > Count) throw new ArgumentOutOfRangeException(nameof(count));

            for (int i = 0; i < count; i++)
                this[destinationIndex + i] = source[sourceIndex + i];
        }
        #endregion
    }
}
