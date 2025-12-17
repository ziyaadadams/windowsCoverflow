using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;

namespace WindowsCoverflow.Services
{
    internal static class WindowsGraphicsCaptureService
    {
        private const string GraphicsCaptureItemRuntimeClass = "Windows.Graphics.Capture.GraphicsCaptureItem";

        // WinRT interop
        [ComImport]
        [Guid("3628e81b-3cac-4c60-b7f4-23ce0e0c3356")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsCaptureItemInterop
        {
            GraphicsCaptureItem CreateForWindow(IntPtr window, ref Guid iid);
        }

        [DllImport("combase.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int RoGetActivationFactory(IntPtr hString, ref Guid iid, out IntPtr factory);

        [DllImport("combase.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString, int length, out IntPtr hString);

        [DllImport("combase.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int WindowsDeleteString(IntPtr hString);

        // D3D11 interop
        [Flags]
        private enum D3D11_CREATE_DEVICE_FLAG : uint
        {
            BGRA_SUPPORT = 0x20,
            DEBUG = 0x2
        }

        private enum D3D_DRIVER_TYPE
        {
            UNKNOWN = 0,
            HARDWARE = 1,
            WARP = 5
        }

        private enum D3D_FEATURE_LEVEL : uint
        {
            LEVEL_11_1 = 0xb100,
            LEVEL_11_0 = 0xb000,
            LEVEL_10_1 = 0xa100,
            LEVEL_10_0 = 0xa000
        }

        [DllImport("d3d11.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int D3D11CreateDevice(
            IntPtr pAdapter,
            D3D_DRIVER_TYPE DriverType,
            IntPtr Software,
            D3D11_CREATE_DEVICE_FLAG Flags,
            [In] D3D_FEATURE_LEVEL[] pFeatureLevels,
            uint FeatureLevels,
            uint SDKVersion,
            out IntPtr ppDevice,
            out D3D_FEATURE_LEVEL pFeatureLevel,
            out IntPtr ppImmediateContext);

        [DllImport("d3d11.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(
            IntPtr dxgiDevice,
            out IntPtr graphicsDevice);

        // SoftwareBitmap buffer access
        [ComImport]
        [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private unsafe interface IMemoryBufferByteAccess
        {
            void GetBuffer(out byte* buffer, out uint capacity);
        }

        public static bool IsSupported()
        {
            try
            {
                return GraphicsCaptureSession.IsSupported();
            }
            catch
            {
                return false;
            }
        }

        public static BitmapSource? TryCaptureWindow(IntPtr hwnd, int maxWidth, int maxHeight)
        {
            if (hwnd == IntPtr.Zero)
                return null;

            if (!IsSupported())
                return null;

            try
            {
                var item = CreateItemForWindow(hwnd);
                if (item == null)
                    return null;

                using var frameReady = new ManualResetEventSlim(false);
                Direct3D11CaptureFrame? capturedFrame = null;

                using var device = CreateD3DDevice();
                var size = item.Size;
                if (size.Width <= 1 || size.Height <= 1)
                    return null;

                var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                    device,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    1,
                    size);

                var session = framePool.CreateCaptureSession(item);
                try
                {
                    // Prefer not capturing cursor
                    try { session.IsCursorCaptureEnabled = false; } catch { }

                    TypedEventHandler<Direct3D11CaptureFramePool, object>? handler = null;
                    handler = (s, a) =>
                    {
                        try
                        {
                            var frame = s.TryGetNextFrame();
                            if (frame is null)
                                return;

                            capturedFrame?.Dispose();
                            capturedFrame = frame;
                            frameReady.Set();
                        }
                        catch { }
                    };

                    framePool.FrameArrived += handler;
                    session.StartCapture();

                    // Wait for one frame
                    if (!frameReady.Wait(TimeSpan.FromMilliseconds(120)))
                        return null;

                    if (capturedFrame == null)
                        return null;

                    using (capturedFrame)
                    {
                        var sb = SoftwareBitmap.CreateCopyFromSurfaceAsync(capturedFrame.Surface).AsTask().GetAwaiter().GetResult();
                        if (sb == null)
                            return null;

                        if (sb.BitmapPixelFormat != BitmapPixelFormat.Bgra8 || sb.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
                        {
                            sb = SoftwareBitmap.Convert(sb, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                        }

                        var source = SoftwareBitmapToBitmapSource(sb);
                        if (source == null)
                            return null;

                        // Scale down if needed
                        int w = source.PixelWidth;
                        int h = source.PixelHeight;
                        double scale = 1.0;
                        if (w > maxWidth) scale = Math.Min(scale, (double)maxWidth / w);
                        if (h > maxHeight) scale = Math.Min(scale, (double)maxHeight / h);

                        if (scale < 0.999)
                        {
                            var tb = new TransformedBitmap(source, new System.Windows.Media.ScaleTransform(scale, scale));
                            tb.Freeze();
                            return tb;
                        }

                        return source;
                    }
                }
                finally
                {
                    session?.Dispose();
                    framePool?.Dispose();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WGC capture failed: {ex.Message}");
                return null;
            }
        }

        private static GraphicsCaptureItem? CreateItemForWindow(IntPtr hwnd)
        {
            try
            {
                var interop = GetGraphicsCaptureItemInterop();
                Guid iid = typeof(GraphicsCaptureItem).GUID;
                return interop.CreateForWindow(hwnd, ref iid);
            }
            catch
            {
                return null;
            }
        }

        private static IGraphicsCaptureItemInterop GetGraphicsCaptureItemInterop()
        {
            IntPtr hString = IntPtr.Zero;
            IntPtr factoryPtr = IntPtr.Zero;

            try
            {
                var hr = WindowsCreateString(GraphicsCaptureItemRuntimeClass, GraphicsCaptureItemRuntimeClass.Length, out hString);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                Guid iid = typeof(IGraphicsCaptureItemInterop).GUID;
                hr = RoGetActivationFactory(hString, ref iid, out factoryPtr);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                return (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);
            }
            finally
            {
                if (factoryPtr != IntPtr.Zero)
                    Marshal.Release(factoryPtr);
                if (hString != IntPtr.Zero)
                    WindowsDeleteString(hString);
            }
        }

        private sealed class ComReleaser : IDisposable
        {
            private IntPtr _ptr;
            public ComReleaser(IntPtr ptr) => _ptr = ptr;
            public IntPtr Ptr => _ptr;
            public void Dispose()
            {
                if (_ptr != IntPtr.Zero)
                {
                    Marshal.Release(_ptr);
                    _ptr = IntPtr.Zero;
                }
            }
        }

        private static IDirect3DDevice CreateD3DDevice()
        {
            // Create a minimal D3D11 device and wrap it for WinRT capture.
            var featureLevels = new[]
            {
                D3D_FEATURE_LEVEL.LEVEL_11_1,
                D3D_FEATURE_LEVEL.LEVEL_11_0,
                D3D_FEATURE_LEVEL.LEVEL_10_1,
                D3D_FEATURE_LEVEL.LEVEL_10_0
            };

            IntPtr d3dDevice;
            IntPtr d3dContext;
            var hr = D3D11CreateDevice(
                IntPtr.Zero,
                D3D_DRIVER_TYPE.HARDWARE,
                IntPtr.Zero,
                D3D11_CREATE_DEVICE_FLAG.BGRA_SUPPORT,
                featureLevels,
                (uint)featureLevels.Length,
                7, // D3D11_SDK_VERSION
                out d3dDevice,
                out _,
                out d3dContext);

            if (hr < 0)
            {
                // Try WARP fallback
                hr = D3D11CreateDevice(
                    IntPtr.Zero,
                    D3D_DRIVER_TYPE.WARP,
                    IntPtr.Zero,
                    D3D11_CREATE_DEVICE_FLAG.BGRA_SUPPORT,
                    featureLevels,
                    (uint)featureLevels.Length,
                    7,
                    out d3dDevice,
                    out _,
                    out d3dContext);
            }

            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);

            // Immediate context can be released; we only need the device COM object.
            if (d3dContext != IntPtr.Zero)
                Marshal.Release(d3dContext);

            // Query IDXGIDevice from ID3D11Device
            var dxgiDeviceGuid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c"); // IDXGIDevice
            Marshal.QueryInterface(d3dDevice, ref dxgiDeviceGuid, out var dxgiDevice);

            using var d3dDeviceReleaser = new ComReleaser(d3dDevice);
            using var dxgiDeviceReleaser = new ComReleaser(dxgiDevice);

            var hr2 = CreateDirect3D11DeviceFromDXGIDevice(dxgiDeviceReleaser.Ptr, out var winrtDevice);
            if (hr2 < 0)
                Marshal.ThrowExceptionForHR(hr2);

            return (IDirect3DDevice)Marshal.GetObjectForIUnknown(winrtDevice);
        }

        private static unsafe BitmapSource? SoftwareBitmapToBitmapSource(SoftwareBitmap sb)
        {
            try
            {
                using var buffer = sb.LockBuffer(BitmapBufferAccessMode.Read);
                var desc = buffer.GetPlaneDescription(0);

                using var reference = buffer.CreateReference();
                var byteAccess = (IMemoryBufferByteAccess)reference;
                byteAccess.GetBuffer(out byte* data, out _);

                int width = sb.PixelWidth;
                int height = sb.PixelHeight;

                var wb = new WriteableBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
                wb.Lock();

                try
                {
                    byte* srcBase = data + desc.StartIndex;
                    byte* dstBase = (byte*)wb.BackBuffer;
                    int srcStride = desc.Stride;
                    int dstStride = wb.BackBufferStride;
                    int rowBytes = Math.Min(srcStride, dstStride);

                    for (int y = 0; y < height; y++)
                    {
                        Buffer.MemoryCopy(srcBase + y * srcStride, dstBase + y * dstStride, dstStride, rowBytes);
                    }

                    wb.AddDirtyRect(new System.Windows.Int32Rect(0, 0, width, height));
                }
                finally
                {
                    wb.Unlock();
                }

                wb.Freeze();
                return wb;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SoftwareBitmap conversion failed: {ex.Message}");
                return null;
            }
        }
    }
}
