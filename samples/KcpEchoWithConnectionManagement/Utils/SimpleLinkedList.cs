using System.Diagnostics;

namespace System.Collections.Generic
{
    internal class SimpleLinkedList<T>
    {
        // This LinkedList is a doubly-Linked circular list.
        internal SimpleLinkedListNode<T>? head;
        internal int count;
        internal int version;

        public int Count
        {
            get { return count; }
        }

        public SimpleLinkedListNode<T>? First
        {
            get { return head; }
        }

        public void AddLast(SimpleLinkedListNode<T> node)
        {
            ValidateNewNode(node);

            if (head == null)
            {
                InternalInsertNodeToEmptyList(node);
            }
            else
            {
                InternalInsertNodeBefore(head, node);
            }
            node.list = this;
        }

        public void Clear()
        {
            SimpleLinkedListNode<T>? current = head;
            while (current != null)
            {
                SimpleLinkedListNode<T> temp = current;
                current = current.Next;   // use Next the instead of "next", otherwise it will loop forever
                temp.Invalidate();
            }

            head = null;
            count = 0;
            version++;
        }

        public void Remove(SimpleLinkedListNode<T> node)
        {
            ValidateNode(node);
            InternalRemoveNode(node);
        }

        public bool TryRemove(SimpleLinkedListNode<T> node)
        {
            Debug.Assert(node is not null);
            if (node.list != this)
            {
                return false;
            }

            InternalRemoveNode(node);
            return true;
        }

        private void InternalInsertNodeBefore(SimpleLinkedListNode<T> node, SimpleLinkedListNode<T> newNode)
        {
            newNode.next = node;
            newNode.prev = node.prev;
            node.prev!.next = newNode;
            node.prev = newNode;
            version++;
            count++;
        }

        private void InternalInsertNodeToEmptyList(SimpleLinkedListNode<T> newNode)
        {
            Debug.Assert(head == null && count == 0, "LinkedList must be empty when this method is called!");
            newNode.next = newNode;
            newNode.prev = newNode;
            head = newNode;
            version++;
            count++;
        }

        internal void InternalRemoveNode(SimpleLinkedListNode<T> node)
        {
            Debug.Assert(node.list == this, "Deleting the node from another list!");
            Debug.Assert(head != null, "This method shouldn't be called on empty list!");
            if (node.next == node)
            {
                Debug.Assert(count == 1 && head == node, "this should only be true for a list with only one node");
                head = null;
            }
            else
            {
                node.next!.prev = node.prev;
                node.prev!.next = node.next;
                if (head == node)
                {
                    head = node.next;
                }
            }
            node.Invalidate();
            count--;
            version++;
        }

        internal static void ValidateNewNode(SimpleLinkedListNode<T> node)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            if (node.list != null)
            {
                throw new InvalidOperationException();
            }
        }

        internal void ValidateNode(SimpleLinkedListNode<T> node)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            if (node.list != this)
            {
                throw new InvalidOperationException();
            }
        }
    }

    // Note following class is not serializable since we customized the serialization of LinkedList.
    internal sealed class SimpleLinkedListNode<T>
    {
        internal SimpleLinkedList<T>? list;
        internal SimpleLinkedListNode<T>? next;
        internal SimpleLinkedListNode<T>? prev;
        internal T item;

        public SimpleLinkedListNode(T value)
        {
            item = value;
        }

        public SimpleLinkedListNode<T>? Next
        {
            get { return next == null || next == list!.head ? null : next; }
        }

        public T Value
        {
            get { return item; }
            set { item = value; }
        }

        /// <summary>Gets a reference to the value held by the node.</summary>
        public ref T ValueRef => ref item;

        internal void Invalidate()
        {
            list = null;
            next = null;
            prev = null;
        }
    }
}
