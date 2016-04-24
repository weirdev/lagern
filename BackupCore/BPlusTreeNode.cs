using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Serialization;
using System.IO;
using System.Xml;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;

namespace BackupCore
{
    [DataContract(Name = "BPlusTreeNode")]
    class BPlusTreeNode : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        // Differs from/not saved to disk
        [DataMember]
        public bool Dirty { get; set; }

        private string parent;
        private string next;

        private ObservableCollection<byte[]> keys;
        private ObservableCollection<string> children;
        private ObservableCollection<BackupLocation> values;

        [DataMember]
        public string NodeID { get; private set; }

        [DataMember]
        public string Parent
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
        [DataMember]
        public string Next
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
        [DataMember]
        public int NodeSize { get; private set; }

        [DataMember]
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

        [DataMember]
        public bool IsLeafNode { get; private set; }

        // Size m
        [DataMember]
        public ObservableCollection<string> Children
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
        [DataMember]
        public ObservableCollection<BackupLocation> Values
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

        public BPlusTreeNode(string parent, bool isleafnode, int nodesize, string next=null)
        {
            NodeID = (new Guid()).ToString();
            Parent = parent;
            NodeSize = nodesize;
            IsLeafNode = isleafnode;
            Keys = new ObservableCollection<byte[]>();
            if (IsLeafNode)
            {
                Values = new ObservableCollection<BackupLocation>();
                Next = next;
            }
            else
            {
                Children = new ObservableCollection<string>();
            }
        }

        private void NotifyPropertyChanged([CallerMemberName] string propertyName="")
        {
            Dirty = true;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
