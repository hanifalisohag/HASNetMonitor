#if NETFRAMEWORK
// .NET Framework has no IsExternalInit type, which the C# compiler needs for the
// `init` accessors and records used across the app. Supplying it here lets the same
// source build for both targets.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
#endif
