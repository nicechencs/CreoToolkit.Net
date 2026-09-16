using CreoToolkit.App;

namespace CreoToolkit.Host;

/// <summary>
/// <see cref="CreoApplicationLoader.CreateLoaded"/> 的装载结果。
/// Framework 默认 AppDomain 装载，无独立卸载上下文。
/// </summary>
public readonly record struct LoadedApplication(ICreoApplication App);
