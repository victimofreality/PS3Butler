namespace PS3Butler.Tests.TestInfrastructure;

internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
