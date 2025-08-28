using CarInsurance.Api.Data;
using CarInsurance.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Only configure SQLite for non-testing environments
if (builder.Environment.EnvironmentName != "Testing")
{
    builder.Services.AddDbContext<AppDbContext>(opt =>
    {
        opt.UseSqlite(builder.Configuration.GetConnectionString("Default"));
    });
}

builder.Services.AddScoped<CarService>();

// Only register the background service in non-testing environments
if (builder.Environment.EnvironmentName != "Testing")
{
    builder.Services.AddHostedService<PolicyExpirationService>();
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.EnvironmentName != "Testing")
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    SeedData.EnsureSeeded(db);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();

public partial class Program { }
