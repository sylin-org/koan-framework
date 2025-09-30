using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using g1c1.GardenCoop.Models;
using Koan.Data.Core;
using Microsoft.AspNetCore.Mvc;

namespace g1c1.GardenCoop.Controllers;

[ApiController]
[Route("api/garden/readings")]
public sealed class ReadingsController : ControllerBase
{
	[HttpGet]
	public async Task<IActionResult> Get([FromQuery] int take = 50, CancellationToken cancellationToken = default)
	{
		if (take <= 0)
		{
			take = 50;
		}

		var items = await Reading.Query(_ => true, cancellationToken);
		var recent = items
			.OrderByDescending(r => r.SampledAt)
			.Take(Math.Min(take, 200))
			.ToArray();
		return Ok(recent);
	}

	[HttpGet("recent/{plotId}")]
	public async Task<IActionResult> GetRecent(string plotId, [FromQuery] int take = 8, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(plotId))
		{
			return BadRequest("plotId is required.");
		}

		var readings = await Reading.Recent(plotId, take, cancellationToken);
		return Ok(readings);
	}

	[HttpPost]
	public async Task<IActionResult> Post([FromBody] SensorReadingInput input, CancellationToken cancellationToken = default)
	{
		if (input is null)
		{
			return BadRequest("Payload is required.");
		}

		var serial = (input.SensorSerial ?? string.Empty).Trim();
		if (string.IsNullOrEmpty(serial))
		{
			return BadRequest("sensorSerial is required.");
		}

		if (!ModelState.IsValid)
		{
			return ValidationProblem(ModelState);
		}

		var sampleTime = input.SampledAt ?? DateTimeOffset.UtcNow;
		var sensor = await Sensor.GetBySerial(serial, cancellationToken) ?? await new Sensor
		{
			Serial = serial,
			DisplayName = serial
		}.Save(cancellationToken);

		sensor.LastSeenAt = sampleTime;
		sensor.Capabilities |= SensorCapabilities.SoilHumidity | SensorCapabilities.Temperature;
		sensor = await sensor.Save(cancellationToken);

		var reading = await new Reading
		{
			SensorId = sensor.Id,
			PlotId = sensor.PlotId,
			SoilHumidity = input.SoilHumidity,
			TemperatureC = input.TemperatureC,
			SampledAt = sampleTime
		}.Save(cancellationToken);

		return Ok(reading);
	}

	public sealed class SensorReadingInput
	{
		[Required]
		public string SensorSerial { get; set; } = string.Empty;

		[Range(0, 100)]
		public double SoilHumidity { get; set; }

		public double? TemperatureC { get; set; }

		public DateTimeOffset? SampledAt { get; set; }
	}
}
