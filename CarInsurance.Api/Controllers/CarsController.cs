using CarInsurance.Api.Dtos;
using CarInsurance.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CarInsurance.Api.Controllers;

[ApiController]
[Route("api")]
public class CarsController(CarService service) : ControllerBase
{
    private readonly CarService _service = service;

    private static string? ValidateDate(DateOnly date)
    {
        if (date < new DateOnly(1900, 1, 1) || date > new DateOnly(2200, 12, 31))
            return "Date must be between 1900-01-01 and 2200-12-31.";
        return null;
    }

    [HttpGet("cars")]
    public async Task<ActionResult<List<CarDto>>> GetCars()
        => Ok(await _service.ListCarsAsync());

    [HttpGet("cars/{carId:long}/insurance-valid")]
    public async Task<ActionResult<InsuranceValidityResponse>> IsInsuranceValid(long carId, [FromQuery] string date)
    {
        if (string.IsNullOrWhiteSpace(date))
            return BadRequest("Date parameter is required.");

        if (!DateOnly.TryParse(date, out var parsed))
            return BadRequest("Invalid date format. Use YYYY-MM-DD format.");

        var dateValidationError = ValidateDate(parsed);
        if (dateValidationError != null)
            return BadRequest(dateValidationError);

        try
        {
            var valid = await _service.IsInsuranceValidAsync(carId, parsed);
            return Ok(new InsuranceValidityResponse(carId, parsed.ToString("yyyy-MM-dd"), valid));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (KeyNotFoundException)
        {
            return NotFound($"Car with ID {carId} not found.");
        }
    }

    [HttpPost("cars/{carId:long}/claims")]
    public async Task<ActionResult<ClaimDto>> CreateClaim(long carId, [FromBody] CreateClaimRequest request)
    {
        var dateValidationError = ValidateDate(request.ClaimDate);
        if (dateValidationError != null)
            return BadRequest($"ClaimDate: {dateValidationError}");

        try
        {
            var claim = await _service.CreateClaimAsync(carId, request);
            return CreatedAtAction(nameof(CreateClaim), new { carId, id = claim.Id }, claim);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (KeyNotFoundException)
        {
            return NotFound($"Car with ID {carId} not found.");
        }
    }

    [HttpGet("cars/{carId:long}/history")]
    public async Task<ActionResult<CarHistoryResponse>> GetCarHistory(long carId)
    {
        try
        {
            var history = await _service.GetCarHistoryAsync(carId);
            return Ok(history);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (KeyNotFoundException)
        {
            return NotFound($"Car with ID {carId} not found.");
        }
    }
}
