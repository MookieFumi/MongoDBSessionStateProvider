using System;

namespace MongoDBSessionStateProvider.Infrastructure
{
    public class DbCException : Exception
    {
        public DbCException()
        {
        }

        public DbCException(string message)
            : base(message)
        {
        }

        public DbCException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}