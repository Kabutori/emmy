using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Emmy.Core;

namespace DalamudEmmy.Companion;

public static class WindowCapture
{
    [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")]private static extern bool GetClientRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")]private static extern bool ClientToScreen(IntPtr window,ref NativePoint point);
    [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct NativePoint{public int X,Y;}
    public static Frame Capture(string client,long zone)
    {
        var handle=Process.GetCurrentProcess().MainWindowHandle;
        if(handle==IntPtr.Zero||GetForegroundWindow()!=handle||IsIconic(handle))throw new InvalidOperationException("Spielfenster muss sichtbar und im Vordergrund sein");
        if(!GetClientRect(handle,out var rect)||rect.Right<100||rect.Bottom<100||rect.Right>8192||rect.Bottom>8192)throw new InvalidOperationException("Fenstergröße ungültig");
        var origin=new NativePoint();if(!ClientToScreen(handle,ref origin))throw new InvalidOperationException("Fensterposition ungültig");
        // Central scene crop leaves the usual edge HUD and lower chat area out of the model image.
        var cropX=rect.Right*15/100;var cropY=rect.Bottom*8/100;
        var cropWidth=rect.Right*70/100;var cropHeight=rect.Bottom*60/100;
        using var bitmap=new Bitmap(cropWidth,cropHeight,PixelFormat.Format24bppRgb);
        using(var graphics=Graphics.FromImage(bitmap))graphics.CopyFromScreen(origin.X+cropX,origin.Y+cropY,0,0,bitmap.Size);
        if(GetForegroundWindow()!=handle)throw new InvalidOperationException("Fensterfokus während der Aufnahme geändert");
        var at=DateTimeOffset.UtcNow;
        var width=Math.Min(1024,bitmap.Width);using var resized=new Bitmap(bitmap,new Size(width,Math.Max(1,bitmap.Height*width/bitmap.Width)));
        var brightness=new List<int>();
        for(var y=0;y<resized.Height;y+=Math.Max(1,resized.Height/10))for(var x=0;x<resized.Width;x+=Math.Max(1,resized.Width/10)) {var pixel=resized.GetPixel(x,y);brightness.Add(pixel.R+pixel.G+pixel.B);}
        if(brightness.Max()-brightness.Min()<12)throw new InvalidOperationException("Aufnahme ist leer oder enthält keinen nutzbaren Spielinhalt");
        using var data=new MemoryStream();resized.Save(data,ImageFormat.Png);
        if(data.Length>2_800_000)throw new InvalidOperationException("Bild zu groß");
        return new(client,zone,at,"data:image/png;base64,"+Convert.ToBase64String(data.ToArray()));
    }
}
