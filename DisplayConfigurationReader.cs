using System.Runtime.InteropServices;
using System.Windows.Media;

namespace DisplayNotify;

internal sealed record DisplayInfo(string Name, string Connection, bool IsInternal, string PhysicalSize)
{
    public Brush BadgeBackground => new SolidColorBrush(IsInternal ? Color.FromRgb(22, 163, 74) : Color.FromRgb(37, 99, 235));
}

internal static class DisplayConfigurationReader
{
    private const uint QdcOnlyActivePaths = 0x00000002;
    private const uint GetSourceName = 1;
    private const uint GetTargetName = 2;
    private const uint Internal = 0x80000000;
    private const int HorzSize = 4;
    private const int VertSize = 6;

    public static IReadOnlyList<DisplayInfo> GetActiveDisplays()
    {
        ThrowIfFailed(GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount));
        if (pathCount == 0)
        {
            return Array.Empty<DisplayInfo>();
        }

        var paths = new DisplayConfigPathInfo[pathCount];
        var modes = new DisplayConfigModeInfo[modeCount];
        ThrowIfFailed(QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero));

        var displays = new List<DisplayInfo>();
        for (var index = 0; index < pathCount; index++)
        {
            var path = paths[index];
            if (!TryGetTargetName(path.TargetInfo, out var targetName))
            {
                continue;
            }

            var isInternal = targetName.OutputTechnology == Internal;
            var name = string.IsNullOrWhiteSpace(targetName.MonitorFriendlyDeviceName)
                ? $"显示器 {index + 1}"
                : targetName.MonitorFriendlyDeviceName.Trim();
            var physicalSize = TryGetSourceName(path.SourceInfo, out var sourceName)
                ? GetPhysicalSize(sourceName.ViewGdiDeviceName)
                : "物理尺寸未报告";
            displays.Add(new DisplayInfo(name, isInternal ? "内部显示器" : ToConnectionName(targetName.OutputTechnology), isInternal, physicalSize));
        }
        return displays;
    }

    private static bool TryGetTargetName(DisplayConfigPathTargetInfo targetInfo, out DisplayConfigTargetDeviceName targetName)
    {
        targetName = new DisplayConfigTargetDeviceName
        {
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = GetTargetName,
                Size = (uint)Marshal.SizeOf<DisplayConfigTargetDeviceName>(),
                AdapterId = targetInfo.AdapterId,
                Id = targetInfo.Id
            }
        };
        return DisplayConfigGetDeviceInfo(ref targetName) == 0;
    }

    private static bool TryGetSourceName(DisplayConfigPathSourceInfo sourceInfo, out DisplayConfigSourceDeviceName sourceName)
    {
        sourceName = new DisplayConfigSourceDeviceName
        {
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = GetSourceName,
                Size = (uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>(),
                AdapterId = sourceInfo.AdapterId,
                Id = sourceInfo.Id
            }
        };
        return DisplayConfigGetDeviceInfo(ref sourceName) == 0;
    }

    private static string GetPhysicalSize(string gdiDeviceName)
    {
        if (string.IsNullOrWhiteSpace(gdiDeviceName))
        {
            return "物理尺寸未报告";
        }

        var deviceContext = CreateDC("DISPLAY", gdiDeviceName, null, IntPtr.Zero);
        if (deviceContext == IntPtr.Zero)
        {
            return "物理尺寸未报告";
        }

        try
        {
            var widthMm = GetDeviceCaps(deviceContext, HorzSize);
            var heightMm = GetDeviceCaps(deviceContext, VertSize);
            if (widthMm <= 0 || heightMm <= 0)
            {
                return "物理尺寸未报告";
            }

            var diagonalInches = Math.Sqrt(widthMm * widthMm + heightMm * heightMm) / 25.4;
            return $"{diagonalInches:F1}\" ({widthMm}×{heightMm}mm)";
        }
        finally
        {
            DeleteDC(deviceContext);
        }
    }

    private static void ThrowIfFailed(int error)
    {
        if (error != 0)
        {
            throw new InvalidOperationException($"Windows 显示配置 API 返回错误 {error}。");
        }
    }

    // DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY values, matching the Windows SDK / Monitorian mapping.
    private static string ToConnectionName(uint technology) => technology switch
    {
        0 => "VGA",
        1 or 2 or 3 or 8 => "模拟视频",
        4 => "DVI",
        5 => "HDMI",
        6 => "LVDS",
        9 => "SDI",
        10 or 11 or 18 => "DisplayPort",
        12 or 13 => "UDI",
        14 => "SDTV Dongle",
        15 => "Miracast / 无线显示",
        16 => "间接有线显示",
        17 => "间接虚拟显示",
        0xffffffff => "其他接口",
        _ => "未知接口"
    };

    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] DisplayConfigPathInfo[] paths, ref uint modeCount, [Out] DisplayConfigModeInfo[] modes, IntPtr topology);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetDeviceName request);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName request);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateDC(string driver, string? device, string? output, IntPtr initData);
    [DllImport("gdi32.dll")] private static extern int DeleteDC(IntPtr deviceContext);
    [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(IntPtr deviceContext, int index);

    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint LowPart; public int HighPart; }
    [StructLayout(LayoutKind.Sequential)] private struct DisplayConfigRational { public uint Numerator; public uint Denominator; }
    [StructLayout(LayoutKind.Sequential)] private struct DisplayConfigPathSourceInfo { public Luid AdapterId; public uint Id; public uint ModeInfoIdx; public uint StatusFlags; }
    [StructLayout(LayoutKind.Sequential)] private struct DisplayConfigPathTargetInfo { public Luid AdapterId; public uint Id; public uint ModeInfoIdx; public uint OutputTechnology; public uint Rotation; public uint Scaling; public DisplayConfigRational RefreshRate; public uint ScanLineOrdering; [MarshalAs(UnmanagedType.Bool)] public bool TargetAvailable; public uint StatusFlags; }
    [StructLayout(LayoutKind.Sequential)] private struct DisplayConfigPathInfo { public DisplayConfigPathSourceInfo SourceInfo; public DisplayConfigPathTargetInfo TargetInfo; public uint Flags; }
    [StructLayout(LayoutKind.Explicit, Size = 64)] private struct DisplayConfigModeInfo { [FieldOffset(0)] public uint InfoType; [FieldOffset(4)] public uint Id; [FieldOffset(8)] public Luid AdapterId; }
    [StructLayout(LayoutKind.Sequential)] private struct DisplayConfigDeviceInfoHeader { public uint Type; public uint Size; public Luid AdapterId; public uint Id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DisplayConfigTargetDeviceName { public DisplayConfigDeviceInfoHeader Header; public uint Flags; public uint OutputTechnology; public ushort EdidManufactureId; public ushort EdidProductCodeId; public uint ConnectorInstance; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string MonitorFriendlyDeviceName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string MonitorDevicePath; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DisplayConfigSourceDeviceName { public DisplayConfigDeviceInfoHeader Header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string ViewGdiDeviceName; }
}