using Hangfire;

namespace MusicHub.Api.Jobs;

/// <summary>Creates each job (and what it needs) from a new dependency injection scope, like a request.</summary>
public sealed class ScopedJobActivator(IServiceScopeFactory scopes) : JobActivator
{
    public override JobActivatorScope BeginScope(JobActivatorContext context) => new Scope(scopes.CreateScope());

    private sealed class Scope(IServiceScope scope) : JobActivatorScope
    {
        public override object Resolve(Type type) => ActivatorUtilities.GetServiceOrCreateInstance(scope.ServiceProvider, type);

        public override void DisposeScope() => scope.Dispose();
    }
}
