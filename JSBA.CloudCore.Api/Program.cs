using JSBA.CloudCore.Contracts.Interfaces;
using JSBA.CloudCore.Extractor;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace JSBA.CloudCore.Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();
            builder.Services.AddControllers();          // <-- enable controllers

            // Register core services.
            //builder.Services.AddSingleton<IPdfExtractor, PdfExtractor>();
            builder.Services.AddScoped<IPdfExtractor, PdfExtractor>();

            // Configure file upload limits (50MB max)
            builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
            {
                options.MultipartBodyLengthLimit = 52428800; // 50MB
            });

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseRouting();

            // Map attribute-routed controllers like RoomsController
            app.MapControllers();                       // <-- hook them in

            // Root ping endpoint
            app.MapGet("/", () => "JSBA Cloud Core API is running.");

            app.Run();
        }
    }
}
