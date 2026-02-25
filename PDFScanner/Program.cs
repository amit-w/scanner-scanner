using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System.Drawing;
using System.IO;
using Windows.UI.WebUI;
using static PdfSharp.Capabilities.Features;

record UserInput(string Name);

record FakeScanName(string FileName);

internal class SessionState
{
    public void ReplaceImage(Image new_image)
    {
        Console.WriteLine(":: replace image");
        currentImage = new_image;

        System.Diagnostics.Debug.WriteLine($"ReplaceImage: {images.Count}+1");
    }

    public void AddCurrent()
    {
        images.Add(currentImage ?? throw new Exception("no current image"));
        Console.WriteLine("AddCurrent. Length is now: " + images.Count);
        currentImage = null;
    }

    public List<Image> Take()
    {
        if (currentImage != null)
        {
            System.Diagnostics.Debug.WriteLine($"Take: {images.Count}+1");

            images.Add(currentImage);
            currentImage = null;
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"Take: {images.Count}");

        }

        var retVal = images;
        images = [];
        return retVal;
    }

    private Image? currentImage = null;
    private List<Image> images = [];

    public int Count { get { return images.Count; } }
}


class NoScanner : IScanner
{
    protected override Bitmap? ScanPageUnsafe()
    {
        throw new NotImplementedException();
    }
}

internal class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Optional: make sure console logging is enabled (default in dev)
        builder.Logging.AddConsole();

        var app = builder.Build();

        app.UseDefaultFiles();
        app.UseStaticFiles();

        //var scanner = new Scanner();

        // Serve a single HTML page at "/"
        //app.MapGet("/", async context =>
        //{

        //    context.Response.ContentType = "text/html; charset=utf-8";
        //    await context.Response.WriteAsync(html);
        //});

        //Bitmap currentImage = null;
        //var images = new List<Bitmap>();

        var state = new SessionState();
        //var scanner = new Scanner();
        var scanner = new NoScanner();  // TODO revert

        // Endpoint that prints to stdout
        app.MapPost("/scan", async (HttpContext context) =>
        {
            var image = scanner.ScanPage() ?? throw new Exception("image was null");
            await SendScan(context, image);

            return;
        });

        app.MapPost("/fakeScan", async (HttpContext context, FakeScanName fileName) =>
        {
            var imagePath = Path.Combine("wwwroot", "images", fileName.FileName + ".bmp");
            var bitmap = new StaticFileScanner(imagePath).ScanPage() ?? throw new Exception("no image");

            await SendScan(context, bitmap);
        });

        static async Task SendScan(HttpContext context, Bitmap bitmap)
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
            var rawImage = ms.ToArray();

            // state.ReplaceImage(bitmap);

            context.Response.ContentType = "image/json";
            await context.Response.Body.WriteAsync(rawImage);

            System.Diagnostics.Debug.WriteLine("page scanned");
            return;
        }

        app.MapPost("/approve", [Consumes("multipart/form-data")] async (HttpContext context) =>
        {
            Console.WriteLine(":: approve");
            var form = await context.Request.ReadFormAsync();
            var file = form.Files["file"];

            if (file != null)
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);

                var image = new Bitmap(ms);
                var image2 = new Bitmap(image);
                state.ReplaceImage(image);
            }

            state.AddCurrent();
            Console.WriteLine(":: /approve");

            return Results.Ok();
        });

        app.MapPost("/finish2", [Consumes("multipart/form-data")] async (HttpContext context) =>
        {
            Console.WriteLine(":: finish2");
            var form = await context.Request.ReadFormAsync();
            var filename = form["filename"][0] ?? throw new Exception("no filename provided");

            Console.WriteLine("amount of files: " + form.Files.Count);

            using var ms = new MemoryStream();
            foreach (var file in form.Files)
            {
                Console.WriteLine("> file");
                ms.Seek(0, SeekOrigin.Begin);
                await file.CopyToAsync(ms);

                var image = new Bitmap(ms);
                var image2 = new Bitmap(image);
                // TODO remove this "state" thing
                state.ReplaceImage(image);
                state.AddCurrent();
            }
            Console.WriteLine(state.Count);

            CreatePdf(state.Take(), filename);
            Console.WriteLine(":: /finish2");
            return Results.NoContent();

        });

        // app.MapGet("/debug-endpoints", () =>
        // {
        //     var endpoints = app.Services.GetRequiredService<EndpointDataSource>().Endpoints;
        //     var info = endpoints.Select(ep =>
        //     {
        //         var routePattern = (ep as RouteEndpoint)?.RoutePattern?.RawText ?? ep.DisplayName;
        //         var metadata = ep.Metadata.Select(m => m.GetType().Name).ToList();
        //         return new { routePattern, metadata };
        //     });

        //     return Results.Json(info);
        // });

        app.MapPost("/finish", (UserInput input) =>
        {
            CreatePdf(state.Take(), input.Name);
            System.Diagnostics.Debug.WriteLine($"world hello {input.Name}");
            return Results.NoContent();
        });

        // TODO use a file from APPDATA or an envvar or something like that to control url
        // TODO or even Properties/launch.json - it exists exactly for that
        //app.Urls.Add("http://...:53353");
        app.Run();
    }

    public static void CreatePdf(List<Image> images, string name)
    {
        var pdf = new PdfDocument();
        foreach (var img in images)
        {
            var page = pdf.AddPage();
            using var ms = new MemoryStream();
            System.Diagnostics.Debug.WriteLine($"Format: {img.PixelFormat}");
            img.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
            System.Diagnostics.Debug.WriteLine($"Ok height: {img.Height}");
            ms.Position = 0;

            using var xImg = XImage.FromStream(ms);
            double dpiY = xImg.VerticalResolution;
            double heightInPoints = xImg.PixelHeight * 72.0 / dpiY;
            page.Height = XUnit.FromPoint(heightInPoints);
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawImage(xImg, 0, 0, page.Width.Point, heightInPoints);
        }

        // TODO probably do it in a more robust way
        pdf.Save($"output\\{name}.pdf");
        Console.WriteLine("PDF saved as ManualScan.pdf");
    }
}


/* Pure JS Download
 * ================
function downloadData(data, filename, type = 'text/plain') {
  // Create a Blob object from the data
  const blob = new Blob([data], { type });
  // Generate a temporary URL for the Blob
  const url = URL.createObjectURL(blob);

  // Create a temporary anchor element
  const link = document.createElement('a');
  link.href = url;
  link.download = filename; // Suggests a filename for the download
  link.style.display = 'none'; // Hide the link

  // Append link to body, trigger click, and remove it
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);

  // Revoke the temporary URL to free up memory
  URL.revokeObjectURL(url);
}

// Usage example:
// Download a text file
downloadData('Hello, World!', 'greeting.txt');

// Download a CSV file
downloadData('name,age\nJohn,30', 'data.csv', 'text/csv');
 */