using Koan.Flow.Attributes;
using Koan.Flow.Model;

namespace S8.Flow.Shared;

/// <summary>
/// Dynamic manufacturer entity that receives data from multiple adapters.
/// BMS provides manufacturing details, OEM provides support and certification info.
/// All data is composed dynamically through JSON paths.
/// </summary>
[FlowModel("Manufacturer")]
[AggregationKeys("identifier.code", "identifier.name")]
public sealed class Manufacturer : DynamicFlowEntity<Manufacturer>
{
    // 
    // Expected data structure from adapters:
    // 
    // BMS adapter provides:
    //   identifier.code: "MFG001"
    //   identifier.name: "Acme Corp"  
    //   identifier.external.bms: "BMS-MFG-001"
    //   manufacturing.country: "USA"
    //   manufacturing.established: "1985"
    //   manufacturing.facilities: ["Plant A", "Plant B"]
    //   products.categories: ["sensors", "actuators"]
    //   
    // OEM adapter provides:
    //   identifier.code: "MFG001"
    //   identifier.name: "Acme Corp"
    //   identifier.external.oem: "OEM-VENDOR-42"
    //   support.phone: "1-800-ACME"
    //   support.email: "support@acme.com"
    //   support.tier: "Premium"
    //   certifications.iso9001: true
    //   certifications.iso14001: true
    //   warranty.standard: "2 years"
    //   warranty.extended: "5 years"
}