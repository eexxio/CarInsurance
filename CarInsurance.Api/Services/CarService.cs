using CarInsurance.Api.Data;
using CarInsurance.Api.Dtos;
using CarInsurance.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CarInsurance.Api.Services;

public class CarService(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    public async Task<List<CarDto>> ListCarsAsync()
    {
        return await _db.Cars.Include(c => c.Owner)
            .Select(c => new CarDto(c.Id, c.Vin, c.Make, c.Model, c.YearOfManufacture,
                                    c.OwnerId, c.Owner.Name, c.Owner.Email))
            .ToListAsync();
    }

    public async Task<bool> IsInsuranceValidAsync(long carId, DateOnly date)
    {
        var carExists = await _db.Cars.AnyAsync(c => c.Id == carId);
        if (!carExists) throw new KeyNotFoundException($"Car {carId} not found");

        return await _db.Policies.AnyAsync(p =>
            p.CarId == carId &&
            p.StartDate <= date &&
            (p.EndDate == null || p.EndDate >= date)
        );
    }

    public async Task<ClaimDto> CreateClaimAsync(long carId, CreateClaimRequest request)
    {
        var carExists = await _db.Cars.AnyAsync(c => c.Id == carId);
        if (!carExists) throw new KeyNotFoundException($"Car {carId} not found");

        var claim = new Claim
        {
            CarId = carId,
            ClaimDate = request.ClaimDate,
            Description = request.Description,
            Amount = request.Amount
        };

        _db.Claims.Add(claim);
        await _db.SaveChangesAsync();

        return new ClaimDto(claim.Id, claim.CarId, claim.ClaimDate, claim.Description, claim.Amount);
    }

    public async Task<CarHistoryResponse> GetCarHistoryAsync(long carId)
    {
        var carExists = await _db.Cars.AnyAsync(c => c.Id == carId);
        if (!carExists) throw new KeyNotFoundException($"Car {carId} not found");

        var policies = await _db.Policies
            .Where(p => p.CarId == carId)
            .OrderBy(p => p.StartDate)
            .ToListAsync();

        var claims = await _db.Claims
            .Where(c => c.CarId == carId)
            .ToListAsync();

        var policyPeriods = policies.Select(policy =>
        {
            var policyClaims = claims
                .Where(claim => claim.ClaimDate >= policy.StartDate && claim.ClaimDate <= policy.EndDate)
                .Select(claim => new ClaimSummaryDto(claim.Id, claim.ClaimDate, claim.Description, claim.Amount))
                .OrderBy(claim => claim.ClaimDate)
                .ToList();

            return new PolicyPeriodWithClaimsDto(policy.Id, policy.Provider, policy.StartDate, policy.EndDate, policyClaims);
        }).ToList();

        return new CarHistoryResponse(carId, policyPeriods);
    }
}
