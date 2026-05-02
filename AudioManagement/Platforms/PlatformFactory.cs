using Serilog;

namespace AudioManagement.Platforms;

public interface IPlatformFactory<out T> where T : Platform
{
    public static abstract T CreateNew(ILogger logger);
}