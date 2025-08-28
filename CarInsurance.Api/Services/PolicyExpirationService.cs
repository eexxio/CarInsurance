using CarInsurance.Api.Data;
using CarInsurance.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CarInsurance.Api.Services;

public class PolicyExpirationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PolicyExpirationService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(30);

    public PolicyExpirationService(IServiceProvider serviceProvider, ILogger<PolicyExpirationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckForExpiredPoliciesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while checking for expired policies");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    public async Task CheckForExpiredPoliciesAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var utcNow = DateTime.UtcNow;
        var currentDate = DateOnly.FromDateTime(utcNow);

        // Find policies that expired and haven't been processed yet
        var expiredPolicies = await dbContext.Policies
            .Include(p => p.Car)
            .ThenInclude(c => c.Owner)
            .Where(p => p.EndDate <= currentDate)
            .Where(p => !dbContext.PolicyExpirationLogs.Any(log => log.PolicyId == p.Id))
            .ToListAsync();

        foreach (var policy in expiredPolicies)
        {
            // Check if policy expired within the last 24 hours
            var expirationDateTime = policy.EndDate.ToDateTime(TimeOnly.MinValue);
            var timeSinceExpiration = utcNow - expirationDateTime;

            if (timeSinceExpiration <= TimeSpan.FromDays(1) && timeSinceExpiration >= TimeSpan.Zero)
            {
                _logger.LogWarning(
                    "Policy {PolicyId} for car {CarVin} (Owner: {OwnerName}) expired on {ExpirationDate}. Provider: {Provider}",
                    policy.Id,
                    policy.Car.Vin,
                    policy.Car.Owner.Name,
                    policy.EndDate,
                    policy.Provider);

                var expirationLog = new PolicyExpirationLog
                {
                    PolicyId = policy.Id,
                    ExpirationDate = policy.EndDate,
                    ProcessedAt = utcNow
                };

                dbContext.PolicyExpirationLogs.Add(expirationLog);
            }
        }

        await dbContext.SaveChangesAsync();
    }
}