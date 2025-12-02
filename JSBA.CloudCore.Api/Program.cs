using JSBA.CloudCore.Contracts.Interfaces;
using JSBA.CloudCore.Extractor;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register PDF Extractor service (interface-oriented design)
builder.Services.AddScoped<IPdfExtractor, ExtractorFacade>();

// Configure file upload limits (50MB max)
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 52428800; // 50MB
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();
