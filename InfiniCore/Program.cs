using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.Extensions.FileProviders;
using System.Text.Json;
using InfiniCore.FileTree;
using InfiniCore.ImageHandle;
using System.Net;
using System.Net.Sockets;

namespace InfiniCore
{
    public class Program
    {
        public class ServerFilesConfig
        {
            public string RootPath { get; set; }
            public string PagePath { get; set; }

            public string[] NameOverrides { get; set; }

            /// <summary>
            /// Leave empty to use same address as file server
            /// </summary>
            public string MaskHost { get; set; } = string.Empty;
            public int MaskPort { get; set; } = 7880;

            /// <summary>
            /// Leave empty to use same address as file server
            /// </summary>
            public string InPaintHost { get; set; } = string.Empty;
            public int InPaintPort { get; set; } = 7860;

            public ServerFilesConfig(string rootPath, string pagePath)
            {
                RootPath = rootPath;
                PagePath = pagePath;

                NameOverrides = Array.Empty<string>();
            }
        }

        public static void Main(string[] args)
        {
            if (!File.Exists("server_files.json")) // Create default server files config
            {
                var configTextDefo = JsonSerializer.Serialize(new ServerFilesConfig(
                        string.Empty, string.Empty), new JsonSerializerOptions()
                                { WriteIndented = true });

                File.WriteAllText("server_files.json", configTextDefo);
            }

            // Read server files config
            var configText = File.ReadAllText("server_files.json");
            var config = JsonSerializer.Deserialize<ServerFilesConfig>(configText);

            if (config is null || string.IsNullOrEmpty(config.PagePath) || string.IsNullOrEmpty(config.RootPath))
            {
                throw new Exception("Server file paths not setup!");
            }

            // Set up server directories
            PathHelper.SetRootDirectory(config.RootPath);
            PathHelper.SetPageDirectory(config.PagePath);

            // Load display name overrides
            FileTreeHelper.LoadFileNameOverrides(config.RootPath, config.NameOverrides);

            var builder = WebApplication.CreateBuilder(args);

            // Configure logging
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();

            // Add services to the container
            builder.Services.AddAuthorization();

            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // See https://stackoverflow.com/questions/56164407/cors-asp-net-core-webapi-missing-access-control-allow-origin-header
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("CorsPolicy",
                    builder => builder
                        .AllowAnyMethod()
                        .AllowCredentials()
                        .SetIsOriginAllowed((host) => true)
                        .AllowAnyHeader());
            });

            var app = builder.Build();

            // CORS Policy
            app.UseCors("CorsPolicy");

            // Configure the HTTP request pipeline
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();
            app.UseAuthorization();

            // Rewrite server host url to '/index.html' ("^$" matches an empty path on server)
            // See https://learn.microsoft.com/en-us/aspnet/core/fundamentals/url-rewriting
            app.UseRewriter(new RewriteOptions().AddRewrite("^$", "index.html", true));

            app.UseStaticFiles(new StaticFileOptions()
            {
                FileProvider = new PhysicalFileProvider(PathHelper.GetPageDirectory()),
                RequestPath = new PathString(string.Empty),
                ContentTypeProvider = new FileExtensionContentTypeProvider(),
                ServeUnknownFileTypes = true
            });

            app.UseStaticFiles(new StaticFileOptions()
            {
                FileProvider = new PhysicalFileProvider(PathHelper.GetRootDirectory()),
                RequestPath = new PathString("/files"),
                ContentTypeProvider = new FileExtensionContentTypeProvider(),
                ServeUnknownFileTypes = true
            });

            app.MapGet("/fileindex", (string? path) =>
            {
                var data = FileTreeHelper.GetFileTree(PathHelper.GetRootDirectory(), path ?? "");
                var tree = TreeDataBuilder.BuildTree(data);
                
                return Results.Json(tree);
            })
            .WithName("GetFileIndex");

            app.MapGet("/dummyimg", (string? text) =>
            {
                var bytes = DummyImageHelper.GetDummyPng(text ?? "Hello World!", 512, 512);

                return Results.File(bytes, fileDownloadName: "dummy.png");
            })
            .WithName("GetDummyImage");

            app.MapGet("/savedmaskimg", (string path) =>
            {
                var bytes = MaskImageHelper.GetMaskImageBytes(PathHelper.GetRootDirectory(), path);

                return Results.File(bytes, fileDownloadName: "mask.png");
            })
            .WithName("GetSavedMaskImage");

            app.MapGet("/maskserver", () =>
            {
                if (!string.IsNullOrWhiteSpace(config.MaskHost))
                {
                    return Results.Text($"http://{config.MaskHost}:{config.MaskPort}");
                }

                IPHostEntry ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());

                foreach (IPAddress address in ipHostInfo.AddressList)
                {
                    if (address.AddressFamily == AddressFamily.InterNetwork)
                        return Results.Text($"http://{address}:{config.MaskPort}");
                }

                return Results.Empty;
            })
            .WithName("GetMaskServer");

            app.MapGet("/inpaintserver", () =>
            {
                if (!string.IsNullOrWhiteSpace(config.InPaintHost))
                {
                    return Results.Text($"http://{config.InPaintHost}:{config.InPaintPort}");
                }

                IPHostEntry ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());

                foreach (IPAddress address in ipHostInfo.AddressList)
                {
                    if (address.AddressFamily == AddressFamily.InterNetwork)
                        return Results.Text($"http://{address}:{config.InPaintPort}");
                }

                return Results.Empty;
            })
            .WithName("GetInPaintServer");

            app.Run();
        }
    }
}