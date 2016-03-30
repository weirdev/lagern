using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Serialization;

namespace BackupCore
{
    [DataContract]
    public class SerializableKeyValuePair<K, V>
    {
        public K Key { get; set; }
        public V Value { get; set; }

        public SerializableKeyValuePair() { }

        public SerializableKeyValuePair(K key, V value)
        {
            Key = key;
            Value = value;
        }

        public SerializableKeyValuePair(KeyValuePair<K, V> keyvaluepair)
        {
            Key = keyvaluepair.Key;
            Value = keyvaluepair.Value;
        }
    }
}
