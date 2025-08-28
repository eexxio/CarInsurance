using CarInsurance.Api.Data;
using CarInsurance.Api.Models;
using CarInsurance.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CarInsurance.Api.Tests.Services;

public class PolicyExpirationServiceTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly IServiceProvider _serviceProvider;

    public PolicyExpirationServiceTests()
    {
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<AppDbContext>();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        if (_serviceProvider is IDisposable disposable)
            disposable.Dispose();
    }

    [Fact]
    public async Task CheckForExpiredPolicies_CanCreateService()
    {
        // Arrange
        var logger = _serviceProvider.GetRequiredService<ILogger<PolicyExpirationService>>();
        var service = new PolicyExpirationService(_serviceProvider, logger);

        // Act
        await service.CheckForExpiredPoliciesAsync();
        
        // Assert
        Assert.True(true);
    }

    [Fact]
    public async Task CheckForExpiredPolicies_LogsPolicyThatExpiredToday()
    {
        // Arrange
        var owner = new Owner { Name = "Test Owner", Email = "test@example.com" };
        _dbContext.Owners.Add(owner);
        await _dbContext.SaveChangesAsync();
        
        var car = new Car { Vin = "TEST123", Make = "Test", Model = "Car", YearOfManufacture = 2020, OwnerId = owner.Id };
        _dbContext.Cars.Add(car);
        await _dbContext.SaveChangesAsync();
        
        var today = DateTime.UtcNow;
        var expiredPolicy = new InsurancePolicy
        {
            CarId = car.Id,
            Provider = "Test Provider",
            StartDate = DateOnly.FromDateTime(today.AddDays(-30)),
            EndDate = DateOnly.FromDateTime(today) // Expired today
        };
        _dbContext.Policies.Add(expiredPolicy);
        await _dbContext.SaveChangesAsync();

        var logger = _serviceProvider.GetRequiredService<ILogger<PolicyExpirationService>>();
        var service = new PolicyExpirationService(_serviceProvider, logger);

        // Act
        await service.CheckForExpiredPoliciesAsync();

        // Assert
        var logCount = await _dbContext.PolicyExpirationLogs.CountAsync();
        Assert.Equal(1, logCount);
        
        var expirationLog = await _dbContext.PolicyExpirationLogs.FirstAsync();
        Assert.Equal(expiredPolicy.Id, expirationLog.PolicyId);
        Assert.Equal(expiredPolicy.EndDate, expirationLog.ExpirationDate);
    }

    [Fact]
    public async Task CheckForExpiredPolicies_DoesNotLogPolicyExpiredMoreThanOneHourAgo()
    {
        // Arrange
        var owner = new Owner { Name = "Test Owner", Email = "test@example.com" };
        _dbContext.Owners.Add(owner);
        await _dbContext.SaveChangesAsync();
        
        var car = new Car { Vin = "TEST123", Make = "Test", Model = "Car", YearOfManufacture = 2020, OwnerId = owner.Id };
        _dbContext.Cars.Add(car);
        await _dbContext.SaveChangesAsync();
        
        var twoDaysAgo = DateTime.UtcNow.AddDays(-2);
        var expiredPolicy = new InsurancePolicy
        {
            CarId = car.Id,
            Provider = "Test Provider",
            StartDate = DateOnly.FromDateTime(twoDaysAgo.AddDays(-30)),
            EndDate = DateOnly.FromDateTime(twoDaysAgo) // Expired 2 days ago
        };
        _dbContext.Policies.Add(expiredPolicy);
        await _dbContext.SaveChangesAsync();

        var logger = _serviceProvider.GetRequiredService<ILogger<PolicyExpirationService>>();
        var service = new PolicyExpirationService(_serviceProvider, logger);

        // Act
        await service.CheckForExpiredPoliciesAsync();

        // Assert
        var logCount = await _dbContext.PolicyExpirationLogs.CountAsync();
        Assert.Equal(0, logCount);
    }

    [Fact]
    public async Task CheckForExpiredPolicies_DoesNotLogAlreadyProcessedPolicy()
    {
        // Arrange
        var owner = new Owner { Name = "Test Owner", Email = "test@example.com" };
        _dbContext.Owners.Add(owner);
        await _dbContext.SaveChangesAsync();
        
        var car = new Car { Vin = "TEST123", Make = "Test", Model = "Car", YearOfManufacture = 2020, OwnerId = owner.Id };
        _dbContext.Cars.Add(car);
        await _dbContext.SaveChangesAsync();
        
        var today = DateTime.UtcNow;
        var expiredPolicy = new InsurancePolicy
        {
            CarId = car.Id,
            Provider = "Test Provider",
            StartDate = DateOnly.FromDateTime(today.AddDays(-30)),
            EndDate = DateOnly.FromDateTime(today) // Expired today
        };
        _dbContext.Policies.Add(expiredPolicy);
        await _dbContext.SaveChangesAsync();

        var existingLog = new PolicyExpirationLog
        {
            PolicyId = expiredPolicy.Id,
            ExpirationDate = expiredPolicy.EndDate,
            ProcessedAt = DateTime.UtcNow.AddMinutes(-10)
        };
        _dbContext.PolicyExpirationLogs.Add(existingLog);
        await _dbContext.SaveChangesAsync();

        var logger = _serviceProvider.GetRequiredService<ILogger<PolicyExpirationService>>();
        var service = new PolicyExpirationService(_serviceProvider, logger);

        // Act
        await service.CheckForExpiredPoliciesAsync();

        // Assert
        var logCount = await _dbContext.PolicyExpirationLogs.CountAsync();
        Assert.Equal(1, logCount); // Only the existing log, no new one added
    }

    [Fact]
    public async Task CheckForExpiredPolicies_DoesNotLogFuturePolicies()
    {
        // Arrange
        var owner = new Owner { Name = "Test Owner", Email = "test@example.com" };
        _dbContext.Owners.Add(owner);
        await _dbContext.SaveChangesAsync();
        
        var car = new Car { Vin = "TEST123", Make = "Test", Model = "Car", YearOfManufacture = 2020, OwnerId = owner.Id };
        _dbContext.Cars.Add(car);
        await _dbContext.SaveChangesAsync();
        
        var futureDate = DateTime.UtcNow.AddDays(30);
        var futurePolicy = new InsurancePolicy
        {
            CarId = car.Id,
            Provider = "Test Provider",
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(futureDate) // Expires in the future
        };
        _dbContext.Policies.Add(futurePolicy);
        await _dbContext.SaveChangesAsync();

        var logger = _serviceProvider.GetRequiredService<ILogger<PolicyExpirationService>>();
        var service = new PolicyExpirationService(_serviceProvider, logger);

        // Act
        await service.CheckForExpiredPoliciesAsync();

        // Assert
        var logCount = await _dbContext.PolicyExpirationLogs.CountAsync();
        Assert.Equal(0, logCount);
    }

    [Fact]
    public async Task CheckForExpiredPolicies_DoesNotLogPolicyThatExpiredYesterday()
    {
        // Arrange
        var owner = new Owner { Name = "Test Owner", Email = "test@example.com" };
        _dbContext.Owners.Add(owner);
        await _dbContext.SaveChangesAsync();
        
        var car = new Car { Vin = "TEST123", Make = "Test", Model = "Car", YearOfManufacture = 2020, OwnerId = owner.Id };
        _dbContext.Cars.Add(car);
        await _dbContext.SaveChangesAsync();
        
        var yesterday = DateTime.UtcNow.AddDays(-1);
        var expiredPolicy = new InsurancePolicy
        {
            CarId = car.Id,
            Provider = "Test Provider",
            StartDate = DateOnly.FromDateTime(yesterday.AddDays(-30)),
            EndDate = DateOnly.FromDateTime(yesterday) // Expired yesterday (more than 24 hours ago)
        };
        _dbContext.Policies.Add(expiredPolicy);
        await _dbContext.SaveChangesAsync();

        var logger = _serviceProvider.GetRequiredService<ILogger<PolicyExpirationService>>();
        var service = new PolicyExpirationService(_serviceProvider, logger);

        // Act
        await service.CheckForExpiredPoliciesAsync();

        // Assert
        var logCount = await _dbContext.PolicyExpirationLogs.CountAsync();
        Assert.Equal(0, logCount); // Should not log policies that expired more than 24 hours ago
    }

    [Fact]
    public async Task CheckForExpiredPolicies_DoesNotLogPolicyThatExpiredExactly24HoursAgo()
    {
        // Arrange
        var owner = new Owner { Name = "Test Owner", Email = "test@example.com" };
        _dbContext.Owners.Add(owner);
        await _dbContext.SaveChangesAsync();
        
        var car = new Car { Vin = "TEST123", Make = "Test", Model = "Car", YearOfManufacture = 2020, OwnerId = owner.Id };
        _dbContext.Cars.Add(car);
        await _dbContext.SaveChangesAsync();
        
        var exactly24HoursAgo = DateTime.UtcNow.AddHours(-24);
        var expiredPolicy = new InsurancePolicy
        {
            CarId = car.Id,
            Provider = "Test Provider",
            StartDate = DateOnly.FromDateTime(exactly24HoursAgo.AddDays(-30)),
            EndDate = DateOnly.FromDateTime(exactly24HoursAgo) // Expired exactly 24 hours ago
        };
        _dbContext.Policies.Add(expiredPolicy);
        await _dbContext.SaveChangesAsync();

        var logger = _serviceProvider.GetRequiredService<ILogger<PolicyExpirationService>>();
        var service = new PolicyExpirationService(_serviceProvider, logger);

        // Act
        await service.CheckForExpiredPoliciesAsync();

        // Assert
        var logCount = await _dbContext.PolicyExpirationLogs.CountAsync();
        Assert.Equal(0, logCount); // Should not log policy that expired exactly 24 hours ago
    }

    [Fact]
    public async Task CheckForExpiredPolicies_LogsPolicyThatExpiredWithinLast24Hours()
    {
        // Arrange
        var owner = new Owner { Name = "Test Owner", Email = "test@example.com" };
        _dbContext.Owners.Add(owner);
        await _dbContext.SaveChangesAsync();
        
        var car = new Car { Vin = "TEST123", Make = "Test", Model = "Car", YearOfManufacture = 2020, OwnerId = owner.Id };
        _dbContext.Cars.Add(car);
        await _dbContext.SaveChangesAsync();
        
        // Since service works with DateOnly and converts to DateTime at midnight,
        // a policy that expired "today" (current date) will be within 24 hours
        var currentDate = DateTime.UtcNow.Date; // Get today's date at midnight
        var expiredPolicy = new InsurancePolicy
        {
            CarId = car.Id,
            Provider = "Test Provider",
            StartDate = DateOnly.FromDateTime(currentDate.AddDays(-30)),
            EndDate = DateOnly.FromDateTime(currentDate) // Expired today (within 24 hours)
        };
        _dbContext.Policies.Add(expiredPolicy);
        await _dbContext.SaveChangesAsync();

        var logger = _serviceProvider.GetRequiredService<ILogger<PolicyExpirationService>>();
        var service = new PolicyExpirationService(_serviceProvider, logger);

        // Act
        await service.CheckForExpiredPoliciesAsync();

        // Assert
        var logCount = await _dbContext.PolicyExpirationLogs.CountAsync();
        Assert.Equal(1, logCount);
        
        var expirationLog = await _dbContext.PolicyExpirationLogs.FirstAsync();
        Assert.Equal(expiredPolicy.Id, expirationLog.PolicyId);
        Assert.Equal(expiredPolicy.EndDate, expirationLog.ExpirationDate);
    }

    [Fact]
    public async Task CheckForExpiredPolicies_ServiceRunsMultipleTimesWithoutDuplicatingLogs()
    {
        // Arrange
        var owner = new Owner { Name = "Test Owner", Email = "test@example.com" };
        _dbContext.Owners.Add(owner);
        await _dbContext.SaveChangesAsync();
        
        var car = new Car { Vin = "TEST123", Make = "Test", Model = "Car", YearOfManufacture = 2020, OwnerId = owner.Id };
        _dbContext.Cars.Add(car);
        await _dbContext.SaveChangesAsync();
        
        var today = DateTime.UtcNow;
        var expiredPolicy = new InsurancePolicy
        {
            CarId = car.Id,
            Provider = "Test Provider",
            StartDate = DateOnly.FromDateTime(today.AddDays(-30)),
            EndDate = DateOnly.FromDateTime(today) // Expired today
        };
        _dbContext.Policies.Add(expiredPolicy);
        await _dbContext.SaveChangesAsync();

        var logger = _serviceProvider.GetRequiredService<ILogger<PolicyExpirationService>>();
        var service = new PolicyExpirationService(_serviceProvider, logger);

        // Act - Run service multiple times
        await service.CheckForExpiredPoliciesAsync();
        await service.CheckForExpiredPoliciesAsync();
        await service.CheckForExpiredPoliciesAsync();

        // Assert - Should only have one log entry even if ther are multiple runs
        var logCount = await _dbContext.PolicyExpirationLogs.CountAsync();
        Assert.Equal(1, logCount);
    }

    [Fact]
    public async Task CheckForExpiredPolicies_LogsMultiplePoliciesThatExpiredToday()
    {
        // Arrange
        var owner = new Owner { Name = "Test Owner", Email = "test@example.com" };
        _dbContext.Owners.Add(owner);
        await _dbContext.SaveChangesAsync();
        
        var car1 = new Car { Vin = "TEST123", Make = "Test", Model = "Car", YearOfManufacture = 2020, OwnerId = owner.Id };
        var car2 = new Car { Vin = "TEST456", Make = "Test", Model = "Car2", YearOfManufacture = 2021, OwnerId = owner.Id };
        _dbContext.Cars.AddRange(car1, car2);
        await _dbContext.SaveChangesAsync();
        
        var today = DateTime.UtcNow;
        
        var expiredPolicy1 = new InsurancePolicy
        {
            CarId = car1.Id,
            Provider = "Provider 1",
            StartDate = DateOnly.FromDateTime(today.AddDays(-30)),
            EndDate = DateOnly.FromDateTime(today) // Expired today
        };
        
        var expiredPolicy2 = new InsurancePolicy
        {
            CarId = car2.Id,
            Provider = "Provider 2",
            StartDate = DateOnly.FromDateTime(today.AddDays(-30)),
            EndDate = DateOnly.FromDateTime(today) // Expired today
        };

        _dbContext.Policies.AddRange(expiredPolicy1, expiredPolicy2);
        await _dbContext.SaveChangesAsync();

        var logger = _serviceProvider.GetRequiredService<ILogger<PolicyExpirationService>>();
        var service = new PolicyExpirationService(_serviceProvider, logger);

        // Act
        await service.CheckForExpiredPoliciesAsync();

        // Assert
        var logCount = await _dbContext.PolicyExpirationLogs.CountAsync();
        Assert.Equal(2, logCount);
        
        var logs = await _dbContext.PolicyExpirationLogs.ToListAsync();
        Assert.Contains(logs, log => log.PolicyId == expiredPolicy1.Id);
        Assert.Contains(logs, log => log.PolicyId == expiredPolicy2.Id);
    }

    [Fact]
    public async Task CheckForExpiredPolicies_IncludesCarAndOwnerInformationWhenLogging()
    {
        // Arrange
        var owner = new Owner { Name = "John Smith", Email = "john.smith@example.com" };
        _dbContext.Owners.Add(owner);
        await _dbContext.SaveChangesAsync();
        
        var car = new Car { Vin = "UNIQUE123VIN", Make = "Toyota", Model = "Camry", YearOfManufacture = 2022, OwnerId = owner.Id };
        _dbContext.Cars.Add(car);
        await _dbContext.SaveChangesAsync();
        
        var today = DateTime.UtcNow;
        var expiredPolicy = new InsurancePolicy
        {
            CarId = car.Id,
            Provider = "Premium Insurance",
            StartDate = DateOnly.FromDateTime(today.AddDays(-365)),
            EndDate = DateOnly.FromDateTime(today) // Expired today
        };
        _dbContext.Policies.Add(expiredPolicy);
        await _dbContext.SaveChangesAsync();

        var logger = _serviceProvider.GetRequiredService<ILogger<PolicyExpirationService>>();
        var service = new PolicyExpirationService(_serviceProvider, logger);

        // Act
        await service.CheckForExpiredPoliciesAsync();

        // Assert
        var logCount = await _dbContext.PolicyExpirationLogs.CountAsync();
        Assert.Equal(1, logCount);
        
        var expirationLog = await _dbContext.PolicyExpirationLogs
            .Include(log => log.Policy)
            .ThenInclude(p => p.Car)
            .ThenInclude(c => c.Owner)
            .FirstAsync();
            
        Assert.Equal(expiredPolicy.Id, expirationLog.PolicyId);
        Assert.Equal(expiredPolicy.EndDate, expirationLog.ExpirationDate);
        Assert.Equal("UNIQUE123VIN", expirationLog.Policy.Car.Vin);
        Assert.Equal("John Smith", expirationLog.Policy.Car.Owner.Name);
        Assert.Equal("Premium Insurance", expirationLog.Policy.Provider);
    }

}