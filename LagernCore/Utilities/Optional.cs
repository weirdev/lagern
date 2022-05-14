using System;

namespace LagernCore.Utilities
{
    public class Optional<T> where T : class
    {
        private readonly T? value;
        private readonly bool isOk;

        private Optional(T? value, bool isOk)
        {
            if (isOk)
            {
                if (value == null)
                {
                    throw new InvalidOperationException("Value cannot be null when Optional initialized with Optional.Of()");
                }
                else
                {
                    this.value = value;
                }
            }
            else if (!isOk)
            {
                this.value = null;
            }
            this.isOk = isOk;
        }

        public static Optional<T> Of(T value)
        {
            return new(value, true);
        }

        public static Optional<T> Empty()
        {
            return new(null, false);
        }

        public T GetOrThrow()
        {
            if (isOk)
            {
                if (value == null)
                {
                    throw new InvalidOperationException("Value cannot be null when Optional initialized with Optional.Of()");
                }
                return value;
            }
            else
            {
                throw new InvalidOperationException("Cannot get value from empty Optional");
            }
        }

        public Optional<T2> Map<T2>(Func<T, T2> func) where T2 : class
        {
            if (isOk)
            {
                if (value == null)
                {
                    throw new InvalidOperationException("Value cannot be null when Optional initialized with Optional.Of()");
                }
                return Optional<T2>.Of(func.Invoke(value));
            }
            else
            {
                return Optional<T2>.Empty();
            }
        }

        public Optional<T2> FlatMap<T2>(Func<T, Optional<T2>> func) where T2 : class
        {
            if (isOk)
            {
                if (value == null)
                {
                    throw new InvalidOperationException("Value cannot be null when Optional initialized with Optional.Of()");
                }
                return func.Invoke(value);
            }
            else
            {
                return Optional<T2>.Empty();
            }
        }
    }
}
