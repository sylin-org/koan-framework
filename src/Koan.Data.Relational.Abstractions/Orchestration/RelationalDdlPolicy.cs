namespace Koan.Data.Relational.Orchestration;

/// <summary>Controls whether Koan may change a relational schema.</summary>
public enum RelationalDdlPolicy { NoDdl, Validate, AutoCreate }
