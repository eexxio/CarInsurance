using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using CarInsurance.Api.Data;
using CarInsurance.Api.Models;
using System.Net.Http;
using System.Net;
using System.Text.Json;
using CarInsurance.Api.Dtos;
using Xunit;
using Microsoft.AspNetCore.Hosting;
using System.Net.Http.Json;

namespace CarInsurance.Api.Tests.Controllers;

public class CarsControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _dbName;

    public CarsControllerTests(WebApplicationFactory<Program> factory)
    {
        _dbName = Guid.NewGuid().ToString();
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                // Remove all database-related services
                var descriptors = services.Where(d => 
                    d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                    d.ServiceType == typeof(AppDbContext) ||
                    (d.ServiceType.IsGenericType && d.ServiceType.GetGenericTypeDefinition() == typeof(DbContextOptions<>)))
                    .ToArray();
                
                foreach (var descriptor in descriptors)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<AppDbContext>(options =>
                    options.UseInMemoryDatabase(databaseName: _dbName));
            });
        });
        _client = _factory.CreateClient();
    }

    private async Task SeedTestDataAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        await context.Database.EnsureCreatedAsync();
        
        context.Owners.Add(new Owner { Id = 1, Name = "Test Owner", Email = "test@example.com" });
        context.Cars.Add(new Car { Id = 1, OwnerId = 1, Vin = "TEST123", Make = "Test", Model = "Car", YearOfManufacture = 2020 });
        
        var today = DateOnly.FromDateTime(DateTime.Today);
        context.Policies.Add(new InsurancePolicy 
        { 
            Id = 1, 
            CarId = 1, 
            Provider = "TestInsurance", 
            StartDate = today.AddDays(-30), 
            EndDate = today.AddDays(30) 
        });
        
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task IsInsuranceValid_ValidCarAndDate_ReturnsTrue()
    {
        await SeedTestDataAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);
        
        var response = await _client.GetAsync($"/api/cars/1/insurance-valid?date={today:yyyy-MM-dd}");
        
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<InsuranceValidityResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        
        Assert.NotNull(result);
        Assert.True(result.Valid);
        Assert.Equal(1, result.CarId);
    }

    [Fact]
    public async Task IsInsuranceValid_NonExistentCar_Returns404()
    {
        await SeedTestDataAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);
        
        var response = await _client.GetAsync($"/api/cars/999/insurance-valid?date={today:yyyy-MM-dd}");
        
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task IsInsuranceValid_NegativeCarId_Returns400()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        
        var response = await _client.GetAsync($"/api/cars/-1/insurance-valid?date={today:yyyy-MM-dd}");
        
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Car ID must be a positive number", content);
    }

    [Fact]
    public async Task IsInsuranceValid_ZeroCarId_Returns400()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        
        var response = await _client.GetAsync($"/api/cars/0/insurance-valid?date={today:yyyy-MM-dd}");
        
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Car ID must be a positive number", content);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task IsInsuranceValid_EmptyOrNullDate_Returns400(string? date)
    {
        var response = await _client.GetAsync($"/api/cars/1/insurance-valid?date={date}");
        
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        // In ASP.NET Core 8, BadRequest() returns JSON problem details or plain text
        Assert.True(content.Contains("Date parameter is required") || content.Contains("date"), 
            $"Expected error message about date parameter, but got: {content}");
    }

    [Theory]
    [InlineData("invalid-date")]
    [InlineData("2024-13-01")]
    [InlineData("2024-02-30")]
    [InlineData("not-a-date")]
    [InlineData("32-01-2024")]
    public async Task IsInsuranceValid_InvalidDateFormat_Returns400(string invalidDate)
    {
        var response = await _client.GetAsync($"/api/cars/1/insurance-valid?date={invalidDate}");
        
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid date format", content);
    }

    [Theory]
    [InlineData("1899-12-31")]
    [InlineData("2201-01-01")]
    public async Task IsInsuranceValid_DateOutOfBounds_Returns400(string outOfBoundsDate)
    {
        var response = await _client.GetAsync($"/api/cars/1/insurance-valid?date={outOfBoundsDate}");
        
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Date must be between 1900-01-01 and 2200-12-31", content);
    }

    [Fact]
    public async Task IsInsuranceValid_DateBeforePolicyStart_ReturnsFalse()
    {
        await SeedTestDataAsync();
        var beforePolicyDate = DateOnly.FromDateTime(DateTime.Today).AddDays(-31);
        
        var response = await _client.GetAsync($"/api/cars/1/insurance-valid?date={beforePolicyDate:yyyy-MM-dd}");
        
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<InsuranceValidityResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        
        Assert.NotNull(result);
        Assert.False(result.Valid);
    }

    [Fact]
    public async Task IsInsuranceValid_DateAfterPolicyEnd_ReturnsFalse()
    {
        await SeedTestDataAsync();
        var afterPolicyDate = DateOnly.FromDateTime(DateTime.Today).AddDays(31);
        
        var response = await _client.GetAsync($"/api/cars/1/insurance-valid?date={afterPolicyDate:yyyy-MM-dd}");
        
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<InsuranceValidityResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        
        Assert.NotNull(result);
        Assert.False(result.Valid);
    }

    [Fact]
    public async Task IsInsuranceValid_ExactPolicyStartDate_ReturnsTrue()
    {
        await SeedTestDataAsync();
        var policyStartDate = DateOnly.FromDateTime(DateTime.Today).AddDays(-30);
        
        var response = await _client.GetAsync($"/api/cars/1/insurance-valid?date={policyStartDate:yyyy-MM-dd}");
        
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<InsuranceValidityResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        
        Assert.NotNull(result);
        Assert.True(result.Valid);
    }

    [Fact]
    public async Task IsInsuranceValid_ExactPolicyEndDate_ReturnsTrue()
    {
        await SeedTestDataAsync();
        var policyEndDate = DateOnly.FromDateTime(DateTime.Today).AddDays(30);
        
        var response = await _client.GetAsync($"/api/cars/1/insurance-valid?date={policyEndDate:yyyy-MM-dd}");
        
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<InsuranceValidityResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        
        Assert.NotNull(result);
        Assert.True(result.Valid);
    }

    [Fact]
    public async Task CreateClaim_NegativeCarId_Returns400()
    {
        var request = new { ClaimDate = "2024-01-01", Description = "Test", Amount = 100.00 };
        
        var response = await _client.PostAsJsonAsync("/api/cars/-1/claims", request);
        
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Car ID must be a positive number", content);
    }

    [Fact]
    public async Task CreateClaim_ZeroCarId_Returns400()
    {
        var request = new { ClaimDate = "2024-01-01", Description = "Test", Amount = 100.00 };
        
        var response = await _client.PostAsJsonAsync("/api/cars/0/claims", request);
        
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Car ID must be a positive number", content);
    }

    [Fact]
    public async Task GetCarHistory_NegativeCarId_Returns400()
    {
        var response = await _client.GetAsync("/api/cars/-1/history");
        
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Car ID must be a positive number", content);
    }

    [Fact]
    public async Task GetCarHistory_ZeroCarId_Returns400()
    {
        var response = await _client.GetAsync("/api/cars/0/history");
        
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Car ID must be a positive number", content);
    }

    [Fact]
    public async Task CreateClaim_InvalidClaimDate_Returns400()
    {
        await SeedTestDataAsync();
        var request = new { ClaimDate = "1800-01-01", Description = "Test", Amount = 100.00 };
        
        var response = await _client.PostAsJsonAsync("/api/cars/1/claims", request);
        
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Date must be between 1900-01-01 and 2200-12-31", content);
    }

    [Fact]
    public async Task CreateClaim_FutureClaimDate_Returns400()
    {
        await SeedTestDataAsync();
        var request = new { ClaimDate = "2300-01-01", Description = "Test", Amount = 100.00 };
        
        var response = await _client.PostAsJsonAsync("/api/cars/1/claims", request);
        
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Date must be between 1900-01-01 and 2200-12-31", content);
    }

    [Fact]
    public async Task CreateClaim_ValidClaimDate_Returns201()
    {
        await SeedTestDataAsync();
        var request = new { ClaimDate = "2024-01-15", Description = "Valid claim", Amount = 500.00 };
        
        var response = await _client.PostAsJsonAsync("/api/cars/1/claims", request);
        
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}