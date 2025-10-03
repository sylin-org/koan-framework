# g1c1.GardenCoop

## Contract

- **Inputs**: .NET 9 SDK, Koan repo checked out, no external services. Sensors simulated via UI or curl.
- **Outputs**: Console-hosted Koan slice with write-path automation and static AngularJS dashboard under `wwwroot/`.
- **Error modes**: Startup misconfig (missing Koan deps), API failures during sensor loop, reminder lifecycle exceptions.
- **Success criteria**: `dotnet run` launches console host, dashboard available at `http://localhost:5000`, reminders activate/retire as readings arrive, window close stops app.

## Running the slice

```pwsh
cd samples/guides/g1c1.GardenCoop
pwsh ./start.ps1    # optional helper, or just `dotnet run`
```

- The console prints lifecycle notes ("Sending email (fake)") when reminders activate.
- Open the browser dashboard, toggle the sensor loop, or post readings manually.
- Flip to the **Admin** tab to inspect sensors, their GUID serials, and rebind them to plots without reflashing hardware.
- Close the console window or press Ctrl+C to shut down.

## Key ingredients

- **Entity statics** (`Plot`, `Sensor`, `Reading`, `Reminder`, `Member`) with Koan relationships, including `SensorCapabilities` flags (soil humidity + temperature in Chapter 1).
- **Lifecycle automation** (`GardenAutomation`) that averages the latest readings and upserts reminders on the write path.
- **Seeder** populating three plots/sensors and generating initial readings so one reminder fires on boot.
- **AngularJS dashboard + admin view** served from `wwwroot/`, using CDN assets to keep scaffolding minimal.

## Next steps

- Add hysteresis (activate below 20%, retire above 24%).
- Introduce per-plot thresholds stored on `Plot`.
- Light up additional `SensorCapabilities` (air humidity for rain dampening, GPS for future chapters).
- Chapter 2 (`g1c2`) can pivot to Mongo or add a digest worker without rewriting the slice.
