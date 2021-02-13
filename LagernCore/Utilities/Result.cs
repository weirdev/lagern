using System;
using System.Collections.Generic;
using System.Text;

namespace LagernCore.Utilities
{
    class Result<T, E> where T : class where E : Exception
    {
        private readonly T? value;
        private readonly E? exception;
        private readonly bool isOk;

        private Result(T? value, E? exception, bool isOk)
        {
            this.value = value;
            this.exception = exception;
            this.isOk = isOk;
        }

        public static Result<T, E> ok(T value)
        {
            return new Result<T, E>(value, null, true);
        }

        public static Result<T, E> err(E exception)
        {
            return new Result<T, E>(null, exception, false);
        }

        public T getOrThrow()
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

        public Result<T2, E> map<T2>(Func<T, T2> func) where T2 : class
        {
            if (isOk)
            {
                if (value == null)
                {
                    throw new InvalidOperationException("Value cannot be null when Result initialized with Result.ok()");
                }
                return Result<T2, E>.ok(func.Invoke(value));
            } 
            else
            {
                if (exception == null)
                {
                    throw new InvalidOperationException("Value cannot be null when Result initialized with Result.err()");
                }
                return Result<T2, E>.err(exception);
            }
        }
    }
}
