using Sora.Flow.Model;
using System.Collections.Generic;

namespace S8.Flow.Shared;

public sealed class SensorLatestReading : ProjectionView<Sensor, Dictionary<string, object>> { }
