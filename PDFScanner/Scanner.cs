using NTwain;
using NTwain.Data;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Security.Principal;
using static PdfSharp.Capabilities.Features;
using static System.Net.Mime.MediaTypeNames;

abstract class IScanner
{
    public Bitmap? ScanPage()
    {
        var bitmap = this.ScanPageUnsafe();
        if (bitmap == null) { return null; }

        return new Bitmap(bitmap);
    }

    abstract protected Bitmap? ScanPageUnsafe();
}

class Scanner : IScanner
{
    public Scanner()
    {
        var appId = TWIdentity.CreateFromAssembly(DataGroups.Image, typeof(Program).Assembly);
        this.session = new TwainSession(appId);

        session.Open();

        var source = session.FirstOrDefault() ?? throw new Exception("no source");
        this.source = source;

        session.OpenSource(source.Name);

        scanEnded = new TaskCompletionSource<bool>();
        currentBitmap = null;

        session.DataTransferred += (s, e) =>
        {
            if (e.NativeData != nint.Zero)
            {
                using var stream = e.GetNativeImageStream();
                if (stream != null)
                {
                    using var bmp = new Bitmap(stream);
                    currentBitmap = (new Bitmap(bmp));
                }
            }

            scanEnded.SetResult(true);
        };
    }

    protected override Bitmap? ScanPageUnsafe()
    {
        // Acquire one page
        source.Enable(SourceEnableMode.NoUI, false, IntPtr.Zero);

        System.Diagnostics.Debug.WriteLine("started task");

        scanEnded.Task.Wait();
        System.Diagnostics.Debug.WriteLine("task ended");
        scanEnded = new TaskCompletionSource<bool>();
        System.Diagnostics.Debug.WriteLine("===");
        var bitmap = currentBitmap;
        System.Diagnostics.Debug.WriteLine("got bitmap");
        currentBitmap = null;

        return bitmap;
    }

    private readonly TwainSession session;
    private readonly DataSource source;
    private Bitmap? currentBitmap;
    private TaskCompletionSource<bool> scanEnded;
}

class StaticFileScanner(string path) : IScanner
{
    protected override Bitmap? ScanPageUnsafe()
    {
        using var file = File.OpenRead(path);
        return new Bitmap(file);
    }
}
