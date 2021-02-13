using System.Collections;

namespace LagernCore.Utilities
{
    public static class EnumerationTools
    {
        public static bool DeepSequenceEqual(this IEnumerable first, IEnumerable other)
        {
            if (first == null || other == null)
            {
                return first == null && other == null;
            }

            IEnumerator firstEnumerator = first.GetEnumerator();
            IEnumerator otherEnumerator = other.GetEnumerator();
            
            while (firstEnumerator.MoveNext() && otherEnumerator.MoveNext())
            {
                if (firstEnumerator.Current == null || otherEnumerator.Current == null)
                {
                    if (!(firstEnumerator.Current == null && otherEnumerator.Current == null))
                    {
                        return false;
                    }
                }
                else
                {

                    if (typeof(ICollection).IsAssignableFrom(firstEnumerator.Current.GetType()) &&
                        typeof(ICollection).IsAssignableFrom(otherEnumerator.Current.GetType()))
                    {
                        if (!((ICollection)firstEnumerator.Current).DeepSequenceEqual((ICollection)firstEnumerator.Current))
                        {
                            return false;
                        }
                    }
                    else if (typeof(IEnumerable).IsAssignableFrom(firstEnumerator.Current.GetType()) &&
                        typeof(IEnumerable).IsAssignableFrom(otherEnumerator.Current.GetType()))
                    {
                        if (!((IEnumerable) firstEnumerator.Current).DeepSequenceEqual((IEnumerable)firstEnumerator.Current))
                        {
                            return false;
                        }
                    }
                    else if (!firstEnumerator.Current.Equals(otherEnumerator.Current))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public static bool DeepSequenceEqual(this ICollection first, ICollection other)
        {
            if (first == null || other == null)
            {
                return first == null && other == null;
            }

            if (first.Count != other.Count)
            {
                return false;
            }

            return ((IEnumerable)first).DeepSequenceEqual(other);
        }
    }
}
