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
        current_bitmap = null;

        session.DataTransferred += (s, e) =>
        {
            if (e.NativeData != nint.Zero)
            {
                using var stream = e.GetNativeImageStream();
                if (stream != null)
                {
                    using var bmp = new Bitmap(stream);
                    current_bitmap = (new Bitmap(bmp));
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
        var bitmap = current_bitmap;
        System.Diagnostics.Debug.WriteLine("got bitmap");
        current_bitmap = null;

        return bitmap;
    }

    private readonly TwainSession session;
    private readonly DataSource source;
    private Bitmap? current_bitmap;
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

class State(IScanner scanner)
{
    public void ScanPage()
    {
        var image = scanner.ScanPage();
        if (image != null)
        {
            this.Images.Add(image);
        }
        System.Diagnostics.Debug.WriteLine("</ScanPage>");
    }

    public void ExportToPdf()
    {
        var pdf = new PdfDocument();
        foreach (var img in Images)
        {
            var page = pdf.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            using var ms = new MemoryStream();
            img.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;

            using var xImg = XImage.FromStream(ms);
            gfx.DrawImage(xImg, 0, 0, page.Width.Point, page.Height.Point);
        }

        pdf.Save("ManualScan.pdf");
        Console.WriteLine("PDF saved as ManualScan.pdf");
    }

    public List<Bitmap> Images { get; } = [];
    //private readonly List<Bitmap> images;
    private readonly IScanner scanner = scanner;
}
