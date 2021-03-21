using System;
using System.Collections.Generic;

namespace LagernCore.Utilities
{
    class Either<T1, T2> where T1 : class where T2 : class
    {
        private readonly T1? one;
        private readonly T2? two;
        private readonly bool oneElseTwo;

        private Either(T1? one, T2? two, bool oneElseTwo)
        {
            if (oneElseTwo)
            {
                if (one == null)
                {
                    throw new InvalidOperationException("One cannot be null when Either initialized with Either.one()");
                }
                else
                {
                    this.one = one;
                    this.two = null;
                }
            }
            else
            {
                if (two == null)
                {
                    throw new InvalidOperationException("Two cannot be null when Either initialized with Either.two()");
                }
                else
                {
                    this.one = null;
                    this.two = two;
                }
            }
            this.oneElseTwo = oneElseTwo;
        }

        public static Either<T1, T2> One(T1 one)
        {
            return new(one, null, true);
        }

        public static Either<T1, T2> Two(T2 two)
        {
            return new(null, two, false);
        }

        public Result<T1, KeyNotFoundException> GetOne()
        {
            if (oneElseTwo)
            {
                if (one == null)
                {
                    throw new InvalidOperationException("One cannot be null when Either initialized with Either.one()");
                } 
                else
                {
                    return Result<T1, KeyNotFoundException>.Ok(one);
                }
            }
            else
            {
                return Result<T1, KeyNotFoundException>.Err(new KeyNotFoundException("One not present when Either initialized with Either.Two()"));
            }
        }

        public Result<T2, KeyNotFoundException> GetTwo()
        {
            if (!oneElseTwo)
            {
                if (two == null)
                {
                    throw new InvalidOperationException("Two cannot be null when Either initialized with Either.two()");
                }
                else
                {
                    return Result<T2, KeyNotFoundException>.Ok(two);
                }
            }
            else
            {
                return Result<T2, KeyNotFoundException>.Err(new KeyNotFoundException("Two not present when Either initialized with Either.One()"));
            }
        }
    }
}
