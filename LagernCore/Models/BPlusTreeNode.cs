using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using System.ComponentModel;
using System.Collections.ObjectModel;

namespace BackupCore
{
    class BPlusTreeNode<T> : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private BPlusTreeNode<T>? parent;
        private BPlusTreeNode<T>? next;

        private ObservableCollection<byte[]> keys;
        private ObservableCollection<BPlusTreeNode<T>>? children;
        private ObservableCollection<T>? values;
        
        public BPlusTreeNode<T>? Parent
        {
            get { return parent; }
            set
            {
                if (value != parent)
                {
                    parent = value;
                    NotifyPropertyChanged();
                }
            }
        }

        // Only for leaf nodes makes a linked list for efficient in-order traversal
        public BPlusTreeNode<T>? Next
        {
            get { return next; }
            set
            {
                if (value != next)
                {
                    next = value;
                    NotifyPropertyChanged();
                }
            }
        }

        // m
        public int NodeSize { get; private set; }
        
        public ObservableCollection<byte[]> Keys
        {
            get { return keys; }
            set
            {
                if (value != keys)
                {
                    keys = value;
                    NotifyPropertyChanged();
                }
            }
        }
        
        public bool IsLeafNode { get; private set; }

        // Size m
        public ObservableCollection<BPlusTreeNode<T>>? Children
        {
            get { return children; }
            set
            {
                if (value != children)
                {
                    children = value;
                    NotifyPropertyChanged();
                }
            }
        }

        // Size m-1
        public ObservableCollection<T>? Values
        {
            get { return values; }
            set
            {
                if (value != values)
                {
                    values = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public BPlusTreeNode(BPlusTreeNode<T>? parent, bool isleafnode, int nodesize, BPlusTreeNode<T>? next=null)
        {
            Parent = parent;
            NodeSize = nodesize;
            IsLeafNode = isleafnode;
            keys = new ObservableCollection<byte[]>();
            if (IsLeafNode)
            {
                Values = new ObservableCollection<T>();
                Next = next;
            }
            else
            {
                Children = new ObservableCollection<BPlusTreeNode<T>>();
            }
        }

        private void NotifyPropertyChanged([CallerMemberName] string propertyName="")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
