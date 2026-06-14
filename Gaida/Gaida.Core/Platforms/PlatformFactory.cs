using Serilog;

namespace Gaida.Core.Platforms;

public interface IPlatformFactory<out T> where T : Platform
{
    public static abstract T CreateNew(ILogger logger);
}