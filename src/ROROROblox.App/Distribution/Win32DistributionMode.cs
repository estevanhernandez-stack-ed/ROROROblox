using Windows.Win32;
using Windows.Win32.Foundation;

namespace ROROROblox.App.Distribution;

/// <summary>
/// Real distribution-mode probe. <c>GetCurrentPackageFullName</c> returns
/// <see cref="WIN32_ERROR.APPMODEL_ERROR_NO_PACKAGE"/> when the process has no package identity
/// (unpackaged) and <c>ERROR_INSUFFICIENT_BUFFER</c> when it does (packaged, because we pass a
/// zero-length buffer just to probe). Anything other than the no-package code means packaged.
/// </summary>
internal sealed class Win32DistributionMode : IDistributionMode
{
    public bool IsPackaged
    {
        get
        {
            uint length = 0;
            // GetCurrentPackageFullName is [SupportedOSPlatform("windows8.0")]; RoRoRo's real floor is
            // Windows 10/11 already (WPF-UI, WebView2, MSIX), so the platform-compat analyzer is moot here.
#pragma warning disable CA1416
            var rc = PInvoke.GetCurrentPackageFullName(ref length, null);
#pragma warning restore CA1416
            return rc != WIN32_ERROR.APPMODEL_ERROR_NO_PACKAGE;
        }
    }
}
