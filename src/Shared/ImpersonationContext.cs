using System;

namespace Shared
{
    public class ImpersonationContext : IDisposable
    {
        private readonly SecurityContext context;

        public ImpersonationContext(SecurityContext context)
        {
            this.context = context;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                context.RevertImpersonation();
            }
        }
    }
}