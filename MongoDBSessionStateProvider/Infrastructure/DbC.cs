using System;

namespace MongoDBSessionStateProvider.Infrastructure
{
    public static class DbC
    {
        public static void Require(Func<dynamic, bool> condition, dynamic value, string message)
        {
            if (!condition(value))
            {
                throw new DbCException(message);
            }
        }

        public static void Require(Func<bool> condition, string message)
        {
            if (!condition())
            {
                throw new DbCException(message);
            }
        }

        public static void RequireNotEmpty(string value, string message)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new DbCException(message);
            }
        }

        public static void RequireIfNotEmpty(Func<dynamic, bool> condition, string value, string message)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }
            Require(condition, value, message);
        }

        public static void IntegerGreaterThanZero(int value)
        {
            if (value <= 0)
            {
                throw new DbCException(string.Format("{0} must be greater than 0", value));
            }
        }
    }
}
