using System;

namespace LagernCore.Utilities
{
    public class Result<T, E> where T : class where E : Exception
    {
        private readonly T? value;
        private readonly E? exception;
        private readonly bool isOk;

        private Result(T? value, E? exception, bool isOk)
        {
            if (isOk)
            {
                if (value == null)
                {
                    throw new InvalidOperationException("Value cannot be null when Result initialized with Result.ok()");
                }
                else
                {
                    this.value = value;
                    this.exception = null;
                }
            }
            else if (!isOk)
            {
                if (exception == null)
                {
                    throw new InvalidOperationException("Exception cannot be null when Result initialized with Result.Err()");
                }
                else
                {
                    this.value = null;
                    this.exception = exception;
                }
            }
            this.isOk = isOk;
        }

        public static Result<T, E> Ok(T value)
        {
            return new(value, null, true);
        }

        public static Result<T, E> Err(E exception)
        {
            return new(null, exception, false);
        }

        public T GetOrThrow()
        {
            if (isOk)
            {
                if (value == null)
                {
                    throw new InvalidOperationException("Value cannot be null when Result initialized with Result.ok()");
                }
                return value;
            }
            else
            {
                if (exception == null)
                {
                    throw new InvalidOperationException("Value cannot be null when Result initialized with Result.err()");
                }
                throw exception;
            }
        }

        public E GetErr()
        {
            if (isOk)
            {
                throw new InvalidOperationException("Cannot GetErr() on Result initialized with Result.Ok()");
            }
            else
            {
                if (exception == null)
                {
                    throw new InvalidOperationException("Exception cannot be null when Result initialized with Result.err()");
                }
                throw exception;
            }
        }

        public Result<T2, E> Map<T2>(Func<T, T2> func) where T2 : class
        {
            if (isOk)
            {
                if (value == null)
                {
                    throw new InvalidOperationException("Value cannot be null when Result initialized with Result.Ok()");
                }
                return Result<T2, E>.Ok(func.Invoke(value));
            }
            else
            {
                if (exception == null)
                {
                    throw new InvalidOperationException("Exception cannot be null when Result initialized with Result.Err()");
                }
                return Result<T2, E>.Err(exception);
            }
        }

        public Result<T, E2> MapErr<E2>(Func<E, E2> func) where E2 : Exception
        {
            if (!isOk)
            {
                if (exception == null)
                {
                    throw new InvalidOperationException("Exception cannot be null when Result initialized with Result.Err()");
                }
                return Result<T, E2>.Err(func.Invoke(exception));
            }
            else
            {
                if (value == null)
                {
                    throw new InvalidOperationException("Value cannot be null when Result initialized with Result.Ok()");
                }
                return Result<T, E2>.Ok(value);
            }
        }

        public Result<T2, E> FlatMap<T2>(Func<T, Result<T2, E>> func) where T2 : class
        {
            if (isOk)
            {
                if (value == null)
                {
                    throw new InvalidOperationException("Value cannot be null when Result initialized with Result.Ok()");
                }
                return func.Invoke(value);
            }
            else
            {
                if (exception == null)
                {
                    throw new InvalidOperationException("Exception cannot be null when Result initialized with Result.Err()");
                }
                return Result<T2, E>.Err(exception);
            }
        }
    }
}
