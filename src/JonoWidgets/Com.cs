// COM class factory + registration for the widget provider.
// Adapted from Microsoft's WindowsAppSDK-Samples (Samples/Widgets/cs-console-packaged/WidgetHelper), MIT License.

using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets.Providers;
using WinRT;

namespace JonoWidgets;

internal static class ComGuids
{
    public const string IClassFactory = "00000001-0000-0000-C000-000000000046";
    public const string IUnknown = "00000000-0000-0000-C000-000000000046";
}

[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid(ComGuids.IClassFactory)]
internal interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);

    [PreserveSig]
    int LockServer(bool fLock);
}

internal static class ClassObject
{
    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rclsid,
        [MarshalAs(UnmanagedType.IUnknown)] object pUnk,
        uint dwClsContext,
        uint flags,
        out uint lpdwRegister);

    [DllImport("ole32.dll")]
    private static extern int CoRevokeClassObject(uint dwRegister);

    public static uint Register(Guid clsid, object factory)
    {
        // CLSCTX_LOCAL_SERVER, REGCLS_MULTIPLEUSE
        int hr = CoRegisterClassObject(clsid, factory, 0x4, 0x1, out uint cookie);
        if (hr != 0) Marshal.ThrowExceptionForHR(hr);
        return cookie;
    }

    public static void Revoke(uint cookie) => CoRevokeClassObject(cookie);
}

internal sealed class WidgetProviderFactory<T> : IClassFactory where T : IWidgetProvider, new()
{
    private const int CLASS_E_NOAGGREGATION = -2147221232;
    private const int E_NOINTERFACE = -2147467262;

    public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
    {
        ppvObject = IntPtr.Zero;
        if (pUnkOuter != IntPtr.Zero) Marshal.ThrowExceptionForHR(CLASS_E_NOAGGREGATION);

        if (riid == typeof(T).GUID || riid == Guid.Parse(ComGuids.IUnknown))
        {
            ppvObject = MarshalInspectable<IWidgetProvider>.FromManaged(new T());
        }
        else
        {
            Marshal.ThrowExceptionForHR(E_NOINTERFACE);
        }
        return 0;
    }

    int IClassFactory.LockServer(bool fLock) => 0;
}

/// <summary>Registers the provider's class factory; disposing revokes it.</summary>
internal sealed class Registration<T> : IDisposable where T : IWidgetProvider, new()
{
    private readonly uint cookie;
    private readonly WidgetProviderFactory<T> factory = new();
    private bool disposed;

    public Registration()
    {
        cookie = ClassObject.Register(typeof(T).GUID, factory);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ClassObject.Revoke(cookie);
        GC.KeepAlive(factory);
    }
}
